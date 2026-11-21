using StarterNG.Application;
using StarterNG.Domain.Settings;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class GraphicsPresetTests
{
    private static SimulatorSettings Defaults()
    {
        var installation = new TestInstallation();
        installation.SettingsStore.Load();
        return installation.SettingsStore.Settings;
    }

    private static SimulatorSettings AtHigh()
    {
        var settings = Defaults().Copy();
        GraphicsPreset.High.ApplyTo(settings);
        return settings;
    }

    [Fact]
    public void The_keys_named_are_the_keys_a_level_sets()
    {
        var store = new TestInstallation().SettingsStore;
        var low = Defaults().Copy();
        var top = Defaults().Copy();
        (GraphicsPreset.Minimal with { Extras = null }).ApplyTo(low);
        (GraphicsPreset.Ultra with { Extras = null, Top = null }).ApplyTo(top);

        var lowKeys = store.Render(low);
        var topKeys = store.Render(top);
        var differing = lowKeys.Keys.Union(topKeys.Keys)
            .Where(key => lowKeys.GetValueOrDefault(key) != topKeys.GetValueOrDefault(key));

        Assert.Equal(GraphicsPreset.Keys.OrderBy(k => k), differing.OrderBy(k => k));
    }

    // ── The scale ────────────────────────────────────────────────────────────

    private static void Climbs(Func<GraphicsPreset, double> value, int repeatsAllowed)
    {
        var values = GraphicsPreset.All.Select(value).ToList();

        Assert.Equal(values.OrderBy(v => v), values);
        Assert.True(values.Count - values.Distinct().Count() <= repeatsAllowed, string.Join(", ", values));
    }

    [Fact]
    public void Every_slider_moves_from_level_to_level_as_far_as_it_has_steps_for_six()
    {
        // Sizes and filtering run out of steps above High.
        Climbs(p => p.TextureSize, 1);
        Climbs(p => p.CabTextureSize, 2);
        Climbs(p => p.Anisotropy, 1);

        // Four steps or three for six levels.
        Climbs(p => (int)p.AntiAliasing, 2);
        Climbs(p => p.SmokeFidelity, 2);
        Climbs(p => p.SplineFidelity, 2);
        Climbs(p => p.ReflectionsFidelity, 3);
        Climbs(p => p.ShadowRank, 3);

        // The simulator takes no more than its own 7 lights.
        Climbs(p => p.DynamicLights, 2);
        Climbs(p => p.DrawRangeFactor, 3);
        Climbs(p => p.CabShadowsRange, 1);

        Climbs(p => p.ShadowMap, 2);
        Climbs(p => p.ShadowRange, 1);
        // The old starter's steps begin at once a second: the lower levels share it.
        Climbs(p => p.ReflectionsRate, 2);
    }

    [Fact]
    public void No_step_up_a_scale_is_more_than_a_few_steps_of_its_slider()
    {
        // In steps of what the screen offers, so Ultra does not jump to the top from far below.
        var rates = GraphicsPreset.All.Select(p => Steps.IndexOf(SliderRanges.ReflectionUpdates, p.ReflectionsRate)).ToList();
        Assert.All(rates.Zip(rates.Skip(1)), step => Assert.InRange(step.Second - step.First, 0, 2));

        foreach (Func<GraphicsPreset, int> fidelity in new Func<GraphicsPreset, int>[] { p => p.SmokeFidelity, p => p.SplineFidelity })
        {
            var levels = GraphicsPreset.All.Select(fidelity).ToList();
            Assert.All(levels.Zip(levels.Skip(1)), step => Assert.InRange(step.Second - step.First, 0, 1));
        }
    }

    [Fact]
    public void High_is_the_simulators_own_but_where_that_is_the_bottom_of_a_scale()
    {
        var store = new TestInstallation().SettingsStore;
        var own = store.Render(Defaults());
        var high = store.Render(AtHigh());

        var differing = own.Keys.Union(high.Keys)
            .Where(key => own.GetValueOrDefault(key) != high.GetValueOrDefault(key));

        Assert.Equal(new[] { "gfx.reflections.fidelity", "gfx.reflections.framerate", "gfx.shadows.cab.range",
                             "gfx.smoke.fidelity", "splinefidelity" }, differing.OrderBy(k => k));
        Assert.Equal(1, Defaults().SmokeFidelity);
        Assert.Equal(1, Defaults().SplineFidelity);
    }

    [Fact]
    public void A_player_with_no_settings_of_their_own_starts_at_high()
    {
        var installation = new TestInstallation();
        var store = installation.SettingsStore;
        store.Load();

        store.StartAtHighWithoutOwnSettings();

        Assert.Same(GraphicsPreset.High, GraphicsPreset.Matching(store.Settings));
        Assert.False(store.LastSaveFailed);

        var again = new TestInstallation(installation.Files).SettingsStore;
        again.Load();
        Assert.Same(GraphicsPreset.High, GraphicsPreset.Matching(again.Settings));
        // Nothing of an older starter to bring into the standard mode.
        Assert.True(again.Settings.MovedToStandard);
    }

    [Fact]
    public void A_player_with_settings_of_their_own_stays_as_they_are()
    {
        var installation = new TestInstallation();
        var store = installation.SettingsStore;
        store.Load();
        GraphicsPreset.Low.ApplyTo(store.Settings);
        store.Save();

        var again = new TestInstallation(installation.Files).SettingsStore;
        again.Load();
        again.StartAtHighWithoutOwnSettings();

        Assert.Same(GraphicsPreset.Low, GraphicsPreset.Matching(again.Settings));
        Assert.False(again.Settings.MovedToStandard);
    }

    [Fact]
    public void A_copy_of_the_file_is_kept_once_and_not_written_over()
    {
        var installation = new TestInstallation();
        var store = installation.SettingsStore;
        store.Load();
        store.Settings.FieldOfView = 50;
        store.Save();
        string copy = store.SavePath + ".before-standard";

        store.KeepCopy(".before-standard");
        string first = installation.Files.ReadAllText(copy);
        Assert.Equal(installation.Files.ReadAllText(store.SavePath), first);

        store.Settings.FieldOfView = 60;
        store.Save();
        store.KeepCopy(".before-standard");
        Assert.Equal(first, installation.Files.ReadAllText(copy));
    }

    [Fact]
    public void Ultra_is_the_top_of_what_the_old_starter_offered()
    {
        var ultra = GraphicsPreset.Ultra;

        Assert.Equal(8192, ultra.TextureSize);
        Assert.Equal(GraphicsRules.MaxCabTextureSize, ultra.CabTextureSize);
        Assert.Equal(8192, ultra.ShadowMap);
        Assert.Equal(GraphicsRules.MaxShadowRange, ultra.ShadowRange);
        Assert.Equal(Steps.Anisotropy.Max(), ultra.Anisotropy);
        Assert.Equal(AntiAliasingMode.Msaa8, ultra.AntiAliasing);
        Assert.Equal(SliderRanges.ReflectionUpdates.Max(), ultra.ReflectionsRate);
        Assert.Equal(4, ultra.SmokeFidelity);
        Assert.Equal(4, ultra.SplineFidelity);
        Assert.Equal(SliderRanges.DrawRangeFactors.Max(), ultra.DrawRangeFactor);
        Assert.Equal(100, ultra.CabShadowsRange);

        var extras = ultra.Extras!;
        Assert.Equal(200, extras.PythonUpdateMs);
        Assert.True(extras.MotionBlur && extras.ChromaticAberration && extras.Smoke);

        Assert.True(ultra.Top!.Screens);
    }

    [Fact]
    public void No_level_goes_past_what_the_old_starter_gave()
    {
        foreach (var preset in GraphicsPreset.All)
        {
            Assert.True(preset.ShadowRange <= GraphicsRules.MaxShadowRange, preset.NameKey);
            Assert.True(preset.CabTextureSize <= GraphicsRules.MaxCabTextureSize, preset.NameKey);
        }
    }

    [Fact]
    public void No_level_sets_what_only_the_advanced_mode_shows()
    {
        var set = GraphicsPreset.Keys.Concat(GraphicsExtras.Keys).Concat(GraphicsTop.Keys).ToList();

        // The rows of keys by name, the buffer and the resources are all the advanced mode's.
        Assert.Empty(set.Intersect(SimulatorKeys.All.Select(key => key.Key)));
        Assert.Empty(set.Intersect(new[] { "starter.bufferscale", "gfx.framebuffer.width", "gfx.framebuffer.height",
                                           "compresstex", "python.mipmaps", "gfx.skippipeline" }));
    }

    [Fact]
    public void A_level_leaves_what_only_the_advanced_mode_shows_as_it_is()
    {
        foreach (var preset in GraphicsPreset.All)
        {
            var settings = Defaults().Copy();
            settings.BufferScalePercent = 80;
            settings.CompressTextures = false;
            settings.ScreenMipmaps = false;
            settings.Extra["basedrawrange"] = "3000";
            settings.Extra["targetfps"] = "60";

            preset.ApplyTo(settings);

            Assert.Equal(80, settings.BufferScalePercent);
            Assert.False(settings.CompressTextures);
            Assert.False(settings.ScreenMipmaps);
            Assert.Equal("3000", settings.Extra["basedrawrange"]);
            Assert.Equal("60", settings.Extra["targetfps"]);
            Assert.Same(preset, GraphicsPreset.Matching(settings));
        }
    }

    [Fact]
    public void At_ultra_the_shadows_are_longest()
    {
        // The bottom of that scale.
        Assert.Equal(0.2, GraphicsPreset.Ultra.Top!.ShadowAngleLimit);
    }

    [Fact]
    public void Ultra_undoes_what_was_moved_earlier()
    {
        var settings = Defaults().Copy();
        settings.ShadowAngleLimit = 0.8;
        settings.IgnoreIrrelevantTrains = true;
        settings.PythonScreens = false;

        GraphicsPreset.Ultra.ApplyTo(settings);

        Assert.Same(GraphicsPreset.Ultra, GraphicsPreset.Matching(settings));
        Assert.Equal(0.2, settings.ShadowAngleLimit);
        Assert.True(settings.PythonScreens);
        Assert.True(settings.IgnoreIrrelevantTrains);
    }

    [Fact]
    public void Ultra_leaves_the_screens_in_their_own_windows_to_the_player()
    {
        var settings = Defaults().Copy();
        settings.Extra["python.displaywindows"] = "yes";

        GraphicsPreset.Ultra.ApplyTo(settings);

        Assert.Equal("yes", settings.Extra["python.displaywindows"]);
        Assert.Same(GraphicsPreset.Ultra, GraphicsPreset.Matching(settings));
    }

    [Fact]
    public void The_other_levels_leave_what_only_ultra_sets()
    {
        foreach (var preset in GraphicsPreset.All.Where(preset => preset != GraphicsPreset.Ultra))
        {
            var settings = Defaults().Copy();
            settings.IgnoreIrrelevantTrains = true;
            settings.ShadowAngleLimit = 0.8;

            preset.ApplyTo(settings);

            Assert.True(settings.IgnoreIrrelevantTrains, preset.NameKey);
            Assert.Equal(0.8, settings.ShadowAngleLimit);
            Assert.Same(preset, GraphicsPreset.Matching(settings));
        }
    }

    [Fact]
    public void Ultra_is_not_recognised_with_one_of_its_values_moved()
    {
        var settings = Defaults().Copy();
        GraphicsPreset.Ultra.ApplyTo(settings);
        Assert.Same(GraphicsPreset.Ultra, GraphicsPreset.Matching(settings));

        settings.ReflectionsFramerate = 25;

        Assert.Null(GraphicsPreset.Matching(settings));
    }

    [Fact]
    public void Shadows_reach_further_with_every_level_that_has_them()
    {
        static double Reach(GraphicsPreset preset) =>
            SimulatorSettings.ShadowReach(preset.ShadowRange, preset.ShadowMap, legacyRenderer: false);

        var reaches = GraphicsPreset.All.Where(preset => preset.Shadows).Select(Reach).ToList();

        Assert.Equal(reaches.Distinct().OrderBy(reach => reach), reaches);
        Assert.Equal(250, Reach(GraphicsPreset.High));
        Assert.Equal(800, Reach(GraphicsPreset.VeryHigh));
    }

    [Fact]
    public void Without_extras_the_settings_are_what_the_simulator_goes_by()
    {
        Assert.Equal(GraphicsExtras.None, GraphicsExtras.Of(Defaults()));
        Assert.Equal(GraphicsTop.None, GraphicsTop.Of(Defaults()));
    }

    [Fact]
    public void A_level_leaves_what_only_the_lowest_one_sets()
    {
        var settings = Defaults().Copy();
        settings.MotionBlur = false;

        GraphicsPreset.Low.ApplyTo(settings);

        Assert.False(settings.MotionBlur);
        Assert.Same(GraphicsPreset.Low, GraphicsPreset.Matching(settings));
    }

    [Fact]
    public void Every_level_is_within_what_the_sliders_offer()
    {
        foreach (var preset in GraphicsPreset.All)
        {
            Assert.True(preset.TextureSize >= SliderRanges.MinTextureSize, preset.NameKey);
            Assert.True(preset.ShadowMap >= SliderRanges.MinShadowMap, preset.NameKey);
            Assert.True(preset.DynamicLights >= SliderRanges.MinDynamicLights, preset.NameKey);
            Assert.Contains(preset.ReflectionsRate, SliderRanges.ReflectionUpdates);
        }
    }

    [Fact]
    public void The_lowest_level_is_not_recognised_with_one_of_its_extras_undone()
    {
        var settings = Defaults().Copy();
        GraphicsPreset.Minimal.ApplyTo(settings);

        settings.Smoke = true;

        Assert.Null(GraphicsPreset.Matching(settings));
    }

    [Fact]
    public void The_extras_sit_on_the_steps_the_screen_offers()
    {
        foreach (var extras in new[] { GraphicsPreset.Minimal.Extras!, GraphicsPreset.Ultra.Extras! })
            Assert.Contains(extras.PythonUpdateMs, new[] { 200, 400, 800, 1600 });
    }

    // ── Leaving an end of the scale ──────────────────────────────────────────

    [Fact]
    public void A_level_between_the_ends_chooses_only_its_own_keys()
    {
        var settings = Defaults().Copy();
        settings.MotionBlur = false;
        settings.ShadowAngleLimit = 0.8;

        var applied = new GraphicsPresetMemory().Apply(GraphicsPreset.Medium, settings);

        Assert.Same(GraphicsPreset.Medium, GraphicsPreset.Matching(settings));
        Assert.False(settings.MotionBlur);
        Assert.Equal(0.8, settings.ShadowAngleLimit);
        Assert.Equal(GraphicsPreset.Keys, applied.ChosenKeys);
    }

    [Fact]
    public void Leaving_the_lowest_level_brings_back_what_was_there_before_it()
    {
        var memory = new GraphicsPresetMemory();
        var settings = Defaults().Copy();
        settings.MotionBlur = false;
        var before = GraphicsExtras.Of(settings);

        var entering = memory.Apply(GraphicsPreset.Minimal, settings);
        Assert.Equal(GraphicsPreset.Minimal.Extras, GraphicsExtras.Of(settings));
        Assert.Equal(GraphicsPreset.Keys.Concat(GraphicsExtras.Keys), entering.ChosenKeys);

        var leaving = memory.Apply(GraphicsPreset.Medium, settings);

        Assert.Equal(before, GraphicsExtras.Of(settings));
        Assert.Same(GraphicsPreset.Medium, GraphicsPreset.Matching(settings));
        Assert.Equal(GraphicsPreset.Keys.Concat(GraphicsExtras.Keys), leaving.ChosenKeys);
    }

    [Fact]
    public void Leaving_ultra_brings_back_what_was_there_before_it()
    {
        var memory = new GraphicsPresetMemory();
        var settings = Defaults().Copy();
        settings.MotionBlur = false;
        settings.ShadowAngleLimit = 0.8;
        var extras = GraphicsExtras.Of(settings);
        var top = GraphicsTop.Of(settings);

        memory.Apply(GraphicsPreset.Ultra, settings);
        var applied = memory.Apply(GraphicsPreset.VeryHigh, settings);

        Assert.Equal(extras, GraphicsExtras.Of(settings));
        Assert.Equal(top, GraphicsTop.Of(settings));
        Assert.Same(GraphicsPreset.VeryHigh, GraphicsPreset.Matching(settings));
        Assert.Equal(GraphicsPreset.Keys.Concat(GraphicsExtras.Keys).Concat(GraphicsTop.Keys), applied.ChosenKeys);
    }

    [Fact]
    public void Leaving_an_end_chosen_before_this_run_gives_the_simulators_own_values()
    {
        var settings = Defaults().Copy();
        GraphicsPreset.Ultra.ApplyTo(settings);

        new GraphicsPresetMemory().Apply(GraphicsPreset.Low, settings);

        Assert.Equal(GraphicsExtras.None, GraphicsExtras.Of(settings));
        Assert.Equal(GraphicsTop.None, GraphicsTop.Of(settings));
        Assert.Same(GraphicsPreset.Low, GraphicsPreset.Matching(settings));
    }

    [Fact]
    public void One_end_to_the_other_keeps_what_was_there_before_either()
    {
        var memory = new GraphicsPresetMemory();
        var settings = Defaults().Copy();
        settings.MotionBlur = false;
        settings.ShadowAngleLimit = 0.8;
        var extras = GraphicsExtras.Of(settings);
        var top = GraphicsTop.Of(settings);

        memory.Apply(GraphicsPreset.Minimal, settings);
        memory.Apply(GraphicsPreset.Ultra, settings);
        Assert.Same(GraphicsPreset.Ultra, GraphicsPreset.Matching(settings));

        memory.Apply(GraphicsPreset.Minimal, settings);
        Assert.Same(GraphicsPreset.Minimal, GraphicsPreset.Matching(settings));
        // Ultra's own values go, Minimal has no say over them.
        Assert.Equal(top, GraphicsTop.Of(settings));

        memory.Apply(GraphicsPreset.Medium, settings);
        Assert.Equal(extras, GraphicsExtras.Of(settings));
    }

    [Fact]
    public void What_was_remembered_comes_back_only_once()
    {
        var memory = new GraphicsPresetMemory();
        var settings = Defaults().Copy();
        settings.MotionBlur = false;

        memory.Apply(GraphicsPreset.Minimal, settings);
        memory.Apply(GraphicsPreset.Medium, settings);
        Assert.False(settings.MotionBlur);

        // Moved since, and no end left behind this time: it stays as moved.
        settings.MotionBlur = true;
        var applied = memory.Apply(GraphicsPreset.High, settings);

        Assert.True(settings.MotionBlur);
        Assert.Equal(GraphicsPreset.Keys, applied.ChosenKeys);
    }

    [Fact]
    public void A_level_applied_twice_at_an_end_remembers_what_came_before_the_first()
    {
        var memory = new GraphicsPresetMemory();
        var settings = Defaults().Copy();
        settings.MotionBlur = false;
        var before = GraphicsExtras.Of(settings);

        memory.Apply(GraphicsPreset.Minimal, settings);
        memory.Apply(GraphicsPreset.Minimal, settings);
        memory.Apply(GraphicsPreset.Low, settings);

        Assert.Equal(before, GraphicsExtras.Of(settings));
    }

    [Fact]
    public void A_level_applied_is_recognised_until_one_value_moves()
    {
        foreach (var preset in GraphicsPreset.All)
        {
            var settings = Defaults().Copy();
            preset.ApplyTo(settings);
            Assert.Same(preset, GraphicsPreset.Matching(settings));

            settings.DynamicLights = 6;
            Assert.Null(GraphicsPreset.Matching(settings));
        }
    }

    [Fact]
    public void Every_level_sits_on_the_steps_the_screen_offers()
    {
        foreach (var preset in GraphicsPreset.All)
        {
            Assert.True(Steps.IndexOf(Steps.Anisotropy, preset.Anisotropy) >= 0, preset.NameKey);
            Assert.True(Steps.IndexOf(SliderRanges.ReflectionUpdates, preset.ReflectionsRate) >= 0, preset.NameKey);
            Assert.True(Steps.IndexOf(SliderRanges.DrawRangeFactors, preset.DrawRangeFactor) >= 0, preset.NameKey);
            foreach (int size in new[] { preset.TextureSize, preset.CabTextureSize, preset.ShadowMap })
                Assert.True(size is >= 512 and <= 8192 && (size & (size - 1)) == 0, preset.NameKey);
            Assert.True(preset.ShadowRange >= SimulatorSettings.ShadowRangeFloor(preset.ShadowMap), preset.NameKey);
            Assert.True(Steps.IndexOf(SliderRanges.CabShadowRanges, preset.CabShadowsRange) >= 0, preset.NameKey);
        }
    }

    // ── A level for one part ─────────────────────────────────────────────────

    private static GraphicsGroupLevels Groups() => new(new TestInstallation().SettingsStore);

    [Fact]
    public void Every_key_a_level_sets_but_those_of_the_whole_belongs_to_one_part()
    {
        var inGroups = GraphicsGroup.All.SelectMany(group => group.Keys).ToList();

        Assert.Equal(inGroups.Distinct(), inGroups);
        Assert.Empty(inGroups.Except(GraphicsPreset.Keys).Except(GraphicsExtras.Keys).Except(GraphicsTop.Keys));
        Assert.Equal(new[] { "dynamiclights", "gfx.extraeffects", "gfx.smoke.fidelity", "multisampling" },
                     GraphicsPreset.Keys.Except(inGroups).OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    public void One_part_moves_alone()
    {
        var groups = Groups();
        var settings = AtHigh();

        var moved = groups.Apply(GraphicsGroup.Shadows, GraphicsPreset.Low, settings);

        Assert.Equal(GraphicsPreset.Low.ShadowMap, moved.ShadowMapResolution);
        Assert.Equal(GraphicsPreset.Low.ShadowRange, moved.ShadowProjectionRange);
        Assert.Equal(GraphicsPreset.Low.ShadowRank, moved.ShadowRankCutoff);
        Assert.Equal(settings.MaxTextureSize, moved.MaxTextureSize);
        Assert.Equal(settings.DynamicLights, moved.DynamicLights);
        Assert.Equal(settings.BufferScalePercent, moved.BufferScalePercent);

        var levels = groups.LevelsOf(moved);
        Assert.Same(GraphicsPreset.Low, levels[GraphicsGroup.Shadows]);
        Assert.Same(GraphicsPreset.High, levels[GraphicsGroup.Textures]);
        Assert.Null(GraphicsPreset.Matching(moved));
    }

    [Fact]
    public void Every_part_shows_ultra_when_the_whole_is_at_ultra()
    {
        var settings = Defaults().Copy();
        GraphicsPreset.Ultra.ApplyTo(settings);

        Assert.All(Groups().LevelsOf(settings).Values, level => Assert.Same(GraphicsPreset.Ultra, level));
    }

    [Fact]
    public void A_part_moved_by_hand_is_at_no_level_and_the_others_keep_theirs()
    {
        var settings = AtHigh();
        settings.ReflectionsFidelity = 0;

        var levels = Groups().LevelsOf(settings);

        Assert.Null(levels[GraphicsGroup.Reflections]);
        Assert.Same(GraphicsPreset.High, levels[GraphicsGroup.Shadows]);
    }

    [Fact]
    public void Every_part_at_a_level_and_the_rest_with_it_is_that_level()
    {
        var groups = Groups();
        foreach (var level in GraphicsPreset.All.Where(level => level.Extras is null))
        {
            var settings = Defaults().Copy();
            foreach (var group in GraphicsGroup.All)
                settings = groups.Apply(group, level, settings);
            settings.AntiAliasing = level.AntiAliasing;
            settings.DynamicLights = level.DynamicLights;
            settings.SmokeFidelity = level.SmokeFidelity;

            Assert.Same(level, GraphicsPreset.Matching(settings));
        }
    }

    [Fact]
    public void Every_part_tells_every_level_from_the_others()
    {
        var groups = Groups();
        foreach (var group in GraphicsGroup.All)
            foreach (var level in GraphicsPreset.All)
                Assert.Same(level, groups.LevelsOf(groups.Apply(group, level, Defaults()))[group]);
    }

    [Fact]
    public void Leaving_an_end_in_a_part_gives_the_simulators_own_for_what_only_the_end_sets()
    {
        var groups = Groups();
        var settings = AtHigh();
        settings.ShadowAngleLimit = 0.8;

        var ultra = groups.Apply(GraphicsGroup.Shadows, GraphicsPreset.Ultra, settings);
        Assert.Equal(0.2, ultra.ShadowAngleLimit);
        Assert.Same(GraphicsPreset.Ultra, groups.LevelsOf(ultra)[GraphicsGroup.Shadows]);

        var back = groups.Apply(GraphicsGroup.Shadows, GraphicsPreset.High, ultra);
        Assert.Equal(GraphicsTop.None.ShadowAngleLimit, back.ShadowAngleLimit);
        Assert.Same(GraphicsPreset.High, groups.LevelsOf(back)[GraphicsGroup.Shadows]);
    }

    [Fact]
    public void A_level_in_a_part_between_the_ends_leaves_what_only_an_end_sets()
    {
        var settings = AtHigh();
        settings.MotionBlur = false;

        settings.Smoke = false;

        var shadows = Groups().Apply(GraphicsGroup.Shadows, GraphicsPreset.VeryHigh, settings);
        Assert.False(shadows.Smoke);
        Assert.False(shadows.MotionBlur);
        Assert.Equal(GraphicsPreset.VeryHigh.ShadowMap, shadows.ShadowMapResolution);
    }
}
