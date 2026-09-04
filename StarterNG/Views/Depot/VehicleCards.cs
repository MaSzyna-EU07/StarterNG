using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Material.Icons;
using Material.Icons.Avalonia;

using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Domain.Vehicles;

namespace StarterNG.Views;

public sealed class VehicleCards
{
    private readonly Panel consistStack;

    private readonly VehicleCatalog _db;
    private readonly Consist _consist;
    private readonly Cargo _cargo;
    private readonly MiniTextures _minis;
    private readonly Cursor _hand;

    private readonly Action _redraw;
    private readonly Action _refreshDetails;
    private readonly Action<Dynamic> _selectInBrowser;
    private readonly Action<int, PointerPressedEventArgs> _armDrag;

    private readonly List<(Border Frame, ConsistItem Item, Dynamic Car)> _memberChrome = new();

    private readonly List<(Button Badge, ConsistItem Item)> _driverChrome = new();

    public Dynamic? PressedCar { get; set; }

    public VehicleCards(Panel consistStack, VehicleCatalog db, Consist consist,
                              Cargo cargo, MiniTextures minis, Cursor hand,
                              Action redraw, Action refreshDetails, Action<Dynamic> selectInBrowser,
                              Action<int, PointerPressedEventArgs> armDrag)
    {
        this.consistStack = consistStack;
        _db = db;
        _consist = consist;
        _cargo = cargo;
        _minis = minis;
        _hand = hand;
        _redraw = redraw;
        _refreshDetails = refreshDetails;
        _selectInBrowser = selectInBrowser;
        _armDrag = armDrag;
    }

    public void Forget()
    {
        _memberChrome.Clear();
        _driverChrome.Clear();
    }

