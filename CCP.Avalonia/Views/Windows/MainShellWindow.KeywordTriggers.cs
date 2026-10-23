// ConditioningControlPanel/MainWindow/MainWindow.KeywordTriggers.cs (621 lines) on this head.
//
// WPF's MainWindow owned these handlers because the Awareness markup was inline in MainWindow.xaml.
// Here the controls moved, and so did the handlers - a member restored HERE would be a second copy
// no click can reach:
//   - the two cooldown sliders: CCP.Avalonia/Views/Tabs/AwarenessTabView.axaml.cs
//   - the four detail sliders, the two OCR combos, SyncKeywordRescuePanelUi (incl. the HasAccess
//     gate on the OCR detail), add / import, RefreshKeywordTriggerList, CreateKeywordTriggerRow and
//     all nine per-row editors: CCP.Avalonia/Views/Controls/Companion/KeywordTriggersPanel*.cs
//
// The Windows keyword runtime is Platform/KeywordTriggerHead.cs; buffer/matching/cooldown
// rules live in Core and are also consumed by WPF's service. ponytail: OCR, highlight windows,
// recent-app chips, OverlayPulse/BrainDrain and a Linux source remain
// follow-ups (see the ledger and hand-off).

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
    }
}
