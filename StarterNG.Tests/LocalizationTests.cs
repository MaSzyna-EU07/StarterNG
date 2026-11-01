using System.Xml.Linq;
using StarterNG.Infrastructure.Adapters;
using StarterNG.Services;
using StarterNG.Tests.Fakes;

namespace StarterNG.Tests;

public class LocalizationTests
{
    private static LocalizationService Build() =>
        new(TestLog.Over());

    private static Dictionary<string, string> Strings(string path) =>
        XDocument.Load(path).Descendants("String")
            .ToDictionary(s => (string)s.Attribute("key")!, s => s.Value);

    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();
        foreach (var lang in LocalizationService.AvailableLanguages())
            if (lang.Code != "en")
                data.Add(lang.Code);
        return data;
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_language_has_every_key_but_the_optional_ones(string code)
    {
        var all = LocalizationService.AvailableLanguages();
        var english = Strings(all.Single(l => l.Code == "en").Path);
        var other = Strings(all.Single(l => l.Code == code).Path);

        var missing = english.Keys
            .Where(key => !key.EndsWith("Desc", StringComparison.Ordinal) &&
                          !key.StartsWith("Cmd.", StringComparison.Ordinal))
            .Where(key => !other.ContainsKey(key));

        Assert.Empty(missing);
    }

    [Fact]
    public void A_key_description_only_english_has_falls_back_to_english()
    {
        var loc = Build();
        loc.Load("cz");

        Assert.Equal(string.Empty, loc.Opt("Cmd.mastercontrollerincrease"));
        Assert.Equal("master controller up", loc.OptOrEnglish("Cmd.mastercontrollerincrease"));
    }

    [Fact]
    public void A_string_the_language_has_is_not_replaced_with_english()
    {
        var loc = Build();
        loc.Load("pl");

        Assert.Equal(loc["Language"], loc.OptOrEnglish("Language"));
        Assert.NotEqual("Language", loc.OptOrEnglish("Language"));
    }

    private const string English =
        "<Language code=\"en\" name=\"English\"><String key=\"Hello\">Hello</String></Language>";

    /// <summary>A file cut off right after its header: it still looks like a language.</summary>
    private const string CutShort = "<Language code=\"pl\" name=\"Polski\">";

    private static (LocalizationService Loc, FileDiagnosticsLog Log, string Directory) InFolderOf(
        params (string File, string Text)[] languages)
    {
        string directory = Directory.CreateTempSubdirectory("starterng-lang-").FullName;
        foreach (var (file, text) in languages)
            File.WriteAllText(Path.Combine(directory, file), text);
        var log = TestLog.Over();
        return (new LocalizationService(log, directory), log, directory);
    }

