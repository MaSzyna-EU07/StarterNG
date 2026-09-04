namespace StarterNG.Application.Abstractions;

public interface IMiniTextureIndex
{
    void Preload();

    bool Has(string? miniName);

    string? PathFor(string? miniName);

    string? FallbackPath { get; }
}
