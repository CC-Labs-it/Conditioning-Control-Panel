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
    internal static Func<MainShellWindow?> Shell = () => MainShellWindow.Current;
    internal static Func<string, bool> Navigate = url =>
        Shell()?.NavigateToUrlInBrowser(url, autoPlayFullscreen: true) == true;

    internal static ICommand CommandFor(string url) => new OpenCommand(url);

    internal static async Task OpenAsync(string? url)
    {
        try
        {
            if (RemoteControlTabView.Relay.IsValueCreated && RemoteControlTabView.Relay.Value.ControllerConnected)
                return;
            if (!CompanionLinkNavigation.TryBrowserUri(url, out var uri)) return;
            // WPF AvatarTubeWindow.Speech.cs:1512-1529: user clicks replace the prior session.
            var shell = Shell();
            shell?.ClaimAvatarBrowserSession();
            bool issued;
            try { issued = Navigate(url!); }
            catch { shell?.EndAvatarBrowserSession(); throw; }
            if (issued) return;
            shell?.EndAvatarBrowserSession();
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
