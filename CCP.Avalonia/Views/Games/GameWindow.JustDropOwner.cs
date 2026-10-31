using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    internal bool JustDropOwnerLinked { get; private set; }

    // WPF ChaosWebViewHost.AttachMainWindowGlue: ownership is useful only while main is up.
    // Keep it native: Show(owner) would also couple Avalonia's managed visibility/lifetime.
    private void AttachJustDropOwner(MainShellWindow shell)
    {
        var lastState = WindowState;
        void RememberState(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty && WindowState != WindowState.Minimized)
                lastState = WindowState;
        }
        void Refresh()
        {
            bool ownerUp = shell.IsVisible && shell.WindowState != WindowState.Minimized;
            JustDropOwnerLinked = NativeWindowOwner.Set(this, ownerUp ? shell : null) && ownerUp;
            if (ownerUp) return;
            // ponytail: WPF also vetoes Win32 cascade hide before it lands; this head repairs
            // it after the notification. The pre-hide veto still needs a native message seam.
            // A manual owner minimize can reach the WM before the property notification. Repair
            // any native cascade once it drains; our own replay duck happens BEFORE attaching.
            Dispatcher.UIThread.Post(() =>
            {
                if (_closing || !IsVisible || (shell.IsVisible && shell.WindowState != WindowState.Minimized)) return;
                if (WindowState == WindowState.Minimized) WindowState = lastState;
                NativeWindowOwner.RepairVisibility(this);
            }, DispatcherPriority.Background);
        }
        void OwnerChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty) Refresh();
        }
        void OwnerClosed(object? sender, EventArgs e) => Close();
        PropertyChanged += RememberState;
        shell.PropertyChanged += OwnerChanged;
        shell.Closed += OwnerClosed;
        Closed += (_, _) =>
        {
            PropertyChanged -= RememberState;
            shell.PropertyChanged -= OwnerChanged;
            shell.Closed -= OwnerClosed;
            JustDropOwnerLinked = false;
        };
        Refresh();
    }
}
