using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Xml;

namespace StarterNG.Services;

/// <summary>
/// Loads UI translations from plain XML files shipped in a <c>lang/</c> folder next
/// to the executable (instead of dictionaries compiled into the assembly). The files
/// are parsed with <see cref="XmlReader"/>, which works under Native AOT — unlike the
/// Avalonia XAML loader, which has no runtime parser in AOT builds.
///
/// File format (lang/en.xml):
/// <code>
/// &lt;Language code="en" name="English"&gt;
///     &lt;String key="NavScenarios"&gt;Scenarios&lt;/String&gt;
/// &lt;/Language&gt;
/// </code>
/// </summary>
public class LocalizationService : INotifyPropertyChanged
{
    /// <summary>Folder holding the language files, resolved as starter/lang next to the executable.</summary>
    public static string LangDirectory =>
        Path.Combine(AppContext.BaseDirectory, "startercfg", "lang");

    private Dictionary<string, string> _strings = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Display name of the active language (e.g. "English", "Polski").</summary>
    public string CurrentLanguage { get; private set; } = "English";

    /// <summary>Short ISO code of the active language (e.g. "en", "pl").</summary>
    public string CurrentLangCode { get; private set; } = "en";

    /// <summary>Indexer used by the XAML bindings: returns the key itself when missing.</summary>
    public string this[string key] =>
        _strings.TryGetValue(key, out var value) ? value : key;

    /// <summary>
    /// Metadata for one discovered language file.
    /// </summary>
    public readonly record struct LanguageInfo(string Code, string Name, string Path);

    /// <summary>
    /// Scans the lang/ folder and returns the header metadata of every *.xml file
    /// without loading all of its strings. Used to populate the language selector.
    /// </summary>
    public static IReadOnlyList<LanguageInfo> AvailableLanguages()
    {
        var list = new List<LanguageInfo>();
        if (!Directory.Exists(LangDirectory))
            return list;

        foreach (var path in Directory.EnumerateFiles(LangDirectory, "*.xml"))
        {
            try
            {
                var (code, name) = ReadHeader(path);
                if (!string.IsNullOrEmpty(code))
                    list.Add(new LanguageInfo(code, string.IsNullOrEmpty(name) ? code : name, path));
            }
            catch
            {
                // Skip malformed files rather than crashing the launcher.
            }
        }
        return list;
    }

    /// <summary>
    /// Loads the language whose code or display name matches <paramref name="codeOrName"/>.
    /// Falls back to English ("en") when no match is found. Returns the resolved
    /// display name so callers can keep their stored setting consistent.
    /// </summary>
    public string Load(string codeOrName)
    {
        var languages = AvailableLanguages();

        LanguageInfo? match = null;
        foreach (var lang in languages)
        {
            if (string.Equals(lang.Code, codeOrName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lang.Name, codeOrName, StringComparison.OrdinalIgnoreCase))
            {
                match = lang;
                break;
            }
        }

        // Fall back to English, then to whatever is available first.
        if (match is null)
        {
            foreach (var lang in languages)
                if (string.Equals(lang.Code, "en", StringComparison.OrdinalIgnoreCase))
                {
                    match = lang;
                    break;
                }
            if (match is null && languages.Count > 0)
                match = languages[0];
        }

        if (match is { } info)
            LoadFile(info.Path, info.Code, info.Name);
        else
        {
            // No files at all: keep keys visible instead of blank UI.
            _strings = new Dictionary<string, string>();
            CurrentLangCode = "en";
            CurrentLanguage = "English";
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        return CurrentLanguage;
    }

    private void LoadFile(string path, string code, string name)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);

        var settings = new XmlReaderSettings { IgnoreComments = true, IgnoreWhitespace = true };
        using var reader = XmlReader.Create(path, settings);

        // NOTE: ReadElementContentAsString() already advances the reader past the
        // element's end tag onto the *next* node, so we must NOT call Read() again
        // in that case - doing so would skip every second <String> entry. We only
        // advance explicitly for nodes we don't consume here.
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Name == "String")
            {
                var key = reader.GetAttribute("key");
                var value = reader.ReadElementContentAsString(); // advances the reader
                if (!string.IsNullOrEmpty(key))
                    dict[key] = value;
            }
            else
            {
                reader.Read();
            }
        }

        _strings = dict;
        CurrentLangCode = code;
        CurrentLanguage = name;
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
