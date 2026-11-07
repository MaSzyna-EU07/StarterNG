using System.IO;
using StarterNG.Application.Abstractions;

namespace StarterNG.Infrastructure.Adapters;

public sealed class GamePaths : IGamePaths
{
    public GamePaths(string root)
    {
        Root = root;
    }

    /// <summary>
    /// The working directory, which is how the game's own shortcuts point at it - or
    /// the starter's folder where only that one holds a game, as when started from a
    /// shortcut set up elsewhere. Moved there too: more than these paths go by it.
    /// </summary>
    public static GamePaths ForCurrentDirectory()
    {
        string current = Directory.GetCurrentDirectory();
        string own = System.AppContext.BaseDirectory;
        if (HoldsGame(current) || !HoldsGame(own))
            return new GamePaths(current);

        Directory.SetCurrentDirectory(own);
        return new GamePaths(Directory.GetCurrentDirectory());
    }

    private static bool HoldsGame(string folder) =>
        Directory.Exists(Path.Combine(folder, "scenery")) && Directory.Exists(Path.Combine(folder, "dynamic"));

    public string Root { get; }

    public string Scenery => Path.Combine(Root, "scenery");

    public string Dynamic => Path.Combine(Root, "dynamic");

    public string MiniTextures => Path.Combine(Root, "textures", "mini");

    public string Data => Path.Combine(Root, "data");

    public string VehicleDatabase => Path.Combine(Root, "databases", "vehicles");

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
