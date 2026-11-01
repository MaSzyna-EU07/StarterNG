using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Sceneries;
using StarterNG.Infrastructure.Adapters;

namespace StarterNG.Infrastructure.Sceneries;

public sealed class TimetableLocator
{
    private const string NoTimetable = "none";

    // The heading of a timetable: [ Rodzaj i numer pociagu | EIE1531 ]
    private static readonly Regex NumberLine = new(@"^\s*\[\s*Rodzaj\b[^|\r\n]*\|([^\]\r\n]+)",
                                                   RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private readonly IFileSystem _files;

    public TimetableLocator(IFileSystem files)
    {
        _files = files;
    }

    public string? Resolve(Scenery scenery, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Equals(NoTimetable, StringComparison.OrdinalIgnoreCase))
            return null;

        string sceneryDir = Path.GetDirectoryName(scenery.Path) ?? ".";
        string root = Path.GetDirectoryName(sceneryDir) ?? ".";

        string[] candidates =
        {
            Path.Combine(root, "timetables", name + ".txt"),
            Path.Combine(sceneryDir, name + ".txt"),
            Path.Combine(root, "scenario", name + ".txt"),
            Path.Combine(root, "timetables", name),
            Path.Combine(sceneryDir, name)
        };

        return candidates.FirstOrDefault(_files.FileExists);
    }

    /// <summary>The number the timetable gives its train; null with no timetable or no such heading in it.</summary>
    public string? TrainNumber(Scenery scenery, string? name)
    {
        if (Resolve(scenery, name) is not { } path)
            return null;

        try
        {
            var heading = NumberLine.Match(LegacyText.Decode(_files.ReadAllBytes(path)));
            string number = heading.Groups[1].Value.Trim();
            return number.Length > 0 ? number : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
