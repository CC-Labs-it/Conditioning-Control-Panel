using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ConditioningControlPanel.Views.Tabs
{
    public partial class AssetsTabView : UserControl
    {
        public AssetsTabView()
        {
            InitializeComponent();
            // FX lifecycle (PR-4b). The Assets tab deliberately has NO ambient loop; this hook
            // only fires the Media Log's unseen-entries pulse when the tab is opened.
            IsVisibleChanged += AssetsTabView_IsVisibleChanged;
        }

        private void AssetsTabView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible) PaintAssetsFolder();
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnAssetsTabVisibilityChanged(IsVisible);
        }

        // ---- FINE-TUNE LAYOUT (#1409, #1410) ------------------------------------------------

        /// <summary>The Fine-tune list only exists while remote media is in play. With the
        /// source on "My own assets" its row is hidden, so an open flag left over from Both
        /// must not keep the Asset Browser collapsed with no way to bring it back (#1409).</summary>
        internal static bool FineTuneShows(bool open, bool remoteInPlay) => open && remoteInPlay;

        /// <summary>
        /// One place decides who owns the page's height. Closed (or local source): the media
        /// block hugs its content and the browser takes the rest, with its 260 px floor. Open:
        /// the browser steps aside, its row gives up the floor too (a RowDefinition MinHeight
        /// holds even when its content is collapsed), and the media row takes the star, so the
        /// list scrolls inside the page and the r/ add field stays reachable (#1410).
        /// </summary>
        internal void ApplyFineTuneLayout(bool open, bool remoteInPlay)
        {
            var shows = FineTuneShows(open, remoteInPlay);
            RemoteFineTuneScroll.Visibility = shows ? Visibility.Visible : Visibility.Collapsed;
            AssetBrowserSection.Visibility = shows ? Visibility.Collapsed : Visibility.Visible;
            RowMediaSource.Height = shows ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            RowAssetBrowser.MinHeight = shows ? 0 : 260;
            RowAssetBrowser.Height = shows ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            TxtRemoteFineTuneChevron.Text = shows ? "" : "";
        }

        // ---- LIBRARY > FOLDERS (nav rework 2026-10-06) --------------------------------------

        /// <summary>Zone keys the Library section strip reaches (nav rework).</summary>
        public static readonly string[] ZoneKeys = { "folders" };

        /// <summary>
        /// "folders": the page does not scroll (fixed rows), so this paints the current folder,
        /// brings the chip into view and glows it and the preset picker once for 2 s (skipped
        /// under reduced or no motion). Any other key does nothing.
        /// </summary>
        public void ScrollToZone(string zone)
        {
            if (!string.Equals((zone ?? "").Trim(), "folders", StringComparison.OrdinalIgnoreCase)) return;
            PaintAssetsFolder();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    ZoneFolders.BringIntoView();
                    App.Logger?.Debug("Assets ScrollToZone(folders)");
                    var sage = global::ConditioningControlPanel.Controls.NavRail.NavStripRules.Accent(global::ConditioningControlPanel.Services.UI.NavSections.Library);
                    global::ConditioningControlPanel.Controls.NavRail.NavGlow.Once(ZoneFolders, sage, why: "library.folders");
                    global::ConditioningControlPanel.Controls.NavRail.NavGlow.Once(CmbAssetPresets, sage, why: "library.presets");
                }
                catch (Exception ex) { App.Logger?.Debug("Assets ScrollToZone: {E}", ex.Message); }
            }), System.Windows.Threading.DispatcherPriority.Normal);
        }

        /// <summary>The folder the app reads today, as one line; the full path is the tooltip.</summary>
        internal void PaintAssetsFolder()
        {
            try
            {
                var path = App.EffectiveAssetsPath ?? "";
                TxtAssetsFolderPath.Text = path;
                TxtAssetsFolderPath.ToolTip = string.IsNullOrEmpty(path) ? null : path;
            }
            catch (Exception ex) { App.Logger?.Debug("PaintAssetsFolder: {E}", ex.Message); }
        }

        private void BtnPickAssetsFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw) mw.RequestPickAssetsFolder();
            PaintAssetsFolder();
        }

        private void PackCard_MouseEnter(object sender, MouseEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnPackCardHover(sender as FrameworkElement, true);
        }

        private void PackCard_MouseLeave(object sender, MouseEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnPackCardHover(sender as FrameworkElement, false);
        }

        private void AssetTreeRow_MouseEnter(object sender, MouseEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnAssetTreeRowHover(sender as FrameworkElement, true);
        }

        private void AssetTreeRow_MouseLeave(object sender, MouseEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.OnAssetTreeRowHover(sender as FrameworkElement, false);
        }

        private void BtnCreatorDiscord_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnCreatorDiscord_Click(sender, e);
        }
        private void BtnDeleteAssetPreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeleteAssetPreset_Click(sender, e);
        }
        private void BtnDeleteDownloadedPacks_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeleteDownloadedPacks_Click(sender, e);
        }
        private void BtnDeselectAllAssets_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnDeselectAllAssets_Click(sender, e);
        }
        private void BtnGetPacks_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnGetPacks_Click(sender, e);
        }
        private void BtnOpenAssetsFolder_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnOpenAssetsFolder_Click(sender, e);
        }
        private void BtnMediaLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new ConditioningControlPanel.MediaHistoryWindow();
                var owner = Window.GetWindow(this);
                if (owner != null && owner.IsLoaded) win.Owner = owner;
                win.Show();
            }
            catch (Exception ex)
            {
                App.Logger?.Error(ex, "Failed to open Media Log window");
            }
        }
        private void BtnPackActivate_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnPackActivate_Click(sender, e);
        }
        private void BtnPackDownload_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnPackDownload_Click(sender, e);
        }
        private void BtnRefreshAssets_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnRefreshAssets_Click(sender, e);
        }
        private void BtnRefreshPacks_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnRefreshPacks_Click(sender, e);
        }
        private void BtnSaveAssetPreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSaveAssetPreset_Click(sender, e);
        }
        private void BtnSelectAllAssets_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnSelectAllAssets_Click(sender, e);
        }
        private void BtnUpdateAssetPreset_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.BtnUpdateAssetPreset_Click(sender, e);
        }
        private void CmbAssetPresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.CmbAssetPresets_SelectionChanged(sender, e);
        }
        private void FolderCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.FolderCheckBox_Changed(sender, e);
        }
        private void PacksScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.PacksScrollViewer_PreviewMouseWheel(sender, e);
        }
        private void ThumbnailCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ThumbnailCheckBox_Changed(sender, e);
        }
        private void ThumbnailItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ThumbnailItem_Click(sender, e);
        }

        private void AssetTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.AssetTreeView_SelectedItemChanged(sender, e);
        }
        private void ThumbnailItem_OpenInExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ThumbnailItem_OpenInExplorer_Click(sender, e);
        }
        private void ThumbnailItem_Preview_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow mw)
                mw.ThumbnailItem_Preview_Click(sender, e);
        }
    }
}
