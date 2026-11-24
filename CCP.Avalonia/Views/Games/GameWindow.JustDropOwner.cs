using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    internal bool JustDropOwnerLinked { get; private set; }
    internal Action? RenderVisibilityResyncForTests;

    internal static bool IsOwnerCascadeHide(uint message, IntPtr wParam, IntPtr lParam) =>
        message == 0x18 && wParam == IntPtr.Zero && lParam == new IntPtr(1);

    internal IntPtr VetoOwnerCascade(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_closing && IsOwnerCascadeHide(message, wParam, lParam))
        {
            NativeWindowOwner.Set(this, null);
            JustDropOwnerLinked = false;
            handled = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (_closing || !IsVisible || WindowState == WindowState.Minimized) return;
                if (RenderVisibilityResyncForTests is { } resync) resync();
                else NativeWindowOwner.ResyncRenderVisibility(this, Web.NativeView?.TryGetPlatformHandle());
            }, DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }

    // WPF DtrhHostService:210 and PieceByPieceHostService:211.
    internal void AttachEligibleGameOwner(Window shell)
    {
        if (Spec.Id is JustDropId or DtrhId or PbpId)
            AttachJustDropOwner(shell);
    }

    // WPF ChaosWebViewHost.AttachMainWindowGlue: ownership is useful only while main is up.
    // Keep it native: Show(owner) would also couple Avalonia's managed visibility/lifetime.
    internal void AttachJustDropOwner(Window shell)
    {
        void Refresh()
        {
            bool ownerUp = shell.IsVisible && shell.WindowState != WindowState.Minimized;
            JustDropOwnerLinked = NativeWindowOwner.Set(this, ownerUp ? shell : null) && ownerUp;
            if (ownerUp) return;
            // WPF EnsureNativeVisible (:1102-1110) never restores WindowState. Repair only
            // native cascade hiding once the queue drains; replay ducks before attaching.
            Dispatcher.UIThread.Post(() =>
            {
                if (_closing || !IsVisible || (shell.IsVisible && shell.WindowState != WindowState.Minimized)) return;
                NativeWindowOwner.RepairVisibility(this);
            }, DispatcherPriority.Background);
        }
        void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty) Refresh();
        }
        void OwnerClosed(object? sender, EventArgs e) => Close();
        // Only Win32 installs this hook; the headless test calls the same callback directly.
        bool hooked = OperatingSystem.IsWindows() &&
            X11Overlay.BackendOf(TryGetPlatformHandle()) == OverlayBackend.Win32;
        if (hooked) Win32Properties.AddWndProcHookCallback(this, VetoOwnerCascade);
        shell.PropertyChanged += OwnerChanged;
        shell.Closed += OwnerClosed;
        Closed += (_, _) =>
        {
            if (hooked) Win32Properties.RemoveWndProcHookCallback(this, VetoOwnerCascade);
            shell.PropertyChanged -= OwnerChanged;
            shell.Closed -= OwnerClosed;
            JustDropOwnerLinked = false;
        };
        Refresh();
    }
}
