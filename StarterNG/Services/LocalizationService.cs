using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Xml;

namespace StarterNG.Services;

public class LocalizationService : INotifyPropertyChanged
{
    public static string LangDirectory =>
        Path.Combine(AppContext.BaseDirectory, "startercfg", "lang");

    private Dictionary<string, string> _strings = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action? LanguageChanged;

    public string CurrentLanguage { get; private set; } = "English";

    public string CurrentLangCode { get; private set; } = "en";

    public string this[string key] =>
        _strings.TryGetValue(key, out var value) ? value : key;

    public readonly record struct LanguageInfo(string Code, string Name, string Path);

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

            }
        }
        return list;
    }

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

            _strings = new Dictionary<string, string>();
            CurrentLangCode = "en";
            CurrentLanguage = "English";
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        LanguageChanged?.Invoke();
        return CurrentLanguage;
    }

    private void LoadFile(string path, string code, string name)
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
