using Eu07.BlackBox;
using Eu07.BlackBox.Views;

namespace StarterNG.Tests.BlackBox;

public class EventTextsTests
{
    [Theory]
    [InlineData("pl", "aidriverenable", "Aktywacja automechanika")]
    [InlineData("en", "aidriverenable", "Engage the AI driver")]
    [InlineData("pl", "mastercontrollerset", "Ustawienie nastawnika głównego")]
    [InlineData("pl", "headlightenablerearleft", "Włączenie lewego reflektora z tyłu")]
    [InlineData("cz", "batteryenable", "Turn the battery on")]
    public void A_command_is_told_by_what_it_does(string language, string command, string text) =>
        Assert.Equal(text, new DefaultBlackBoxStrings(language)[DefaultBlackBoxStrings.CommandPrefix + command]);

    [Fact]
    public void A_command_with_no_text_keeps_its_name() =>
        Assert.Equal("somethingnew", new DefaultBlackBoxStrings("pl")[DefaultBlackBoxStrings.CommandPrefix + "somethingnew"]);

    [Fact]
    public void Every_command_has_both_texts_starting_with_a_capital()
    {
        foreach (string command in CommandTexts.Commands)
            foreach (string language in new[] { "pl", "en" })
            {
                string text = CommandTexts.Of(command, language);
                Assert.NotEqual(command, text);
                Assert.True(char.IsUpper(text[0]), $"{command} ({language}): {text}");
            }
    }

    [Fact]
    public void The_events_name_the_commands_by_what_they_do()
    {
        var recording = BlackBoxReader.Read(new BlackBoxFile().Info()
            .Vehicle(1, "EP07-424").User(0, 1, true).Command(12.0, 1, true, "batteryenable").Stream());

        var events = EventList.Collect(recording, new DefaultBlackBoxStrings("pl"));

        Assert.Contains(events, e => e.Text == "Włączenie baterii");
    }
}
