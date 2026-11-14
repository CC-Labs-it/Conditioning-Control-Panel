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
                Hide();
                LauncherWindow.RouteBoot(this);
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
