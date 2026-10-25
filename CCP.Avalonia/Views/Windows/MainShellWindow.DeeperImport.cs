using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal async void ImportDeeperFiles()
    {
        CoreBark.NotifyUiAction("deeper_import");
        try
        {
            var directory = CoreSettings.Current.DeeperLastDirectory;
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Loc.Get("deeper_import_dialog_title"), AllowMultiple = true,
                SuggestedStartLocation = Directory.Exists(directory)
                    ? await StorageProvider.TryGetFolderFromPathAsync(directory!) : null,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Deeper enhancements") { Patterns = new[] { "*.ccpenh.json" } },
                    new FilePickerFileType("JSON files") { Patterns = new[] { "*.json" } },
                    FilePickerFileTypes.All
                }
            });
            if (files.Count > 0) ImportEnhancementFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>());
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Deeper import picker failed"); }
    }

    /// <summary>WPF RevealDeeperLibraryRow's 1800 ms flash; one live timer, stopped on close.</summary>
    internal global::Avalonia.Threading.DispatcherTimer? DeeperRevealTimer;

    internal EnhancementImportBatch ImportEnhancementFiles(IEnumerable<string> paths)
    {
        var result = EnhancementImportBatch.Run(DeeperLibrary, paths);
        ReloadDeeperLibraryFromDisk();
        if (result.Imported.Count > 0)
            App.Notifications.Show(result.Imported.Count == 1
                ? string.Format(Loc.Get("deeper_import_done_one_fmt"), result.Imported[0])
                : string.Format(Loc.Get("deeper_import_done_many_fmt"), result.Imported.Count), Helpers.NotificationType.Success);
        if (result.Errors.Count > 0)
            App.Notifications.Show(string.Format(Loc.Get("deeper_import_failed_fmt"), result.Errors.Count)
                + "\n" + string.Join("\n", result.Errors), Helpers.NotificationType.Warning, TimeSpan.FromSeconds(10));
        else if (result.Imported.Count == 0)
        {
            if (result.DuplicatePath != null)
                App.Notifications.Show(Loc.Get("deeper_import_already_in_library"), Helpers.NotificationType.Info);
            else if (result.Skipped > 0)
                App.Notifications.Show(Loc.Get("deeper_import_skipped_not_enh"), Helpers.NotificationType.Info);
        }
        // WPF RevealDeeperLibraryRow: retain the highlight across watcher-driven row rebuilds.
        var reveal = result.LastImportedPath ?? result.DuplicatePath;
        if (reveal != null && DeeperModel is { } model)
        {
            var index = -1;
            for (var i = 0; i < model.FilteredEntries.Count && index < 0; i++)
                if (Tabs.DeeperTabViewModel.PathsEqual(model.FilteredEntries[i].Entry.FilePath, reveal)) index = i;
            model.Highlight(reveal);
            if (DeeperRevealTimer == null) Closed += (_, _) => DeeperRevealTimer?.Stop();
            else DeeperRevealTimer.Stop();
            var timer = DeeperRevealTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (Tabs.DeeperTabViewModel.PathsEqual(model.HighlightedPath, reveal)) model.Highlight(null);
            };
            timer.Start();
            Named<Tabs.DeeperTabView>("DeeperTab")?.FindControl<ItemsControl>("DeeperLibraryList")?
                .ContainerFromIndex(index)?.BringIntoView();
        }
        return result;
    }

    public void OpenInDeeperPlayer(string mediaPath)
    {
        try { ShowOrActivateDeeperPlayer().OpenLocalMediaFile(mediaPath); }
        catch (Exception ex) { ReportDeeperOpenError(ex, "Player"); }
    }

    public void OpenDeeperEditorFromPlayer(string path)
    {
        if (!string.IsNullOrWhiteSpace(path)) OpenDeeperFile(path);
    }

    public void OpenInDeeperEditorForMedia(string mediaPath)
    {
        try
        {
            OpenDeeperEditor(new Models.Deeper.Enhancement
            {
                MediaType = Services.DropRules.AssetVideoExtensions.Contains(Path.GetExtension(mediaPath))
                    ? Models.Deeper.MediaTypes.Video : Models.Deeper.MediaTypes.Audio,
                MediaSource = mediaPath
            }, null);
        }
        catch (Exception ex) { ReportDeeperOpenError(ex, "Editor"); }
    }

    public void HandlePendingFileOpen(string action, string path)
    {
        if (string.IsNullOrEmpty(action) || string.IsNullOrEmpty(path)) return;
        if (action == "play") OpenInDeeperPlayer(path);
        else if (action == "edit") OpenInDeeperEditorForMedia(path);
    }

    private async void ReportDeeperOpenError(Exception ex, string surface)
    {
        Serilog.Log.Error(ex, "Failed to open Deeper {Surface}", surface);
        if (IsVisible) await Dialogs.MessageDialog.ShowAsync(this, $"Open {surface} failed",
            $"Couldn't open Deeper {surface}:\n\n{ex.GetType().Name}: {ex.Message}");
    }
}
