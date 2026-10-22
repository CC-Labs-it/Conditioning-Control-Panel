// PORTED from ConditioningControlPanel/MainWindow/MainWindow.AssetsFx.cs (226 lines) into the tab
// (CCP.Avalonia/Views/Tabs/AssetsTabView.axaml.cs, "FX" region): the asset-tree row nudge (3 px,
// 130 ms ease-out, gated on AllowTransitions) and the Media Log pulse (3 x 0.45 s dips to 0.45 when
// App.MediaHistory grew since the log was last opened; stopped on hide and on click).
//
// The pack-card hover sheen (OnPackCardHover / DetachPackCardSheen) lives in
// CCP.Avalonia/Views/Tabs/AssetsTabView.Sheen.cs; dormant like WPF while the packs strip is hidden.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
