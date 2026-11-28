using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.Services.Stakes;

namespace ConditioningControlPanel.Services.Dares;

/// <summary>The app's host. Each member reaches one existing service, the way its own feature does.</summary>
public sealed class AppDareHost : IDareHost
{
    private TimeSpan _armed;

    public AppDareHost()
    {
        try
        {
            var l = App.Lockdown;
            if (l == null) return;
            // Deactivate clears Duration before the event, so the length is taken when it starts.
            l.LockdownActivated += () => _armed = l.Duration;
            l.LockdownDeactivated += () =>
            {
                var set = _armed;
                _armed = TimeSpan.Zero;
                try { LockdownEnded?.Invoke(set, l.LastActiveDuration); }
                catch (Exception ex) { App.Logger?.Debug("Dares lockdown end failed: {E}", ex.Message); }
            };
        }
        catch (Exception ex) { App.Logger?.Debug("Dares lockdown hook failed: {E}", ex.Message); }
    }

    public event Action<TimeSpan, TimeSpan>? LockdownEnded;

    public string? Account()
    {
        try { return BackRoom.BackRoomApi.AppIdentity()?.UnifiedId; } catch { return null; }
    }

    public int TzMinutes()
    {
        try { return (int)Math.Round(TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalMinutes); } catch { return 0; }
    }

    public bool ChasterReady()
    {
        try
        {
            var c = App.Chaster;
            var s = App.Settings?.Current;
            if (c == null || s == null) return false;
            return StakeRules.TimeAllowed(c.IsLinked, s.ChasterTabEnabled, s.ChasterPaused, c.SafetyHoldRemaining);
        }
        catch { return false; }
    }

    public int BookLockSeconds(int seconds)
    {
        if (seconds <= 0) return 0;
        try
        {
            // Pressing "Add the minutes" is the consent to the row, the way a time stake is.
            StakeAppHooks.AppEnableLossRow();
            // The row id is literal on purpose: the price-row wiring test reads it.
            return Math.Max(0, App.Chaster?.NoteSeconds("pvp_loss", seconds).AppliedSeconds ?? 0);
        }
        catch (Exception ex) { App.Logger?.Debug("Dares lock booking failed: {E}", ex.Message); return 0; }
    }

    public void AdoptSp(string account, int sp)
    {
        // The account is checked AGAIN inside the marshalled write (StakeSettlement's shape).
        App.Current?.Dispatcher?.BeginInvoke(new Action(() =>
        {
            if (App.Settings?.Current is not { } s) return;
            var same = string.Equals(BackRoom.BackRoomApi.AppIdentity()?.UnifiedId, account, StringComparison.Ordinal);
            if (!V2WalletAdoption.Decide(same, fromBuy: true, sp, s.SkillPoints, out var next)) return;
            SparklesLedger.Write(s, next, SparkleSource.Stake);
            App.Settings.Save();
        }));
    }

    public int? Sparkles()
    {
        try { return App.Settings?.Current?.SkillPoints; } catch { return null; }
    }

    public bool LockdownActive => App.Lockdown?.IsActive == true;

    public bool StartLockdown(TimeSpan duration)
    {
        try
        {
            var l = App.Lockdown;
            if (l == null || l.IsActive) return false;
            l.Activate(duration);
            return l.IsActive;
        }
        catch (Exception ex) { App.Logger?.Warning("Dares lockdown start failed: {E}", ex.Message); return false; }
    }
}
