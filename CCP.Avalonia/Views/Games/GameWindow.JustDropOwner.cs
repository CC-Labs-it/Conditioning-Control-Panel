using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    internal bool JustDropOwnerLinked { get; private set; }

    // WPF ChaosWebViewHost.AttachMainWindowGlue: ownership is useful only while main is up.
    // Keep it native: Show(owner) would also couple Avalonia's managed visibility/lifetime.
    internal void AttachJustDropOwner(Window shell)
    {
        void Refresh()
        {
            bool ownerUp = shell.IsVisible && shell.WindowState != WindowState.Minimized;
            JustDropOwnerLinked = NativeWindowOwner.Set(this, ownerUp ? shell : null) && ownerUp;
            if (ownerUp) return;
            // ponytail: WPF also vetoes Win32 cascade hide before it lands; this head repairs
            // it after the notification. The pre-hide veto still needs a native message seam.
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
        shell.PropertyChanged += OwnerChanged;
        shell.Closed += OwnerClosed;
        Closed += (_, _) =>
        {
            shell.PropertyChanged -= OwnerChanged;
            shell.Closed -= OwnerClosed;
            JustDropOwnerLinked = false;
        };
        Refresh();
    }
}
