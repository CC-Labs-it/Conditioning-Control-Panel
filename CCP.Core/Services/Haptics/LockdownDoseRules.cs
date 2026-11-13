using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Haptics;

public static class LockdownDoseRules
{
    /// <summary>A wall feature the keeper can read (and, for Tier 0/1, switch on). Keys are the wall
    /// keys <c>MainWindow.SetWallFeature</c> understands. Tier 0 = starter picks (cheap, visual, need no
    /// special assets); Tier 1 = escalation picks (round 2+, only when their assets exist); Tier 2 =
    /// counts as a dose when ON but is never picked (too loud, level-gated, or audio-dependent).</summary>
    public sealed record DoseFeature(string Key, string Name, Func<AppSettings, bool> IsOn, int Tier);

    public static readonly IReadOnlyList<DoseFeature> Catalog = new DoseFeature[]
    {
        new("flash",        "Flash",            s => s.FlashEnabled,           0),
        new("subliminal",   "Subliminals",      s => s.SubliminalEnabled,      0),
        new("spiral",       "the Spiral",       s => s.SpiralEnabled,          0),
        new("pinkfilter",   "the Pink Filter",  s => s.PinkFilterEnabled,      0),
        new("bouncingtext", "Bouncing Text",    s => s.BouncingTextEnabled,    0),
        new("bubbles",      "Bubbles",          s => s.BubblesEnabled,         0),
        new("video",        "Mandatory Videos", s => s.MandatoryVideosEnabled, 1),
        new("mindwipe",     "Mind Wipe",        s => s.MindWipeEnabled,        2),
        new("lockcard",     "the Lock Card",    s => s.LockCardEnabled,        2),
        new("bubblecount",  "Bubble Count",     s => s.BubbleCountEnabled,     2),
        new("braindrain",   "Brain Drain",      s => s.BrainDrainEnabled,      2),
    };

    /// <summary>Flags outside the wall that still mean "something is running" - the keeper must not
    /// talk over an audio-only bed, a corner GIF or a takeover just because the wall toggles are off.
    /// (Whisper audio is NOT here on purpose: SubliminalService plays it, so it is already covered by
    /// the subliminal toggle and means nothing on its own.)
    ///
    /// <para>Pop Quiz belongs here rather than in <see cref="Catalog"/>: <c>StartEngine</c> starts it
    /// off <c>PopQuizEnabled</c> like any wall feature, so a lockdown with only Pop Quiz on is NOT an
    /// empty room - but it is not a wall card, so <c>MainWindow.SetWallFeature</c> does not know its
    /// key and it must never be conscriptable. Living outside the catalog makes that true by
    /// construction: it counts, and it can never be picked.</para></summary>
    /// <param name="takeoverRunning">Is the takeover service ACTUALLY running right now
    /// (<c>App.Autonomy.IsEnabled</c>)? The two settings flags are not enough on their own - see the
    /// takeover clause below (#1184).</param>
    private static bool HasOffWallDose(AppSettings s, bool takeoverRunning) =>
        s.AudioOnlySession
        || s.PopQuizEnabled
        || (s.CornerGifOverlays?.Any(o => o != null && o.Enabled) == true)
        // Takeover counts only while it is RUNNING (#1184). AutonomyModeEnabled/AutonomyConsentGiven
        // persist across launches so the toggle remembers its label, and takeover deliberately starts
        // OFF unless AutonomyResumeOnStartup is set - so the flags alone were true in a room where
        // nothing whatsoever was going to happen. The keeper read that as "already dosed", skipped
        // the conscription, started a bare engine, and the lockdown ran empty.
        || (takeoverRunning && s.AutonomyModeEnabled && s.AutonomyConsentGiven);

    /// <summary>Test seam for the off-wall half of the census (Pop Quiz, the audio bed, corner GIFs,
    /// takeover). Public so the suite can pin what counts without a live lockdown.</summary>
    public static bool CountsAsOffWallDose(AppSettings s, bool takeoverRunning)
        => s != null && HasOffWallDose(s, takeoverRunning);

    /// <summary>True when NOTHING would run - no wall feature on and no off-wall dose either.</summary>
    public static bool DoseIsEmpty(AppSettings s, bool takeoverRunning)
    {
        if (s == null) return false;          // unknown = not our call; fail open
        if (HasOffWallDose(s, takeoverRunning)) return false;
        foreach (var f in Catalog)
        {
            try { if (f.IsOn(s)) return false; } catch { }
        }
        return true;
    }

    /// <summary>Keys currently ON, catalog order.</summary>
    public static List<string> KeysOn(AppSettings s)
    {
        var list = new List<string>();
        if (s == null) return list;
        foreach (var f in Catalog)
        {
            try { if (f.IsOn(s)) list.Add(f.Key); } catch { }
        }
        return list;
    }

    /// <summary>How many features round <paramref name="round"/> (1-based) conscripts: 2, 3, 4, 4...</summary>
    public static int WantedFor(int round) => Math.Clamp(1 + Math.Max(1, round), 2, 4);

    /// <summary>Grace (seconds) before an empty dose is refilled: 6 s the first time, then 4, then 2.
    /// Shrinks so the "switch it off again" game gets shorter every round, never longer.</summary>
    public static int DoseGraceFor(int roundsSoFar) => Math.Max(2, 6 - 2 * roundsSoFar);

    /// <summary>Grace (seconds) before a stopped engine is restarted mid-lockdown.</summary>
    public const int EngineGraceSeconds = 4;

    /// <summary>
    /// The pure picker. Order of preference: what the user themselves had on (shuffled), then the
    /// starter pool (shuffled), then - from round 2 - the escalation pool (shuffled). Anything already
    /// on is skipped, so the result is exactly the set of keys to flip. Deterministic under the
    /// supplied Random; never returns more than <see cref="WantedFor"/>.
    /// </summary>
    public static List<string> PickConscripts(int round, IReadOnlyCollection<string> previouslyOn,
        IReadOnlyList<string> starter, IReadOnlyList<string> escalation,
        IReadOnlyCollection<string> currentlyOn, Random rng)
    {
        var want = WantedFor(round);
        var picked = new List<string>(want);
        var skip = new HashSet<string>(currentlyOn ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var known = new HashSet<string>(starter.Concat(escalation), StringComparer.OrdinalIgnoreCase);

        void TakeFrom(IEnumerable<string> pool)
        {
            foreach (var k in Shuffle(pool, rng))
            {
                if (picked.Count >= want) return;
                if (string.IsNullOrEmpty(k) || skip.Contains(k) || !known.Contains(k)) continue;
                if (picked.Contains(k, StringComparer.OrdinalIgnoreCase)) continue;
                picked.Add(k);
            }
        }

        TakeFrom(previouslyOn ?? Array.Empty<string>());
        TakeFrom(starter);
        if (round >= 2) TakeFrom(escalation);
        return picked;
    }

    private static List<string> Shuffle(IEnumerable<string> src, Random rng)
    {
        var list = src.ToList();
        for (int i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }

}
