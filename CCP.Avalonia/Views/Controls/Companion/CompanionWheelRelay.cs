using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ConditioningControlPanel.Views.Controls.Companion;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion;

/// <summary>Pass a spent wheel notch above a bounded list, matching WPF's preview forwarder.</summary>
internal static class CompanionWheelRelay
{
    public static void Attach(Control host)
    {
        Detach(host);
        host.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
    }

    public static void Detach(Control host) =>
        host.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);

    private static void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Handled || sender is not Control host) return;
        var inner = host as ScrollViewer ?? host.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (inner is null || !CompanionWheelLogic.ShouldForward(
            inner.Extent.Height - inner.Viewport.Height, inner.Offset.Y, e.Delta.Y)) return;
        // Raise above the host: a new tunnel through the list would recurse into this handler.
        if (host.GetVisualParent() is not Control parent || TopLevel.GetTopLevel(host) is not { } root) return;
        e.Handled = true;
        parent.RaiseEvent(new PointerWheelEventArgs(parent, e.Pointer, root, e.GetPosition(root),
            e.Timestamp, e.GetCurrentPoint(root).Properties, e.KeyModifiers, e.Delta));
    }
}
