using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.Fyp;

/// <summary>
/// Right-click a library item in the For You feed: "Open file" / "Show in folder" (ccp-bugs #1290).
/// The page only sends the manifest id (a path relative to the assets folder), so the host decides
/// what that id points at and refuses anything that leaves the folder.
/// </summary>
internal static class FypFileMenu
{
    /// <summary>Shows the menu at the pointer. UI thread only.</summary>
    internal static void Show(string? id)
    {
        var path = FypFileRules.ResolveLocal(App.EffectiveAssetsPath, id);
        if (path == null || !File.Exists(path)) return;

        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        var open = new MenuItem { Header = Loc.Get("fyp_menu_open_file") };
        open.Click += (_, _) => OpenFile(path);
        var reveal = new MenuItem { Header = Loc.Get("fyp_menu_show_in_folder") };
        reveal.Click += (_, _) => ExplorerLauncher.RevealInExplorer(path);
        menu.Items.Add(open);
        menu.Items.Add(reveal);
        menu.IsOpen = true;
    }

    private static void OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("FypFileMenu: open failed for {Path}: {E}", path, ex.Message);
        }
    }
}
