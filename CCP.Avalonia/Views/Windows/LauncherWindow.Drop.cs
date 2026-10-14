using System;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class LauncherWindow
    {
        // WPF LauncherWindow.xaml.cs:147-159: accept any file drop; the panel sorts its type.
        private void Launcher_DragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = e.DataTransfer.TryGetFiles() is null ? DragDropEffects.None : DragDropEffects.Copy;
            e.Handled = true;
        }

        private void Launcher_Drop(object? sender, DragEventArgs e)
        {
            if (e.DataTransfer.TryGetFiles() is null) return;
            e.Handled = true;
            var files = MainShellWindow.DroppedPaths(e);
            if (files.Length == 0) return;
            // WPF LauncherHost.OpenPanelWithDrop: import only once the panel is up, so its
            // importer can own any modal and its notification host can report the result.
            OpenPanel(async panel =>
            {
                try { await panel.ImportDroppedFilesAsync(files); }
                catch (Exception ex) { Log.Warning(ex, "[Launcher] dropped files failed to import"); }
            });
        }
    }
}
