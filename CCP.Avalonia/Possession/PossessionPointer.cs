// WPF Services/Possession/PossessionPointer.cs:22-160.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

/// <summary>UI-thread pointer hints for the currently attached shell.</summary>
internal sealed class PossessionPointer
{
    private Window? _window;
    private DateTime _lastMoveAt = DateTime.MinValue;
    internal Point Position { get; private set; }
    internal Vector Velocity { get; private set; }
    internal Control? Hovered { get; private set; }
    internal Control? LastClicked { get; private set; }
    internal DateTime LastClickAt { get; private set; } = DateTime.MinValue;
    internal event Action<Point, Vector>? Moved;
    internal event Action<Control?>? HoverChanged;
    internal event Action<Control>? Pressed;

    internal void Attach(Window window)
    {
        if (ReferenceEquals(_window, window)) return;
        Detach();
        _window = window;
        window.AddHandler(InputElement.PointerMovedEvent, OnMove, RoutingStrategies.Tunnel, true);
        window.AddHandler(InputElement.PointerPressedEvent, OnPress, RoutingStrategies.Tunnel, true);
        window.Closed += OnClosed;
    }

    internal void Detach()
    {
        if (_window is not { } window) return;
        _window = null;
        window.RemoveHandler(InputElement.PointerMovedEvent, OnMove);
        window.RemoveHandler(InputElement.PointerPressedEvent, OnPress);
        window.Closed -= OnClosed;
        Hovered = LastClicked = null;
        Velocity = default;
        _lastMoveAt = DateTime.MinValue;
    }

    private void OnClosed(object? sender, EventArgs e) => Detach();
    private void OnMove(object? sender, PointerEventArgs e)
    {
        try { if (_window is { } window) Move(e.GetPosition(window), e.Source as Visual, DateTime.Now); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Possession pointer move failed"); }
    }
    private void OnPress(object? sender, PointerPressedEventArgs e)
    {
        try { if (_window is { } window) Press(e.GetPosition(window), e.Source as Visual, DateTime.Now); }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Possession pointer press failed"); }
    }

    internal void Move(Point point, Visual? source, DateTime now)
    {
        var dt = (now - _lastMoveAt).TotalSeconds;
        if (dt > 0.001 && dt < 0.5)
            Velocity = new Vector(Velocity.X * 0.65 + (point.X - Position.X) / dt * 0.35,
                                  Velocity.Y * 0.65 + (point.Y - Position.Y) / dt * 0.35);
        Position = point;
        _lastMoveAt = now;
        Moved?.Invoke(Position, Velocity);
        var hit = Interesting(source);
        if (ReferenceEquals(hit, Hovered)) return;
        Hovered = hit;
        HoverChanged?.Invoke(hit);
    }

    internal void Press(Point point, Visual? source, DateTime now)
    {
        Position = point;
        var hit = Interesting(source);
        if (hit == null) return;
        LastClicked = hit;
        LastClickAt = now;
        Pressed?.Invoke(hit);
    }

    internal static Control? Interesting(Visual? source)
    {
        Control? card = null;
        var node = source;
        for (var depth = 0; node != null && depth < 24; depth++, node = node.GetVisualParent())
        {
            if (node is not Control c) continue;
            if (PossessionTree.GetExclude(c)) return null;
            if (PossessionTree.GetRole(c) != PossessionRole.None ||
                c is Button or Slider or ComboBox or TextBox or ListBoxItem or TabItem or ScrollBar)
                return c;
            if (card == null && c is Border b && b.CornerRadius.TopLeft > 0 &&
                b.Background != null && b.Bounds.Height >= 60) card = b;
        }
        return card;
    }
}
