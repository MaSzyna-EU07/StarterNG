using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using StarterNG.Application.Abstractions;

namespace StarterNG.Services;

/// <summary>
/// Where languages other than the bundled ones come from.
/// </summary>
/// <param name="Translations">Folders of *.xml, the first holding a code winning.</param>
/// <param name="OldStarter">Where the old starter kept its lang-XX.txt.</param>
/// <param name="Simulator">The simulator's lang folder, with its XX.po.</param>
public sealed record LanguageFolders(IReadOnlyList<string> Translations, string? OldStarter, string? Simulator);

/// <summary>
/// Loads the translation table from startercfg/lang/*.xml and exposes it as
/// <see cref="ILocalizedStrings"/>. Stays a change-notifying class because the
/// XAML binds to it directly as a static source.
/// </summary>
public class LocalizationService : INotifyPropertyChanged, ILocalizedStrings
{
    public static string LangDirectory =>
        Path.Combine(AppContext.BaseDirectory, "startercfg", "lang");

    private readonly IDiagnosticsLog _log;
    private readonly string _directory;
    private readonly LanguageFolders _folders;

    // Codes eu07.ini named that nothing else has, as the old starter added them to its list.
    private readonly List<string> _kept = new();

    private Dictionary<string, string> _strings = new();

    public LocalizationService(IDiagnosticsLog log, string? directory = null, LanguageFolders? folders = null)
    {
        _log = log;
        _directory = directory ?? LangDirectory;
        _folders = folders ?? new LanguageFolders(new[] { _directory }, null, null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action? LanguageChanged;

    public string CurrentLanguage { get; private set; } = "English";

    public string CurrentLangCode { get; private set; } = "en";

    /// <summary>Whether this language has the starter's own texts; without them they are in English.</summary>
    public bool HasOwnTexts { get; private set; } = true;

    private readonly HashSet<string> _reportedMisses = new(StringComparer.Ordinal);

    /// <summary>
    /// Keys with this suffix hold the optional one-line explanation shown under a
    /// setting. They are only authored in en/pl, so a miss yields an empty string
    /// (which collapses the description line) instead of the raw key, and is not
    /// reported as a translation gap.
    /// </summary>
    private const string OptionalSuffix = "Desc";

    public string this[string key]
    {
        get
        {
            if (_strings.TryGetValue(key, out var value))
                return value;

            if (key.EndsWith(OptionalSuffix, StringComparison.Ordinal))
                return string.Empty;

            lock (_reportedMisses)
                if (_reportedMisses.Add(key))
                    _log.Log($"lang/{CurrentLangCode}: missing key \"{key}\"");

            // A translation someone prepared for an earlier starter lacks the newer texts.
            return English().TryGetValue(key, out value) ? value : key;
        }
    }

    /// <summary>
    /// Looks up a key that may legitimately be absent, without logging a miss.
    /// Returns an empty string when the current language does not define it.
    /// </summary>
    public string Opt(string key) =>
        _strings.TryGetValue(key, out var value) ? value : string.Empty;

    private Dictionary<string, string>? _english;

    /// <summary>
    /// <see cref="Opt"/>, falling back to English for text only the English file
    /// carries - the key descriptions, which read better in English than in Polish
    /// to someone who picked Czech.
    /// </summary>
    public string OptOrEnglish(string key)
    {
        if (_strings.TryGetValue(key, out var value))
            return value;

        return English().TryGetValue(key, out value) ? value : string.Empty;
    }

    private Dictionary<string, string> English()
    {
        if (_english is null)
        {
            _english = new Dictionary<string, string>(StringComparer.Ordinal);
            if (WithCode(AvailableLanguages(_log, _directory), "en") is { } lang)
            {
                try { _english = ReadStrings(lang.Path); }
                catch (Exception ex) { _log.Log($"lang/{Path.GetFileName(lang.Path)}", ex); }
            }
        }
        return _english;
    }

    /// <param name="Path">The translation, empty for a language known by its code alone.</param>
    public readonly record struct LanguageInfo(string Code, string Name, string Path);

    public static IReadOnlyList<LanguageInfo> AvailableLanguages(IDiagnosticsLog? log = null, string? directory = null)
    {
        directory ??= LangDirectory;
        var list = new List<LanguageInfo>();
        if (!Directory.Exists(directory))
            return list;

        foreach (var path in Directory.EnumerateFiles(directory, "*.xml"))
        {
            try
            {
                var (code, name) = ReadHeader(path);
                if (!string.IsNullOrEmpty(code))
                    list.Add(new LanguageInfo(code, string.IsNullOrEmpty(name) ? code : name, path));
            }
            catch (Exception ex)
            {
                log?.Log($"lang/{Path.GetFileName(path)}", ex);
            }
        }
        return list;
    }

    /// <summary>
    /// Every language there is to pick: the translations, the player's own first, then
    /// those only a code is known of - the old starter's lang-XX.txt, the simulator's
    /// XX.po and what eu07.ini names. Picked, those leave this starter in English and
    /// go to the simulator as they are.
    /// </summary>
    public IReadOnlyList<LanguageInfo> Languages()
    {
        var found = new SortedDictionary<string, LanguageInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (string folder in _folders.Translations)
            foreach (var lang in AvailableLanguages(_log, folder))
                found.TryAdd(lang.Code, lang);

        foreach (string code in CodesOnly())
            found.TryAdd(code, new LanguageInfo(code.ToLowerInvariant(), code.ToUpperInvariant(), string.Empty));

        return found.Values.ToList();
    }

    private IEnumerable<string> CodesOnly()
    {
        foreach (string name in FileNames(_folders.OldStarter, "lang-*.txt"))
            yield return name["lang-".Length..];

        // lang/ also holds editor_pl.po and template.po.
        foreach (string name in FileNames(_folders.Simulator, "*.po"))
            if (Regex.IsMatch(name, "^[A-Za-z]{2,3}$"))
                yield return name;

        foreach (string code in _kept)
            yield return code;
    }

    private IEnumerable<string> FileNames(string? folder, string pattern)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return Array.Empty<string>();

        try
        {
            return Directory.GetFiles(folder, pattern).Select(Path.GetFileNameWithoutExtension).ToList()!;
        }
        catch (Exception ex)
        {
            _log.Log($"looking through {folder}", ex);
            return Array.Empty<string>();
        }
    }

    /// <summary>A language eu07.ini names stays on the list, known or not.</summary>
    public void Keep(string? codeOrName)
    {
        if (string.IsNullOrWhiteSpace(codeOrName))
            return;

        string wanted = codeOrName.Trim();
        if (Find(Languages(), wanted) is null)
            _kept.Add(wanted);
    }

    private static LanguageInfo? Find(IReadOnlyList<LanguageInfo> languages, string codeOrName)
    {
        foreach (var lang in languages)
            if (string.Equals(lang.Code, codeOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lang.Name, codeOrName, StringComparison.OrdinalIgnoreCase))
                return lang;
        return null;
    }

    private static LanguageInfo? WithCode(IEnumerable<LanguageInfo> languages, string code)
    {
        foreach (var lang in languages)
            if (string.Equals(lang.Code, code, StringComparison.OrdinalIgnoreCase))
                return lang;
        return null;
    }

    public string Load(string codeOrName)
    {
        var languages = Languages();

        LanguageInfo? match = Find(languages, codeOrName);
        if (match is null)
        {
            match = WithCode(languages, "en");
            if (match is null && languages.Count > 0)
                match = languages[0];
        }

        HasOwnTexts = true;
        if (match is { Path.Length: 0 } codeOnly)
        {
            LoadEnglish(languages);
            CurrentLangCode = codeOnly.Code;
            CurrentLanguage = codeOnly.Name;
            HasOwnTexts = false;
            ReadOldKeyDescriptions(codeOnly.Code);
        }
        else if (match is not { } info || !TryLoadFile(info))
        {
            LoadEnglish(languages);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        LanguageChanged?.Invoke();
        return CurrentLanguage;
    }

    // Without the language asked for, English; without that, the keys themselves. The
    // starter comes up either way.
    private void LoadEnglish(IReadOnlyList<LanguageInfo> languages)
    {
        // Languages() holds one language to a code.
        if (WithCode(languages, "en") is { Path.Length: > 0 } english && TryLoadFile(english))
            return;

        _strings = new Dictionary<string, string>();
        CurrentLangCode = "en";
        CurrentLanguage = "English";
    }

    /// <summary>
    /// The one part of an old starter's language file this one can use: the rest names
    /// that program's controls. Its key descriptions follow a line with the file's name.
    /// </summary>
    private void ReadOldKeyDescriptions(string code)
    {
        if (string.IsNullOrEmpty(_folders.OldStarter) || !Directory.Exists(_folders.OldStarter))
            return;

        try
        {
            // The list holds codes in lower case and the file may be lang-ZH.txt; on Linux those differ.
            string? path = Directory.EnumerateFiles(_folders.OldStarter, "lang-*.txt")
                .FirstOrDefault(file => string.Equals(Path.GetFileNameWithoutExtension(file), $"lang-{code}", StringComparison.OrdinalIgnoreCase));
            if (path is null)
                return;

            bool inDescriptions = false;
            foreach (string line in File.ReadLines(path, Encoding.UTF8))
            {
                if (line.Contains("<eu07_input-keyboard.ini", StringComparison.Ordinal))
                {
                    inDescriptions = true;
                    continue;
                }

                int equals = line.IndexOf('=');
                if (!inDescriptions)
                    continue;
                if (equals <= 0)
                    break;

                _strings["Cmd." + line[..equals]] = line[(equals + 1)..];
            }
        }
        catch (Exception ex)
        {
            _log.Log($"reading lang-{code}.txt", ex);
        }
    }

    /// <summary>A file cut short has a header like any other and fails only here: it counts as missing.</summary>
    private bool TryLoadFile(LanguageInfo language)
    {
        try
        {
            _strings = ReadStrings(language.Path);
        }
        catch (Exception ex)
        {
            _log.Log($"lang/{Path.GetFileName(language.Path)}", ex);
            return false;
        }

        lock (_reportedMisses)
            _reportedMisses.Clear();
        CurrentLangCode = language.Code;
        CurrentLanguage = language.Name;
        return true;
    }

    private static Dictionary<string, string> ReadStrings(string path)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);

        var settings = new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true };
        using var reader = XmlReader.Create(path, settings);

        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Name == "String")
            {
                var key = reader.GetAttribute("key");
                var value = reader.ReadElementContentAsString();
                if (!string.IsNullOrEmpty(key))
                    dict[key] = value;
            }
            else
            {
                reader.Read();
            }
        }

        return dict;
    }

    private static (string code, string name) ReadHeader(string path)
    {
        var settings = new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true };
        using var reader = XmlReader.Create(path, settings);

        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Name == "Language")
            {
                return (reader.GetAttribute("code") ?? "",
                        reader.GetAttribute("name") ?? "");
            }
        }
        return ("", "");
    }
}
