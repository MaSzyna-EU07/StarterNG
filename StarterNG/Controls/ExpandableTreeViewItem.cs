using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StarterNG.Application;

namespace StarterNG.Controls;

/// <summary>
/// A tree entry that tells a screen reader whether it opens and what lies under it.
/// Avalonia's own says only that it is an entry, and hangs the entries under a group
/// on the panel inside the group, so a reader heard every scenery at level one: a
/// group such as Całkowo sounded like a scenery, and what was in it like its neighbours.
/// Under the mouse a reader hears only what lies there, the name in the row; the row
/// is told the state as well, so the mouse also hears that an entry opens.
/// </summary>
public class ExpandableTreeViewItem : TreeViewItem
{
    protected override Type StyleKeyOverride => typeof(TreeViewItem);

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    /// <summary>The words for an open and a closed entry, in the starter's language.</summary>
    public static Func<ExpandCollapseState, string?> StateWords { get; set; } = DefaultStateWords;

    private static string? DefaultStateWords(ExpandCollapseState state) =>
        AppServices.Current?.Localization is { } loc
            ? WordFor(state, key => loc[key])
            : null;

    public static string? WordFor(ExpandCollapseState state, Func<string, string> loc) => state switch
    {
        ExpandCollapseState.Expanded => loc("TreeExpanded"),
        ExpandCollapseState.Collapsed => loc("TreeCollapsed"),
        _ => null,
    };

    private Control? _row;
    private string? _rowName;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _row = e.NameScope.Find<Control>("PART_LayoutRoot");
        // A part of a template is passed over when Windows looks for what lies under the
        // mouse, so the gaps beside the arrow and the name led to the entry, which told no
        // state. As a control the row itself is found there.
        if (_row is not null)
            AutomationProperties.SetAccessibilityView(_row, AccessibilityView.Control);
        TellRowLater();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (AppServices.Current?.Localization is { } loc)
            loc.LanguageChanged += TellRowLater;
        TellRow();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (AppServices.Current?.Localization is { } loc)
            loc.LanguageChanged -= TellRowLater;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsExpandedProperty || change.Property == ItemCountProperty
            || change.Property == AutomationProperties.NameProperty)
            TellRow();
        else if (change.Property == HeaderProperty)
            TellRowLater();
    }

    // A new header is laid out only after it is set.
    private void TellRowLater() => Dispatcher.UIThread.Post(TellRow, DispatcherPriority.Loaded);

    /// <summary>
    /// Gives whatever the mouse may point at in the row the entry's state, and its name
    /// where it shows no text of its own, such as the arrow or an icon.
    /// </summary>
    private void TellRow()
    {
        if (_row is null)
            return;

        string? state = StateWords(ExpandState);
        string? name = AutomationProperties.GetName(this);
        foreach (var part in _row.GetVisualDescendants().OfType<Control>().Prepend(_row))
        {
            if (string.IsNullOrEmpty(state))
                part.ClearValue(AutomationProperties.HelpTextProperty);
            else
                AutomationProperties.SetHelpText(part, state);

            if (part is TextBlock)
                continue;
            string? had = AutomationProperties.GetName(part);
            if (string.IsNullOrEmpty(had) || had == _rowName)
                AutomationProperties.SetName(part, name);
        }
        _rowName = name;
    }

    /// <summary>What a screen reader is told: a leaf, or open or closed.</summary>
    public ExpandCollapseState ExpandState => StateFor(ItemCount > 0, IsExpanded);

    public static ExpandCollapseState StateFor(bool hasChildren, bool expanded) => (hasChildren, expanded) switch
    {
        (false, _) => ExpandCollapseState.LeafNode,
        (true, true) => ExpandCollapseState.Expanded,
        (true, false) => ExpandCollapseState.Collapsed
    };

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
                    RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                                              StateFor(item.ItemCount > 0, was), StateFor(item.ItemCount > 0, now));
            };
        }

        // Only the way up changes: a reader counts the entries over this one to tell its
        // level. The way down stays the controls on screen, so whatever the mouse points
        // at still leads up through its entry to the window.
        protected override AutomationPeer? GetParentCore() =>
            _item.ReadableParent is { } parent ? GetOrCreate(parent) : base.GetParentCore();

        public ExpandCollapseState ExpandCollapseState => _item.ExpandState;

        public bool ShowsMenu => false;

        public void Expand() => _item.IsExpanded = true;

        public void Collapse() => _item.IsExpanded = false;
    }
}
