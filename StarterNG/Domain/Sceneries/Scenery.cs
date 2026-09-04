using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using StarterNG.Application.Abstractions;
using StarterNG.Classes;
using StarterNG.Domain;

namespace StarterNG.Domain.Sceneries;

/// <summary>A file the scenery offers to open alongside it (map, manual, ...).</summary>
public sealed class SceneryAttachment
{
    public string FilePath = "";
    public string Label = "";
}

/// <summary>
/// An <c>include</c> the scenery marks as optional, so the user can decide
/// whether it ends up in the exported scenario.
/// </summary>
public sealed class SceneryInclude
{
    public string FilePath = "";
    public string Desc = "";

    /// <summary>1 = on by default, 2 = only when no companion timetable exists.</summary>
    public int Kind;

    public bool Selected;
}

/// <summary>
/// A scenario the user can start: its consists, loose vehicles, optional
/// includes and weather, plus the ability to render all of that back into a .scn
/// the simulator can load.
/// </summary>
/// <remarks>
/// The aggregate is pure: it never reads or writes files. Parsing lives in
/// <c>Infrastructure.Sceneries.SceneryParser</c> and loading in
/// <c>SceneryRepository</c>, which is what lets the export rules be tested from
/// a string rather than an installation.
///
/// <see cref="Template"/> is the .scn text with every trainset replaced by a
/// <c>{{index}}</c> placeholder and the optional includes stripped, so
/// <see cref="BuildExportContent"/> only has to substitute rather than re-parse.
/// </remarks>
public sealed class Scenery
{
    public Scenery(string path, string template)
    {
        Path = path;
        Template = template;
    }

    /// <summary>Path of the .scn this was parsed from; the aggregate's identity.</summary>
    public string Path { get; }

    /// <summary>The .scn body with trainset placeholders, used for export.</summary>
    public string Template { get; }

    /// <summary>True when a companion .sbt timetable sits next to the .scn.</summary>
    public bool HasCompanionTimetable { get; set; }

    public string? Group { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    public string? ImageName { get; set; }

    /// <summary>Scenery kept for historical interest; hidden unless asked for.</summary>
    public bool Archival { get; set; }

    public List<Trainset> Trainsets { get; } = new();

    public List<SceneryAttachment> Attachments { get; } = new();

    public List<SceneryInclude> Includes { get; } = new();

    /// <summary>Vehicles placed directly as nodes rather than inside a trainset.</summary>
    public List<Dynamic> LooseVehicles { get; } = new();

    /// <summary>
    /// Names of every loose vehicle, including those whose node failed to parse
    /// and therefore has no <see cref="Dynamic"/>.
    /// </summary>
    public List<string> LooseVehicleNames { get; } = new();

    /// <summary>Parse problems worth showing on the scenario's fault list.</summary>
    public List<string> Faults { get; } = new();

    public SceneryWeather Weather { get; } = new();

    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>
    /// The description with any "@key" references resolved against the scenery's
    /// own translation files.
    /// </summary>
    public string LocalizedDescription(ISceneryTranslations translations) =>
        string.IsNullOrEmpty(Description)
            ? ""
            : string.Join("\n", Description.Split('\n').Select(translations.Translate));

    /// <summary>
    /// Renders the scenario the simulator should load: the original file with the
    /// user's consists, weather and optional includes applied.
    /// </summary>
    /// <param name="skipDecorTrainsets">
    /// Drops AI trainsets the user marked irrelevant, which shortens loading.
    /// </param>
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
            // Kind 2 is the scenery's own timetable stand-in: it is only wanted
            // when the user has not supplied a companion .sbt.
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

    /// <summary>
    /// Places generated content ahead of <c>FirstInit</c>, which the simulator
    /// treats as the end of the definition section.
    /// </summary>
    private static string InsertBeforeFirstInit(string text, string addition, bool fallbackAtEnd)
    {
        var firstInit = Regex.Match(text, @"\bFirstInit\b", RegexOptions.IgnoreCase);
        if (firstInit.Success)
            return text[..firstInit.Index] + addition + text[firstInit.Index..];

        return fallbackAtEnd ? text + addition : addition + text;
    }
}
