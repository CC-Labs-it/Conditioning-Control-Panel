using System;
using System.Threading.Tasks;
using System.Windows.Input;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Views.Controls.Companion;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion;

internal static class CompanionLinkLauncher
{
    // Small host seam: tests capture navigation without creating a native browser or real desktop.
    internal static Func<string, bool> Navigate = url =>
        MainShellWindow.Current?.NavigateToUrlInBrowser(url, autoPlayFullscreen: true) == true;

    internal static ICommand CommandFor(string url) => new OpenCommand(url);

    internal static async Task OpenAsync(string? url)
    {
        try
        {
            if (RemoteControlTabView.Relay.IsValueCreated && RemoteControlTabView.Relay.Value.ControllerConnected)
                return;
            if (!CompanionLinkNavigation.TryBrowserUri(url, out var uri)) return;
            // ponytail: browser media-session ownership and the native autoplay/fullscreen hook
            // still belong to the browser follow-up. Plain navigation is already hosted here.
            if (Navigate(url!)) return;
            // WPF's offline gate must also cover this last-resort external path.
            if (CoreSettings.Current.OfflineMode) return;
            if (CompanionLinkNavigation.CanOpenExternally(uri!))
                await AppUpdater.OpenUrl(MainShellWindow.Current, url!);
        }
        catch (Exception ex) { Log.Warning(ex, "Companion watch chip failed to open its link"); }
    }

    private sealed class OpenCommand(string url) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await OpenAsync(url);
    }
}
