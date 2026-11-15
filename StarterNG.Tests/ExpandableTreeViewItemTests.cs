using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using StarterNG.Controls;

namespace StarterNG.Tests;

public class ExpandableTreeViewItemTests
{
    private static IExpandCollapseProvider Peer(ExpandableTreeViewItem item) =>
        Assert.IsAssignableFrom<IExpandCollapseProvider>(ControlAutomationPeer.CreatePeerForElement(item));

    [Fact]
    public void A_scenery_with_nothing_under_it_is_a_leaf()
    {
        var peer = Peer(new ExpandableTreeViewItem { Header = "test32" });

        Assert.Equal(ExpandCollapseState.LeafNode, peer.ExpandCollapseState);
        Assert.False(peer.ShowsMenu);
    }

    [Fact]
    public void A_group_says_whether_it_is_open()
    {
        Assert.Equal(ExpandCollapseState.Collapsed, ExpandableTreeViewItem.StateFor(hasChildren: true, expanded: false));
        Assert.Equal(ExpandCollapseState.Expanded, ExpandableTreeViewItem.StateFor(hasChildren: true, expanded: true));
        Assert.Equal(ExpandCollapseState.LeafNode, ExpandableTreeViewItem.StateFor(hasChildren: false, expanded: true));
    }

    [Fact]
    public void A_screen_reader_can_open_and_close_an_entry()
    {
        var item = new ExpandableTreeViewItem { Header = "Całkowo" };
        var peer = Peer(item);

        peer.Expand();
        Assert.True(item.IsExpanded);

        peer.Collapse();
        Assert.False(item.IsExpanded);
    }

    [Fact]
    public void An_entry_on_its_own_has_nothing_under_it_and_nothing_over_it()
    {
        var item = new ExpandableTreeViewItem { Header = "Całkowo", IsExpanded = true };
        var peer = ControlAutomationPeer.CreatePeerForElement(item);

        Assert.Empty(item.ReadableChildren);
        Assert.Null(item.ReadableParent);
        Assert.Empty(peer.GetChildren());
        Assert.Null(peer.GetParent());
    }
}
