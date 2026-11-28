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
    internal static Func<Window, Window, bool> RestackGameAboveOwner = X11Overlay.RestackAboveIfBelow;

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
                ResyncJustDropRender();
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

    private void ResyncJustDropRender()
    {
        if (RenderVisibilityResyncForTests is { } resync) resync();
        else NativeWindowOwner.ResyncRenderVisibility(this, Web.NativeView?.TryGetPlatformHandle());
    }

    // WPF ChaosWebViewHost.AttachMainWindowGlue: ownership is useful only while main is up.
    // Show(owner) would also couple Avalonia's managed visibility and lifetime.
    internal void AttachJustDropOwner(Window shell)
    {
        // Linux runs X11 on both X11 and Wayland sessions. A transient parent lets the WM
        // minimize and hide its children; use the WM restack request instead so the game keeps
        // its own taskbar entry and remains visible when the panel is minimized.
        var handle = NativeWindowOwner.HandleForTests?.Invoke(this) ?? TryGetPlatformHandle();
        if (X11Overlay.BackendOf(handle) == OverlayBackend.X11)
        {
            void QueueRestack() => Dispatcher.UIThread.Post(() =>
            {
                if (!_closing && IsVisible && WindowState != WindowState.Minimized &&
                    shell.IsVisible && shell.WindowState != WindowState.Minimized)
                    RestackGameAboveOwner(this, shell);
            }, DispatcherPriority.Background);
            void Activated(object? sender, EventArgs e) => QueueRestack();
            void Changed(object? sender, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty) QueueRestack();
            }
            // A raise of an already active panel need not emit Activated. Recheck the WM's
            // stacking order while open, even without focus or state changes.
            var watch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            watch.Tick += (_, _) => QueueRestack();
            watch.Start();
            void X11OwnerClosed(object? sender, EventArgs e) => Close();
            shell.Activated += Activated;
            shell.PropertyChanged += Changed;
            PropertyChanged += Changed;
            shell.Closed += X11OwnerClosed;
            Closed += (_, _) =>
            {
                watch.Stop();
                shell.Activated -= Activated;
                shell.PropertyChanged -= Changed;
                PropertyChanged -= Changed;
                shell.Closed -= X11OwnerClosed;
            };
            QueueRestack();
            return;
        }

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
                // WPF ChaosWebViewHost.cs:1111-1118: a healed hide re-drives the WebView render chain.
                if (NativeWindowOwner.RepairVisibility(this))
                    Dispatcher.UIThread.Post(() => { if (!_closing && IsVisible) ResyncJustDropRender(); }, DispatcherPriority.Background);
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
