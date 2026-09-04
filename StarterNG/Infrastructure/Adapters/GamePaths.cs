using System.IO;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure.Adapters;

/// <summary>
/// The stock MaSzyna layout, rooted at the process working directory (the
/// starter ships inside the installation folder, next to eu07.exe).
/// </summary>
public sealed class GamePaths : IGamePaths
{
    public GamePaths(string root)
    {
        Root = root;
    }

    /// <summary>The installation the starter was launched from.</summary>
    public static GamePaths ForCurrentDirectory() => new(Directory.GetCurrentDirectory());

    public string Root { get; }

    public string Scenery => Path.Combine(Root, "scenery");

    public string Dynamic => Path.Combine(Root, "dynamic");

    public string MiniTextures => Path.Combine(Root, "textures", "mini");

    public string Data => Path.Combine(Root, "data");

    public string StarterConfig => Path.Combine(Root, "starter");

    public string DiagnosticsLog => Path.Combine(StarterConfig, "bledy.txt");

    public string FromRoot(params string[] segments)
    {
        string result = Root;
        foreach (string segment in segments)
            result = Path.Combine(result, segment);
        return result;
    }
}
