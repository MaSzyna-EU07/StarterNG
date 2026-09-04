using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Infrastructure.Sceneries;

public sealed class SceneryTranslations : ISceneryTranslations
{
    private const char ReferencePrefix = '@';

    private const string BaseLanguage = "en";

    private readonly IFileSystem _files;
    private readonly IDiagnosticsLog _log;
    private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);

    public SceneryTranslations(IFileSystem files, IDiagnosticsLog log)
    {
        _files = files;
        _log = log;
    }

    public void LoadFor(Scenery scenery, string langCode)
    {
        _map.Clear();

        string sceneryDir = Path.GetDirectoryName(Path.GetFullPath(scenery.Path)) ?? "scenery";
        string id = Path.GetFileNameWithoutExtension(scenery.Path);
        string i18nDir = Path.Combine(sceneryDir, "i18n");
        string language = NormalizeLanguage(langCode);

        TryLoad(Path.Combine(i18nDir, $"{id}_{BaseLanguage}.json"));
        if (!string.Equals(language, BaseLanguage, StringComparison.OrdinalIgnoreCase))
            TryLoad(Path.Combine(i18nDir, $"{id}_{language}.json"));
    }

    public string Translate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text ?? "";

        string trimmed = text.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != ReferencePrefix)
            return text;

        string key = trimmed[1..].Trim();
        return _map.TryGetValue(key, out string? translated) ? translated : text;
    }

    private void TryLoad(string path)
    {
        if (!_files.FileExists(path))
            return;

        try
        {
            using var document = JsonDocument.Parse(_files.ReadAllText(path));
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                Flatten("", document.RootElement);
        }
        catch (Exception ex)
        {
            _log.Log($"scenery i18n/{Path.GetFileName(path)}", ex);
        }
    }

    private void Flatten(string prefix, JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            string key = string.IsNullOrEmpty(prefix) ? property.Name : prefix + "." + property.Name;
            switch (property.Value.ValueKind)
            {
                case JsonValueKind.String:
                    _map[key] = property.Value.GetString() ?? "";
                    break;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    _map[key] = property.Value.ToString();
                    break;
                case JsonValueKind.Object:
                    Flatten(key, property.Value);
                    break;
            }
        }
    }

    private static string NormalizeLanguage(string language)
    {
        string lower = language.Trim().ToLowerInvariant();
        return lower switch
        {
            "polski" or "pl" => "pl",
            "english" or "en" => "en",
            "čeština" or "cesky" or "czech" or "cs" or "cz" => "cz",
            "magyar" or "hungarian" or "hu" => "hu",
            "русский" or "russian" or "ru" => "ru",
            _ => lower.Length >= 2 ? lower[..2] : "en"
        };
    }
}
