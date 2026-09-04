namespace StarterNG.Application.Abstractions;

public interface IGamePaths
{
    string Root { get; }

    string Scenery { get; }

    string Dynamic { get; }

    string MiniTextures { get; }

    string Data { get; }

    string StarterConfig { get; }

    string DiagnosticsLog { get; }

    string FromRoot(params string[] segments);
}
