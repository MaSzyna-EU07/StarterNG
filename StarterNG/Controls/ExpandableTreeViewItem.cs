using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace StarterNG.Controls;

/// <summary>
/// A tree entry that tells a screen reader whether it opens and what lies under it.
/// Avalonia's own says only that it is an entry, and hangs the entries under a group
/// on the panel inside the group, so a reader heard every scenery at level one: a
/// group such as Całkowo sounded like a scenery, and what was in it like its neighbours.
/// </summary>
public class ExpandableTreeViewItem : TreeViewItem
{
    protected override Type StyleKeyOverride => typeof(TreeViewItem);

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    /// <summary>What a screen reader is told: a leaf, or open or closed.</summary>
    public ExpandCollapseState ExpandState => StateFor(ItemCount > 0, IsExpanded);

    public static ExpandCollapseState StateFor(bool hasChildren, bool expanded) =>
        !hasChildren ? ExpandCollapseState.LeafNode
        : expanded ? ExpandCollapseState.Expanded
        : ExpandCollapseState.Collapsed;

    /// <summary>The entries a reader finds under this one: none while it is closed.</summary>
    public IReadOnlyList<Control> ReadableChildren =>
        IsExpanded ? GetRealizedContainers().Where(c => c.IsVisible).ToList() : Array.Empty<Control>();

    /// <summary>The entry this one hangs under, or null at the top of the tree.</summary>
    public ExpandableTreeViewItem? ReadableParent => ItemsControlFromItemContainer(this) as ExpandableTreeViewItem;

    private sealed class Peer : TreeViewItemAutomationPeer, IExpandCollapseProvider
    {
        private readonly ExpandableTreeViewItem _item;

        public Peer(ExpandableTreeViewItem item) : base(item)
        {
            _item = item;
            item.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsExpandedProperty && e.OldValue is bool was && e.NewValue is bool now)
                {
                    InvalidateChildren();
                    RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                                              StateFor(item.ItemCount > 0, was), StateFor(item.ItemCount > 0, now));
                }
                else if (e.Property == ItemCountProperty)
                    InvalidateChildren();
            };
            item.ContainerPrepared += (_, _) => InvalidateChildren();
            item.ContainerClearing += (_, _) => InvalidateChildren();
        }

        // The entries under it, straight: its name already says what its header shows.
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() =>
            _item.ReadableChildren.Select(GetOrCreate).ToList();

        protected override AutomationPeer? GetParentCore() =>
            _item.ReadableParent is { } parent ? GetOrCreate(parent) : base.GetParentCore();

        public ExpandCollapseState ExpandCollapseState => _item.ExpandState;

        public bool ShowsMenu => false;

        public void Expand() => _item.IsExpanded = true;

        public void Collapse() => _item.IsExpanded = false;
    }
}
