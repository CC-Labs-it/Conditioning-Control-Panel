using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Deeper;

namespace ConditioningControlPanel
{
    // Play > Deeper, play-only (tier 2 testers, 2026-11-16, Discord tier-2 channel): "Deeper in
    // the Play section could use a streamlining redesign. Keep this layout for the Editor link,
    // but in the Play section it only really needs a choose file option and a player", "keep the
    // effect heat map too", "I'd like a 1 click load/maximize/play from that window".
    //
    // One tab, two layouts. Every ShowTab("deeper") lands on the PLAY view; the full layout
    // (library tools, editor, import, catalogue, publishing, webcam setup) is reached through
    // ShowDeeperEditorLayout: the play view's editor link, the Home mosaic's DEEPER EDITOR tile,
    // the tutorial and the catalogue flows that reveal a library row.
    public partial class MainWindow
    {
        /// <summary>Set by <see cref="ShowDeeperEditorLayout"/> for the very next show only.</summary>
        private bool _deeperFullLayoutRequested;

        /// <summary>True while the tab shows the play-only view.</summary>
        internal bool DeeperPlayOnly { get; private set; } = true;

        /// <summary>The file the play view will play (a library entry or a browsed file).</summary>
        private string? _deeperPlayPath;

        /// <summary>A browsed file outside the library, kept at the top of the picker.</summary>
        private EnhancementLibraryEntry? _deeperPlayBrowsedEntry;

        private bool _deeperPlayPickerFilling;

        /// <summary>Open the Deeper tab on its FULL layout (library, editor, import, publishing).</summary>
        internal void ShowDeeperEditorLayout()
        {
            _deeperFullLayoutRequested = true;
            ShowTab("deeper");
            // ShowTab may have refused (an unknown state, a redirect); never leave the flag armed
            // for some later, unrelated door.
            _deeperFullLayoutRequested = false;
        }

        /// <summary>Called from ShowTab's "deeper" case: which layout this visit gets.</summary>
        private void ApplyDeeperLayoutForVisit()
        {
            SetDeeperPlayOnly(!_deeperFullLayoutRequested);
            _deeperFullLayoutRequested = false;
        }

        internal void SetDeeperPlayOnly(bool playOnly)
        {
            DeeperPlayOnly = playOnly;
            var tab = DeeperTab;
            if (tab == null) return;
            try
            {
                tab.DeeperPlayView.Visibility = playOnly ? Visibility.Visible : Visibility.Collapsed;
                tab.DeeperFullBody.Visibility = playOnly ? Visibility.Collapsed : Visibility.Visible;
                // The hero keeps its title; its tools (tutorial, import, new enhancement) are
                // editor work and leave with the full layout.
                tab.DeeperHeroActions.Visibility = playOnly ? Visibility.Collapsed : Visibility.Visible;
                tab.BtnDeeperBackToPlayView.Visibility = playOnly ? Visibility.Collapsed : Visibility.Visible;
                if (playOnly) RefreshDeeperPlayView();
            }
            catch (Exception ex) { Diag.Swallowed(ex, "deeper play view toggle"); }
        }

        internal void BtnDeeperOpenFullLayout_Click(object sender, RoutedEventArgs e) => SetDeeperPlayOnly(false);

        internal void BtnDeeperBackToPlayView_Click(object sender, RoutedEventArgs e) => SetDeeperPlayOnly(true);

        /// <summary>
        /// Refill the picker from the library scan the hub already holds (newest first) and
        /// keep the current choice when it still exists. Cheap: no disk scan of its own.
        /// </summary>
        private void RefreshDeeperPlayView()
        {
            var tab = DeeperTab;
            if (tab == null || !DeeperPlayOnly) return;

            var entries = new List<EnhancementLibraryEntry>();
            if (_deeperPlayBrowsedEntry != null) entries.Add(_deeperPlayBrowsedEntry);
            entries.AddRange(_deeperAllEntries.OrderByDescending(x => x.LastModified));

            var keep = _deeperPlayPath;
            _deeperPlayPickerFilling = true;
            try
            {
                tab.CmbDeeperPlayFile.ItemsSource = entries;
                var pick = entries.FirstOrDefault(x => DeeperPathsEqual(x.FilePath, keep)) ?? entries.FirstOrDefault();
                tab.CmbDeeperPlayFile.SelectedItem = pick;
                _deeperPlayPath = pick?.FilePath;
            }
            finally { _deeperPlayPickerFilling = false; }

            tab.TxtDeeperPlayEmpty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            tab.BtnDeeperPlayStart.IsEnabled = !string.IsNullOrEmpty(_deeperPlayPath);
            UpdateDeeperPlayDetails(tab.CmbDeeperPlayFile.SelectedItem as EnhancementLibraryEntry);
        }

