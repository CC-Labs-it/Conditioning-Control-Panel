// PORTED from ConditioningControlPanel/MainWindow/MainWindow.AssetsFx.cs (226 lines) into the tab
// (CCP.Avalonia/Views/Tabs/AssetsTabView.axaml.cs, "FX" region): the asset-tree row nudge (3 px,
// 130 ms ease-out, gated on AllowTransitions) and the Media Log pulse (3 x 0.45 s dips to 0.45 when
// App.MediaHistory grew since the log was last opened; stopped on hide and on click).
//
// The pack-card sheen is also wired on the existing dormant AssetsTabView template: one card
// at a time, gated by transitions, detached on leave, effective hide and unload. The packs strip
// stays collapsed as on WPF; real ContentPackService card construction belongs to shell-assets.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
