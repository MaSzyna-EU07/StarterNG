using System.IO;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure.Adapters;

public sealed class GamePaths : IGamePaths
{
    public GamePaths(string root)
    {
        Root = root;
    }

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
