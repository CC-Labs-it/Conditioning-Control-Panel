// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.Assets.cs (2911 lines).
// Sorted member by member against the fifteen Core seams. Only the tab's own entry point is real;
// everything else is held back, and the two reasons below are different in kind.
//
// The remote-media picker is PORTED (sync6-media-picker) in CCP.Avalonia/Views/Tabs/AssetsTabView.MediaPicker.cs,
// with the consent ask awaited before MediaSource is written and ResetAllChannels on every change.
// InvalidateAssetPoolsAfterSelectionChange has no target yet: no flash/video service on this head
// consumes online media (open gap, parity row views-tab-assets).
//
// PORTED to the tab itself (CCP.Avalonia/Views/Tabs/AssetsTabView.axaml.cs, entered from
// OnTabShown "assets"): RefreshAssetTree (local images/videos), BuildFolderTree, the folder and
// thumbnail checks, Select/Deselect All, image/video thumbnails, preview
// and reveal, the asset counts and the preset combo + Save As/Update/Delete. AssetPresetService
// .OnlineChannelsReset is seeded in App startup (main 03af6e8bb). The Content Packs node, encrypted
// image thumbnails, pack checks/counts and preview live in AssetsTabView.Packs.cs, using ContentPackStore.
//
// Re-cut shell-assets: local/pack video thumbnails and Windows image shell fallback live in
// AssetsTabView.Packs.cs:37 and AssetsTabView.axaml.cs:320 (WPF Assets.cs:801-806, :921-937).
// Phrase presets already live in WorkshopAccordion.PhrasePresets.cs:20; enabled counts now use
// the existing editor catalogue (:54, :61, :91), as WPF Assets.cs:1790 and Patreon.cs:1403-1406.
// Nested keyword/patch-note wheels forward through Helpers/ParentWheel.cs:12, matching WPF
// Assets.cs:444-457. Quests' horizontal-only viewer already bubbles vertical wheel input.
// The pack strip is unreachable in WPF (MainWindow.xaml.cs:205, PacksSectionEnabled = false).
// Its cards, download/activate/upgrade, links, callbacks and preview rotation remain unported.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>The rail's Library door. One ShowTab call, exactly as in WPF
        /// (MainWindow.Assets.cs:39); the tab refreshes itself from OnTabShown.</summary>
        private void BtnAssets_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => ShowTab("assets");

        /// <summary>WPF ApplyAssetPresetFromLauncher: the Assets page applies the preset, repaints and reloads the pools.</summary>
        internal bool ApplyAssetPresetFromLauncher(string presetId)
        {
            if (Named<Tabs.AssetsTabView>("AssetsTab") is not { } tab) return false;
            tab.ApplyAssetPresetAndRepaint(presetId);
            return true;
        }
    }
}
