using StarterNG.Infrastructure.Sceneries;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class SceneryTranslationsTests
{
    private static string I18n(string file) => Path.Combine(Path.GetFullPath(TestInstallation.At("scenery")),
                                                            "i18n", file);

    [Fact]
    public void An_at_reference_is_resolved_against_the_sceneries_own_table()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TestInstallation.At("scenery", "kato.scn"), "//$d @mission.brief")
            .WithFile(I18n("kato_en.json"), """{ "mission": { "brief": "Take the stopper to Katowice" } }""");
        var installation = new TestInstallation(files);
        var scenery = Assert.Single(installation.Sceneries.LoadAll());
        var translations = new SceneryTranslations(files, installation.Log);

        translations.LoadFor(scenery, "en");

        Assert.Equal("Take the stopper to Katowice", scenery.LocalizedDescription(translations));
    }

    [Fact]
    public void The_users_language_is_layered_over_english()
    {
        var files = new InMemoryFileSystem()
            .WithFile(TestInstallation.At("scenery", "kato.scn"), "//$d @brief")
            .WithFile(I18n("kato_en.json"), """{ "brief": "English" }""")
            .WithFile(I18n("kato_pl.json"), """{ "brief": "Polski" }""");
        var installation = new TestInstallation(files);
        var scenery = Assert.Single(installation.Sceneries.LoadAll());
        var translations = new SceneryTranslations(files, installation.Log);

        translations.LoadFor(scenery, "Polski");

        Assert.Equal("Polski", scenery.LocalizedDescription(translations));
    }

    [Fact]
    public void Literal_text_and_unknown_keys_are_left_alone()
    {
        var installation = new TestInstallation();
        var translations = new SceneryTranslations(installation.Files, installation.Log);

        Assert.Equal("zwykły opis", translations.Translate("zwykły opis"));
        Assert.Equal("@nieznany.klucz", translations.Translate("@nieznany.klucz"));
        Assert.Equal("", translations.Translate(null));
    }
}
