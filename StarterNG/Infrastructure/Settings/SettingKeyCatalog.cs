using System.Collections.Generic;

namespace StarterNG.Infrastructure.Settings;

/// <summary>What the settings screen can state about a key without describing it.</summary>
/// <param name="Key">The eu07.ini key itself.</param>
/// <param name="Default">The value the simulator assumes when the key is absent.</param>
/// <param name="Range">"lo-hi" when the starter clamps the value, otherwise null.</param>
public readonly record struct SettingKeyInfo(string Key, string? Default, string? Range);

/// <summary>
/// The default and the accepted range of every eu07.ini key the starter writes.
/// </summary>
/// <remarks>
/// Read straight off <see cref="SettingsSerializer"/> - these are the same
/// literals that method defaults and clamps with - so the settings screen can
/// state a fact about each key rather than a description of it. If a default or
/// a clamp changes in the serializer, it changes here too.
///
/// Text-valued keys carry no default. The literal the serializer passes to
/// GetString is the last step of a chain, not the value the simulator assumes:
/// an absent <c>lang</c> means the starter follows the operating system, and an
/// absent <c>starter.exe.path</c> means it goes looking for the binary itself.
/// Printing "lang = en" beside the language picker would state something untrue.
/// </remarks>
public static class SettingKeyCatalog
{
    private static readonly Dictionary<string, SettingKeyInfo> Entries = new()
    {
        ["ai.trainman"] = new("ai.trainman", "true", null),
        ["anisotropicfiltering"] = new("anisotropicfiltering", "8", null),
        ["async.trainThreads"] = new("async.trainThreads", "0", null),
        ["brakespeed"] = new("brakespeed", "3.0", null),
        ["brakestep"] = new("brakestep", "1.0", null),
        ["compresstex"] = new("compresstex", "true", null),
        ["convertmodels"] = new("convertmodels", "0", null),
        ["crashdamage"] = new("crashdamage", "true", null),
        ["debugmode"] = new("debugmode", "false", null),
        ["dynamiclights"] = new("dynamiclights", "7", "0-7"),
        ["enabletraction"] = new("enabletraction", "true", null),
        ["feedbackmode"] = new("feedbackmode", "1", "0-5"),
        ["feedbackport"] = new("feedbackport", "888", null),
        ["fieldofview"] = new("fieldofview", "45", "15-75"),
        ["fpslimit"] = new("fpslimit", "30", "1-100"),
        ["friction"] = new("friction", "1.0", null),
        ["fullphysics"] = new("fullphysics", "true", null),
        ["fullscreen"] = new("fullscreen", "false", null),
        ["fullscreenwindowed"] = new("fullscreenwindowed", "false", null),
        ["gfx.drawrange.factor.max"] = new("gfx.drawrange.factor.max", "3.0", null),
        ["gfx.envmap.enabled"] = new("gfx.envmap.enabled", "true", null),
        ["gfx.extraeffects"] = new("gfx.extraeffects", "true", null),
        ["gfx.postfx.chromaticaberration.enabled"] = new("gfx.postfx.chromaticaberration.enabled", "true", null),
        ["gfx.postfx.motionblur.enabled"] = new("gfx.postfx.motionblur.enabled", "true", null),
        ["gfx.reflections.fidelity"] = new("gfx.reflections.fidelity", "0", "0-2"),
        ["gfx.reflections.framerate"] = new("gfx.reflections.framerate", "60", null),
        ["gfx.resource.move"] = new("gfx.resource.move", "false", null),
        ["gfx.resource.sweep"] = new("gfx.resource.sweep", "true", null),
        ["gfx.shadergamma"] = new("gfx.shadergamma", "false", null),
        ["gfx.shadow.angle.min"] = new("gfx.shadow.angle.min", "0.2", null),
        ["gfx.shadow.rank.cutoff"] = new("gfx.shadow.rank.cutoff", "3", null),
        ["gfx.shadows.cab.range"] = new("gfx.shadows.cab.range", "0", null),
        ["gfx.skippipeline"] = new("gfx.skippipeline", "false", null),
        ["gfx.skiprendering"] = new("gfx.skiprendering", "false", null),
        ["gfx.smoke"] = new("gfx.smoke", "true", null),
        ["gfx.smoke.fidelity"] = new("gfx.smoke.fidelity", "1", "1-4"),
        ["gfx.usegles"] = new("gfx.usegles", "false", null),
        ["height"] = new("height", "720", null),
        ["inactivepause"] = new("inactivepause", "true", null),
        ["input.gamepad"] = new("input.gamepad", "true", null),
        ["lang"] = new("lang", null, null),
        ["livetraction"] = new("livetraction", "true", null),
        ["maxcabtexturesize"] = new("maxcabtexturesize", "4096", null),
        ["maxtexturesize"] = new("maxtexturesize", "4096", null),
        ["multiplelogs"] = new("multiplelogs", "false", null),
        ["multisampling"] = new("multisampling", "2", "0-3"),
        ["pause"] = new("pause", "false", null),
        ["physicslog"] = new("physicslog", "false", null),
        ["python.enabled"] = new("python.enabled", "true", null),
        ["python.mipmaps"] = new("python.mipmaps", "true", null),
        ["python.threadedupload"] = new("python.threadedupload", "true", null),
        ["python.updatetime"] = new("python.updatetime", "200", null),
        ["scalespeculars"] = new("scalespeculars", "true", null),
        ["shadows"] = new("shadows", "true", null),
        ["sound.volume"] = new("sound.volume", "1.0", null),
        ["sound.volume.ambient"] = new("sound.volume.ambient", "1.0", null),
        ["sound.volume.positional"] = new("sound.volume.positional", "1.0", null),
        ["sound.volume.radio"] = new("sound.volume.radio", "0.75", null),
        ["sound.volume.vehicle"] = new("sound.volume.vehicle", "1.0", null),
        ["soundenabled"] = new("soundenabled", "true", null),
        ["splinefidelity"] = new("splinefidelity", "1", "1-4"),
        ["starter.autoclose"] = new("starter.autoclose", "false", null),
        ["starter.batterydefault"] = new("starter.batterydefault", "0", "0-2"),
        ["starter.bufferscale"] = new("starter.bufferscale", "100", "50-200"),
        ["starter.drivableonly"] = new("starter.drivableonly", "true", null),
        ["starter.exe.auto"] = new("starter.exe.auto", "true", null),
        ["starter.exe.path"] = new("starter.exe.path", null, null),
        ["starter.expandtree"] = new("starter.expandtree", "false", null),
        ["starter.hide.archivalvehicles"] = new("starter.hide.archivalvehicles", "true", null),
        ["starter.ignoreirrelevant"] = new("starter.ignoreirrelevant", "false", null),
        ["starter.largethumbnails"] = new("starter.largethumbnails", "false", null),
        ["starter.last.scenery"] = new("starter.last.scenery", null, null),
        ["starter.logmissingvehicles"] = new("starter.logmissingvehicles", "false", null),
        ["starter.show.ai"] = new("starter.show.ai", "true", null),
        ["starter.show.archival"] = new("starter.show.archival", "true", null),
        ["starter.window.maximized"] = new("starter.window.maximized", "false", null),
        ["uart"] = new("uart", null, null),
        ["uartdebug"] = new("uartdebug", "false", null),
        ["uarttune"] = new("uarttune", null, null),
        ["usevbo"] = new("usevbo", "true", null),
        ["vsync"] = new("vsync", "false", null),
        ["width"] = new("width", "1280", null),
    };

    /// <summary>
    /// The key annotation for a settings row: the key, its default and its range,
    /// e.g. "dynamiclights = 7 (0-7)". Empty for a key we know nothing about, and
    /// for the composite keys a row only partly owns.
    /// </summary>
    public static string Annotate(string? key)
    {
        if (string.IsNullOrEmpty(key) || !Entries.TryGetValue(key, out var info))
            return key ?? "";

        string text = info.Default is null ? info.Key : $"{info.Key} = {info.Default}";
        return info.Range is null ? text : $"{text} ({info.Range})";
    }
}
