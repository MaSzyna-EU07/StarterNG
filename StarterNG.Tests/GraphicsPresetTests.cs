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
            .Where(key => lowKeys.GetValueOrDefault(key) != topKeys.GetValueOrDefault(key))
            .Where(key => !key.StartsWith("gfx.framebuffer", StringComparison.Ordinal) && key != "gfx.buffer.percent");

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
        Climbs(p => p.CabTextureSize, 1);
        Climbs(p => p.Anisotropy, 1);

        // Four steps or three for six levels.
        Climbs(p => p.Multisampling, 2);
        Climbs(p => p.SmokeFidelity, 2);
        Climbs(p => p.SplineFidelity, 2);
        Climbs(p => p.ReflectionsFidelity, 3);
        Climbs(p => p.ShadowRank, 3);

        // The simulator takes no more than its own 7 lights, and draws no less than 2 km.
        Climbs(p => p.DynamicLights, 2);
        Climbs(p => p.DrawRange, 1);
        Climbs(p => p.DrawRangeFactor, 2);
        Climbs(p => p.CabShadowsRange, 1);

        Climbs(p => p.ShadowMap, 1);
        Climbs(p => p.ShadowRange, 0);
        Climbs(p => p.ReflectionsRate, 0);
        Climbs(p => p.DistanceFactor, 0);
        Climbs(p => p.BufferScale, 0);
        Climbs(p => p.SleeperDistance, 0);
        Climbs(p => p.ReflectedInstances, 0);
        Climbs(p => p.ReflectedVehicles, 0);
    }

    [Fact]
    public void No_step_up_a_scale_is_more_than_a_few_steps_of_its_slider()
    {
        // In steps of what the screen offers, so Ultra does not jump to the top from far below.
        var rates = GraphicsPreset.All.Select(p => Steps.IndexOf(Steps.ReflectionUpdates, p.ReflectionsRate)).ToList();
        Assert.All(rates.Zip(rates.Skip(1)), step => Assert.InRange(step.Second - step.First, 1, 4));

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
            .Where(key => own.GetValueOrDefault(key) != high.GetValueOrDefault(key))
            .Where(key => !GraphicsPreset.RowKeys.Contains(key))
            .Where(key => !key.StartsWith("gfx.framebuffer", StringComparison.Ordinal) && key != "gfx.buffer.percent");

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
    }

    [Fact]
    public void High_is_what_the_new_simulator_does_with_nothing_set()
    {
        Assert.Equal(GraphicsPreset.High.NewRenderer, GraphicsNewRenderer.Of(Defaults()));
    }

    [Fact]
    public void The_new_renderers_scales_never_go_down()
    {
        void WordsClimb(string name, Func<GraphicsNewRenderer, string> word)
        {
            var words = SimulatorKeys.Named(name).Words!;
            var places = GraphicsPreset.All.Select(p => Array.IndexOf(words, word(p.NewRenderer))).ToList();
            Assert.DoesNotContain(-1, places);
            Assert.Equal(places.OrderBy(place => place), places);
        }

        void TurnsOn(Func<GraphicsNewRenderer, bool> on)
        {
            var values = GraphicsPreset.All.Select(p => on(p.NewRenderer)).ToList();
            Assert.Equal(values.OrderBy(value => value), values);
        }

        WordsClimb("gfx.lights.shadows", r => r.LampShadows);
        WordsClimb("gfx.contactshadows", r => r.ContactShadows);
        WordsClimb("gfx.postfx.ao", r => r.Occlusion);
        WordsClimb("gfx.postfx.ao.quality", r => r.OcclusionQuality);
        WordsClimb("gfx.cabprobe", r => r.CabProbe);
        WordsClimb("gfx.ssr", r => r.ScreenReflections);
        WordsClimb("gfx.fog", r => r.Fog);
        WordsClimb("gfx.fog.quality", r => r.FogQuality);
        WordsClimb("gfx.clouds", r => r.Clouds);

        TurnsOn(r => r.OcclusionLighting);
        TurnsOn(r => r.CabProbeReflections);
        TurnsOn(r => r.RayTracing);
        TurnsOn(r => r.CloudShadows);
        TurnsOn(r => r.Grass);
        TurnsOn(r => r.Bloom);

        Climbs(p => p.NewRenderer.GrassDistance, 0);
        Climbs(p => p.NewRenderer.GrassFade, 0);
        Climbs(p => p.NewRenderer.GrassDensity, 0);
        Climbs(p => p.NewRenderer.Precipitation, 0);
        // Frames between refreshes, so the dearer the fewer.
        Climbs(p => -p.NewRenderer.CabProbeInterval, 2);
    }

    [Fact]
    public void Ultra_is_the_top_of_the_new_renderers_scales()
    {
        var ultra = GraphicsPreset.Ultra.NewRenderer;
        double Top(string key) => SimulatorKeys.Named(key).Maximum;
        string TopWord(string key) => SimulatorKeys.Named(key).Words!.Last();

        Assert.Equal(TopWord("gfx.lights.shadows"), ultra.LampShadows);
        Assert.Equal(TopWord("gfx.contactshadows"), ultra.ContactShadows);
        Assert.Equal(TopWord("gfx.postfx.ao"), ultra.Occlusion);
        Assert.Equal(TopWord("gfx.postfx.ao.quality"), ultra.OcclusionQuality);
        Assert.Equal(TopWord("gfx.cabprobe"), ultra.CabProbe);
        Assert.Equal(TopWord("gfx.ssr"), ultra.ScreenReflections);
        Assert.Equal(TopWord("gfx.fog"), ultra.Fog);
        Assert.Equal(TopWord("gfx.fog.quality"), ultra.FogQuality);
        Assert.Equal(TopWord("gfx.clouds"), ultra.Clouds);
        Assert.Equal(Top("gfx.grass.distance"), ultra.GrassDistance);
        Assert.Equal(Top("gfx.grass.fadedistance"), ultra.GrassFade);
        Assert.Equal(Top("gfx.grass.density"), ultra.GrassDensity);
        Assert.Equal(Top("gfx.precipitation.particles"), ultra.Precipitation);
    }

    [Fact]
    public void Grass_fades_out_beyond_where_it_is_drawn_in_full()
    {
        Assert.All(GraphicsPreset.All, p => Assert.True(p.NewRenderer.GrassFade > p.NewRenderer.GrassDistance, p.NameKey));
    }

    [Fact]
    public void A_level_set_in_the_new_renderers_rows_is_told_from_the_others()
    {
        var settings = Defaults().Copy();
        GraphicsPreset.Medium.ApplyTo(settings);
        Assert.Same(GraphicsPreset.Medium, GraphicsPreset.Matching(settings));

        settings.Extra["gfx.fog.quality"] = "ultra";

        Assert.Null(GraphicsPreset.Matching(settings));
    }

    [Fact]
    public void What_is_left_to_the_player_stays_through_a_level()
    {
        var settings = Defaults().Copy();
        settings.Extra["gfx.antialiasing"] = "taa";
        settings.Extra["gfx.framegeneration"] = "x2";
        settings.Extra["gfx.vulkan.device"] = "RTX";

        foreach (var preset in GraphicsPreset.All)
        {
            preset.ApplyTo(settings);

            Assert.Same(preset, GraphicsPreset.Matching(settings));
            Assert.Equal("taa", settings.Extra["gfx.antialiasing"]);
            Assert.Equal("x2", settings.Extra["gfx.framegeneration"]);
            Assert.Equal("RTX", settings.Extra["gfx.vulkan.device"]);
        }
    }

    [Fact]
    public void Ultra_is_the_top_of_everything_the_screen_offers()
    {
        var ultra = GraphicsPreset.Ultra;
        double Top(string key) => SimulatorKeys.Named(key).Maximum;

        Assert.Equal(8192, ultra.TextureSize);
        Assert.Equal(8192, ultra.CabTextureSize);
        Assert.Equal(8192, ultra.ShadowMap);
        Assert.Equal(600, ultra.ShadowRange);
        Assert.Equal(Steps.Anisotropy.Max(), ultra.Anisotropy);
        Assert.Equal(Steps.MultisamplingLevels.Max(), ultra.Multisampling);
        Assert.Equal(Steps.ReflectionUpdates.Max(), ultra.ReflectionsRate);
        Assert.Equal(4, ultra.SmokeFidelity);
        Assert.Equal(4, ultra.SplineFidelity);
        Assert.Equal(200, ultra.BufferScale);
        Assert.Equal(Top(GraphicsPreset.DrawRangeKey), ultra.DrawRange);
        Assert.Equal(Top(GraphicsPreset.DistanceFactorKey), ultra.DistanceFactor);
        Assert.Equal(Top(GraphicsPreset.SleeperKey), ultra.SleeperDistance);
        Assert.Equal(Steps.DrawRangeFactors.Max(), ultra.DrawRangeFactor);
        Assert.Equal(Top(GraphicsPreset.ReflectedInstancesKey), ultra.ReflectedInstances);
        Assert.Equal(Top(GraphicsPreset.ReflectedVehiclesKey), ultra.ReflectedVehicles);
        Assert.Equal(100, ultra.CabShadowsRange);

        var extras = ultra.Extras!;
        Assert.Equal(200, extras.PythonUpdateMs);
        Assert.True(extras.MotionBlur && extras.ChromaticAberration && extras.Smoke);

        var top = ultra.Top!;
        Assert.Equal(Top(GraphicsTop.ScreenWindowFpsKey), top.ScreenWindowFps);
        Assert.True(top.Screens && top.ScreenMipmaps && top.Sky && top.Cab && top.AirCouplers && top.SmoothTraction);
    }

    [Fact]
    public void At_ultra_what_would_only_take_away_is_off()
    {
        var top = GraphicsPreset.Ultra.Top!;

        // The longest shadows, the bottom of that scale.
        Assert.Equal(0.2, top.ShadowAngleLimit);
        Assert.Equal(0, top.TargetFps);
        Assert.Equal(0, top.TextureRelease);
        Assert.False(top.CompressTextures);
        Assert.False(top.SkipDecor);
    }

    [Fact]
    public void Ultra_undoes_what_was_moved_earlier()
    {
        var settings = Defaults().Copy();
        settings.ShadowAngleLimit = 0.8;
        settings.IgnoreIrrelevantTrains = true;
        settings.Extra[GraphicsTop.TargetFpsKey] = "60";
        settings.Extra[GraphicsTop.TextureReleaseKey] = "2";

        GraphicsPreset.Ultra.ApplyTo(settings);

        Assert.Same(GraphicsPreset.Ultra, GraphicsPreset.Matching(settings));
        Assert.Equal(0.2, settings.ShadowAngleLimit);
        Assert.False(settings.IgnoreIrrelevantTrains);
        Assert.Equal("0", settings.Extra[GraphicsTop.TargetFpsKey]);
        Assert.Equal("0", settings.Extra[GraphicsTop.TextureReleaseKey]);
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
            settings.Extra[GraphicsTop.TargetFpsKey] = "60";

            preset.ApplyTo(settings);

            Assert.True(settings.IgnoreIrrelevantTrains, preset.NameKey);
            Assert.Equal("60", settings.Extra[GraphicsTop.TargetFpsKey]);
            Assert.Same(preset, GraphicsPreset.Matching(settings));
        }
    }

    [Fact]
    public void Ultra_is_not_recognised_with_one_of_its_values_moved()
    {
        var settings = Defaults().Copy();
        GraphicsPreset.Ultra.ApplyTo(settings);
        Assert.Same(GraphicsPreset.Ultra, GraphicsPreset.Matching(settings));

        settings.Extra[GraphicsPreset.ReflectedVehiclesKey] = "250";

        Assert.Null(GraphicsPreset.Matching(settings));
    }

    [Fact]
    public void No_level_asks_for_a_range_the_renderer_would_raise_anyway()
    {
        Assert.All(GraphicsPreset.All, preset => Assert.True(preset.DrawRange >= 2000, preset.NameKey));
    }

    [Fact]
    public void The_view_only_shortens_going_down_the_levels()
    {
        var factors = GraphicsPreset.All.Select(preset => preset.DistanceFactor).ToList();

        Assert.Equal(factors.OrderBy(factor => factor), factors);
        Assert.Equal(1, GraphicsPreset.High.DistanceFactor);
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
    public void Every_level_sets_the_buffer_and_is_not_recognised_with_another()
    {
        foreach (var preset in GraphicsPreset.All)
        {
            var settings = Defaults().Copy();
            settings.BufferScalePercent = 120;

            preset.ApplyTo(settings);
            Assert.Equal(preset.BufferScale, settings.BufferScalePercent);

            settings.BufferScalePercent = 120;
            Assert.Null(GraphicsPreset.Matching(settings));
        }
    }

    [Fact]
    public void The_buffer_is_the_screen_at_high_and_climbs_through_it()
    {
        Assert.Equal(100, GraphicsPreset.High.BufferScale);
        Assert.Equal(200, GraphicsPreset.Ultra.BufferScale);
        Assert.InRange(GraphicsPreset.Minimal.BufferScale, 50, 100);
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
        settings.Extra[GraphicsTop.TargetFpsKey] = "60";

        var applied = new GraphicsPresetMemory().Apply(GraphicsPreset.Medium, settings);

        Assert.Same(GraphicsPreset.Medium, GraphicsPreset.Matching(settings));
        Assert.False(settings.MotionBlur);
        Assert.Equal("60", settings.Extra[GraphicsTop.TargetFpsKey]);
        Assert.Equal(GraphicsPreset.RowKeys, applied.RowKeys);
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
        Assert.Equal(GraphicsPreset.RowKeys, entering.RowKeys);
        Assert.Equal(GraphicsPreset.Keys.Concat(GraphicsExtras.Keys), entering.ChosenKeys);

        var leaving = memory.Apply(GraphicsPreset.Medium, settings);

        Assert.Equal(before, GraphicsExtras.Of(settings));
        Assert.Same(GraphicsPreset.Medium, GraphicsPreset.Matching(settings));
        Assert.Equal(GraphicsPreset.RowKeys, leaving.RowKeys);
        Assert.Equal(GraphicsPreset.Keys.Concat(GraphicsExtras.Keys), leaving.ChosenKeys);
    }

    [Fact]
    public void Leaving_ultra_brings_back_what_was_there_before_it()
    {
        var memory = new GraphicsPresetMemory();
        var settings = Defaults().Copy();
        settings.MotionBlur = false;
        settings.ShadowAngleLimit = 0.8;
        settings.Extra[GraphicsTop.TargetFpsKey] = "60";
        var extras = GraphicsExtras.Of(settings);
        var top = GraphicsTop.Of(settings);

        memory.Apply(GraphicsPreset.Ultra, settings);
        var applied = memory.Apply(GraphicsPreset.VeryHigh, settings);

        Assert.Equal(extras, GraphicsExtras.Of(settings));
        Assert.Equal(top, GraphicsTop.Of(settings));
        Assert.Same(GraphicsPreset.VeryHigh, GraphicsPreset.Matching(settings));
        Assert.Equal(GraphicsPreset.RowKeys.Concat(GraphicsTop.RowKeys), applied.RowKeys);
        Assert.Equal(GraphicsPreset.Keys.Concat(GraphicsExtras.Keys).Concat(GraphicsTop.Keys), applied.ChosenKeys);
    }

    [Fact]
    public void Leaving_an_end_chosen_before_this_run_gives_the_simulators_own_values()
    {
        var settings = Defaults().Copy();
        GraphicsPreset.Ultra.ApplyTo(settings);

        var applied = new GraphicsPresetMemory().Apply(GraphicsPreset.Low, settings);

        Assert.Equal(GraphicsExtras.None, GraphicsExtras.Of(settings));
        Assert.Equal(GraphicsTop.None, GraphicsTop.Of(settings));
        Assert.Same(GraphicsPreset.Low, GraphicsPreset.Matching(settings));
        Assert.Equal(GraphicsPreset.RowKeys.Concat(GraphicsTop.RowKeys), applied.RowKeys);
    }

    [Fact]
    public void One_end_to_the_other_keeps_what_was_there_before_either()
    {
        var memory = new GraphicsPresetMemory();
        var settings = Defaults().Copy();
        settings.MotionBlur = false;
        var extras = GraphicsExtras.Of(settings);
        var top = GraphicsTop.Of(settings);

        memory.Apply(GraphicsPreset.Minimal, settings);
        var toUltra = memory.Apply(GraphicsPreset.Ultra, settings);
        Assert.Same(GraphicsPreset.Ultra, GraphicsPreset.Matching(settings));
        Assert.Equal(GraphicsPreset.RowKeys.Concat(GraphicsTop.RowKeys), toUltra.RowKeys);

        var toMinimal = memory.Apply(GraphicsPreset.Minimal, settings);
        Assert.Same(GraphicsPreset.Minimal, GraphicsPreset.Matching(settings));
        // Ultra's own values go, Minimal has no say over them.
        Assert.Equal(top, GraphicsTop.Of(settings));
        Assert.Equal(GraphicsPreset.RowKeys.Concat(GraphicsTop.RowKeys), toMinimal.RowKeys);

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
        Assert.Equal(GraphicsPreset.RowKeys, applied.RowKeys);
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
        void OnSteps(double value, string name)
        {
            var key = SimulatorKeys.Named(name);
            Assert.InRange(value, key.Minimum, key.Maximum);
            double steps = value / key.Step;
            Assert.Equal(Math.Round(steps), steps, 6);
        }

        foreach (var preset in GraphicsPreset.All)
        {
            OnSteps(preset.DistanceFactor, GraphicsPreset.DistanceFactorKey);
            OnSteps(preset.DrawRange, GraphicsPreset.DrawRangeKey);
            OnSteps(preset.SleeperDistance, GraphicsPreset.SleeperKey);
            OnSteps(preset.ReflectedInstances, GraphicsPreset.ReflectedInstancesKey);
            OnSteps(preset.ReflectedVehicles, GraphicsPreset.ReflectedVehiclesKey);
            OnSteps(preset.NewRenderer.CabProbeInterval, "gfx.cabprobe.interval");
            OnSteps(preset.NewRenderer.GrassDistance, "gfx.grass.distance");
            OnSteps(preset.NewRenderer.GrassFade, "gfx.grass.fadedistance");
            OnSteps(preset.NewRenderer.GrassDensity, "gfx.grass.density");
            OnSteps(preset.NewRenderer.Precipitation, "gfx.precipitation.particles");
            Assert.True(Steps.IndexOf(Steps.Anisotropy, preset.Anisotropy) >= 0, preset.NameKey);
            Assert.True(Steps.IndexOf(Steps.MultisamplingLevels, preset.Multisampling) >= 0, preset.NameKey);
            Assert.True(Steps.IndexOf(Steps.ReflectionUpdates, preset.ReflectionsRate) >= 0, preset.NameKey);
            Assert.True(Steps.IndexOf(Steps.DrawRangeFactors, preset.DrawRangeFactor) >= 0, preset.NameKey);
            foreach (int size in new[] { preset.TextureSize, preset.CabTextureSize, preset.ShadowMap })
                Assert.True(size is >= 512 and <= 8192 && (size & (size - 1)) == 0, preset.NameKey);
            Assert.True(preset.ShadowRange >= SimulatorSettings.ShadowRangeFloor(preset.ShadowMap), preset.NameKey);
            Assert.Equal(0, preset.CabShadowsRange % 10);
        }
    }

    // ── A level for one part ─────────────────────────────────────────────────

    private static GraphicsGroupLevels Groups() => new(new TestInstallation().SettingsStore);

    [Fact]
    public void Every_key_a_level_sets_but_the_buffer_belongs_to_one_part()
    {
        var inGroups = GraphicsGroup.All.SelectMany(group => group.Keys).ToList();

        Assert.Equal(inGroups.Distinct(), inGroups);
        Assert.Empty(inGroups.Except(GraphicsPreset.Keys).Except(GraphicsExtras.Keys).Except(GraphicsTop.Keys));
        Assert.Equal(new[] { "starter.bufferscale" }, GraphicsPreset.Keys.Except(inGroups));
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

        // The row of a key at the simulator's own value leaves it out of the file.
        settings.Extra.Remove(GraphicsTop.TextureReleaseKey);

        Assert.Same(GraphicsPreset.Ultra, Groups().LevelsOf(settings)[GraphicsGroup.Textures]);
    }

    [Fact]
    public void A_part_moved_by_hand_is_at_no_level_and_the_others_keep_theirs()
    {
        var settings = AtHigh();
        settings.DynamicLights = 6;

        var levels = Groups().LevelsOf(settings);

        Assert.Null(levels[GraphicsGroup.Effects]);
        Assert.Same(GraphicsPreset.High, levels[GraphicsGroup.Shadows]);
    }

    [Fact]
    public void Every_part_at_a_level_and_the_buffer_with_it_is_that_level()
    {
        var groups = Groups();
        foreach (var level in GraphicsPreset.All.Where(level => level.Extras is null))
        {
            var settings = Defaults().Copy();
            foreach (var group in GraphicsGroup.All)
                settings = groups.Apply(group, level, settings);
            settings.BufferScalePercent = level.BufferScale;

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

        var ultra = groups.Apply(GraphicsGroup.Textures, GraphicsPreset.Ultra, settings);
        Assert.False(ultra.CompressTextures);
        Assert.Same(GraphicsPreset.Ultra, groups.LevelsOf(ultra)[GraphicsGroup.Textures]);

        var back = groups.Apply(GraphicsGroup.Textures, GraphicsPreset.High, ultra);
        Assert.Equal(GraphicsTop.None.CompressTextures, back.CompressTextures);
        Assert.Same(GraphicsPreset.High, groups.LevelsOf(back)[GraphicsGroup.Textures]);
    }

    [Fact]
    public void A_level_in_a_part_between_the_ends_leaves_what_only_an_end_sets()
    {
        var settings = AtHigh();
        settings.MotionBlur = false;

        var moved = Groups().Apply(GraphicsGroup.Effects, GraphicsPreset.VeryHigh, settings);

        Assert.False(moved.MotionBlur);
        Assert.Equal(GraphicsPreset.VeryHigh.SmokeFidelity, moved.SmokeFidelity);
    }
}
