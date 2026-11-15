using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LockdownConsentGambleTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WarningPlacesGambleBetweenSafetyValveAndPossession(bool windows)
    {
        var cfg = new AppSettings { LockdownPossessionEnabled = true };
        var warning = LockdownTabView.LockdownWarning(20, cfg, windows);
        var valve = windows
            ? "  (or Ctrl+Alt+Del → Task Manager as a safety valve)"
            : "  (or ending the app from a system monitor / terminal as a safety valve)";
        const string gamble = "\n- The Emergency Exit button is a gamble: win its little game and you may leave early,\n"
            + "  lose it and the timer restarts at its FULL length";
        Assert.Contains(valve + gamble + "\n- Possession is ON", warning);
    }
}
