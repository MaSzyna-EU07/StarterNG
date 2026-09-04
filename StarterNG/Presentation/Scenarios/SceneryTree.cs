using System;
using System.Collections.Generic;
using System.Linq;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Presentation.Scenarios;

/// <summary>
/// A node of the scenery list: either a scenario, or a group holding scenarios.
/// </summary>
/// <param name="Label">Text to show.</param>
/// <param name="SceneryIndex">Index into the scenery list, or -1 for a group.</param>
/// <param name="Children">Scenarios under a group; empty for a scenario.</param>
public sealed record SceneryTreeNode(string Label, int SceneryIndex, IReadOnlyList<SceneryTreeNode> Children)
{
    public bool IsGroup => SceneryIndex < 0;
}

/// <summary>
/// Turns the flat scenery list into the tree the scenario picker shows:
/// scenarios that declare a group sit under it, the rest stay at the top, and
/// everything is sorted by name.
/// </summary>
/// <remarks>
/// Pulled out of the view so the grouping and the archival filter can be tested
/// without a TreeView. The view's job is reduced to turning these nodes into
/// controls.
/// </remarks>
public sealed class SceneryTreeBuilder
{
    private readonly ISceneryTranslations _translations;

    public SceneryTreeBuilder(ISceneryTranslations translations)
    {
        _translations = translations;
    }

    /// <param name="sceneries">The list the node indices refer to.</param>
    /// <param name="includeArchival">Whether scenarios marked archival are listed.</param>
    /// <param name="langCode">Language for resolving each scenery's own translations.</param>
    public IReadOnlyList<SceneryTreeNode> Build(IReadOnlyList<Scenery> sceneries, bool includeArchival,
                                                string langCode)
    {
        var groups = new Dictionary<string, List<SceneryTreeNode>>(StringComparer.Ordinal);
        var groupLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        var topLevel = new List<SceneryTreeNode>();

        for (int i = 0; i < sceneries.Count; i++)
        {
            var scenery = sceneries[i];
            if (scenery.Archival && !includeArchival)
                continue;

            // The group name may itself be an "@key" reference into the scenery's
            // own translation files, so the table has to be loaded per scenery.
            _translations.LoadFor(scenery, langCode);

            var node = new SceneryTreeNode(scenery.DisplayName, i, Array.Empty<SceneryTreeNode>());

            if (string.IsNullOrEmpty(scenery.Group))
            {
                topLevel.Add(node);
                continue;
            }

            if (!groups.TryGetValue(scenery.Group, out var members))
            {
                members = new List<SceneryTreeNode>();
                groups[scenery.Group] = members;
                groupLabels[scenery.Group] = _translations.Translate(scenery.Group);
            }
            members.Add(node);
        }

        foreach (var (group, members) in groups)
            topLevel.Add(new SceneryTreeNode(groupLabels[group], -1, Sorted(members)));

        return Sorted(topLevel);
    }

    private static List<SceneryTreeNode> Sorted(IEnumerable<SceneryTreeNode> nodes) =>
        nodes.OrderBy(node => node.Label, StringComparer.OrdinalIgnoreCase).ToList();
}
