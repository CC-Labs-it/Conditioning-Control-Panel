using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace ConditioningControlPanel.Avalonia.Helpers;

/// <summary>WPF InnerScrollViewer_PreviewMouseWheel: always scroll the nearest outer viewer.</summary>
internal static class ParentWheel
{
    internal static void Attach(ScrollViewer inner) =>
        inner.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);

    private static void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (sender is not ScrollViewer inner || e.Delta.Y == 0) return;
        if (inner.FindAncestorOfType<ScrollViewer>() is not { } parent) return;
        // WPF uses the raw 120-unit wheel notch, even when the inner viewer can still scroll.
        parent.Offset = parent.Offset.WithY(parent.Offset.Y - e.Delta.Y * 120);
        e.Handled = true;
    }
}
