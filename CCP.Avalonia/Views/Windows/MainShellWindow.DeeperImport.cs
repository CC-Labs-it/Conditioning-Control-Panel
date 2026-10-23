// PORTED from ConditioningControlPanel/MainWindow/MainWindow.DeeperTab.cs BtnDeeperImport_Click and
// ImportEnhancementFiles (:743-:879) and MainWindow.DeeperHub.cs RevealDeeperLibraryRow (:746).
// Both doors land here: the Deeper tab's Import buttons (hero and empty library) and a
// .ccpenh.json dropped anywhere on the window (MainShellWindow.SessionIO.cs, DropType.Enhancement).
// The library is Core EnhancementLibrary (FindDuplicateOf / PromoteToLibrary); this head keeps no
// long-lived instance (no watcher yet, see shell-deeper-hub), so one is made per import and disposed.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF row flash after an import (DeeperHub.cs:771, 1800 ms).</summary>
        internal static readonly TimeSpan DeeperRevealFlash = TimeSpan.FromMilliseconds(1800);

        /// <summary>WPF OpenFileDialog (DeeperTab.cs:746): multi-select, start folder; tests answer it.
        /// Returns local paths, or null when cancelled.</summary>
        internal static Func<Window, string, Task<IReadOnlyList<string>?>> DeeperImportPicker = PickDeeperImportFilesAsync;

        private static async Task<IReadOnlyList<string>?> PickDeeperImportFilesAsync(Window owner, string startDir)
        {
            var sp = owner.StorageProvider;
            if (!sp.CanOpen) return null;
            var options = new FilePickerOpenOptions
            {
                Title = Loc.Get("deeper_import_dialog_title"),
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Deeper enhancements (*.ccpenh.json)") { Patterns = new[] { "*" + EnhancementLibrary.FileSuffix } },
                    new FilePickerFileType("JSON files (*.json)") { Patterns = new[] { "*.json" } },
                    new FilePickerFileType("All files (*.*)") { Patterns = new[] { "*" } },
                },
            };
            try { options.SuggestedStartLocation = await sp.TryGetFolderFromPathAsync(startDir); }
            catch (Exception ex) { Log.Debug("Deeper import: no start folder: {E}", ex.Message); }
            var files = await sp.OpenFilePickerAsync(options);
            return files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        }

        /// <summary>WPF BtnDeeperImport_Click.</summary>
        internal async void BtnDeeperImport_Click()
        {
            try { CoreBark.NotifyUiAction("deeper_import"); } catch (Exception ex) { Log.Debug(ex, "bark"); }
            // WPF EnhancementLibrary.LastDirectory: the last import folder, else the library.
            var last = CoreSettings.Current.DeeperLastDirectory;
            var start = !string.IsNullOrEmpty(last) && Directory.Exists(last) ? last : DeeperLocalLibrary.DefaultFolder;
            IReadOnlyList<string>? files;
            try { files = await DeeperImportPicker(this, start); }
            catch (Exception ex)
            {
                Log.Warning(ex, "Deeper import picker failed");
                return;
            }
            if (files is { Count: > 0 }) ImportEnhancementFiles(files);
        }

        /// <summary>WPF ImportEnhancementFiles: validate, dedupe, copy into the library, toast, reveal.</summary>
        internal void ImportEnhancementFiles(IReadOnlyList<string> paths)
        {
            EnhancementLibrary lib;
            try { lib = new EnhancementLibrary(); }
            catch (Exception ex)
            {
                Log.Warning(ex, "Deeper import: library unavailable");
                App.Notifications.Show(Loc.Get("deeper_import_library_not_ready"), Helpers.NotificationType.Warning);
                return;
            }

            var imported = new List<string>();
            var errors = new List<string>();
            int skippedNonEnhancement = 0;
            string? lastImportedPath = null;
            string? duplicateOfPath = null;

            using (lib)
            {
                foreach (var path in paths)
                {
                    var fileName = Path.GetFileName(path);
                    if (!EnhancementImportRules.IsImportablePath(path))
                    {
                        errors.Add(Loc.GetF("deeper_import_err_not_enh_fmt", fileName));
                        continue;
                    }
                    try
                    {
                        // A plain .json that is not an enhancement is skipped quietly.
                        if (!EnhancementImportRules.FileLooksLikeEnhancement(path))
                        {
                            skippedNonEnhancement++;
                            continue;
                        }
                        // Same file, or identical content under another name: no "Name (2)" copy.
                        var existing = lib.FindDuplicateOf(path);
                        if (existing != null)
                        {
                            duplicateOfPath = existing;
                            continue;
                        }
                        var enhancement = EnhancementSerializer.LoadFromFile(path);
                        var saved = lib.PromoteToLibrary(enhancement, sourceTag: "import");
                        if (saved == null)
                        {
                            errors.Add(Loc.GetF("deeper_import_err_write_fmt", fileName));
                            continue;
                        }
                        lastImportedPath = saved;
                        imported.Add(Path.GetFileName(saved));
                    }
                    catch (EnhancementLoadException ex)
                    {
                        errors.Add($"{fileName}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "ImportEnhancementFiles: import failed");
                        errors.Add($"{fileName}: {ex.GetType().Name}");
                    }
                }
            }

            // Remember the source folder so the next picker opens where the user picked from.
            if (lastImportedPath != null && paths.Count > 0)
            {
                var dir = Path.GetDirectoryName(paths[0]);
                if (!string.IsNullOrEmpty(dir))
                {
                    CoreSettings.Current.DeeperLastDirectory = dir;
                    CoreSettings.Save();
                }
            }

            try { ReloadDeeperLibraryFromDisk(); } catch (Exception ex) { Log.Debug(ex, "Deeper import: reload"); }

            if (imported.Count > 0)
            {
                var msg = imported.Count == 1
                    ? Loc.GetF("deeper_import_done_one_fmt", imported[0])
                    : Loc.GetF("deeper_import_done_many_fmt", imported.Count);
                App.Notifications.Show(msg, Helpers.NotificationType.Success);
            }
            if (errors.Count > 0)
            {
                App.Notifications.Show(
                    Loc.GetF("deeper_import_failed_fmt", errors.Count) + "\n" + string.Join("\n", errors),
                    Helpers.NotificationType.Warning, TimeSpan.FromSeconds(10));
            }
            else if (imported.Count == 0)
            {
                if (duplicateOfPath != null)
                    App.Notifications.Show(Loc.Get("deeper_import_already_in_library"), Helpers.NotificationType.Info);
                else if (skippedNonEnhancement > 0)
                    App.Notifications.Show(Loc.Get("deeper_import_skipped_not_enh"), Helpers.NotificationType.Info);
            }

            var reveal = lastImportedPath ?? duplicateOfPath;
            if (reveal != null) RevealDeeperLibraryRow(reveal);
        }

        /// <summary>WPF RevealDeeperLibraryRow: scroll to the row and flash it briefly.</summary>
        internal void RevealDeeperLibraryRow(string filePath)
        {
            if (DeeperModel is not { } model) return;
            var index = model.Highlight(filePath);
            if (index < 0) return;
            Named<DeeperTabView>("DeeperTab")?.ScrollToRow(index);
            DispatcherTimer.RunOnce(() => model.ClearHighlight(filePath), DeeperRevealFlash);
        }
    }
}
