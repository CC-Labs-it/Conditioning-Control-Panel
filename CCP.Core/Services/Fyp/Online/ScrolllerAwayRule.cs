using Serilog;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// When nobody can be watching (owner, 2026-10-19: no fresh Scrolller fetch for an empty room), both heads: the session
/// is locked, the display is switched off (a dimmed display still counts as watched), or the machine is going to sleep.
/// NEVER on minimise or focus: sessions run minimised and media plays over other apps. Cached media keeps playing.
/// The OS hooks live in each head (WPF ScrolllerAwayWatcher, Avalonia Platform/ScrolllerAwayHost); this is the rule and
/// the one place that sets <see cref="ScrolllerPolicy.Away"/>.
/// </summary>
internal static class ScrolllerAwayRule
{
    /// <summary>GUID_CONSOLE_DISPLAY_STATE values (Windows; Linux only ever reports On).</summary>
    internal const int DisplayOff = 0, DisplayOn = 1, DisplayDimmed = 2;

    /// <summary>The rule, pure: away = locked, the display off, or going to sleep. Dimmed is not away.</summary>
    internal static bool IsAway(bool locked, int displayState, bool sleeping = false)
        => locked || sleeping || displayState == DisplayOff;
}

/// <summary>The live state one head's hooks feed, applied to <see cref="ScrolllerPolicy.Away"/> on every change.
/// Thread-safe; never throws.</summary>
internal sealed class ScrolllerAwayTracker
{
    private readonly object _gate = new();
    private bool _locked, _sleeping;
    private int _display = ScrolllerAwayRule.DisplayOn;

    public void SetLocked(bool locked) { lock (_gate) _locked = locked; Apply(); }
    public void SetDisplay(int state) { lock (_gate) _display = state; Apply(); }
    public void SetSleeping(bool sleeping) { lock (_gate) _sleeping = sleeping; Apply(); }

    /// <summary>Back to "watched" (the hooks stopped): never leaves the policy stuck away.</summary>
    public void Reset()
    {
        lock (_gate) { _locked = false; _sleeping = false; _display = ScrolllerAwayRule.DisplayOn; }
        Apply();
    }

    private void Apply()
    {
        bool locked, sleeping, away;
        int display;
        lock (_gate)
        {
            (locked, sleeping, display) = (_locked, _sleeping, _display);
            away = ScrolllerAwayRule.IsAway(locked, display, sleeping);
        }
        if (away == ScrolllerPolicy.Away) return;
        ScrolllerPolicy.Away = away;
        Log.Information("ScrolllerAway: {State} (locked={Locked}, display={Display}, sleeping={Sleeping})",
            away ? "away, no fresh Scrolller fetch" : "back", locked, display, sleeping);
    }
}
