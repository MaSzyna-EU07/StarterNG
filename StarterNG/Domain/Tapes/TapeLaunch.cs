using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StarterNG.Domain.Tapes;

/// <summary>How a vehicle records its runs: a Hasler-type paper tape, or an electronic recorder read out on a screen.</summary>
public enum RecorderKind
{
    Hasler,
    Electronic
}

/// <summary>A vehicle of a launched scenery, as the simulator names it in physicslog/.</summary>
public sealed record TapeVehicle(string Name, string DataFolder, string TypeName);

/// <summary>What a tape is to the one who started the simulator; in the order they are listed by.</summary>
public enum TapeRole
{
    Other,
    OwnConsist,
    DrivenEarlier,
    StartedIn,
    Driven
}

/// <summary>
/// What the starter knew when it launched the simulator and the log does not
/// say: the time of day the scenery starts at - the log only counts seconds from
/// there - which vehicle stands behind each name the logs are written under,
/// which of them the player went off in: every manned vehicle leaves a log - the
/// scenario by its name and the number of the train the timetable gives. Once
/// the next launch is noted, also where the player has been.
/// </summary>
public sealed record TapeLaunch(DateTime LaunchedUtc, string Scenery, TimeSpan StartClock,
                                IReadOnlyList<TapeVehicle> Vehicles, string? Driven = null,
                                IReadOnlyList<string>? Consist = null, string? SceneryName = null,
                                string? Train = null, PlayerVehicles? Player = null)
{
    private static readonly Regex NextLaunch = new(@"(?m)^(?=launched\t)", RegexOptions.Compiled);

    public TapeVehicle? Find(string vehicleName) =>
        Vehicles.FirstOrDefault(v => string.Equals(v.Name, vehicleName, StringComparison.OrdinalIgnoreCase));

    public TapeRole RoleOf(string vehicleName) =>
        string.Equals(Driven, vehicleName, StringComparison.OrdinalIgnoreCase)
            ? TapeRole.Driven
            : Consist?.Contains(vehicleName, StringComparer.OrdinalIgnoreCase) == true
                ? TapeRole.OwnConsist
                : TapeRole.Other;

    /// <summary>The role as the run went: the vehicle the player used last is the driven one, whichever they set off in.</summary>
    public TapeRole RoleOf(string vehicleName, PlayerVehicles player)
    {
        var role = RoleOf(vehicleName);
        if (!player.Known)
            return role == TapeRole.Driven ? TapeRole.StartedIn : role;
        if (player.Current is not { } current)
            return role;
        if (string.Equals(current, vehicleName, StringComparison.OrdinalIgnoreCase))
            return TapeRole.Driven;
        return role == TapeRole.Driven || player.Used(vehicleName) ? TapeRole.DrivenEarlier : role;
    }

    /// <summary>
    /// A log belongs to this launch if the simulator wrote it after the starter
    /// started it; the simulator truncates the log of a vehicle on every run.
    /// </summary>
    public bool Covers(DateTime logWrittenUtc) => logWrittenUtc >= LaunchedUtc - TimeSpan.FromSeconds(5);

    // One fact a line, tab separated: names and folders may hold spaces.
    public string Format()
    {
        var text = new StringBuilder();
        text.Append("launched\t").Append(LaunchedUtc.ToString("O", CultureInfo.InvariantCulture)).Append('\n');
        text.Append("scenery\t").Append(Scenery).Append('\n');
        text.Append("clock\t").Append(StartClock.ToString(@"hh\:mm", CultureInfo.InvariantCulture)).Append('\n');
        if (!string.IsNullOrWhiteSpace(SceneryName))
            text.Append("name\t").Append(SceneryName).Append('\n');
        if (!string.IsNullOrWhiteSpace(Train))
            text.Append("train\t").Append(Train).Append('\n');
        foreach (var v in Vehicles)
            text.Append("vehicle\t").Append(v.Name).Append('\t').Append(v.DataFolder).Append('\t').Append(v.TypeName)
                .Append('\n');
        if (Driven is not null)
            text.Append("driven\t").Append(Driven).Append('\n');
        foreach (string name in Consist ?? Array.Empty<string>())
            text.Append("consist\t").Append(name).Append('\n');
        if (Player is { Known: true } player)
            text.AppendJoin('\t', player.Names.Prepend("played")).Append('\n');
        return text.ToString();
    }

    /// <summary>Every launch a file holds, oldest first.</summary>
    public static IReadOnlyList<TapeLaunch> ParseAll(string text) =>
        NextLaunch.Split(text).Select(Parse).OfType<TapeLaunch>().OrderBy(launch => launch.LaunchedUtc).ToList();

    public static TapeLaunch? Parse(string text)
    {
        DateTime? launched = null;
        string scenery = "";
        var clock = TimeSpan.Zero;
        var vehicles = new List<TapeVehicle>();
        string? driven = null;
        var consist = new List<string>();
        string? name = null;
        string? train = null;
        PlayerVehicles? player = null;
        foreach (var line in text.Split('\n'))
        {
            var parts = line.TrimEnd('\r').Split('\t');
            switch (parts[0])
            {
                case "launched" when parts.Length > 1 &&
                                     DateTime.TryParse(parts[1], CultureInfo.InvariantCulture,
                                                       DateTimeStyles.RoundtripKind, out var at):
                    launched = at.ToUniversalTime();
                    break;
                case "scenery" when parts.Length > 1:
                    scenery = parts[1];
                    break;
                case "clock" when parts.Length > 1:
                    TimeSpan.TryParseExact(parts[1], @"h\:mm", CultureInfo.InvariantCulture, out clock);
                    break;
                case "vehicle" when parts.Length > 3:
                    vehicles.Add(new TapeVehicle(parts[1], parts[2], parts[3]));
                    break;
                case "driven" when parts.Length > 1:
                    driven = parts[1];
                    break;
                case "consist" when parts.Length > 1:
                    consist.Add(parts[1]);
                    break;
                case "name" when parts.Length > 1:
                    name = parts[1];
                    break;
                case "train" when parts.Length > 1:
                    train = parts[1];
                    break;
                case "played":
                    player = PlayerVehicles.Of(parts.Skip(1));
                    break;
            }
        }
        return launched is { } when
            ? new TapeLaunch(when, scenery, clock, vehicles, driven, consist, name, train, player)
            : null;
    }
}

