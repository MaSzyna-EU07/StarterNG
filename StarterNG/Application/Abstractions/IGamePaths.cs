namespace StarterNG.Application.Abstractions;

public interface IGamePaths
{
    string Root { get; }

    string Scenery { get; }

    string Dynamic { get; }

    string MiniTextures { get; }

    string Data { get; }

    /// <summary>Where the JSON vehicle database and the packages layered on it live.</summary>
    string VehicleDatabase { get; }

    string StarterConfig { get; }

    string DiagnosticsLog { get; }

    string FromRoot(params string[] segments);
}
