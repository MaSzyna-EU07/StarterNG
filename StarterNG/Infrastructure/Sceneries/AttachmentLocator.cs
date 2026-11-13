using StarterNG.Application.Abstractions;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Infrastructure.Sceneries;

/// <summary>Where the file a scenery attaches (<c>//$f</c>) is on disk.</summary>
public sealed class AttachmentLocator
{
    private readonly IGamePaths _paths;
    private readonly CaseInsensitivePaths _names;

    public AttachmentLocator(IFileSystem files, IGamePaths paths)
    {
        _paths = paths;
        _names = new CaseInsensitivePaths(files);
    }

    /// <summary>
    /// The attachment's file, named from the game's folder or in full, with its letters
    /// as on disk; null when it is not there.
    /// </summary>
    public string? Resolve(SceneryAttachment attachment) =>
        string.IsNullOrWhiteSpace(attachment.FilePath) ? null : _names.Find(_paths.Root, attachment.FilePath.Trim());
}
