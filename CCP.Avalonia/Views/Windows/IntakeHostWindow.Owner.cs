using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

internal sealed partial class IntakeHostWindow
{
    internal bool IntakeOwnerLinked { get; private set; }

    internal IntPtr VetoOwnerCascade(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_closed && message == 0x18 && wParam == IntPtr.Zero && lParam == new IntPtr(1))
        {
            NativeWindowOwner.Set(this, null);
            IntakeOwnerLinked = false;
            handled = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_closed && IsVisible && WindowState != WindowState.Minimized)
                    Web.RepairWindowsRenderVisibility();
            }, DispatcherPriority.Background);
        }
        return IntPtr.Zero;
    }

    // WPF ChaosWebViewHost.AttachMainWindowGlue: ownership is useful only while main is up.
    // Keep it native: Show(owner) would also couple Avalonia's managed visibility/lifetime.
    internal void AttachIntakeOwner(Window shell)
    {
        void Refresh()
        {
            bool ownerUp = shell.IsVisible && shell.WindowState != WindowState.Minimized;
            IntakeOwnerLinked = NativeWindowOwner.Set(this, ownerUp ? shell : null) && ownerUp;
            // WPF EnsureNativeVisible (:1102-1110) never restores WindowState. Repair only
            // native cascade hiding once the queue drains, even if the owner has restored.
            Dispatcher.UIThread.Post(() =>
            {
                if (_closed || !IsVisible) return;
                if (NativeWindowOwner.RepairVisibility(this))
                    Web.RepairWindowsRenderVisibility();
            }, DispatcherPriority.Background);
        }
        void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty) Refresh();
        }
        void OwnerClosed(object? sender, EventArgs e) => Close();
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
            IntakeOwnerLinked = false;
        };
        Refresh();
    }
}
