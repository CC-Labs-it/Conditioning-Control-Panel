// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.Awareness.cs.
// The settings controls live in AwarenessTabView.axaml.cs; presets in its .Presets partial.
// wire-shell-awareness: app-list commit now uses Core AwarenessAppScopeRules. WPF's trigger service
// delegates to the same parser/matcher, so both heads agree about case, separators and .exe.
// ponytail: wire-shell-awareness-b still owns the runtime pulse feed, OCR/keyboard lifecycle,
// recently focused app suggestions, highlight overlays/capture affinity and the status pulse.
// The separate v2 title observer/ledger path is also incomplete. No new desktop observer is
// installed by this slice, and none of these methods is referenced directly from shell XAML.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
    }
}
