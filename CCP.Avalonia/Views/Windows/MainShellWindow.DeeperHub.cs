// PORTED from ConditioningControlPanel/MainWindow/MainWindow.DeeperHub.cs (row actions, keyboard
// selection, reload) and the library half of MainWindow.DeeperTab.cs (OpenDeeperFile,
// OpenDeeperEditor, the player entry points, open folder, two-step delete with undo, the welcome
// card's demo/tour buttons). The index, filter and sort live in DeeperTabViewModel over Core
// DeeperLocalLibrary; this partial owns the windows, dialogs, timers and files.
//
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF BtnDeeperCatalogue_Click (MainWindow.DeeperHub.cs:477).</summary>
        internal const string DeeperCatalogueUrl = "https://app.cclabs.app/catalogue";

        /// <summary>WPF DeeperDeleteGraceMs: the row is gone at once, the file after this.</summary>
        internal static readonly TimeSpan DeeperDeleteGrace = TimeSpan.FromMilliseconds(6000);
        internal static TimeProvider DeeperDeleteTime = TimeProvider.System;

        /// <summary>WPF ShowStyledDialog(title, msg, Delete, Cancel); tests answer it.</summary>
        internal static Func<Window, string, string, Task<bool>> DeeperConfirm = (owner, title, message) =>
            MessageDialog.ConfirmAsync(owner, title, message, okText: Loc.Get("btn_delete"), cancelText: Loc.Get("btn_cancel"));

        private readonly Dictionary<string, DeeperEditorWindow> _deeperOpenEditors = new(StringComparer.OrdinalIgnoreCase);
        private EnhancementPlayerWindow? _deeperPlayer;
        private DispatcherTimer? _deeperDeleteTimer;

        private DeeperTabViewModel? DeeperModel => Named<DeeperTabView>("DeeperTab")?.DataContext as DeeperTabViewModel;

        /// <summary>Reloads the local index when the Deeper view is attached or the tab opens.</summary>
        private EnhancementLibrary? _deeperLibrary;
        private bool _deeperHubClosed;
        internal void InitializeDeeperHub()
        {
            if (_deeperHubClosed) return;
            EnsureDeeperLibrary();
            ReloadDeeperLibraryFromDisk();
        }

        /// <summary>Creates the watched library once, without a rescan (import reloads once itself).</summary>
        private void EnsureDeeperLibrary()
        {
            if (_deeperHubClosed) return;
            if (_deeperLibrary == null)
            {
                _deeperLibrary = new EnhancementLibrary();
                _deeperLibrary.LibraryChanged += OnDeeperLibraryChanged;
                Closed += (_, _) =>
                {
                    _deeperHubClosed = true;
                    _deeperLibrary.LibraryChanged -= OnDeeperLibraryChanged;
                    _deeperLibrary.Dispose();
                };
            }
        }

        private void OnDeeperLibraryChanged(object? sender, EventArgs e)
            => Dispatcher.UIThread.Post(() => { if (!_deeperHubClosed && Named<DeeperTabView>("DeeperTab") is { IsVisible: true }) ReloadDeeperLibraryFromDisk(); });

        private void ReloadDeeperLibraryFromDisk() => DeeperModel?.ReloadLibrary();

        /// <summary>WPF ApplyDeeperFilterAndSort after a submission record changed: repaint badges.</summary>
        internal void RefreshDeeperLibraryRows() => DeeperModel?.Refresh();

        /// <summary>WPF OpenDeeperFile (MainWindow.DeeperTab.cs:748): load, then the editor.</summary>
        internal async void OpenDeeperFile(string path)
        {
            try
            {
                var enhancement = EnhancementSerializer.LoadFromFile(path);
                DeeperEditorWindow.TouchRecent(path);   // EnhancementLibrary.Open records it
                OpenDeeperEditor(enhancement, path);
            }
            catch (Exception ex)
            {
                if (ex is not EnhancementLoadException) Log.Warning(ex, "Failed to open Deeper file");
                if (IsVisible) await MessageDialog.ShowAsync(this, "Deeper", ex.Message);
            }
        }

        /// <summary>WPF OpenDeeperEditor: one editor per file (re-activated, not duplicated); closing
        /// it refreshes the hub list.</summary>
        internal void OpenDeeperEditor(Models.Deeper.Enhancement enhancement, string? filePath)
        {
            string? key = filePath is null ? null : DeeperTabViewModel.FullPath(filePath);
            if (key != null && _deeperOpenEditors.TryGetValue(key, out var existing))
            {
                if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
                existing.Activate();
                return;
            }
            var window = new DeeperEditorWindow(enhancement, filePath);
            if (key != null) _deeperOpenEditors[key] = window;
            window.Closed += (_, _) =>
            {
                if (key != null) _deeperOpenEditors.Remove(key);
                ReloadDeeperLibraryFromDisk();
            };
            if (IsVisible) window.Show(this); else window.Show();
        }

        /// <summary>WPF EnhancementPlayerWindow.ShowOrActivate: one player window, reused.</summary>
        private EnhancementPlayerWindow ShowOrActivateDeeperPlayer()
        {
            if (_deeperPlayer is { } open)
            {
                if (open.WindowState == WindowState.Minimized) open.WindowState = WindowState.Normal;
                open.Activate();
                return open;
            }
            var player = new EnhancementPlayerWindow(null, null);
            player.Closed += (_, _) => { if (ReferenceEquals(_deeperPlayer, player)) _deeperPlayer = null; };
            _deeperPlayer = player;
            if (IsVisible) player.Show(this); else player.Show();
            return player;
        }

        /// <summary>WPF BtnDeeperOpenPlayer_Click (MainWindow.DeeperTab.cs:202).</summary>
        internal void BtnDeeperOpenPlayer_Click()
        {
            CoreBark.NotifyUiAction("deeper_player");
            try { ShowOrActivateDeeperPlayer(); }
            catch (Exception ex) { Log.Error(ex, "Failed to open Deeper player"); }
        }

        /// <summary>WPF DeeperRowPlay_Click (MainWindow.DeeperHub.cs:577): a player that already has
        /// this file is only brought forward - reloading would restart playback from the top.</summary>
        internal void PlayDeeperLibraryEntry(string path)
        {
            try
            {
                var already = _deeperPlayer != null && DeeperTabViewModel.PathsEqual(_deeperPlayer.LoadedFilePath, path);
                var player = ShowOrActivateDeeperPlayer();
                if (!already) player.LoadEnhancementFile(path);
            }
            catch (Exception ex) { Log.Error(ex, "Failed to open Deeper player for enhancement"); }
        }

        /// <summary>WPF BtnDeeperOpenLibraryFolder_Click: create it if needed, then the file manager.</summary>
        internal void OpenDeeperLibraryFolder()
        {
            try
            {
                Directory.CreateDirectory(DeeperLocalLibrary.DefaultFolder);
                Platform.ExternalOpener.Open(DeeperLocalLibrary.DefaultFolder);
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to open Deeper library folder"); }
        }

        /// <summary>WPF DeeperRowMenuReveal_Click (explorer /select). Linux file managers have no
        /// portable "select this file", so the containing folder opens.</summary>
        internal static void RevealDeeperFile(string path)
        {
            try
            {
                if (File.Exists(path)) Platform.ExternalOpener.Open(Path.GetDirectoryName(Path.GetFullPath(path)));
            }
            catch (Exception ex) { Log.Warning(ex, "Deeper: reveal in folder failed"); }
        }

        internal void ImportEnhancementFiles(System.Collections.Generic.IEnumerable<string> paths)
        {
            var sourcePaths = paths.ToArray();
            EnsureDeeperLibrary();
            var lib = _deeperLibrary;
            if (lib == null)
            {
                App.Notifications?.Show(Loc.Get("deeper_import_library_not_ready"),
                    Helpers.NotificationType.Warning);
                return;
            }

            var imported = new System.Collections.Generic.List<string>();
            var errors = new System.Collections.Generic.List<string>();
            int skippedNonEnhancement = 0;
            string? lastImportedPath = null;
            string? duplicateOfPath = null;

            foreach (var path in sourcePaths)
            {
                var fileName = System.IO.Path.GetFileName(path);
                if (!EnhancementImportRules.IsImportablePath(path))
                {
                    errors.Add(string.Format(Loc.Get("deeper_import_err_not_enh_fmt"), fileName));
                    continue;
                }
                try
                {
                    // Plain .json that is not an enhancement (settings.json, a
                    // preset...) is skipped quietly rather than reported as a failure.
                    if (!ConditioningControlPanel.Services.Deeper.EnhancementImportRules.FileLooksLikeEnhancement(path))
                    {
                        skippedNonEnhancement++;
                        continue;
                    }
                    // Same file, or identical content under another name: no
                    // "Name (2)" copy, just point at the row that is already there.
                    var existing = lib.FindDuplicateOf(path);
                    if (existing != null)
                    {
                        duplicateOfPath = existing;
                        continue;
                    }
                    // Validate by loading; bad schema / oversized files throw with
                    // a useful message from the serializer.
                    var enhancement = ConditioningControlPanel.Services.Deeper.EnhancementSerializer.LoadFromFile(path);
                    var saved = lib.PromoteToLibrary(enhancement, sourceTag: "import");
                    if (saved == null)
                    {
                        errors.Add(string.Format(Loc.Get("deeper_import_err_write_fmt"), fileName));
                        continue;
                    }
                    lastImportedPath = saved;
                    imported.Add(System.IO.Path.GetFileName(saved));
                }
                catch (ConditioningControlPanel.Services.Deeper.EnhancementLoadException ex)
                {
                    errors.Add($"{fileName}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "ImportEnhancementFiles: import failed");
                    errors.Add($"{fileName}: {ex.GetType().Name}");
                }
            }

            // Remember the source folder for next manual import so the file dialog
            // opens where the user picked from.
            if (lastImportedPath != null)
            {
                var src = sourcePaths.FirstOrDefault();
                if (!string.IsNullOrEmpty(src))
                {
                    var dir = System.IO.Path.GetDirectoryName(src);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        CoreSettings.Current.DeeperLastDirectory = dir;
                        CoreSettings.Save();
                    }
                }
            }

            // Force-refresh the hub list now in addition to the FileSystemWatcher
            // signal, since the watcher's debounce can lag a fast manual import.
            try { ReloadDeeperLibraryFromDisk(); } catch (Exception ex) { Diag.Swallowed(ex); }

            // Toasts, like the rest of the tab, instead of stock modals.
            if (imported.Count > 0)
            {
                var msg = imported.Count == 1
                    ? string.Format(Loc.Get("deeper_import_done_one_fmt"), imported[0])
                    : string.Format(Loc.Get("deeper_import_done_many_fmt"), imported.Count);
                App.Notifications?.Show(msg, Helpers.NotificationType.Success);
            }
            if (errors.Count > 0)
            {
                App.Notifications?.Show(
                    string.Format(Loc.Get("deeper_import_failed_fmt"), errors.Count) + "\n" + string.Join("\n", errors),
                    Helpers.NotificationType.Warning, TimeSpan.FromSeconds(10));
            }
            else if (imported.Count == 0)
            {
                if (duplicateOfPath != null)
                    App.Notifications?.Show(Loc.Get("deeper_import_already_in_library"), Helpers.NotificationType.Info);
                else if (skippedNonEnhancement > 0)
                    App.Notifications?.Show(Loc.Get("deeper_import_skipped_not_enh"), Helpers.NotificationType.Info);
            }

            var reveal = lastImportedPath ?? duplicateOfPath;
            if (reveal != null) RevealDeeperLibraryRow(reveal);
        }

        internal async Task PickDeeperImportsAsync()
        {
            try { CoreBark.NotifyUiAction("deeper_import"); } catch (Exception ex) { Diag.Swallowed(ex); }   // WPF MainWindow.DeeperTab.cs:745
            EnsureDeeperLibrary();
            if (_deeperLibrary == null) return;
            var files = await StorageProvider.OpenFilePickerAsync(new global::Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = Loc.Get("deeper_import_dialog_title"), AllowMultiple = true,
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(_deeperLibrary!.LastDirectory),
                FileTypeFilter = new[]
                {
                    new global::Avalonia.Platform.Storage.FilePickerFileType("Deeper enhancements") { Patterns = new[] { "*.ccpenh.json" } },
                    new global::Avalonia.Platform.Storage.FilePickerFileType("JSON files") { Patterns = new[] { "*.json" } },
                    global::Avalonia.Platform.Storage.FilePickerFileTypes.All,
                }
            });
            if (files.Count == 0) return;   // cancelled: WPF returns without a rescan
            ImportEnhancementFiles(files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray());
        }

        internal void RevealDeeperLibraryRow(string path)
        {
            if (Named<DeeperTabView>("DeeperTab") is not { } view || DeeperModel is not { } model) return;
            var index = model.Highlight(path);
            if (index < 0) return;
            Dispatcher.UIThread.Post(() => view.ScrollToRow(index), DispatcherPriority.Loaded);
            _ = ClearRevealAsync(model, path);
        }

        /// <summary>The reveal's 1800 ms wait (WPF RevealDeeperLibraryRow); tests swap in a completion signal.</summary>
        internal static Func<TimeSpan, Task> DeeperRevealDelay = Task.Delay;

        private static async Task ClearRevealAsync(DeeperTabViewModel model, string path)
        {
            await DeeperRevealDelay(TimeSpan.FromMilliseconds(1800));
            model.ClearHighlight(path);
        }

        // ---- the welcome card (WPF MainWindow.DeeperTab.cs:71-118) ----

        internal void BtnDeeperWelcomeTour_Click()
        {
            CoreBark.NotifyUiAction("deeper_tour");
            DismissDeeperWelcomeCard();
            // ponytail: StartTutorial(TutorialType.Deeper) - no TutorialService on this head.
        }

        internal void BtnDeeperWelcomeDemo_Click()
        {
            DismissDeeperWelcomeCard();
            OpenDeeperBundledDemo();
        }

        /// <summary>The bundled demo is matched by file name, so a moved library still finds it.</summary>
        private async void OpenDeeperBundledDemo()
        {
            try
            {
                var match = DeeperLocalLibrary.Scan().Entries.FirstOrDefault(e =>
                    string.Equals(Path.GetFileName(e.FilePath), "welcome.ccpenh.json", StringComparison.OrdinalIgnoreCase));
                if (match != null) { OpenDeeperFile(match.FilePath); return; }
                if (IsVisible) await MessageDialog.ShowAsync(this, Loc.Get("deeper_dialog_title"), Loc.Get("deeper_demo_missing_text"));
            }
            catch (Exception ex) { Log.Debug("Open bundled Deeper demo failed: {Error}", ex.Message); }
        }

        // ---- two-step delete (WPF MainWindow.DeeperTab.cs:926-1020) ----

        /// <summary>Confirm, hide the row at once, recycle the file after the grace; the toast's
        /// Undo cancels it. Pending paths stay out of every rescan.</summary>
        internal async void DeleteDeeperLibraryEntry(EnhancementLibraryEntry entry)
        {
            try
            {
                var label = string.IsNullOrEmpty(entry.Name) ? Path.GetFileName(entry.FilePath) : entry.Name;
                if (!await DeeperConfirm(this, Loc.Get("deeper_library_delete_title"),
                        string.Format(Loc.Get("deeper_library_delete_confirm_fmt"), label))) return;
                if (DeeperModel is not { } model) return;

                var key = DeeperTabViewModel.FullPath(entry.FilePath);
                if (model.PendingDeletes.ContainsKey(key)) return;
                model.PendingDeletes[key] = DeeperDeleteTime.GetTimestamp();
                _deeperDeleteTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Background,
                    (_, _) => CommitDueDeeperDeletes());
                _deeperDeleteTimer.Start();
                ReloadDeeperLibraryFromDisk();

                App.Notifications.Show(string.Format(Loc.Get("deeper_library_deleted_toast_fmt"), label),
                    Helpers.NotificationType.Info, DeeperDeleteGrace, Loc.Get("btn_undo"), () =>
                    {
                        if (!model.PendingDeletes.Remove(key)) return;
                        ReloadDeeperLibraryFromDisk();
                    });
            }
            catch (Exception ex) { Log.Warning(ex, "Deeper: delete failed"); }
        }

        /// <summary>The grace timer's tick: recycles every pending delete whose grace has run out.</summary>
        internal void CommitDueDeeperDeletes()
        {
            if (DeeperModel is not { } model) return;
            foreach (var (key, started) in model.PendingDeletes.ToList())
            {
                if (DeeperDeleteTime.GetElapsedTime(started) < DeeperDeleteGrace) continue;
                model.PendingDeletes.Remove(key);
                CommitDeeperDelete(key);
            }
            if (model.PendingDeletes.Count == 0) _deeperDeleteTimer?.Stop();
        }

        /// <summary>WPF: called on a real exit so a confirmed delete is not lost with the timer.</summary>
        private void CommitPendingDeeperDeletesOnExit()
        {
            _deeperDeleteTimer?.Stop();
            if (DeeperModel is not { } model || model.PendingDeletes.Count == 0) return;
            foreach (var key in model.PendingDeletes.Keys.ToList())
            {
                model.PendingDeletes.Remove(key);
                try { CommitDeeperDelete(key); } catch { }
            }
        }

        private void CommitDeeperDelete(string fullPath)
        {
            try
            {
                if (File.Exists(fullPath)) Platform.TrashBin.Move(fullPath);
                // The submission record is keyed by this path; a later file of the same name must
                // not inherit its badge.
                if (CoreSettings.Current.DeeperSubmissions.Remove(CanonicalCataloguePathKey(fullPath)))
                    CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to delete Deeper library entry");
                App.Notifications.Show(ex.Message, Helpers.NotificationType.Error);
            }
            ReloadDeeperLibraryFromDisk();
        }
    }
}
