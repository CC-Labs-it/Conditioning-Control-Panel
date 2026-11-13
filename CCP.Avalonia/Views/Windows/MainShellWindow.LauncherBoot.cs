using System;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    /// <summary>WPF MainWindow.Launcher.cs:51 ShowHiddenForBoot: run Opened work without exposing or activating the panel.</summary>
    internal void PrepareLauncherBoot()
    {
        bool activated = ShowActivated, taskbar = ShowInTaskbar;
        // Portable opacity concealment before Show; native chrome visibility still needs live evidence.
        Opacity = 0;
        ShowActivated = false;
        ShowInTaskbar = false;
        BuildingHiddenForBoot = true;
        Platform.StartupLadder.Hold();
        Opened += Route;
        void Route(object? sender, EventArgs e)
        {
            Opened -= Route;
            try
            {
                // WPF MainWindow.Launcher.cs:74 hides before App.xaml.cs:4290 routes,
                // even if Lockdown began during Opened. The launcher veil owns that surface.
                // RouteBoot hides through HideForLauncher too, and releases the hold if routing fails.
                try { Hide(); }
                catch (Exception ex) { Serilog.Log.Debug(ex, "[Launcher] hide before boot routing"); }
                LauncherWindow.RouteBoot(this);
            }
            catch
            {
                // WPF App.xaml.cs:4358 releases on any routing failure; a Hide throw here must not
                // leave the ladder held (WPF's own Hide throw, App.xaml.cs:3704, rethrows held).
                Platform.StartupLadder.Release();
                throw;
            }
            finally
            {
                BuildingHiddenForBoot = false;
                ShowActivated = activated;
                ShowInTaskbar = taskbar;
                Opacity = 1;
            }
        }
    }
}
