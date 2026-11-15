using System;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace StarterNG.Controls;

/// <summary>
/// A tree entry that tells a screen reader whether it opens. Avalonia's own says only
/// that it is an entry, so a scenery group such as Całkowo sounded like a scenery and
/// nobody without the arrow on screen knew there was more under it.
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

    private sealed class Peer : TreeViewItemAutomationPeer, IExpandCollapseProvider
    {
        private readonly ExpandableTreeViewItem _item;

        public Peer(ExpandableTreeViewItem item) : base(item)
        {
            _item = item;
            item.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsExpandedProperty && e.OldValue is bool was && e.NewValue is bool now)
                    RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                                              StateFor(item.ItemCount > 0, was), StateFor(item.ItemCount > 0, now));
            };
        }

        public ExpandCollapseState ExpandCollapseState => _item.ExpandState;

        public bool ShowsMenu => false;

        public void Expand() => _item.IsExpanded = true;

        public void Collapse() => _item.IsExpanded = false;
    }
}
