using Avalonia.Controls;
using Avalonia.LogicalTree;
using StarterNG.Application;
using StarterNG.Tests.Fakes;
using StarterNG.Views;

namespace StarterNG.Tests;

/// <summary>The starter's questions over its window, answered with the mouse or not at all.</summary>
[Collection(AvaloniaControls.Name)]
public class MessageBoxTests
{
    // The buttons are labelled in the starter's language.
    static MessageBoxTests()
    {
        HeadlessDrawing.Ensure();
        if (AppServices.CurrentOrNull is null)
        {
            var installation = new TestInstallation(new InMemoryFileSystem());
            AppServices.Initialize(installation.Paths, installation.Files, installation.Clock, installation.Environment);
        }
    }

    private static (Window Owner, Window Dialog, Task<bool?> Answer) Choose()
    {
        var owner = new Window();
        owner.Show();
        var answer = MessageBox.Choose(owner, "Zapisać zmiany przed startem?", "Niezapisane zmiany", "Zapisz", "Nie zapisuj");
        Controls.Settle();
        return (owner, Assert.Single(owner.OwnedWindows), answer);
    }

    // The answer is given once the dialog's closing has gone through the dispatcher.
    private static bool? Answered(Task<bool?> answer)
    {
        Assert.True(SpinWait.SpinUntil(() =>
        {
            Controls.Settle();
            return answer.IsCompleted;
        }, TimeSpan.FromSeconds(5)));
        return answer.Result;
    }

    private static Button[] Buttons(Window dialog) => dialog.GetLogicalDescendants().OfType<Button>().ToArray();

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, null)]
    public void A_choice_gives_the_answer_clicked_and_the_way_back_gives_none(int clicked, bool? expected)
    {
        var (owner, dialog, answer) = Choose();
        try
        {
            Assert.Equal("Niezapisane zmiany", dialog.Title);
            Assert.Contains(dialog.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "Zapisać zmiany przed startem?");
            var buttons = Buttons(dialog);
            Assert.Equal(["Zapisz", "Nie zapisuj", App.Loc["Cancel"]], buttons.Select(b => b.Content as string));
            Assert.False(answer.IsCompleted);

            buttons[clicked].RaiseEvent(Controls.Click());

            Assert.Equal(expected, Answered(answer));
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            owner.Close();
        }
    }

    [Fact]
    public void Closing_the_window_of_a_choice_picks_neither_answer()
    {
        var (owner, dialog, answer) = Choose();
        try
        {
            dialog.Close();

            Assert.Null(Answered(answer));
        }
        finally
        {
            owner.Close();
        }
    }
}
