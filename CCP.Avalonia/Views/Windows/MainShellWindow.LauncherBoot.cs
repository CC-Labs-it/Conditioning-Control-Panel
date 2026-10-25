using System;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    /// <summary>Run Opened initialization without displaying the panel before the launcher.
    /// WPF MainWindow.ShowHiddenForBoot + App.RouteBootSurface.</summary>
    internal void PrepareHiddenForLauncherBoot()
    {
        bool activated = ShowActivated, inTaskbar = ShowInTaskbar;
        double opacity = Opacity;
        StartupLadder.HoldForLauncher(); // before any Opened handler can offer a surface
        ShowActivated = false;
        ShowInTaskbar = false;
        BuildingHiddenForBoot = true;
        // ponytail: X11 has no DWM cloak; conceal its content before the first frame.
        // Windows also cloaks the native surface, including its chrome and shadow.
        Opacity = 0;
        LauncherBootCloak.Set(this, true);
        void Route(object? sender, EventArgs e)
        {
            Opened -= Route;
            try { LauncherWindow.RouteBoot(this); }
            finally
            {
                BuildingHiddenForBoot = false;
                ShowActivated = activated;
                ShowInTaskbar = inTaskbar;
                Opacity = opacity;
                LauncherBootCloak.Set(this, false);
            }
        }
        Opened += Route;
    }
}
