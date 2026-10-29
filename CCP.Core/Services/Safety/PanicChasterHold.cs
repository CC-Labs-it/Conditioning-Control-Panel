using System;

namespace ConditioningControlPanel.Services.Safety
{
    /// <summary>
    /// Circe's Tab: every panic opens the ten-minute safety hold (<see cref="Chaster.ChasterService.SafetyHold"/>,
    /// <see cref="Chaster.ChasterService.NoteSafetyExit"/>). The way out is never priced (TabPrices.NeverPriced);
    /// without the hold, prices kept booking for the ten minutes after a panic that did not go through the
    /// key (main 7e14c2870: the voice panic and the off-thread fallback never opened it).
    ///
    /// <para>The ONE seam the panic paths call: the Avalonia head's stop pass (PanicSurfaces, so the panic
    /// key, the tray Stop everything, the voice panic and the leash gate's Panic all reach it), its
    /// PanicWatchdog fallback, and a controller's trigger_panic (<see cref="RemoteCommands.StopEffects"/>).
    /// The WPF head calls <c>App.Chaster?.NoteSafetyExit()</c> on its own panic paths as main does
    /// (key, voice/remote panic, the fallback, trigger_panic). A pause (Escape on a game) is not a panic and never comes here.</para>
    /// <para>Thread-safe: NoteSafetyExit takes the service's lock and only raises the repaint event.</para>
    /// </summary>
    public static class PanicChasterHold
    {
        /// <summary>The head's Chaster service (WPF App.Chaster, Avalonia ChasterHead.Service). Null or a
        /// null result: no tab is built, nothing to hold.</summary>
        public static Func<Chaster.ChasterService?>? Service { get; set; }

        /// <summary>Opens the hold. Never throws.</summary>
        public static void Open(string reason)
        {
            try
            {
                if (Service?.Invoke() is not { } chaster) return;
                chaster.NoteSafetyExit();
                Serilog.Log.Debug("[Chaster] safety hold opened ({Reason})", reason);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] safety hold ({Reason}) failed", reason); }
        }
    }
}