        internal void CmbDeeperPlayFile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_deeperPlayPickerFilling) return;
            var entry = DeeperTab?.CmbDeeperPlayFile.SelectedItem as EnhancementLibraryEntry;
            _deeperPlayPath = entry?.FilePath;
            if (DeeperTab != null) DeeperTab.BtnDeeperPlayStart.IsEnabled = entry != null;
            UpdateDeeperPlayDetails(entry);
        }

        /// <summary>The meta line and the heat map for the chosen file. Reads the file, never writes it.</summary>
        private void UpdateDeeperPlayDetails(EnhancementLibraryEntry? entry)
        {
            var tab = DeeperTab;
            if (tab == null) return;
            if (entry == null)
            {
                tab.TxtDeeperPlayMeta.Text = "";
                tab.TxtDeeperPlayHeatSpan.Text = "";
                tab.DeeperPlayHeat.Values = null;
                return;
            }

            Models.Deeper.Enhancement? enh = null;
            try { enh = EnhancementSerializer.LoadFromFile(entry.FilePath); }
            catch (Exception ex) { App.Logger?.Debug("Deeper play view: could not read {File}: {Error}", Path.GetFileName(entry.FilePath), ex.Message); }

            var duration = EffectHeatMap.ResolveDuration(enh, entry.DurationSeconds);
            tab.DeeperPlayHeat.Values = EffectHeatMap.Compute(enh, duration, 120);
            tab.TxtDeeperPlayHeatSpan.Text = duration > 0 ? FormatDeeperPlayTime(duration) : "";

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(entry.Creator)) parts.Add(entry.Creator);
            parts.Add(string.Equals(entry.MediaType, Models.Deeper.MediaTypes.Audio, StringComparison.OrdinalIgnoreCase)
                ? Loc.Get("deeper_playview_type_audio")
                : Loc.Get("deeper_playview_type_video"));
            if (enh == null) parts.Add(Loc.Get("deeper_playview_unreadable"));
            tab.TxtDeeperPlayMeta.Text = string.Join("  ·  ", parts);
        }

        private static string FormatDeeperPlayTime(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Round(seconds));
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
        }

        internal void BtnDeeperPlayBrowse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.Get("deeper_playview_browse_title"),
                Filter = "Deeper enhancements (*.ccpenh.json)|*.ccpenh.json|All files (*.*)|*.*",
                DefaultExt = ".ccpenh.json",
                CheckFileExists = true,
                InitialDirectory = App.EnhancementLibrary?.LastDirectory
            };
            if (dlg.ShowDialog(this) != true) return;
            var path = dlg.FileName;

            var inLibrary = _deeperAllEntries.FirstOrDefault(x => DeeperPathsEqual(x.FilePath, path));
            if (inLibrary == null)
            {
                // Played where it is: the play view never imports or copies (that is editor work).
                string name = Path.GetFileName(path);
                string creator = "", mediaType = Models.Deeper.MediaTypes.Video;
                try
                {
                    var enh = EnhancementSerializer.LoadFromFile(path);
                    if (!string.IsNullOrWhiteSpace(enh.Metadata?.Name)) name = enh.Metadata!.Name;
                    creator = enh.Metadata?.Creator ?? "";
                    mediaType = enh.MediaType;
                }
                catch (Exception ex)
                {
                    App.Logger?.Warning(ex, "Deeper play view: browsed file is not an enhancement");
                    MessageBox.Show(this, ex.Message, "Deeper", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                _deeperPlayBrowsedEntry = new EnhancementLibraryEntry
                {
                    FilePath = path, Name = name, Creator = creator, MediaType = mediaType,
                    LastModified = DateTime.Now,
                };
            }
            _deeperPlayPath = path;
            RefreshDeeperPlayView();
        }

        /// <summary>The one-click button: load the chosen file, maximise the player, start playback.</summary>
        internal void BtnDeeperPlayStart_Click(object sender, RoutedEventArgs e)
        {
            var path = _deeperPlayPath;
            if (string.IsNullOrEmpty(path)) return;
            if (!File.Exists(path))
            {
                RefreshDeeperLibraryUI();
                return;
            }
            try { App.Bark?.NotifyUiAction("deeper_player"); } catch (Exception ex) { Diag.Swallowed(ex); }
            try
            {
                // Same rule as the library row's play button: a player that already holds this
                // file is not reloaded (that would restart it from the top), only shown and started.
                bool alreadyLoaded = Views.Deeper.EnhancementPlayerWindow.Current != null
                                     && DeeperPathsEqual(App.DeeperHost?.LoadedFilePath, path);
                Views.Deeper.EnhancementPlayerWindow.ShowOrActivate(this, w =>
                {
                    if (!alreadyLoaded) w.LoadEnhancementFile(path);
                    w.MaximizeAndPlayWhenReady(path);
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Deeper player from the play view");
                MessageBox.Show(this,
                    $"Couldn't open Deeper Player:\n\n{ex.GetType().Name}: {ex.Message}",
                    "Open Player failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
