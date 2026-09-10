using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

using StarterNG.Classes;
using StarterNG.Domain;
using StarterNG.Infrastructure;
using StarterNG.Domain.Vehicles;

namespace StarterNG.Views;

public sealed class ConsistDragging
{
    private const double DragThreshold = 6;

    private const string DragVehicleFormat = "starterng/vehicle";

    private readonly Panel consistStack;
    private readonly ScrollViewer consistScroll;
    private readonly Canvas consistOverlay;
    private readonly Control consistDropTarget;
    private readonly ListBox vehicleListBox;
    private readonly Control miniPreviewPanel;

    private readonly Consist _consist;
    private readonly Func<VehicleTexture?> _browserSelected;
    private readonly Action<VehicleTexture, int> _insertTexture;
    private readonly Action<Dynamic> _selectInBrowser;

    private readonly DropGap _dropGap = new();
    private readonly EdgeScroller _edgeScroller = new();
    private readonly DropIndexTracker _dropTracker = new();

    private PointerPressedEventArgs? _pendingDragPress;
    private Point _pendingDragOrigin;
    private VehicleTexture? _pendingDragTexture;
    private int _pendingDragConsistIndex = -1;
    private ConsistItem? _pendingBrowserSync;
    private bool _dragSessionActive;

    private int _pressCardIndex = -1;
    private Point _pressPoint;
    private bool _cardDragging;
    private double _grabOffset;
    private double _dragCardWidth = 64;
    private Control? _carried;

    private VehicleTexture? _dragTexture;
    private int _dragConsistIndex = -1;

    public ConsistDragging(
        Panel consistStack, ScrollViewer consistScroll, Canvas consistOverlay,
        Control consistDropTarget, ListBox vehicleListBox, Control miniPreviewPanel,
        Consist consist, Func<VehicleTexture?> browserSelected,
        Action<VehicleTexture, int> insertTexture, Action<Dynamic> selectInBrowser)
    {
        this.consistStack = consistStack;
        this.consistScroll = consistScroll;
        this.consistOverlay = consistOverlay;
        this.consistDropTarget = consistDropTarget;
        this.vehicleListBox = vehicleListBox;
        this.miniPreviewPanel = miniPreviewPanel;

        _consist = consist;
        _browserSelected = browserSelected;
        _insertTexture = insertTexture;
        _selectInBrowser = selectInBrowser;
    }

    public void Attach()
    {
        consistStack.PointerMoved += Strip_OnPointerMoved;
        consistStack.PointerReleased += Strip_OnPointerReleased;
        consistStack.PointerCaptureLost += (_, _) => EndCardDrag();

        DragDrop.SetAllowDrop(consistDropTarget, true);
        consistDropTarget.AddHandler(DragDrop.DragOverEvent, Consist_OnDragOver);
        consistDropTarget.AddHandler(DragDrop.DropEvent, Consist_OnDrop);
        consistDropTarget.AddHandler(DragDrop.DragLeaveEvent, (_, _) => CloseDropGap());

        vehicleListBox.AddHandler(InputElement.PointerMovedEvent, PendingDrag_OnPointerMoved, handledEventsToo: true);
        vehicleListBox.AddHandler(InputElement.PointerReleasedEvent, PendingDrag_OnPointerReleased, handledEventsToo: true);
        miniPreviewPanel.AddHandler(InputElement.PointerMovedEvent, PendingDrag_OnPointerMoved, handledEventsToo: true);
        miniPreviewPanel.AddHandler(InputElement.PointerReleasedEvent, PendingDrag_OnPointerReleased, handledEventsToo: true);
    }

    public void ArmCardDrag(int index, PointerPressedEventArgs e)
    {
        _pressCardIndex = index;
        _pressPoint = e.GetPosition(consistStack);
        _cardDragging = false;
    }

    public void Reset()
    {
        _dropGap.Forget();
        _dropTracker.Reset();
    }

