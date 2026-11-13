using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;

namespace ConditioningControlPanel.Avalonia.Controls;

// WPF Controls/ScrolllerCreditMark.cs:79: one independent click-through credit for all holders.
internal static class ScrolllerCreditChip
{
    private static readonly HashSet<object> Holders = new(ReferenceEqualityComparer.Instance);
    private static Window? _window;
    internal static int HolderCount => Holders.Count;

    internal static bool Hold(object owner, PixelRect stage)
    {
        if (Holders.Contains(owner)) return true;
        if (_window == null)
        {
            var bounds = new PixelRect(stage.Right - 112, stage.Bottom - 35, 100, 25);
            var badge = new ScrolllerCreditBadge();
            var w = new Window
            {
                SystemDecorations = WindowDecorations.None,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                Background = Brushes.Transparent,
                ShowInTaskbar = false, ShowActivated = false, Focusable = false,
                IsHitTestVisible = false, CanResize = false, Topmost = true,
                Position = bounds.Position, Width = bounds.Width, Height = bounds.Height,
                Content = badge
            };
            try
            {
                var safe = X11Overlay.SetClickThrough(w, true) & X11Overlay.SetOverrideRedirect(w, bounds);
                if (!safe && !ChaosFlashOverlay.SkipPlatformChecksForTests) { w.Close(); return false; }
                w.Show();
                X11Overlay.SetOverrideRedirect(w, bounds);
                X11Overlay.Raise(w);
                badge.SetShown(true);
                _window = w;
            }
            catch (Exception ex) { Serilog.Log.Debug("Scrolller credit: {Error}", ex.Message); w.Close(); return false; }
        }
        Holders.Add(owner);
        return true;
    }

    internal static void Release(object owner)
    {
        if (!Holders.Remove(owner) || Holders.Count != 0) return;
        _window?.Close();
        _window = null;
    }
}
