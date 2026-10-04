using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Settings;

namespace StarterNG.Application;

/// <summary>A build to offer: what to call it on the top bar and where to get it.</summary>
public sealed record AvailableUpdate(string Label, string PageUrl);

public enum UpdateCheckOutcome
{
    UpToDate,
    UpdateAvailable,
    NoAnswer
}

public sealed class UpdateCheck
{
    private const string DayFormat = "yyyy-MM-dd";
    private const int ShortCommit = 7;

    private readonly SimulatorSettings _settings;
    private readonly IReleaseFeed _feed;
    private readonly string? _commit;
    private readonly IFileSystem _files;
    private readonly IClock _clock;
    private readonly IDiagnosticsLog _log;
    private readonly string _recordPath;

    public UpdateCheck(SimulatorSettings settings, IReleaseFeed feed, Version current, string? commit,
                       IFileSystem files, IClock clock, IDiagnosticsLog log, string recordPath)
    {
        _settings = settings;
        _feed = feed;
        _commit = commit;
        _files = files;
        _clock = clock;
        _log = log;
        _recordPath = recordPath;
        Current = Normalize(current);
    }

    public Version Current { get; }

    /// <summary>The build to offer, as last heard of; null when up to date or not asked yet.</summary>
    public AvailableUpdate? Available { get; private set; }

    public event Action? Changed;

    /// <summary>
    /// The check made on start. GitHub is asked at most once a day, and its answer
    /// is remembered, so a second start the same day still shows the update.
    /// </summary>
    public async Task CheckDailyAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.CheckForUpdates)
            return;

        var channel = _settings.UpdateChannel;
        if (ReadRecord() is { } record && record.Day == Today() && record.Channel == channel)
        {
            Publish(channel, record.Release);
            return;
        }

        await CheckNowAsync(channel, cancellationToken);
    }

    /// <summary>Asks straight away, whatever the switch in the settings says.</summary>
    public async Task<UpdateCheckOutcome> CheckNowAsync(UpdateChannel channel,
                                                        CancellationToken cancellationToken = default)
    {
        var lookup = await _feed.LatestAsync(channel, cancellationToken);

        // Not recorded, so the next start asks again instead of waiting a day.
        if (!lookup.Answered)
            return UpdateCheckOutcome.NoAnswer;

        WriteRecord(channel, lookup.Release);
        Publish(channel, lookup.Release);
        return Available is null ? UpdateCheckOutcome.UpToDate : UpdateCheckOutcome.UpdateAvailable;
    }

    /// <summary>
    /// "v1.4.107" as 1.4.107. Anything that is not a plain version - a suffix, a
    /// rolling tag like "staging" - is not an update.
    /// </summary>
    public static Version? ParseTag(string tag)
    {
        string text = tag.StartsWith('v') || tag.StartsWith('V') ? tag[1..] : tag;
        return Version.TryParse(text, out var version) ? Normalize(version) : null;
    }

    /// <summary>The commit a build carries after the "+" of its informational version.</summary>
    public static string? CommitOf(string? informationalVersion)
    {
        int plus = informationalVersion?.IndexOf('+') ?? -1;
        return plus >= 0 && plus < informationalVersion!.Length - 1 ? informationalVersion[(plus + 1)..] : null;
    }

    private void Publish(UpdateChannel channel, ReleaseInfo? release)
    {
        // Compared again on every start: the build remembered this morning may be
        // the very one installed since.
        var available = channel == UpdateChannel.Staging ? StagingUpdate(release) : StableUpdate(release);
        if (available == Available)
            return;

        Available = available;
        Changed?.Invoke();
    }

    private AvailableUpdate? StableUpdate(ReleaseInfo? release) =>
        release is not null && ParseTag(release.Tag) is { } version && version > Current
            ? new AvailableUpdate(version.ToString(), release.PageUrl)
            : null;

    // The staging build has no version of its own, only its commit, and the newest
    // one is the only one there is: any build but the running one is worth offering.
    private AvailableUpdate? StagingUpdate(ReleaseInfo? release) =>
        release is { Commit: { Length: > 0 } commit } && !SameCommit(commit, _commit)
            ? new AvailableUpdate($"{release.Tag} {commit[..Math.Min(ShortCommit, commit.Length)]}", release.PageUrl)
            : null;

    private static bool SameCommit(string a, string? b) =>
        !string.IsNullOrEmpty(b) &&
        (a.StartsWith(b, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a, StringComparison.OrdinalIgnoreCase));

    private string Today() => _clock.Now.ToString(DayFormat, CultureInfo.InvariantCulture);

    private (string Day, UpdateChannel Channel, ReleaseInfo? Release)? ReadRecord()
    {
        try
        {
            if (!_files.FileExists(_recordPath))
                return null;

            string[] lines = _files.ReadAllText(_recordPath).Split('\n', StringSplitOptions.TrimEntries);
            if (lines.Length < 5 || !Enum.TryParse(lines[1], out UpdateChannel channel))
                return null;

            var release = lines[2].Length > 0 && lines[3].Length > 0
                ? new ReleaseInfo(lines[2], lines[3], lines[4].Length > 0 ? lines[4] : null)
                : null;
            return (lines[0], channel, release);
        }
        catch (Exception ex)
        {
            _log.Log($"reading {_recordPath}", ex);
            return null;
        }
    }

    private void WriteRecord(UpdateChannel channel, ReleaseInfo? release)
    {
        try
        {
            if (Path.GetDirectoryName(_recordPath) is { Length: > 0 } directory)
                _files.CreateDirectory(directory);

            _files.WriteAllText(_recordPath, string.Join('\n', Today(), channel, release?.Tag ?? "",
                                                         release?.PageUrl ?? "", release?.Commit ?? ""));
        }
        catch (Exception ex)
        {
            _log.Log($"writing {_recordPath}", ex);
        }
    }

    // The assembly carries four parts and the tags three; an unset part reads as -1.
    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
