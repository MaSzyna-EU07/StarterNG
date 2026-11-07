using StarterNG.Application;
using StarterNG.Domain.Sceneries;
using StarterNG.Infrastructure.Adapters;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class SceneryFileWatchTests
{
    private static readonly string Scn = TestInstallation.At("scenery", "kato.scn");
    private static readonly string Other = TestInstallation.At("scenery", "lk4.scn");
    private static readonly string Inc = TestInstallation.At("scenery", "auta.inc");

    private const string Text =
        "//$n Katowice\n" +
        "FirstInit\n" +
        "trainset a tor 0 0\n" +
        "node -1 0 v1 dynamic d s m 0 headdriver 3 0 enddynamic\n" +
        "endtrainset\n" +
        "trainset b tor 0 0\n" +
        "node -1 0 v2 dynamic d s m 0 headdriver 3 0 enddynamic\n" +
        "endtrainset\n";

    private static (TestInstallation Installation, SceneryFileWatch Watch, AppState State) Rig(InMemoryFileSystem files)
    {
        var installation = new TestInstallation(files);
        installation.Library.ReloadSceneries();
        var state = new AppState();
        return (installation, new SceneryFileWatch(files, installation.Sceneries, installation.Library, state), state);
    }

    private static InMemoryFileSystem Folder() => new InMemoryFileSystem().WithFile(Scn, Text);

    [Fact]
    public void Files_as_they_were_read_are_no_change()
    {
        var (installation, watch, _) = Rig(Folder());

        Assert.Empty(watch.Look(installation.Library.Sceneries));
    }

    [Fact]
    public void A_file_changed_outside_is_read_anew()
    {
        var files = Folder();
        var (installation, watch, _) = Rig(files);
        var current = installation.Library.Sceneries.Single();

        files.WriteAllText(Scn, Text.Replace("Katowice", "Katowice Główne"));

        var change = Assert.Single(watch.Look(installation.Library.Sceneries));
        Assert.Same(current, change.Current);
        Assert.Equal("Katowice Główne", change.Fresh!.Name);
    }

    [Fact]
    public void An_include_that_changed_or_came_makes_the_scenery_read_anew()
    {
        var files = new InMemoryFileSystem().WithFile(Scn, Text + "include auta.inc end\n");
        var (installation, watch, _) = Rig(files);
        Assert.Empty(watch.Look(installation.Library.Sceneries));

        files.WriteAllText(Inc, "// auta\n");
        Assert.Single(watch.Look(installation.Library.Sceneries));

        installation.Library.ReloadSceneries();
        Assert.Empty(watch.Look(installation.Library.Sceneries));

        files.WriteAllText(Inc, "// inne auta\n");
        Assert.Single(watch.Look(installation.Library.Sceneries));
    }

    [Fact]
    public void A_new_file_in_the_folder_comes_as_an_added_scenery()
    {
        var files = Folder();
        var (installation, watch, _) = Rig(files);

        files.WriteAllText(Other, "//$n Lublin\n");

        var change = Assert.Single(watch.Look(installation.Library.Sceneries));
        Assert.Null(change.Current);
        Assert.Equal("Lublin", change.Fresh!.Name);
    }

    [Fact]
    public void A_file_is_gone_only_if_still_away_on_the_next_look()
    {
        var files = Folder();
        var (installation, watch, _) = Rig(files);
        var current = installation.Library.Sceneries.Single();

        files.DeleteFile(Scn);
        Assert.Empty(watch.Look(installation.Library.Sceneries));

        var change = Assert.Single(watch.Look(installation.Library.Sceneries));
        Assert.Same(current, change.Current);
        Assert.Null(change.Fresh);
    }

    [Fact]
    public void A_file_replaced_between_two_looks_is_not_gone()
    {
        var files = Folder();
        var (installation, watch, _) = Rig(files);

        files.DeleteFile(Scn);
        Assert.Empty(watch.Look(installation.Library.Sceneries));

        files.WriteAllText(Scn, Text);
        Assert.Empty(watch.Look(installation.Library.Sceneries));
    }

    [Fact]
    public void A_file_that_will_not_load_is_tried_and_logged_once_until_it_can_be()
    {
        var files = Folder();
        var (installation, watch, _) = Rig(files);
        files.WriteAllText(Other, "//$n Lublin\n");
        files.FailReading(Other);

        Assert.Empty(watch.Look(installation.Library.Sceneries));
        string once = installation.Log.Text;
        Assert.Contains("lk4.scn", once);

        Assert.Empty(watch.Look(installation.Library.Sceneries));
        Assert.Equal(once, installation.Log.Text);

        files.AllowReading(Other);
        Assert.Equal("Lublin", Assert.Single(watch.Look(installation.Library.Sceneries)).Fresh!.Name);
    }

    [Fact]
    public void Its_own_save_is_no_change_from_outside()
    {
        var files = Folder();
        var (installation, watch, _) = Rig(files);
        var scenery = installation.Library.Sceneries.Single();
        var trainset = scenery.Trainsets[0];
        trainset.ReadyToGo = true;

        var log = new FileDiagnosticsLog(files, installation.Clock, installation.Paths);
        Assert.Equal(SceneryTrainsetSaveOutcome.Saved, new SaveSceneryTrainset(files, log).Execute(scenery, trainset).Outcome);

        Assert.False(watch.IsStale(scenery));
        Assert.Empty(watch.Look(installation.Library.Sceneries));
    }

    [Fact]
    public void A_consist_whose_text_stands_goes_over_with_its_edits()
    {
        var files = Folder();
        var (installation, watch, _) = Rig(files);
        var current = installation.Library.Sceneries.Single();
        var edited = current.Trainsets[1];
        edited.ReadyToGo = true;

        files.WriteAllText(Scn, Text.Replace("Katowice", "Katowice Główne"));
        var fresh = watch.Look(installation.Library.Sceneries).Single().Fresh!;

        Assert.Empty(SceneryFileWatch.CarryOver(current, fresh));
        Assert.Same(edited, fresh.Trainsets[1]);
        Assert.True(SceneryTrainsetText.IsModified(fresh.Trainsets[1]));
    }

    [Fact]
    public void An_edited_consist_whose_text_changed_too_is_the_conflict()
    {
        var files = Folder();
        var (installation, watch, _) = Rig(files);
        var current = installation.Library.Sceneries.Single();
        var edited = current.Trainsets[1];
        edited.ReadyToGo = true;

        files.WriteAllText(Scn, Text.Replace(" v2 ", " v22 "));
        var fresh = watch.Look(installation.Library.Sceneries).Single().Fresh!;

        Assert.Same(edited, Assert.Single(SceneryFileWatch.CarryOver(current, fresh)));
        Assert.NotSame(edited, fresh.Trainsets[1]);
    }

    [Fact]
    public void A_consist_changed_but_not_edited_just_gives_way()
    {
        var files = Folder();
        var (installation, watch, _) = Rig(files);
        var current = installation.Library.Sceneries.Single();

        files.WriteAllText(Scn, Text.Replace(" v2 ", " v22 "));
        var fresh = watch.Look(installation.Library.Sceneries).Single().Fresh!;

        Assert.Empty(SceneryFileWatch.CarryOver(current, fresh));
        Assert.Equal("v22", fresh.Trainsets[1].Vehicles.Single().Name);
    }

    [Fact]
    public void The_weather_chosen_by_hand_stays_and_the_authored_one_follows_the_file()
    {
        var files = Folder();
        var (installation, watch, _) = Rig(files);
        var current = installation.Library.Sceneries.Single();
        current.Weather.Day = 123;

        files.WriteAllText(Scn, Text.Replace("Katowice", "Katowice Główne"));
        var fresh = watch.Look(installation.Library.Sceneries).Single().Fresh!;
        SceneryFileWatch.CarryOver(current, fresh);

        Assert.Equal(123, fresh.Weather.Day);
    }

    [Fact]
    public void What_is_picked_follows_into_the_scenery_read_anew()
    {
        var files = Folder();
        var (installation, watch, state) = Rig(files);
        var current = installation.Library.Sceneries.Single();
        var picked = current.Trainsets[1];
        state.CurrentScenery = current;
        state.CurrentTrainset = picked;
        state.StartingVehicleName = "v2";

        files.WriteAllText(Scn, Text.Replace("Katowice", "Katowice Główne"));
        var change = watch.Look(installation.Library.Sceneries).Single();
        SceneryFileWatch.CarryOver(change.Current!, change.Fresh!);
        watch.Apply(change);

        Assert.Same(change.Fresh, installation.Library.Sceneries.Single());
        Assert.Same(change.Fresh, state.CurrentScenery);
        Assert.Same(picked, state.CurrentTrainset);
        Assert.Equal("v2", state.StartingVehicleName);
    }

    [Fact]
    public void A_picked_consist_changed_in_the_file_is_followed_by_its_name()
    {
        var files = Folder();
        var (installation, watch, state) = Rig(files);
        var current = installation.Library.Sceneries.Single();
        var picked = current.Trainsets[1];
        state.CurrentScenery = current;
        state.CurrentTrainset = picked;
        state.StartingVehicleName = "v2";

        files.WriteAllText(Scn, Text.Replace(" v2 ", " v22 "));
        var change = watch.Look(installation.Library.Sceneries).Single();
        SceneryFileWatch.CarryOver(change.Current!, change.Fresh!);
        watch.Apply(change);

        Assert.Same(change.Fresh!.Trainsets[1], state.CurrentTrainset);
        Assert.Null(state.StartingVehicleName);
    }

    [Fact]
    public void A_change_to_a_scenery_reloaded_since_the_look_is_left()
    {
        var files = Folder();
        var (installation, watch, _) = Rig(files);

        files.WriteAllText(Scn, Text.Replace("Katowice", "Katowice Główne"));
        var change = watch.Look(installation.Library.Sceneries).Single();
        installation.Library.ReloadSceneries();

        Assert.Equal(SceneryVerdict.Leave, watch.Judge(change).Verdict);
    }

    [Fact]
    public void A_change_to_a_scenery_saved_from_here_since_the_look_is_left_and_nothing_carried_over()
    {
        var files = Folder();
        var (installation, watch, _) = Rig(files);
        var current = installation.Library.Sceneries.Single();

        files.WriteAllText(Scn, Text.Replace("Katowice", "Katowice Główne"));
        var change = watch.Look(installation.Library.Sceneries).Single();
        current.Sources = change.Fresh!.Sources;

        Assert.Equal(SceneryVerdict.Leave, watch.Judge(change).Verdict);
        Assert.NotSame(current.Trainsets[0], change.Fresh.Trainsets[0]);
    }

    [Fact]
    public void A_scenery_new_in_the_folder_is_taken()
    {
        var files = Folder();
        var (installation, watch, _) = Rig(files);

        files.WriteAllText(Other, "//$n Lublin\n");
        var change = watch.Look(installation.Library.Sceneries).Single();

        Assert.Equal(SceneryVerdict.Take, watch.Judge(change).Verdict);
    }

    [Fact]
    public void A_scenery_new_in_the_folder_that_got_in_since_the_look_is_left()
    {
        var (installation, watch, _) = Rig(Folder());

        var change = new SceneryChange(null, installation.Library.Sceneries.Single());

        Assert.Equal(SceneryVerdict.Leave, watch.Judge(change).Verdict);
    }

    [Fact]
    public void A_change_that_loses_no_edits_is_taken_with_the_edits_carried_over()
    {
        var files = Folder();
        var (installation, watch, _) = Rig(files);
        var edited = installation.Library.Sceneries.Single().Trainsets[1];
        edited.ReadyToGo = true;

        files.WriteAllText(Scn, Text.Replace("Katowice", "Katowice Główne"));
        var change = watch.Look(installation.Library.Sceneries).Single();

        var judgement = watch.Judge(change);

        Assert.Equal(SceneryVerdict.Take, judgement.Verdict);
        Assert.Empty(judgement.Lost);
        Assert.Same(edited, change.Fresh!.Trainsets[1]);
    }

    [Fact]
    public void A_change_that_loses_edits_is_asked_about_naming_them()
    {
        var files = Folder();
        var (installation, watch, _) = Rig(files);
        var current = installation.Library.Sceneries.Single();
        var edited = current.Trainsets[1];
        edited.ReadyToGo = true;

        files.WriteAllText(Scn, Text.Replace(" v2 ", " v22 "));
        var change = watch.Look(installation.Library.Sceneries).Single();

        var judgement = watch.Judge(change);

        Assert.Equal(SceneryVerdict.AskFirst, judgement.Verdict);
        Assert.Same(edited, Assert.Single(judgement.Lost));
        Assert.Same(current.Trainsets[0], change.Fresh!.Trainsets[0]);
    }

    [Fact]
    public void A_scenery_gone_with_edits_in_it_stays_for_them_to_be_saved_elsewhere()
    {
        var (installation, watch, _) = Rig(Folder());
        var current = installation.Library.Sceneries.Single();
        current.Trainsets[1].ReadyToGo = true;

        Assert.Equal(SceneryVerdict.Leave, watch.Judge(new SceneryChange(current, null)).Verdict);
    }

    [Fact]
    public void A_scenery_gone_with_no_edits_in_it_is_taken_off()
    {
        var (installation, watch, _) = Rig(Folder());
        var current = installation.Library.Sceneries.Single();

        Assert.Equal(SceneryVerdict.Take, watch.Judge(new SceneryChange(current, null)).Verdict);
    }

    [Fact]
    public void A_scenery_gone_takes_what_was_picked_with_it()
    {
        var files = Folder();
        var (installation, watch, state) = Rig(files);
        var current = installation.Library.Sceneries.Single();
        state.CurrentScenery = current;
        state.CurrentTrainset = current.Trainsets[0];
        state.StartingVehicleName = "v1";

        watch.Apply(new SceneryChange(current, null));

        Assert.Empty(installation.Library.Sceneries);
        Assert.Null(state.CurrentScenery);
        Assert.Null(state.CurrentTrainset);
        Assert.Null(state.StartingVehicleName);
    }
}