    [Fact]
    public void A_translation_cut_short_counts_as_missing_and_is_logged()
    {
        var (loc, log, directory) = InFolderOf(("en.xml", English), ("pl.xml", CutShort));
        try
        {
            Assert.Equal("English", loc.Load("pl"));

            Assert.Equal("en", loc.CurrentLangCode);
            Assert.Equal("Hello", loc["Hello"]);
            Assert.Contains("lang/pl.xml", log.Text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void With_every_translation_cut_short_the_starter_still_starts()
    {
        var (loc, log, directory) = InFolderOf(("pl.xml", CutShort));
        try
        {
            Assert.Equal("English", loc.Load("pl"));

            Assert.Equal("en", loc.CurrentLangCode);
            Assert.Equal("Hello", loc["Hello"]);
            Assert.Contains("lang/pl.xml", log.Text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_language_cut_short_leaves_english_rather_than_the_one_in_use_before()
    {
        var (loc, _, directory) = InFolderOf(
            ("cz.xml", "<Language code=\"cz\" name=\"Cestina\"><String key=\"Hello\">Ahoj</String></Language>"),
            ("en.xml", English),
            ("pl.xml", CutShort));
        try
        {
            loc.Load("cz");
            Assert.Equal("Ahoj", loc["Hello"]);

            loc.Load("pl");

            Assert.Equal("en", loc.CurrentLangCode);
            Assert.Equal("Hello", loc["Hello"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A player's folder, the bundled one with English in it, the old starter's and the simulator's.</summary>
    private sealed class Folders : IDisposable
    {
        public readonly string Root = Directory.CreateTempSubdirectory("starterng-langs-").FullName;

        public string Own => Sub("own");
        public string Bundled => Sub("bundled");
        public string OldStarter => Sub("starter");
        public string Simulator => Sub("lang");

        private string Sub(string name) => Directory.CreateDirectory(Path.Combine(Root, name)).FullName;

        public LocalizationService Service() =>
            new(TestLog.Over(),
                Bundled, new LanguageFolders(new[] { Own, Bundled }, OldStarter, Simulator));

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [Fact]
    public void A_players_own_translation_wins_over_the_bundled_one()
    {
        using var folders = new Folders();
        File.WriteAllText(Path.Combine(folders.Bundled, "en.xml"), English);
        File.WriteAllText(Path.Combine(folders.Bundled, "pl.xml"),
                          "<Language code=\"pl\" name=\"Polski\"><String key=\"Hello\">Czesc</String></Language>");
        File.WriteAllText(Path.Combine(folders.Own, "moj.xml"),
                          "<Language code=\"pl\" name=\"Moj polski\"><String key=\"Hello\">Witaj</String></Language>");
        var loc = folders.Service();

        Assert.Equal("Moj polski", loc.Load("pl"));
        Assert.Equal("Witaj", loc["Hello"]);
        Assert.True(loc.HasOwnTexts);
        Assert.Single(loc.Languages(), lang => lang.Code == "pl");
    }

    [Fact]
    public void An_old_starters_language_is_offered_in_english_with_its_key_descriptions()
    {
        using var folders = new Folders();
        File.WriteAllText(Path.Combine(folders.Bundled, "en.xml"), English);
        File.WriteAllText(Path.Combine(folders.OldStarter, "lang-ZH.txt"),
                          "[Main]\ncbLang.Items.Text=PL|EN\n[labels]\nx=y\n<eu07_input-keyboard.ini\nhornhighactivate=gwizd\n\n[frmOther]\nz=q\n");
        var loc = folders.Service();

        Assert.Contains(loc.Languages(), lang => lang.Code == "zh" && lang.Name == "ZH" && lang.Path.Length == 0);

        loc.Load("zh");

        Assert.Equal("zh", loc.CurrentLangCode);
        Assert.False(loc.HasOwnTexts);
        Assert.Equal("Hello", loc["Hello"]);
        Assert.Equal("gwizd", loc.Opt("Cmd.hornhighactivate"));
        Assert.Equal(string.Empty, loc.Opt("Cmd.z"));
    }

    [Fact]
    public void Of_the_simulators_files_only_a_language_code_counts()
    {
        using var folders = new Folders();
        File.WriteAllText(Path.Combine(folders.Bundled, "en.xml"), English);
        foreach (string file in new[] { "hu.po", "editor_pl.po", "template.po" })
            File.WriteAllText(Path.Combine(folders.Simulator, file), string.Empty);

        var codes = folders.Service().Languages().Select(lang => lang.Code).ToList();

        Assert.Contains("hu", codes);
        Assert.DoesNotContain("editor_pl", codes);
        Assert.DoesNotContain("template", codes);
    }

    [Fact]
    public void A_code_eu07_ini_names_stays_on_the_list_once()
    {
        using var folders = new Folders();
        File.WriteAllText(Path.Combine(folders.Bundled, "en.xml"), English);
        var loc = folders.Service();

        loc.Keep("sk");
        loc.Keep("SK");
        loc.Keep("en");

        Assert.Single(loc.Languages(), lang => lang.Code == "sk");
        Assert.Equal(2, loc.Languages().Count);
    }

    [Fact]
    public void A_key_a_translation_lacks_comes_in_english()
    {
        using var folders = new Folders();
        File.WriteAllText(Path.Combine(folders.Bundled, "en.xml"), English);
        File.WriteAllText(Path.Combine(folders.Own, "sk.xml"),
                          "<Language code=\"sk\" name=\"Slovensky\"><String key=\"Other\">Ine</String></Language>");
        var loc = folders.Service();

        loc.Load("sk");

        Assert.Equal("Hello", loc["Hello"]);
        Assert.Equal("Ine", loc["Other"]);
    }
}
