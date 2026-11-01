using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace StarterNG.Domain.Settings;

/// <summary>What the simulator takes after its uart keys.</summary>
public static class Uart
{
    /// <summary>The speeds the simulator offers in its own UART panel.</summary>
    public static readonly int[] BaudRates =
        { 300, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 74880, 115200, 230400, 250000, 500000, 1000000, 2000000 };

    // The simulator has none of its own: "uart" must name all three.
    public const int DefaultBaud = 57600;
    public const double DefaultUpdateTime = 0.1;

    public const int TuneValues = 18;

    /// <summary>
    /// The calibration as the simulator reads it - eighteen numbers - or null.
    /// With fewer, it takes the next keys of the file in their place.
    /// </summary>
    public static string? NormalizeTune(string? text)
    {
        var values = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return values.Length == TuneValues && values.All(IsNumber)
            ? string.Join(' ', values)
            : null;
    }

    /// <summary>
    /// The file's uarttune line as a calibration to show. With a word in it, it holds
    /// no calibration at all but the next keys of the file, where the line was left short.
    /// </summary>
    public static string TuneInFile(string? text) =>
        (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).All(IsNumber) ? text ?? "" : "";

    private static bool IsNumber(string word) =>
        double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    /// <summary>The simulator reads the port as one word.</summary>
    public static bool IsPortName(string? port) =>
        !string.IsNullOrWhiteSpace(port) && !port.Trim().Any(char.IsWhiteSpace);

    /// <summary>The interval as its box can hold it, whatever the file says.</summary>
    public static decimal ShownUpdateTime(double seconds) =>
        double.IsNaN(seconds) ? (decimal)DefaultUpdateTime : (decimal)Math.Clamp(seconds, 0, MaxShownUpdateTime);

    public const double MaxShownUpdateTime = 10;

    /// <summary>
    /// uartfeature with nothing selected. Every build of the simulator reads the four
    /// words, only newer ones "none"; the features they do not name are off by default.
    /// </summary>
    public const string NoFeatures = "no no no no";

    // In the order the older form's four words go by.
    private static readonly string[] FeatureNames =
        { "main", "scnd", "train", "local", "radiovolume", "radiochannel", "dynamicbrake" };

    private const int OldFormWords = 4;

    /// <summary>
    /// The features a uartfeature value has on, as Globals.cpp reads it: a first word
    /// with a pipe, "none" or a feature's name is a list; anything else is the older
    /// form, four yes-or-no words for the first four features. Bare, nothing is on.
    /// </summary>
    public static ISet<string> FeaturesOn(string? value)
    {
        var words = (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var on = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (words.Length == 0)
            return on;

        if (IsFeatureList(words[0]))
        {
            foreach (string name in words[0].Split('|', StringSplitOptions.RemoveEmptyEntries))
                if (FeatureNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                    on.Add(name);
        }
        else
        {
            for (int i = 0; i < Math.Min(OldFormWords, words.Length); i++)
                if (words[i].ToLowerInvariant() is "yes" or "true" or "1")
                    on.Add(FeatureNames[i]);
        }
        return on;
    }

    /// <summary>
    /// Whether the file's own uartfeature line can stay in place of the one that would
    /// be written: it means the same, and every simulator reads it whole. The older
    /// form short of its words takes the next keys in; an empty list is what
    /// <see cref="NoFeatures"/> is for.
    /// </summary>
    public static bool FeaturesStand(string? inFile, string? written)
    {
        if (written is null || string.IsNullOrWhiteSpace(inFile))
            return false;

        var on = FeaturesOn(inFile);
        if (!on.SetEquals(FeaturesOn(written)))
            return false;

        var words = inFile.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return IsFeatureList(words[0]) ? on.Count > 0 : words.Length >= OldFormWords;
    }

    private static bool IsFeatureList(string word) =>
        word.Contains('|') ||
        word.Equals("none", StringComparison.OrdinalIgnoreCase) ||
        FeatureNames.Contains(word, StringComparer.OrdinalIgnoreCase);
}