public static class Recorders
{
    /// <summary>
    /// Vehicles with an electronic recorder in place of a Hasler, matched on the vehicle
    /// folder and type, or failing that on the name the log is written under.
    /// </summary>
    private static readonly Regex Electronic = new(
        @"(?ix) (^|[^a-z0-9])
          ( en57a[lk][a-z]* | en71ak[a-z]* | en76 | en96 | en97 | ed74 | ed160 | ed161 | ed250 | ed78 | ed72a
          | ep07p | eu07a | e6act | e6acti | e4dcu | e4mcu | e4msu | e6mcu | 111e
          | x4e | traxx | vectron | griffin | dragon | husarz | elf | impuls | flirt
          | pendolino | sa1[0-9][0-9] | 22we | 27we | 31we | 34we | 35we | 36we | 45we )
          ($|[^a-z0-9])",
        RegexOptions.Compiled);

    public static RecorderKind Guess(TapeVehicle? vehicle, string tapeName)
    {
        if (vehicle is not null)
            return Electronic.IsMatch(vehicle.DataFolder.Replace('\\', '/').Split('/').Last())
                   || Electronic.IsMatch(vehicle.TypeName)
                ? RecorderKind.Electronic
                : RecorderKind.Hasler;
        return Electronic.IsMatch(tapeName) ? RecorderKind.Electronic : RecorderKind.Hasler;
    }
}
