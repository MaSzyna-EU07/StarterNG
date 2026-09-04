using StarterNG.Domain.Sceneries;

namespace StarterNG.Application.Abstractions;

public interface ISceneryTranslations
{
    void LoadFor(Scenery scenery, string langCode);

    string Translate(string? text);
}