    public void MiniPreviewPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_browserSelected() is null || !e.GetCurrentPoint(miniPreviewPanel).Properties.IsLeftButtonPressed)
            return;
        ArmVehicleDrag(e, miniPreviewPanel, _browserSelected());
    }

    public void VehicleListPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(vehicleListBox).Properties.IsLeftButtonPressed)
            return;

        var hit = e.Source as Control;
        while (hit != null && hit is not ListBoxItem)
            hit = hit.Parent as Control;
        if (hit is ListBoxItem { Tag: VehicleTexture texture })
            ArmVehicleDrag(e, vehicleListBox, texture);
    }

    private void ArmVehicleDrag(PointerPressedEventArgs e, Visual relativeTo, VehicleTexture texture)
    {
        ClearPendingDrag();
        _pendingDragPress = e;
        _pendingDragOrigin = e.GetPosition(relativeTo);
        _pendingDragTexture = texture;
        _pendingDragConsistIndex = -1;
    }

    private void ClearPendingDrag()
    {
        _pendingDragPress = null;
        _pendingDragTexture = null;
        _pendingDragConsistIndex = -1;
        _pendingBrowserSync = null;
        _dragSessionActive = false;
    }

    private async void PendingDrag_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pendingDragPress is null || _dragSessionActive || sender is not Visual relativeTo)
            return;
        if (!e.GetCurrentPoint(relativeTo).Properties.IsLeftButtonPressed)
        {
            FinishPendingClick();
            return;
        }

        var delta = e.GetPosition(relativeTo) - _pendingDragOrigin;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
            return;

        var press = _pendingDragPress;
        var texture = _pendingDragTexture;
        int consistIndex = _pendingDragConsistIndex;

        _pendingBrowserSync = null;
        _pendingDragPress = null;
        _dragSessionActive = true;

        if (texture != null)
        {
            _dragTexture = texture;
            _dragConsistIndex = -1;
            CaptureCardMidpoints();
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(DragVehicleFormat));
            try
            {
                await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy);
            }
            finally
            {
                _dragTexture = null;
                CloseDropGap();
                ClearPendingDrag();
            }
            return;
        }

    }

    private void PendingDrag_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragSessionActive) return;
        FinishPendingClick();
    }

    private void FinishPendingClick()
    {
        var sync = _pendingBrowserSync;
        ClearPendingDrag();

        if (sync is { Cars.Count: > 0 })
            _selectInBrowser(sync.Cars[0]);
    }

    private void Consist_OnDragOver(object? sender, DragEventArgs e)
    {

        if (_dragTexture != null || _dragConsistIndex >= 0)
        {
            e.DragEffects = _dragConsistIndex >= 0 ? DragDropEffects.Move : DragDropEffects.Copy;
            e.Handled = true;

            double pointerX = e.GetPosition(consistScroll).X;
            UpdateDropIndex(pointerX + consistScroll.Offset.X);
            _edgeScroller.Update(consistScroll, pointerX);
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void Strip_OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(consistStack);

        if (!_cardDragging)
        {
            if (_pressCardIndex < 0 ||
                !e.GetCurrentPoint(consistStack).Properties.IsLeftButtonPressed)
                return;

            var delta = point - _pressPoint;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
                return;

            BeginCardDrag(e);
            if (!_cardDragging) return;
        }

        Canvas.SetLeft(_carried!, point.X - _grabOffset);

        double pointerX = e.GetPosition(consistScroll).X;
        UpdateDropIndex(pointerX + consistScroll.Offset.X);
        _edgeScroller.Update(consistScroll, pointerX);
    }

    private void Strip_OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_cardDragging)
        {
            int clicked = _pressCardIndex;
            _pressCardIndex = -1;

            if (e.InitialPressMouseButton == MouseButton.Left &&
                clicked >= 0 && clicked < _consist.Count &&
                _consist[clicked].Cars.Count > 0)
                _selectInBrowser(_consist[clicked].Cars[0]);
            return;
        }

        int from = _dragConsistIndex;
        int target = _dropTracker.Index;

        EndCardDrag();

        if (from >= 0 && target >= 0)
            _consist.Move(from, target);
    }

    private void BeginCardDrag(PointerEventArgs e)
    {
        int card = _pressCardIndex;
        if (card < 0 || 2 * card + 1 >= consistStack.Children.Count)
            return;

        var cardVisual = consistStack.Children[2 * card];
        var coupler = consistStack.Children[2 * card + 1];
        var bounds = cardVisual.Bounds;

        _dragConsistIndex = card;
        _dragTexture = null;
        _dragCardWidth = Math.Max(bounds.Width + coupler.Bounds.Width, 24);
        _grabOffset = Math.Clamp(_pressPoint.X - bounds.Left, 0, _dragCardWidth);

        CaptureCardMidpoints();

        _carried = new Border
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Opacity = 0.85,
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(2),
            BorderBrush = VehicleCardStyle.BorderSelected,
            Background = VehicleCardStyle.BackgroundSelected,
            Child = new Image
            {
                Source = CardSnapshot(cardVisual),
                Stretch = Stretch.Uniform
            }
        };
        Canvas.SetTop(_carried, bounds.Top);
        Canvas.SetLeft(_carried, bounds.Left);
        consistOverlay.Children.Add(_carried);

        cardVisual.IsVisible = false;
        coupler.IsVisible = false;

        _cardDragging = true;
        e.Pointer.Capture(consistStack);
    }

    private static Bitmap? CardSnapshot(Control source)
    {
        if (source.Bounds.Width < 1 || source.Bounds.Height < 1)
            return null;

        try
        {
            var size = new PixelSize((int)source.Bounds.Width, (int)source.Bounds.Height);
            var bitmap = new RenderTargetBitmap(size, new Vector(96, 96));
            bitmap.Render(source);
            return bitmap;
        }
        catch (Exception ex)
        {
            Infrastructure.Diagnostics.Log("Card snapshot", ex);
            return null;
        }
    }

    private void EndCardDrag()
    {
        if (_dragConsistIndex >= 0 && 2 * _dragConsistIndex + 1 < consistStack.Children.Count)
        {
            consistStack.Children[2 * _dragConsistIndex].IsVisible = true;
            consistStack.Children[2 * _dragConsistIndex + 1].IsVisible = true;
        }

        if (_carried != null)
            consistOverlay.Children.Remove(_carried);
        _carried = null;

        CloseDropGap();
        _dragConsistIndex = -1;
        _pressCardIndex = -1;
        _cardDragging = false;
    }

    private void CaptureCardMidpoints()
    {
        var midpoints = new List<double>();
        for (int i = 0; i < consistStack.Children.Count; i += 2)
        {
            int card = i / 2;
            if (card == _dragConsistIndex) continue;

            var child = consistStack.Children[i];
            if (_dropGap.IsGap(child)) continue;

            midpoints.Add(child.Bounds.Center.X -
                          (_dragConsistIndex >= 0 && card > _dragConsistIndex ? _dragCardWidth : 0));
        }
        _dropTracker.Capture(midpoints);
    }

    private void UpdateDropIndex(double contentX)
    {
        if (!_dropTracker.Update(contentX)) return;

        int card = _dropTracker.Index;
        if (_dragConsistIndex >= 0 && card > _dragConsistIndex)
            card++;

        _dropGap.Show(consistStack, 2 * card, _dragCardWidth);
    }

    private void CloseDropGap()
    {
        _dropTracker.Reset();
        _edgeScroller.Stop();
        _dropGap.Hide(consistStack);
    }

    private void Consist_OnDrop(object? sender, DragEventArgs e)
    {
        int target = _dropTracker.Index >= 0 ? _dropTracker.Index : _consist.Count;
        CloseDropGap();

        if (_dragTexture != null)
        {
            _insertTexture(_dragTexture, target);
            e.Handled = true;
        }
        else if (_dragConsistIndex >= 0)
        {
            _consist.Move(_dragConsistIndex, target);
            e.Handled = true;
        }
    }
}
