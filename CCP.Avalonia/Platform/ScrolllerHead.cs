using System;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The head half of the Scrolller licence work (main a870695b0 / 9c54f11ba / 4af799d30 / 9e4cb5393): what WPF
/// App.xaml.cs:2507-2516 starts at launch and App.OnExit:6206-6209 flushes, over the Core
/// <see cref="ScrolllerPolicyService"/>, <see cref="ScrolllerAllowance"/> and <see cref="ScrolllerDiskCache"/>.
/// ponytail: WPF ScrolllerAwayWatcher (Win32 session lock / display off sets ScrolllerPolicy.Away) has no
/// Linux twin yet - follow-up lane sync7-scrolller-b-next; until then this head is never "away".
/// </summary>
internal static class ScrolllerHead
{
    /// <summary>The licence switch is read from the real server only outside a sandbox, or when a sandbox points
    /// the Back Room API at its own (the SkipStartupFetch rule: a sandbox never reaches the production proxy).</summary>
    internal static bool ReadsSwitch(string? userDataDir, string apiBaseUrl) =>
        string.IsNullOrEmpty(userDataDir) || !string.Equals(apiBaseUrl, BackRoomApi.ProductionBaseUrl, StringComparison.OrdinalIgnoreCase);

    /// <summary>WPF App.xaml.cs:2507-2516. Never throws, never holds startup.</summary>
    internal static void Start(string? userDataDir)
    {
        try
        {
            // The licence switch: read now (off the UI thread) and every 30 min. No answer = the shipped behaviour.
            if (ReadsSwitch(userDataDir, BackRoomApi.BaseUrl)) ScrolllerPolicyService.Start();
            else
            {
                ScrolllerAllowance.WatchAccount();   // Start's other half: the daily share follows sign-in
                Log.Information("ScrolllerPolicy: sandboxed profile without its own API - licence switch not read");
            }
            // The device cache loads its books in the background, then sweeps (24 h licence) now and hourly.
            _ = ScrolllerDiskCache.WarmAsync();
            ScrolllerDiskCache.StartSweeper();
        }
        catch (Exception ex) { Log.Warning(ex, "Scrolller start failed"); }
    }

    /// <summary>WPF App.OnExit:6206-6209: the ledger and the device cache index have debounced writes.</summary>
    internal static void Flush()
    {
        try { ScrolllerAllowance.Flush(); } catch (Exception ex) { Log.Debug("Scrolller ledger flush: {E}", ex.Message); }
        try { ScrolllerDiskCache.Flush(); } catch (Exception ex) { Log.Debug("Scrolller cache flush: {E}", ex.Message); }
    }
}