    public Control BuildCard(ConsistItem item, int index)
    {
        bool selected = ReferenceEquals(_consist.Selected, item);

        var minis = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 1,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var cars = item.Flipped ? item.Cars.AsEnumerable().Reverse() : item.Cars;
        foreach (var car in cars)
        {
            var bmp = _minis.Get(car.MiniName, VehicleCardStyle.ThumbHeight, strict: true)
                      ?? _minis.Get(car.SkinFile, VehicleCardStyle.ThumbHeight, strict: true)
                      ?? _minis.Get(car.MiniName, VehicleCardStyle.ThumbHeight);
            if (bmp != null)
            {
                var img = MiniTextures.Sharp(new Image
                {
                    Source = bmp, Height = VehicleCardStyle.ThumbHeight, Stretch = Stretch.Uniform
                });
                if (item.Flipped)
                {
                    img.RenderTransform = new ScaleTransform(-1, 1);
                    img.RenderTransformOrigin = RelativePoint.Center;
                }

                minis.Children.Add(MemberFrame(img, item, car, CardCaption(item, car, index)));
            }
            else
            {
                minis.Children.Add(MemberFrame(new Border
                {
                    Height = VehicleCardStyle.ThumbHeight, Width = 90, Background = VehicleCardStyle.Placeholder,
                    CornerRadius = new CornerRadius(4),
                    Child = new TextBlock
                    {
                        Text = car.SkinFile, FontSize = 10, TextWrapping = TextWrapping.Wrap,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center
                    }
                }, item, car, CardCaption(item, car, index)));
            }
        }

        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 2)
        };
        controls.Children.Add(DriverButton(item));
        controls.Children.Add(FlipButton(item));

        if (item.Grouped && item.Cars.Count > 1)
            controls.Children.Add(SmallButton(MaterialIconKind.CallSplit, App.Loc["TipSplit"], () => _consist.Split(item)));
        else if ((index + 1 < _consist.Count &&
                  (Consist.HoldsTail(item) || _consist.CanFormUnit(item, _consist[index + 1]))) ||
                 (index > 0 && Consist.HoldsTail(_consist[index - 1])))
            controls.Children.Add(SmallButton(MaterialIconKind.Link, App.Loc["TipJoin"], () => _consist.Join(item)));

        var remove = SmallButton(MaterialIconKind.Close, App.Loc["RemoveSelectedVehicle"], () => _consist.Remove(item));
        remove.Foreground = new SolidColorBrush(Color.Parse("#E05252"));
        remove.FontWeight = FontWeight.Bold;
        controls.Children.Add(remove);

        var inner = new StackPanel { Spacing = 3 };
        inner.Children.Add(minis);
        inner.Children.Add(controls);

        var card = new Border
        {
            Margin = new Thickness(3, 2, 3, 6),
            Padding = new Thickness(selected ? 4 : 5),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(selected ? 2 : 1),
            BorderBrush = selected ? VehicleCardStyle.BorderSelected : VehicleCardStyle.Border,
            Background = selected ? VehicleCardStyle.BackgroundSelected : Brushes.Transparent,
            Cursor = _hand,
            Child = inner,
            Tag = item,
            // Focusable so the strip can be reached with Tab and walked with the
            // arrow keys; the focus ring itself comes from Carbon.axaml.
            Focusable = true
        };
        card.Classes.Add("ConsistCard");
        card.GotFocus += (_, _) => Select(item, null);
        card.KeyDown += (_, e) => OnCardKey(card, item, e);
        card.PointerPressed += (_, e) =>
        {

            if (!e.GetCurrentPoint(card).Properties.IsLeftButtonPressed)
                return;

            if (e.Source is Control src && src.GetVisualAncestors().OfType<Button>().Any())
                return;

            _consist.Selected = item;
            _consist.SelectedCar = PressedCar ?? item.Cars[0];
            PressedCar = null;
            _consist.SyncStartingVehicle();
            RefreshCardSelectionChrome();
            _refreshDetails();
            _selectInBrowser(_consist.SelectedCar);

            _armDrag(index, e);
        };
        card.ContextMenu = BuildCardMenu(card, item);
        return card;
    }

    private string CardCaption(ConsistItem item, Dynamic car, int index) =>
        TrainsetDisplay.VehicleLabel(car,
            head: index == 0 && ReferenceEquals(car, Consist.HeadCar(item)) && TrainsetDisplay.IsPowered(car));

    /// <summary>
    /// The name is not printed under the thumbnail - it costs a row on every card
    /// and the tooltip says the same thing when it is actually wanted.
    /// </summary>
    private Control MemberFrame(Control visual, ConsistItem item, Dynamic car, string caption)
    {
        var frame = new Border
        {
            Child = visual,
            Padding = new Thickness(1),
            BorderThickness = new Thickness(0, 2),
            BorderBrush = Brushes.Transparent,
            Cursor = _hand
        };

        var lines = new List<string> { caption };
        if (!string.IsNullOrWhiteSpace(car.Name) && car.Name != caption)
            lines.Add(car.Name);
        if (item.Grouped && item.Cars.Count > 1)
            lines.Add(Consist.UnitLabel(item));

        string tip = string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));
        if (tip.Length > 0)
            ToolTip.SetTip(frame, tip);

        var picked = car;
        frame.PointerPressed += (_, _) => PressedCar = picked;
        _memberChrome.Add((frame, item, car));
        return frame;
    }

    public void RefreshMemberChrome()
    {
        foreach (var (badge, item) in _driverChrome)
            PaintDriverButton(badge, item);

        foreach (var (frame, item, car) in _memberChrome)
        {
            bool lit = item.Cars.Count > 1 &&
                       ReferenceEquals(item, _consist.Selected) &&
                       ReferenceEquals(car, _consist.ActiveCar(item));
            frame.BorderBrush = lit ? VehicleCardStyle.BorderSelected : Brushes.Transparent;
        }
    }

    public void RefreshCardSelectionChrome()
    {
        RefreshMemberChrome();

        foreach (var child in consistStack.Children)
        {
            if (child is not Border { Tag: ConsistItem ci } border)
                continue;
            bool sel = ReferenceEquals(ci, _consist.Selected);
            border.Padding = new Thickness(sel ? 4 : 5);
            border.BorderThickness = new Thickness(sel ? 2 : 1);
            border.BorderBrush = sel ? VehicleCardStyle.BorderSelected : VehicleCardStyle.Border;
            border.Background = sel ? VehicleCardStyle.BackgroundSelected : Brushes.Transparent;
        }
    }

    /// <summary>
    /// Selects an item without going through a redraw. <paramref name="car"/> picks the
    /// active member of a multi-vehicle unit; null keeps the current one when possible.
    /// </summary>
    private void Select(ConsistItem item, Dynamic? car)
    {
        var target = car ?? (ReferenceEquals(_consist.Selected, item)
            ? _consist.ActiveCar(item)
            : item.Cars[0]);

        if (ReferenceEquals(_consist.Selected, item) && ReferenceEquals(_consist.SelectedCar, target))
            return;

        _consist.Selected = item;
        _consist.SelectedCar = target;
        _consist.SyncStartingVehicle();
        RefreshCardSelectionChrome();
        _refreshDetails();
        _selectInBrowser(target);
    }

    private Border? CardFor(ConsistItem item) =>
        consistStack.Children.OfType<Border>()
            .FirstOrDefault(b => ReferenceEquals(b.Tag, item));

    /// <summary>
    /// Set before an action that rebuilds the strip, so focus lands back on the card
    /// instead of falling out to whatever comes next in the tab order.
    /// </summary>
    private bool _refocusAfterRedraw;

    public void FocusSelectedCard(bool onlyIfRequested = false)
    {
        if (onlyIfRequested && !_refocusAfterRedraw)
            return;

        _refocusAfterRedraw = false;
        if (_consist.Selected is { } item && CardFor(item) is { } card)
        {
            card.Focus();
            card.BringIntoView();
        }
    }

    private void Redraw(bool keepFocus)
    {
        _refocusAfterRedraw = keepFocus;
        _redraw();
    }

    private void OnCardKey(Border card, ConsistItem item, KeyEventArgs e)
    {
        int index = _consist.IndexOf(item);
        if (index < 0)
            return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        switch (e.Key)
        {
            case Key.Left when ctrl:
                _consist.MoveLeft(item);
                Redraw(keepFocus: true);
                break;

            case Key.Right when ctrl:
                _consist.MoveRight(item);
                Redraw(keepFocus: true);
                break;

            case Key.Left:
            case Key.Right:
                FocusCardAt(index + (e.Key == Key.Left ? -1 : 1));
                break;

            case Key.Home:
                FocusCardAt(0);
                break;

            case Key.End:
                FocusCardAt(_consist.Count - 1);
                break;

            // Within a coupled unit the arrow keys pick which member the detail
            // panels talk about - the same thing clicking a thumbnail does.
            case Key.Up:
            case Key.Down:
                StepMember(item, e.Key == Key.Down ? 1 : -1);
                break;

            case Key.Space:
                _consist.Flip(item);
                Redraw(keepFocus: true);
                break;

            case Key.Enter:
                card.ContextMenu?.Open(card);
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private void FocusCardAt(int index)
    {
        if (index < 0 || index >= _consist.Count)
            return;

        var item = _consist[index];
        Select(item, null);
        if (CardFor(item) is { } card)
        {
            card.Focus();
            // Focusing alone does not scroll a plain Border into view.
            card.BringIntoView();
        }
    }

    private void StepMember(ConsistItem item, int delta)
    {
        if (item.Cars.Count < 2)
            return;

        int at = item.Cars.IndexOf(_consist.ActiveCar(item));
        int next = Math.Clamp(at + delta, 0, item.Cars.Count - 1);
        if (next != at)
            Select(item, item.Cars[next]);
    }

    /// <summary>Ctrl+N on the selected card, from the window's shortcut table.</summary>
    public void ShowNumberFlyoutForSelection()
    {
        if (_consist.Selected is not { } item || CardFor(item) is not { } card)
            return;

        ShowWagonNumberFlyout(card, _consist.ActiveCar(item));
    }

    private void ShowWagonNumberFlyout(Control anchor, Dynamic car)
    {
        var num = new NumericUpDown
        {
            Minimum = 0, Maximum = 99999, Increment = 1, FormatString = "0",
            Value = Consist.GetWagonNumber(car), MinWidth = 120
        };
        var apply = new Button { Content = App.Loc["Set"], HorizontalAlignment = HorizontalAlignment.Right };
        apply.Classes.Add("Flat");
        apply.Classes.Add("Accent");

        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(8), MinWidth = 160 };
        panel.Children.Add(new TextBlock { Text = App.Loc["WagonNumber"], FontWeight = FontWeight.Bold, FontSize = 12 });
        panel.Children.Add(num);
        panel.Children.Add(apply);

        var flyout = new Flyout { Content = panel, Placement = PlacementMode.Top };
        apply.Click += (_, _) =>
        {
            _consist.SetWagonNumber(car, (int)(num.Value ?? 0));
            flyout.Hide();
        };
        flyout.ShowAt(anchor);
    }

    public Control BuildCoupler(ConsistItem item, bool trailing = false)
    {
        var glyph = new TextBlock
        {
            Text = trailing ? "╡" : "≣",
            FontSize = 18,
            Opacity = trailing ? 0.55 : 0.7,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var coupler = new Button
        {
            Padding = new Thickness(4, 6, 4, 6),
            MinWidth = 0,
            Cursor = _hand,
            VerticalAlignment = VerticalAlignment.Center,
            Content = glyph,
            Flyout = new Flyout
            {
                Content = BuildCouplingBox(item),
                Placement = PlacementMode.Top
            }
        };
        coupler.Classes.Add("Basic");
        ToolTip.SetTip(coupler, $"{App.Loc["Coupling"]} — {App.Loc["TipReorder"]}");
        return coupler;
    }

    private Control BuildCouplingBox(ConsistItem item)
    {
        var d = Consist.TailCar(item);
        int unitIdx = _consist.IndexOf(item);

        var panel = new StackPanel { Spacing = 2 };

        var copy = new Button
        {
            Content = "\u00AB",
            FontSize = 12,
            Padding = new Thickness(6, 1),
            Margin = new Thickness(12, 0, 0, 0),
            IsEnabled = unitIdx > 0,
            Cursor = _hand
        };
        copy.Classes.Add("Flat");
        ToolTip.SetTip(copy, App.Loc["CopyCoupler"]);
        copy.Click += (_, _) =>
        {
            if (unitIdx <= 0) return;
            d.Coupling.Flags = Consist.TailCar(_consist[unitIdx - 1]).Coupling.Flags;
            _redraw();
        };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        DockPanel.SetDock(copy, Dock.Right);
        header.Children.Add(copy);
        header.Children.Add(new TextBlock
        {
            Text = App.Loc["Coupling"],
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(header);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowSpacing = 2,
            ColumnSpacing = 12
        };
        for (int r = 0; r < (CouplingBits.BitKeys.Length + 1) / 2; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (int i = 0; i < CouplingBits.BitKeys.Length; i++)
        {
            int bit = 1 << i;
            var check = new CheckBox
            {
                Content = App.Loc[CouplingBits.BitKeys[i]],
                IsChecked = d.Coupling.Has(bit),
                FontSize = 12
            };
            check.IsCheckedChanged += (_, _) => d.Coupling.Set(bit, check.IsChecked == true);
            Grid.SetRow(check, i / 2);
            Grid.SetColumn(check, i % 2);
            grid.Children.Add(check);
        }
        panel.Children.Add(grid);

        var thermo = new CheckBox
        {
            Content = App.Loc["ThermoAmbient"],
            IsChecked = d.Coupling.ThermoDynamic,
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0)
        };
        thermo.IsCheckedChanged += (_, _) => d.Coupling.ThermoDynamic = thermo.IsChecked == true;
        panel.Children.Add(thermo);

        return new Border { Padding = new Thickness(8), Child = panel };
    }

    /// <summary>
    /// Card buttons draw vector icons rather than glyphs - Windows renders half
    /// the arrow and link characters as colour emoji, whatever the font asks for.
    /// </summary>
    private Button SmallButton(MaterialIconKind icon, string tip, Action onClick)
    {
        var btn = SmallButton(tip, onClick);
        btn.Content = new MaterialIcon { Kind = icon, Width = 16, Height = 16 };
        return btn;
    }

    private Button SmallButton(string tip, Action onClick)
    {
        var btn = new Button
        {
            FontSize = 13,
            Padding = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Width = 28,
            Height = 22,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = _hand
        };
        btn.Classes.Add("Basic");
        ToolTip.SetTip(btn, tip);
        btn.Click += (_, _) => onClick();
        return btn;
    }

    /// <summary>
    /// Orientation is a state, not just an action, so the button carries it: lit means
    /// this vehicle sits reversed. Reading it off the card beats a checkbox in a panel
    /// two columns away, and the card is rebuilt on every flip so the paint is current.
    /// </summary>
    private Button FlipButton(ConsistItem item)
    {
        var btn = SmallButton(MaterialIconKind.SwapHorizontal, App.Loc["TipFlip"],
                              () => _consist.Flip(item));
        if (item.Flipped)
        {
            btn.Foreground = VehicleCardStyle.Flipped;
            ToolTip.SetTip(btn, $"{App.Loc["TipFlip"]} \u2014 {App.Loc["Reversed"]}");
        }
        return btn;
    }

    private Button DriverButton(ConsistItem item)
    {
        var btn = SmallButton(App.Loc["TipDriver"], () => _consist.CycleDriver(item));
        btn.FontWeight = FontWeight.Bold;
        _driverChrome.Add((btn, item));
        PaintDriverButton(btn, item);
        return btn;
    }

    private void PaintDriverButton(Button btn, ConsistItem item)
    {
        var type = ReferenceEquals(item, _consist.Selected) && item.Cars.Count > 1
            ? _consist.ActiveCar(item).DriverType
            : item.Driver;

        (string glyph, string color) = type switch
        {
            eDriverType.Headdriver => ("1", "#4CAF50"),
            eDriverType.Reardriver => ("2", "#FF9800"),
            eDriverType.Passenger => ("P", "#2196F3"),
            _ => ("–", "#888888")
        };

        btn.Content = glyph;
        btn.Foreground = new SolidColorBrush(Color.Parse(color));
    }

    private ContextMenu BuildCardMenu(Control anchor, ConsistItem item)
    {
        var menu = new ContextMenu();
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();

            var num = new MenuItem { Header = App.Loc["WagonNumber"] };
            num.Click += (_, _) => ShowWagonNumberFlyout(anchor, item.Cars[0]);
            menu.Items.Add(num);

            menu.Items.Add(new Separator());

            var copyPrev = new MenuItem
            {
                Header = App.Loc["LoadCopyPrev"],
                IsEnabled = _consist.IndexOf(item) > 0
            };
            copyPrev.Click += (_, _) => { _cargo.CopyFromPrevious(_consist, item); _redraw(); };
            menu.Items.Add(copyPrev);

            var copyToAll = new MenuItem
            {
                Header = App.Loc["LoadCopyToAll"],
                IsEnabled = _cargo.IsLoadable(item)
            };
            copyToAll.Click += (_, _) => { _cargo.CopyToFollowing(_consist, item); _redraw(); };
            menu.Items.Add(copyToAll);

            var max = new MenuItem
            {
                Header = App.Loc["LoadMax"],
                IsEnabled = _cargo.IsLoadable(item)
            };
            max.Click += (_, _) => { _cargo.FillUnit(item); _redraw(); };
            menu.Items.Add(max);
        };
        return menu;
    }
}
