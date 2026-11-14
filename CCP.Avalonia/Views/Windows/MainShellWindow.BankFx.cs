// Part A shares WPF Services/BankAccumulator.cs unchanged in CCP.Core and exposes
// ProgressionBank.XPAwarded (WPF Services/Progression/ProgressionService.cs:123-126).
// Part B remains unwired: WPF MainWindow.BankFx.cs:199 lifecycle, :394 hold, :488 poll,
// :540 flight, :594 landings, :635 abort, :657 counter, :679 pop, :734 flash, :750 audio.
// Keep the existing live header writer until the complete hold/flight/cleanup path is ported.

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.
    }
}
