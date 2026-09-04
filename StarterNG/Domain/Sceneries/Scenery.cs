using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;
using StarterNG.Domain;

namespace StarterNG.Domain.Sceneries;

public sealed class SceneryAttachment
{
    public string FilePath = "";
    public string Label = "";
}

public sealed class SceneryInclude
{
    public string FilePath = "";
    public string Desc = "";

    public int Kind;

    public bool Selected;
}

public sealed class Scenery
{
    public Scenery(string path, string template)
    {
        Path = path;
        Template = template;
    }

    public string Path { get; }

    public string Template { get; }

    public bool HasCompanionTimetable { get; set; }

    public string? Group { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public string? ImageName { get; set; }

    public bool Archival { get; set; }

    public List<Trainset> Trainsets { get; } = new();

    public List<SceneryAttachment> Attachments { get; } = new();

    public List<SceneryInclude> Includes { get; } = new();

    public List<Dynamic> LooseVehicles { get; } = new();

    public List<string> LooseVehicleNames { get; } = new();

    public List<string> Faults { get; } = new();

    public SceneryWeather Weather { get; } = new();

    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(Path);

    public string LocalizedDescription(ISceneryTranslations translations) =>
        string.IsNullOrEmpty(Description)
            ? ""
            : string.Join("\n", Description.Split('\n').Select(translations.Translate));

    public string BuildExportContent(bool skipDecorTrainsets, IRandomSource random)
    {
        string result = Template;

        if (Weather.Dirty)
            result = SceneryWeatherWriter.Rewrite(result, Weather, random);

        result = InjectIncludes(result);

        for (int i = 0; i < Trainsets.Count; i++)
        {
            string entry = skipDecorTrainsets && Trainsets[i].Decor
                ? string.Empty
                : Trainsets[i].ToSceneryEntry();
            result = result.Replace("{{" + i + "}}", entry);
        }

        if (LooseVehicles.Count > 0)
            result = AppendLooseVehicles(result);

        return result;
    }

    private string AppendLooseVehicles(string text)
    {
        var sb = new StringBuilder();
        foreach (var vehicle in LooseVehicles)
            sb.Append(vehicle.ToLooseNode());
        if (sb.Length == 0)
            return text;

        return InsertBeforeFirstInit(text, sb.ToString(), fallbackAtEnd: true);
    }

    private string InjectIncludes(string text)
    {
        if (Includes.Count == 0)
            return text;

        var sb = new StringBuilder();
        foreach (var include in Includes)
        {
            if (include.Kind == 2)
            {
                if (!HasCompanionTimetable)
                    sb.Append("include ").Append(include.FilePath).Append(" end\r\n");
                continue;
            }
            if (include.Selected)
                sb.Append("include ").Append(include.FilePath).Append(" end\r\n");
        }

        if (sb.Length == 0)
            return text;

        return InsertBeforeFirstInit(text, sb.ToString(), fallbackAtEnd: false);
    }

    private static string InsertBeforeFirstInit(string text, string addition, bool fallbackAtEnd)
    {
        var firstInit = Regex.Match(text, @"\bFirstInit\b", RegexOptions.IgnoreCase);
        if (firstInit.Success)
            return text[..firstInit.Index] + addition + text[firstInit.Index..];

        return fallbackAtEnd ? text + addition : addition + text;
    }
}
