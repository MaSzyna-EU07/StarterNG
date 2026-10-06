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

    public const string Star = "\u2605 ";

    /// <summary>
    /// The tree of the list: groups and loose sceneries by name, and the starred ones
    /// gathered once more in a group of their own on top, starred wherever they appear.
    /// </summary>
    public IReadOnlyList<SceneryTreeNode> Build(IReadOnlyList<Scenery> sceneries, bool includeArchival,
                                                string langCode, Func<Scenery, bool>? isFavorite = null,
                                                string favoritesLabel = "")
    {
        var groups = new Dictionary<string, List<SceneryTreeNode>>(StringComparer.Ordinal);
        var groupLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        var topLevel = new List<SceneryTreeNode>();
        var favorites = new List<SceneryTreeNode>();

        for (int i = 0; i < sceneries.Count; i++)
        {
            var scenery = sceneries[i];
            if (scenery.Archival && !includeArchival)
                continue;

            _translations.LoadFor(scenery, langCode);

            bool favorite = isFavorite?.Invoke(scenery) == true;
            var node = new SceneryTreeNode((favorite ? Star : "") + scenery.DisplayName, i,
                                           Array.Empty<SceneryTreeNode>());
            if (favorite)
                favorites.Add(node);

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

        var tree = Sorted(topLevel);
        if (favorites.Count > 0)
            tree.Insert(0, new SceneryTreeNode(Star + favoritesLabel, -1, Sorted(favorites)));
        return tree;
    }

    private static List<SceneryTreeNode> Sorted(IEnumerable<SceneryTreeNode> nodes) =>
        nodes.OrderBy(node => node.Label, StringComparer.OrdinalIgnoreCase).ToList();
}
