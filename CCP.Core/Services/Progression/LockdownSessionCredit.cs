using System;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Decides whether a finished lockdown also counts as one finished session for the Session quests
/// (Daily and Weekly Devotion, "Complete N sessions"). ccp-bugs #1737: players call a lockdown a
/// "lockdown session", but only the session engine fed that category, so a 45-minute lockdown
/// moved Locked Away and left Weekly Devotion where it was.
///
/// <para>RULE: a lockdown that counts for the Lockdown quests (<see cref="QuestService.LockdownQuestMinimum"/>,
/// 20 minutes) also credits one session, UNLESS a preset session already credited a session while
/// that lockdown ran. A preset played inside a lockdown is the same sitting, so it earns one
/// session, not two.</para>
///
/// <para>The lockdown's window is rebuilt from its end time and its served length
/// (<see cref="LockdownService.LastActiveDuration"/> runs from the first start, through any
/// Emergency Exit restarts), so no lockdown event needs wiring here.</para>
/// </summary>
internal sealed class LockdownSessionCredit
{
    private DateTime? _lastSessionCreditUtc;

    /// <summary>A preset session just credited the Session category.</summary>
    public void SessionCredited(DateTime utc) => _lastSessionCreditUtc = utc;

    /// <summary>
    /// True when a lockdown that ended at <paramref name="endedUtc"/> after <paramref name="served"/>
    /// should credit one session.
    /// </summary>
    public bool LockdownEarnsSession(TimeSpan served, DateTime endedUtc)
    {
        if (!QuestService.LockdownCountsForQuests(served)) return false;
        var startedUtc = endedUtc - served;
        return _lastSessionCreditUtc is not { } s || s < startedUtc || s > endedUtc;
    }
}
