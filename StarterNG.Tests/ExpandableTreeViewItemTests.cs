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
    public void An_entry_on_its_own_has_nothing_over_it()
    {
        var item = new ExpandableTreeViewItem { Header = "Całkowo", IsExpanded = true };
        var peer = ControlAutomationPeer.CreatePeerForElement(item);

        Assert.Null(item.ReadableParent);
        Assert.Null(peer.GetParent());
    }

    [Fact]
    public void The_row_under_the_mouse_is_told_open_or_closed_and_a_leaf_nothing()
    {
        static string Loc(string key) => key == "TreeExpanded" ? "rozwinięte" : "zwinięte";

        Assert.Equal("rozwinięte", ExpandableTreeViewItem.WordFor(ExpandCollapseState.Expanded, Loc));
        Assert.Equal("zwinięte", ExpandableTreeViewItem.WordFor(ExpandCollapseState.Collapsed, Loc));
        Assert.Null(ExpandableTreeViewItem.WordFor(ExpandCollapseState.LeafNode, Loc));
    }
}
