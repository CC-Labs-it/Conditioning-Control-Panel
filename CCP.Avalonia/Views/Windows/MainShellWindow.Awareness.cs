// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.Awareness.cs.
// The settings controls live in AwarenessTabView.axaml.cs; presets in its .Presets partial.
// wire-shell-awareness: app-list commit now uses Core AwarenessAppList. WPF's trigger service
// delegates to the same parser/matcher, so both heads agree about case, separators and .exe.
// fin-shell-awareness: the tab's .Apps partial offers the runtime's recent process names; tutorial
// and typed avatar comments dispatch through KeywordAvatarComment (including panic cancellation).
// ponytail: OCR/Linux keyboard sources, highlight overlays/capture affinity, remaining action types,
// cross-service preset notifications and the v2 observer/ledger remain incomplete; see the ledger.
// The status pulse uses MainShellWindow.TabFxTakeoverLabStatus.cs; this partial has no direct callers.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
    }
}
