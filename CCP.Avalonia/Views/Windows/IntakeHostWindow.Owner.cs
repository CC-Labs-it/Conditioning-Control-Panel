using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

internal sealed partial class IntakeHostWindow
{
    internal bool IntakeOwnerLinked { get; private set; }

    // WPF ChaosWebViewHost.AttachMainWindowGlue: ownership is useful only while main is up.
    // Keep it native: Show(owner) would also couple Avalonia's managed visibility/lifetime.
    internal void AttachIntakeOwner(Window shell)
    {
        void Refresh()
        {
            bool ownerUp = shell.IsVisible && shell.WindowState != WindowState.Minimized;
            IntakeOwnerLinked = NativeWindowOwner.Set(this, ownerUp ? shell : null) && ownerUp;
            // WPF also vetoes Win32 cascade hide before it lands; this head repairs
            // it after the notification. The pre-hide veto still needs a native message seam.
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
        shell.PropertyChanged += OwnerChanged;
        shell.Closed += OwnerClosed;
        Closed += (_, _) =>
        {
            shell.PropertyChanged -= OwnerChanged;
            shell.Closed -= OwnerClosed;
            IntakeOwnerLinked = false;
        };
        Refresh();
    }
}
