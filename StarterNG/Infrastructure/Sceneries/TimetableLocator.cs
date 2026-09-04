using System;
using System.IO;
using System.Linq;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Infrastructure.Sceneries;

public sealed class TimetableLocator
{
    private const string NoTimetable = "none";

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
}
