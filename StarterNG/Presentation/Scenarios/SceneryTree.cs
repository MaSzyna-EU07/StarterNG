using System;
using System.Collections.Generic;
using System.Linq;
using StarterNG.Application.Abstractions;
using StarterNG.Domain.Sceneries;

namespace StarterNG.Presentation.Scenarios;

public sealed record SceneryTreeNode(string Label, int SceneryIndex, IReadOnlyList<SceneryTreeNode> Children)
{
    public bool IsGroup => SceneryIndex < 0;
}

public sealed class SceneryTreeBuilder
{
    private readonly ISceneryTranslations _translations;

    public SceneryTreeBuilder(ISceneryTranslations translations)
    {
        _translations = translations;
    }

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
