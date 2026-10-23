// Adapted from WPF MainWindow.DeeperTab.cs at 888cdbffe4d2ecf3967cee4a4334557ee533e54d.
using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Deeper;
using Serilog;
namespace ConditioningControlPanel.Avalonia.Views.Windows;
public partial class MainShellWindow
{
        private void ImportEnhancementFiles(System.Collections.Generic.IEnumerable<string> paths)
        {
            using var lib = new EnhancementLibrary();
            var imported = new System.Collections.Generic.List<string>();
            var errors = new System.Collections.Generic.List<string>();
            int skippedNonEnhancement = 0;
            string? lastImportedPath = null;
            string? duplicateOfPath = null;

            foreach (var path in paths)
            {
                var fileName = System.IO.Path.GetFileName(path);
                if (!EnhancementImportRules.IsImportablePath(path))
                {
                    errors.Add(string.Format(Loc.Get("deeper_import_err_not_enh_fmt"), fileName));
                    continue;
                }
                try
                {
                    if (!EnhancementImportRules.FileLooksLikeEnhancement(path))
                    {
                        skippedNonEnhancement++;
                        continue;
                    }
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
                        errors.Add(string.Format(Loc.Get("deeper_import_err_write_fmt"), fileName));
                        continue;
                    }
                    lastImportedPath = saved;
                    imported.Add(System.IO.Path.GetFileName(saved));
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

            if (lastImportedPath != null)
            {
                var src = paths.FirstOrDefault();
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

            try { ReloadDeeperLibraryFromDisk(); } catch (Exception ex) { Diag.Swallowed(ex); }

            if (imported.Count > 0)
            {
                var msg = imported.Count == 1
                    ? string.Format(Loc.Get("deeper_import_done_one_fmt"), imported[0])
                    : string.Format(Loc.Get("deeper_import_done_many_fmt"), imported.Count);
                App.Notifications.Show(msg, Helpers.NotificationType.Success);
            }
            if (errors.Count > 0)
            {
                App.Notifications.Show(
                    string.Format(Loc.Get("deeper_import_failed_fmt"), errors.Count) + "\n" + string.Join("\n", errors),
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


        private string? _deeperHighlightedPath;
        private void RevealDeeperLibraryRow(string path)
        {
            var row = DeeperModel?.FilteredEntries.FirstOrDefault(r => Tabs.DeeperTabViewModel.PathsEqual(r.Entry.FilePath, path));
            if (row == null) return;
            _deeperHighlightedPath = path;
            row.IsHighlighted = true;
            var list = Named<Tabs.DeeperTabView>("DeeperTab")?.FindControl<ItemsControl>("DeeperLibraryList");
            list?.ContainerFromIndex(DeeperModel!.FilteredEntries.IndexOf(row))?.BringIntoView();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1800) };
            timer.Tick += (_, _) => { timer.Stop(); row.IsHighlighted = false;
                if (Tabs.DeeperTabViewModel.PathsEqual(_deeperHighlightedPath, path))
                {
                    _deeperHighlightedPath = null;
                    foreach (var current in DeeperModel!.FilteredEntries.Where(r => Tabs.DeeperTabViewModel.PathsEqual(r.Entry.FilePath, path))) current.IsHighlighted = false;
                } };
            timer.Start();
        }
}
