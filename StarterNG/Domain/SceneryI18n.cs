using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using StarterNG.Classes;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Domain;

public static class SceneryI18n
{
    private static readonly Dictionary<string, string> Map =
        new(StringComparer.OrdinalIgnoreCase);

    public static void Clear() => Map.Clear();

    public static void LoadFor(Scenery scenery, string langCode = "en")
    {
        Map.Clear();
        string scnDir = Path.GetDirectoryName(Path.GetFullPath(scenery.Path)) ?? "scenery";
        string id = Path.GetFileNameWithoutExtension(scenery.Path);
        string lang = NormalizeLang(langCode);
        string i18nDir = Path.Combine(scnDir, "i18n");

        TryLoad(Path.Combine(i18nDir, $"{id}_en.json"));
        if (!string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase))
            TryLoad(Path.Combine(i18nDir, $"{id}_{lang}.json"));
    }

    public static string T(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? "";
        string t = text.TrimStart();
        if (t.Length == 0 || t[0] != '@') return text;
        string key = t[1..].Trim();
        return Map.TryGetValue(key, out var v) ? v : text;
    }

    private static void TryLoad(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                Flatten("", doc.RootElement);
        }
        catch (Exception ex)
        {
            StarterNG.Infrastructure.Diagnostics.Log($"scenery i18n/{Path.GetFileName(path)}", ex);
        }
    }

    private static void Flatten(string prefix, JsonElement obj)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            string key = string.IsNullOrEmpty(prefix) ? prop.Name : prefix + "." + prop.Name;
            switch (prop.Value.ValueKind)
            {
                case JsonValueKind.String:
                    Map[key] = prop.Value.GetString() ?? "";
                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    Map[key] = prop.Value.ToString();
                    break;
                case JsonValueKind.Object:
                    Flatten(key, prop.Value);
                    break;
            }
        }
    }

    private static string NormalizeLang(string language)
    {
        string l = language.Trim().ToLowerInvariant();
        return l switch
        {
            "polski" or "pl" => "pl",
            "english" or "en" => "en",
            "čeština" or "cesky" or "czech" or "cs" or "cz" => "cz",
            "magyar" or "hungarian" or "hu" => "hu",
            "русский" or "russian" or "ru" => "ru",
            _ => l.Length >= 2 ? l[..2] : "en"
        };
    }
}
