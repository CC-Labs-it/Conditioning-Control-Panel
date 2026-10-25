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
// thumbnail checks, Select/Deselect All, thumbnails, preview
// and reveal, the asset counts and the preset combo + Save As/Update/Delete. AssetPresetService
// .OnlineChannelsReset is seeded in App startup (main 03af6e8bb). The Content Packs node, encrypted
// image thumbnails, pack checks/counts and preview live in AssetsTabView.Packs.cs, using ContentPackStore.
//
// Phrase presets (InitializePhrasePresets and its handlers) live on the cell that owns the controls:
// CCP.Avalonia/Views/Controls/Companion/Runtime/WorkshopTriggersCell.axaml.cs. Video thumbnails (local and
// pack) use Platform/ShellVideoThumbnail on Windows and OpenCV on other platforms.
//
// Not ported, because WPF never reaches it: the packs strip. MainWindow.xaml.cs:208 PacksSectionEnabled is
// a const false, PacksSection and BtnRefreshPacks/BtnDeleteDownloadedPacks are Collapsed, so RefreshPacksAsync,
// the pack-card download/activate/upgrade/Patreon/Discord handlers, the four OnPack* callbacks, the preview
// rotation and PacksScrollViewer_PreviewMouseWheel are dead code there.
//
// Nested wheel forwarding now lives in Helpers/ParentWheel.cs on the keyword list and patch notes.
// Quests' horizontal-only viewer naturally bubbles vertical wheel input to its parent.
// Voice-line rows and active counts use CCP.Core CompanionVoiceLines through the phrase editor.

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
