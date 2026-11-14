using System.Linq;
using System.Threading.Tasks;
using Avalonia.LogicalTree;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // The behavior cell is in a different tab from the Home browser, just as on WPF.
    internal Task<bool> SetBrowserPausedFromWorkshopAsync(bool paused, bool preserveSavedMute = true) =>
        BrowserView is { } browser ? browser.SetPausedAsync(paused, preserveSavedMute) : Task.FromResult(false);

    /// <summary>WPF SyncQuickControlsUI(pauseBrowser:) (MainWindow.Patreon.cs): the tube's Pause
    /// browser item repaints the Workshop switch without re-sending the pause.</summary>
    internal void SyncPauseBrowserUI(bool paused)
    {
        foreach (var cell in this.GetLogicalDescendants().OfType<WorkshopBehaviorCell>())
            cell.SetPauseBrowserChecked(paused);
    }
}
