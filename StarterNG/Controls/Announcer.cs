using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace StarterNG.Controls;

/// <summary>
/// Says what a click changed out of sight, such as a quality level that moved a dozen
/// sliders or a time set to now. It wraps the whole window: a screen reader learns of
/// an element only on its way to the focused one, and this lies on every such way.
/// A text marked <see cref="ChangesProperty"/> is read whenever it changes.
/// </summary>
public class Announcer : Decorator
{
    /// <summary>How long a message stays its name, so it is not read again later on.</summary>
    public static readonly TimeSpan Shown = TimeSpan.FromSeconds(3);

    public static readonly AttachedProperty<bool> ChangesProperty =
        AvaloniaProperty.RegisterAttached<Announcer, TextBlock, bool>("Changes");

    public static bool GetChanges(TextBlock block) => block.GetValue(ChangesProperty);

    public static void SetChanges(TextBlock block, bool value) => block.SetValue(ChangesProperty, value);

    static Announcer()
    {
        TextBlock.TextProperty.Changed.AddClassHandler<TextBlock>((block, _) =>
        {
            if (GetChanges(block))
                Say(block, block.Text);
        });
    }

    private readonly Func<DateTime> _now;
    private string? _message;
    private DateTime _saidAt;

    public Announcer() : this(() => DateTime.UtcNow)
    {
    }

    public Announcer(Func<DateTime> now) => _now = now;

    /// <summary>The message while it is fresh, then nothing.</summary>
    public string? Message => _message is not null && _now() - _saidAt < Shown ? _message : null;

    /// <summary>Says the text through the announcer of the window <paramref name="from"/> is in.</summary>
    public static void Say(Visual from, string? text)
    {
        var announcer = from as Announcer ?? from.FindAncestorOfType<Announcer>();
        announcer?.Say(text);
    }

    public void Say(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        string? before = Message;
        _message = text.Trim();
        _saidAt = _now();
        // Its name changing is what makes a live region speak.
        if (ControlAutomationPeer.CreatePeerForElement(this) is Peer peer)
            peer.Told(before, _message);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    private sealed class Peer : ControlAutomationPeer
    {
        public Peer(Announcer owner) : base(owner)
        {
        }

        private Announcer Announcer => (Announcer)Owner;

        public void Told(string? before, string now) =>
            RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, before, now);

        protected override string? GetNameCore() => Announcer.Message;

        protected override AutomationLiveSetting GetLiveSettingCore() => AutomationLiveSetting.Polite;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    }
}
