// Window-wide imports. WPF MainWindow.SessionIO.cs:1470-1654.
// Session library CRUD remains in PresetsTabView; enhancement import in DeeperImport.
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        internal void CloseDroppedMediaPlayer() => _deeperPlayer?.Close();

        private AssetImportService? _assetImportService;

        internal static string[] DroppedPaths(DragEventArgs e) =>
            e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray()
            ?? Array.Empty<string>();

        private void SetDropOverlay(bool visible)
        {
            if (Named<Border>("GlobalDropOverlay") is { } overlay) overlay.IsVisible = visible;
        }

        /// <summary>WPF Window_DragEnter (SessionIO.cs:1072): an unrecognised drop is accepted
        /// with no overlay (the drop explains itself in a toast).</summary>
        private void Window_DragEnter(object? sender, DragEventArgs e)
        {
            var files = DroppedPaths(e);
            var dropType = DropRules.Detect(files);
            e.DragEffects = dropType == DropType.None ? DragDropEffects.None : DragDropEffects.Copy;
            if (dropType is not DropType.None and not DropType.Unrecognised)
            {
                UpdateDropOverlay(dropType, files);
                SetDropOverlay(true);
            }
            e.Handled = true;
        }

        private void Window_DragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = DropRules.Detect(DroppedPaths(e)) == DropType.None ? DragDropEffects.None : DragDropEffects.Copy;
            e.Handled = true;
        }

        private void Window_DragLeave(object? sender, RoutedEventArgs e) => SetDropOverlay(false);

        private async void Window_Drop(object? sender, DragEventArgs e)
        {
            SetDropOverlay(false);
            if (e.Handled) return;   // the Sessions tab already imported it
            e.Handled = true;
            await ImportDroppedFilesAsync(DroppedPaths(e));
        }

        /// <summary>WPF ImportDroppedFilesAsync (SessionIO.cs:1470): the one door for a file drop.</summary>
        internal async Task ImportDroppedFilesAsync(string[]? files)
        {
            if (files == null || files.Length == 0) return;

            // WPF MainWindow.SessionIO.cs:1474-1484; MainWindow.Presets.cs:822-921.
            if (files.Length == 1 && File.Exists(files[0]) && DropRules.IsDeeperPlayableMedia(files[0]))
            {
                var choice = await new Dialogs.MediaDropDialog(files[0]).ShowDialogSafe<Dialogs.MediaDropChoice>(this);
                try
                {
                    switch (choice)
                    {
                        case Dialogs.MediaDropChoice.Play:
                            ShowOrActivateDeeperPlayer().OpenLocalMediaFile(files[0]); break;
                        case Dialogs.MediaDropChoice.Edit:
                            OpenDeeperEditor(new Models.Deeper.Enhancement
                            {
                                MediaType = DropRules.AssetVideoExtensions.Contains(Path.GetExtension(files[0]))
                                    ? Models.Deeper.MediaTypes.Video : Models.Deeper.MediaTypes.Audio,
                                MediaSource = files[0],
                            }, null); break;
                        case Dialogs.MediaDropChoice.Library: await HandleAssetDropAsync(files); break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to open dropped media");
                    var target = choice == Dialogs.MediaDropChoice.Play ? "Player" : "Editor";
                    await Dialogs.MessageDialog.ShowAsync(this, $"Open {target} failed",
                        $"Couldn't open Deeper {target}:\n\n{ex.GetType().Name}: {ex.Message}");
                }
                return;
            }

            switch (DropRules.Detect(files))
            {
                case DropType.Session:
                    Named<Tabs.PresetsTabView>("PresetsTab")?.HandleSessionDrop(files[0]);
                    break;
                case DropType.Preset:
                    Named<Tabs.PresetsTabView>("PresetsTab")?.HandlePresetDrop(files[0]);
                    break;
                case DropType.Enhancement:
                    ImportEnhancementFiles(files);
                    break;
                case DropType.Mod:
                    await HandleModDropAsync(files[0]);
                    break;
                case DropType.Assets:
                case DropType.Zip:
                case DropType.Folder:
                    await HandleAssetDropAsync(files);
                    break;
                case DropType.Unrecognised:
                    App.Notifications.Show(Loc.Get("drop_not_recognised"), Helpers.NotificationType.Info);
                    break;
            }
        }

        /// <summary>WPF UpdateDropOverlay (SessionIO.cs:1621), same icons, keys and subtitles.</summary>
        private void UpdateDropOverlay(DropType dropType, string[] files)
        {
            if (Named<TextBlock>("DropOverlayIcon") is not { } icon
                || Named<TextBlock>("DropOverlayTitle") is not { } title
                || Named<TextBlock>("DropOverlaySubtitle") is not { } subtitle) return;

            var first = Path.GetFileName(files[0]);
            (icon.Text, title.Text, subtitle.Text) = dropType switch
            {
                DropType.Session => ("📋", Loc.Get("label_drop_to_import_session"), first),
                DropType.Preset => ("🎛️", Loc.Get("label_drop_to_import_preset"), first),
                DropType.Enhancement => ("🌊", Loc.Get("label_drop_to_import_enhancement"),
                    files.Length == 1 ? first : $"{files.Length} enhancements"),
                DropType.Mod => ("🧩", Loc.Get("label_drop_to_install_mod"), first),
                DropType.Zip => ("📦", Loc.Get("label_drop_to_extract_assets"), ZipSubtitle(files)),
                DropType.Folder => ("📁", Loc.Get("label_drop_to_import_folder"), "Scan for images & videos"),
                DropType.Assets => ("🖼️", Loc.Get("label_drop_to_import_assets"),
                    files.Length == 1 ? first : $"{files.Length} files"),
                _ => (icon.Text, title.Text, subtitle.Text),
            };
        }

        private static string ZipSubtitle(string[] files)
        {
            var zips = files.Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToArray();
            return zips.Length == 1 ? Path.GetFileName(zips[0]) : $"{zips.Length} ZIP files";
        }

        /// <summary>WPF HandleAssetDropAsync (SessionIO.cs:1707): Core AssetImportService copies
        /// (never moves) into the assets folder, then the Assets tab and the summary.</summary>
        private async Task HandleAssetDropAsync(string[] paths)
        {
            try
            {
                _assetImportService ??= new AssetImportService();
                var result = await Task.Run(() => _assetImportService.ImportAsync(paths));
                // WPF MainWindow.SessionIO.cs:1638-1642 invalidates the dealt video queue.
                if (result.VideosImported > 0) CoreEngine.Video?.ReloadAssets();
                ShowTab("assets");
                Log.Information("Asset import complete: {Summary}", result.GetSummary());
                await Dialogs.MessageDialog.ShowAsync(this, Loc.Get("title_import_complete"), result.GetSummary());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Asset import failed");
                await Dialogs.MessageDialog.ShowAsync(this, Loc.Get("title_import_error"), Loc.GetF("msg_import_failed_0", ex.Message));
            }
        }
    }
}
