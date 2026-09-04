using StarterNG.Infrastructure.Settings;

namespace StarterNG.Tests;

public class SettingKeyCatalogTests
{
    [Fact]
    public void A_clamped_key_is_annotated_with_its_default_and_range()
    {
        Assert.Equal("dynamiclights = 7 (0-7)", SettingKeyCatalog.Annotate("dynamiclights"));
        Assert.Equal("starter.bufferscale = 100 (50-200)", SettingKeyCatalog.Annotate("starter.bufferscale"));
    }

    [Fact]
    public void An_unclamped_key_is_annotated_with_its_default_alone()
    {
        Assert.Equal("maxtexturesize = 4096", SettingKeyCatalog.Annotate("maxtexturesize"));
        Assert.Equal("vsync = false", SettingKeyCatalog.Annotate("vsync"));
    }

    [Fact]
    public void A_composite_or_unknown_key_is_shown_as_written()
    {
        Assert.Equal("shadowtune", SettingKeyCatalog.Annotate("shadowtune"));
        Assert.Equal("", SettingKeyCatalog.Annotate(null));
    }

    [Fact]
    public void The_annotation_matches_what_the_serializer_actually_defaults_to()
    {
        var settings = new StarterNG.Domain.Settings.SimulatorSettings();
        new SettingsSerializer().Read(settings, new StarterNG.Classes.ConfigFile());

        Assert.Equal($"maxtexturesize = {settings.MaxTextureSize}", SettingKeyCatalog.Annotate("maxtexturesize"));
        Assert.Equal($"fieldofview = {settings.FieldOfView} (15-75)", SettingKeyCatalog.Annotate("fieldofview"));
    }
}
