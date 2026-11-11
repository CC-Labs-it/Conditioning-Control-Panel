using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

/// <summary>
/// WPF Services/Account/EntitlementTierSync.cs OnAppFocused + RefreshAsync: the FOCUS THROTTLE only. The main
/// window came to the front, so the profile is read again, at most once per <see cref="FocusCheckGap"/> and one
/// read at a time; offline or signed out never reads. What the read found is not applied here: the head feeds
/// every server tier reading (profile load, heartbeat, this refresh) through its one path, Avalonia
/// <c>AccountSeed.ApplyTierReading</c> on Core <see cref="EntitlementReadings"/>, rise AND lower (#4073), so a
/// payer whose tier fell lapses the way WPF does instead of keeping access for 14 days.
/// </summary>
public sealed class EntitlementTierMonitor
{
    public static readonly TimeSpan FocusCheckGap = TimeSpan.FromSeconds(20);

    private DateTime _lastFocusCheckUtc = DateTime.MinValue;
    private int _inFlight;

    /// <summary>One read at a time, at most once per 20 seconds; offline/unsigned never reads.
    /// A late reading carries the account id it started with, so the head can refuse it after an account switch.</summary>
    public async Task OnAppFocusedAsync(AppSettings settings, DateTime nowUtc, Func<string, Task> read)
    {
        if (settings.OfflineMode || string.IsNullOrEmpty(settings.UnifiedId) || string.IsNullOrEmpty(settings.AuthToken)
            || nowUtc - _lastFocusCheckUtc < FocusCheckGap || Interlocked.Exchange(ref _inFlight, 1) == 1) return;
        _lastFocusCheckUtc = nowUtc;
        try { await read(settings.UnifiedId); }
        finally { Interlocked.Exchange(ref _inFlight, 0); }
    }
}
