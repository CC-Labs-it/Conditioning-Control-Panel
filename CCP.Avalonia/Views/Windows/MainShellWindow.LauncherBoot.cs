using System;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    /// <summary>WPF ShowHiddenForBoot: run Opened work without exposing or activating the panel.</summary>
    internal void PrepareLauncherBoot()
    {
        bool activated = ShowActivated, taskbar = ShowInTaskbar;
        // ponytail: top-level opacity is the portable boot cloak; unlike the panel crossfade,
        // this hides the entire native window, including chrome, before its first Show.
        Opacity = 0;
        ShowActivated = false;
        ShowInTaskbar = false;
        BuildingHiddenForBoot = true;
        Platform.StartupLadder.Hold();
        Opened += Route;
        void Route(object? sender, EventArgs e)
        {
            Opened -= Route;
            try { LauncherWindow.RouteBoot(this); }
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
