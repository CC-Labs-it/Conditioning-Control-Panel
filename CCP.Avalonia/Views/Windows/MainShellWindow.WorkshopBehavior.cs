using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Views.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // The behavior cell is in a different tab from the Home browser, just as on WPF.
    internal Task<bool> SetBrowserPausedFromWorkshopAsync(bool paused) =>
        BrowserView is { } browser ? browser.SetPausedAsync(paused) : Task.FromResult(false);
}
