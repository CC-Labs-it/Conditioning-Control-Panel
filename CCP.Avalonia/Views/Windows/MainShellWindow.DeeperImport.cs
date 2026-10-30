using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal static Func<MainShellWindow, Task<string[]>> DeeperImportPicker = PickDeeperImports;

    private static async Task<string[]> PickDeeperImports(MainShellWindow owner)
    {
        var folder = CoreSettings.Current.DeeperLastDirectory;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) folder = DeeperLocalLibrary.DefaultFolder;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Get("deeper_import_dialog_title"),
            AllowMultiple = true,
            SuggestedStartLocation = await owner.StorageProvider.TryGetFolderFromPathAsync(folder),
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Deeper enhancements") { Patterns = new[] { "*.ccpenh.json" } },
                new FilePickerFileType("JSON files") { Patterns = new[] { "*.json" } },
                FilePickerFileTypes.All,
            },
        });
        return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
    }

    internal async void BtnDeeperImport_Click()
    {
        CoreBark.NotifyUiAction("deeper_import");
        try { ImportEnhancementFiles(await DeeperImportPicker(this)); }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Deeper import picker failed"); }
    }

    internal void ImportEnhancementFiles(string[] paths)
    {
        if (paths.Length == 0) return;
        var result = EnhancementImportBatch.Run(DeeperLibrary, paths);
        if (result.LastImportedPath != null && Path.GetDirectoryName(paths[0]) is { Length: > 0 } source)
        {
            CoreSettings.Current.DeeperLastDirectory = source;
            CoreSettings.Save();
        }
        ReloadDeeperLibraryFromDisk();
        if (result.Imported.Count > 0)
            App.Notifications.Show(result.Imported.Count == 1
                ? Loc.GetF("deeper_import_done_one_fmt", result.Imported[0])
                : Loc.GetF("deeper_import_done_many_fmt", result.Imported.Count), Helpers.NotificationType.Success);
        if (result.Errors.Count > 0)
            App.Notifications.Show(Loc.GetF("deeper_import_failed_fmt", result.Errors.Count) + "\n" + string.Join("\n", result.Errors),
                Helpers.NotificationType.Warning, TimeSpan.FromSeconds(10));
        else if (result.Imported.Count == 0)
        {
            if (result.DuplicateOfPath != null)
                App.Notifications.Show(Loc.Get("deeper_import_already_in_library"), Helpers.NotificationType.Info);
            else if (result.SkippedNonEnhancement > 0)
                App.Notifications.Show(Loc.Get("deeper_import_skipped_not_enh"), Helpers.NotificationType.Info);
        }
        if (result.RevealPath is { } reveal) Named<DeeperTabView>("DeeperTab")?.RevealLibraryRow(reveal);
    }
    // WPF MainWindow.DeeperTab.cs:604-674: file-association and player editor entry points.
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
