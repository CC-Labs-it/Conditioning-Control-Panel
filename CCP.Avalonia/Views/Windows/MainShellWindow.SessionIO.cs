// NOT PORTED from ConditioningControlPanel/MainWindow/MainWindow.SessionIO.cs (2212 lines).
// Sorted member by member against the fifteen Core seams. SessionBtn_Edit and
// BtnCreateSession_Click are restored in PresetsTabView (EditSession / BtnCreateSession_Click) over
// Core SessionManager + SessionFileService; the rest is below.
//
// FIRST: the session rack pill (WPF MainWindow.SessionIO.cs:432-435) is live in PresetsTabView's
// rack row builder, over CreateCatalogueStatusBadge (MainShellWindow.CatalogueStatus.cs).
//
// SECOND: Services.SessionManager, SessionFileService and AssetImportService are now in Core
// (CCP.Core/Services/Session, CCP.Core/Services/Content). SessionManager owns AllSessions, the
// reload and add/remove events, DeleteSession's refusal to touch a built-in, and the disk sync;
// every member below either reads it or repaints something it changed. Models.Session IS in Core,
// which is why the FILTER half looks portable - see the next paragraph for why it is still out.
//
//   The rack - RepaintSessionRack, EnumerateRackSessions, BuildSessionRackRow, MakeRackMeta,
//   MakeRackPill, AttachRoundedClip, CreateSessionActionButton, CreateSessionDeleteButton,
//   CreateSessionRowButton, SelectSession, RefreshSessionRackSelection, HasSessionRackRow,
//   GenerateSessionTimelineDescription, RevealSessionInLibrary. The host panel for these rows
//   already exists on this head (Views/Tabs/PresetsTabView.axaml:653 - "ONE panel.
//   RepaintSessionRack clears and refills it"), so this is a real, bounded next layer.
//
//   The rack toolbar - EnsureSessionRackToolbar, RackSourceChip_Changed, RackDifficultyChip_Changed,
//   CmbRackSort_SelectionChanged, TxtRackSearch_TextChanged, ClearSessionRackFilters,
//   UpdateRackToolbarCounts, RackSourceLabel, RackSourceChipLabel, RackDifficultyLabel, the four
//   RackSource* constants and the six filter fields.
//
//   RackAccepts, SortRackSessions and RackFileStamp now live in Core's shared SessionRackQuery and
//   are consumed by PresetsTabView for its mounted available-only rack. The remaining WPF builder
//   and lifecycle stay here as execution/CRUD/import work; this note does not claim those actions
//   are restored on the Avalonia head.
//
//   Session lifecycle - InitializeSessionManager, OnSessionsReloaded, OnSessionAdded,
//   OnSessionRemoved, RegisterExternallySavedSession, SyncCustomSessionsFromDisk, GetSessionById,
//   SessionBtn_Share, SessionContextMenu_Export. RESTORED in PresetsTabView (session-io):
//   SessionBtn_Export, BtnExportSession_Click, ExportSessionToFile, SessionBtn_Delete and
//   HandleSessionDrop (a .session.json / .preset.json dropped on the Sessions tab).
//   SessionBtn_Share also needs the catalogue client (App.Catalogue) and the WRITE half of
//   MainShellWindow.CatalogueSubmissions.cs, which is out for its head-only SubmissionResult type.
//
//   Drag and drop - PORTED below: Window_DragEnter/Over/Leave/Drop, ImportDroppedFilesAsync,
//   UpdateDropOverlay, HandleAssetDropAsync, over Core DropRules (the DetectDropType lift both
//   heads call). Session/preset drops go to PresetsTabView; .ccpmod to HandleModDropAsync
//   (MainShellWindow.ModCatalogue.cs). Not ported: the single-media Play/Edit choices and the
//   enhancement import (no Deeper player/editor/library on this head) - a lone playable file
//   offers "Add to Asset Library"/Cancel only, and an enhancement drop says the library is not
//   ready, WPF's own string (docs/avalonia-decisions.html, 2026-10-09 window-wide drop).
//
//   The two global hotkeys - ApplyGlobalChatHotkey, ApplyGlobalCameraHotkey, ApplyCameraShortcutTo,
//   ParseModifiers, FormatCameraShortcut, RefreshChatShortcutLabel, RefreshCameraShortcutLabel,
//   BtnChatShortcut_Click, BtnCameraShortcut_Click, ToggleCameraCommand, OpenAvatarChat_Executed,
//   BringToForegroundAndOpenChat, _cameraCommandBound. RoutedUICommand and ModifierKeys are WPF
//   types, and a DESKTOP-WIDE hotkey is a Win32 RegisterHotKey - the bucket-E problem CLAUDE.md
//   describes, not a port. ToggleCameraCommand is additionally a camera control: see the refusal
//   already recorded for the two device pills in MainShellWindow.LabTab.cs.
//
//   RefreshImagesList / RefreshVideosList - the assets tab's lists, blocked with the asset tree in
//   MainShellWindow.Assets.cs.
//
// Checked and NOT the blocker: CoreSession. It answers the RUNNING session (start, stop, current
// state); this file is the session LIBRARY - files on disk, the rack that lists them, import and
// export. They share the word and nothing else.
//
// No member of this partial is referenced from MainShellWindow.axaml.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
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

            if (files.Length == 1 && File.Exists(files[0]) && DropRules.IsDeeperPlayableMedia(files[0]))
            {
                var choice = await ShowMediaDropChoiceAsync(files[0]);
                try
                {
                    switch (choice)
                    {
                    case "play": ShowOrActivateDeeperPlayer().OpenLocalMediaFile(files[0]); break;
                    case "edit":
                        OpenDeeperEditor(new Models.Deeper.Enhancement
                        {
                            MediaType = DropRules.AssetVideoExtensions.Contains(Path.GetExtension(files[0]))
                                ? Models.Deeper.MediaTypes.Video : Models.Deeper.MediaTypes.Audio,
                            MediaSource = files[0]
                        }, null);
                        break;
                    case "library": await HandleAssetDropAsync(files); break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to open dropped media");
                    await Dialogs.MessageDialog.ShowAsync(this, Loc.Get("title_import_error"),
                        Loc.GetF("msg_import_failed_0", ex.Message));
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

        private Window? _mediaDropDialog;

        // WPF ShowMediaDropChoiceDialog (MainWindow.Presets.cs:825-935): DarkerBg card with a pink
        // border and pink bold title, three pink buttons and a faint Cancel link. TextBlock
        // children, not Content strings: Avalonia parses "_" as an access key (CLAUDE.md trap 1).
        private async Task<string?> ShowMediaDropChoiceAsync(string path)
        {
            T? Res<T>(string key) where T : class => this.TryFindResource(key, out var r) ? r as T : null;
            var pink = Res<IBrush>("PinkBrush");
            var dialog = new Window
            {
                Title = Loc.Get("dlg_media_drop_title"), Width = 460, CanResize = false, MinHeight = 240, MaxHeight = 600,
                SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Res<IBrush>("DarkerBgBrush")
            };
            var scope = new NameScope();
            NameScope.SetNameScope(dialog, scope);
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = dialog.Title, Foreground = pink, FontSize = 18, FontWeight = FontWeight.Bold,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center, Margin = new global::Avalonia.Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBlock { Text = Path.GetFileName(path), Foreground = Brushes.White, FontSize = 12, Opacity = 0.75,
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new global::Avalonia.Thickness(0, 0, 0, 16) });
            foreach (var action in new[] { "play", "edit", "library", "cancel" })
            {
                var value = action;
                var cancel = action == "cancel";
                var button = new Button
                {
                    Name = "MediaDrop_" + action,
                    Theme = Res<global::Avalonia.Styling.ControlTheme>(cancel ? "SecondaryButton" : "ActionButton"),
                    Content = new TextBlock { Text = Loc.Get("dlg_media_drop_" + action) },
                    HorizontalAlignment = cancel ? global::Avalonia.Layout.HorizontalAlignment.Center : global::Avalonia.Layout.HorizontalAlignment.Stretch,
                    Padding = cancel ? new global::Avalonia.Thickness(8, 6) : new global::Avalonia.Thickness(20, 12),
                    Margin = new global::Avalonia.Thickness(0, cancel ? 8 : 0, 0, cancel ? 0 : 8),
                    FontSize = cancel ? 11 : 13, FontWeight = cancel ? FontWeight.Normal : FontWeight.SemiBold
                };
                if (cancel) { button.Background = Brushes.Transparent; button.Opacity = 0.7; }
                scope.Register(button.Name!, button);
                button.Click += (_, _) => dialog.Close(value);
                stack.Children.Add(button);
            }
            dialog.Content = new Border
            {
                Background = Res<IBrush>("DarkerBgBrush"), BorderBrush = pink, BorderThickness = new global::Avalonia.Thickness(2),
                CornerRadius = new global::Avalonia.CornerRadius(12), Padding = new global::Avalonia.Thickness(20), Child = stack
            };
            _mediaDropDialog = dialog;
            try { return await dialog.ShowDialogSafe<string?>(this); }
            finally { if (_mediaDropDialog == dialog) _mediaDropDialog = null; }
        }

        internal void StopDroppedMediaForPanic()
        {
            _mediaDropDialog?.Close();
            _deeperPlayer?.Close();
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
