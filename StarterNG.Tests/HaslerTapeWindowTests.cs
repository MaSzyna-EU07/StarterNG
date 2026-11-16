using Avalonia.Controls;
using Avalonia.LogicalTree;
using StarterNG.Application;
using StarterNG.Domain.Tapes;
using StarterNG.Tests.Fakes;
using StarterNG.Views.Tapes;

namespace StarterNG.Tests;

/// <summary>The tapes' window over the tapes of an installation of its own, read as it is opened.</summary>
[Collection(AvaloniaControls.Name)]
public class HaslerTapeWindowTests
{
    private static readonly DateTime Launched = new(2025, 12, 31, 23, 0, 0, DateTimeKind.Utc);

    static HaslerTapeWindowTests() => HeadlessDrawing.Ensure();

    private static AppServices Services(InMemoryFileSystem files)
    {
        var installation = new TestInstallation(files);
        installation.Clock.Now = Launched;
        return AppServices.Initialize(installation.Paths, installation.Files, installation.Clock, installation.Environment);
    }

    // The window reads the tapes off the UI thread and shows them on it: what it shows is waited for.
    private static void Until(Func<bool> shown) =>
        Assert.True(SpinWait.SpinUntil(() =>
        {
            Controls.Settle();
            return shown();
        }, TimeSpan.FromSeconds(5)));

    private static Button Button(Window window, string key) =>
        window.GetLogicalDescendants().OfType<Button>().Single(b => b.Content as string == App.Loc[key]);

    [Fact]
    public void With_nothing_recorded_it_offers_to_record_and_turns_on_the_black_box_and_the_trains_tapes()
    {
        var services = Services(new InMemoryFileSystem());
        var window = new HaslerTapeWindow();
        bool told = false;
        window.LogEnabled += () => told = true;
        window.Show();
        try
        {
            var strip = (Control)Button(window, "HaslerRecordingEnable").Parent!;
            Until(() => strip.IsVisible);

            Button(window, "HaslerRecordingEnable").RaiseEvent(Controls.Click());

            Assert.True(services.Settings.BlackBox);
            Assert.True(services.Settings.PhysicsLogTrains);
            Assert.False(services.Settings.PhysicsLog);
            Assert.True(told);
            Assert.False(strip.IsVisible);
            // the simulator's own log is on as it comes: nothing more to offer
            Assert.False(((Control)Button(window, "HaslerLogEnable").Parent!).IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public void A_tape_of_the_last_launch_is_summed_up_with_its_scenario_and_train()
    {
        string path = TestInstallation.At("physicslog", "EP07-424.dat");
        var files = new InMemoryFileSystem()
            .WithFile(path, HaslerTapeTests.Header + HaslerTapeTests.Line(0, 36, 0, 0) + HaslerTapeTests.Line(60, 36, 0, 0));
        var services = Services(files);
        services.Tapes.RecordLaunch("td.scn", TimeSpan.Zero, Array.Empty<TapeVehicle>(), "EP07-424", new[] { "EP07-424" },
                                    "Tor doświadczalny", "EIE1531");
        var window = new HaslerTapeWindow();
        window.Show();
        try
        {
            string scenario = string.Format(App.Loc["HaslerScenario"], "Tor doświadczalny");
            var texts = window.GetLogicalDescendants().OfType<TextBlock>();
            Until(() => texts.Any(t => t.Text?.Contains(scenario) == true));
            string summary = texts.First(t => t.Text?.Contains(scenario) == true).Text!;

            Assert.StartsWith("EP07-424", summary);
            Assert.Contains(string.Format(App.Loc["HaslerTrain"], "EIE1531"), summary);
        }
        finally
        {
            window.Close();
        }
        // closed, the tape it read is no longer followed: begun anew and longer, it is read whole
        files.WithFile(path, HaslerTapeTests.Header + HaslerTapeTests.Line(0, 72, 0, 0) + HaslerTapeTests.Line(1, 72, 0, 0)
                             + HaslerTapeTests.Line(2, 72, 0, 0));
        var tape = services.Tapes.Load(services.Tapes.List().Single(), follow: true)!;
        Assert.All(tape.Samples, s => Assert.Equal(72, s.SpeedKmh, 6));
    }
}
