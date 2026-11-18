using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Dares;

namespace ConditioningControlPanel.Controls.Dares;

/// <summary>The blanks of a slip, in the owner's order (DARES-HANDOFF "The slip").</summary>
public enum DareStep { Who, How, What, When, Mine, Theirs, Camera }

/// <summary>
/// A slip while it is being written. One blank at a time: <see cref="DareSlipRules.Current"/> is the
/// blank in hand (the first open one, or the one tapped to change). In the composer I am always the
/// sender, so <see cref="Mine"/> is the FROM stake and <see cref="Theirs"/> the TO stake.
/// </summary>
public sealed class DareDraft
{
    public DareFriend? Who { get; set; }

    /// <summary>The friend's tier (0 free, 1 Basic, 2 Prime) when the friends list knows it.</summary>
    public int? WhoTier { get; set; }

    public DareType? Type { get; set; }
    /// <summary>For a dare kind the number is assigned at send time (<see cref="DareSlipRules.Numbers"/>).</summary>
    public DareWhat? What { get; set; }
    public DareDeadline? Deadline { get; set; }
    public DareStake? Mine { get; set; }
    public DareStake? Theirs { get; set; }
    public bool? Camera { get; set; }

    /// <summary>A filled blank tapped to change it, or null.</summary>
    public DareStep? Editing { get; set; }

    /// <summary>A kind tile pressed that still needs its amount: a <see cref="DareKind"/> for What, a
    /// <see cref="DareStakeKind"/> for a stake. Null when nothing waits.</summary>
    public object? Pending { get; set; }
}

/// <summary>A tile that can be pressed, one shut with a reason, or one locked as an ad whose press
/// opens the upgrade door.</summary>
public enum DareTileLock { Open, Shut, Upsell }

/// <summary>What kind of piece a sentence segment is.</summary>
public enum DareSegKind { Text, Chip, Blank }

/// <summary>The colour family of a chip or tile (DareLook maps it to a brush). One per kind.</summary>
public enum DareTone { Who, Cant, Duel, Pics, Minutes, Video, Session, Bubbles, Dare, When, Sp, Lock, Lockdown, CameraOn, CameraOff }

/// <summary>The mark at the head of an outcome line: tick, cross, me, them.</summary>
public enum DareMark { None, Yes, No, Me, Them }

/// <summary>One piece of a sentence line. For a chip: its words, its tone, its tooltip key, and the
/// blank it fills (so a tap in the composer opens that blank).</summary>
public sealed record DareSeg(DareSegKind Kind, string Text, DareStep? Step = null, DareTone Tone = DareTone.When, string? TipKey = null);

public sealed record DareLine(DareMark Mark, IReadOnlyList<DareSeg> Segs)
{
    /// <summary>The line as plain words (an Inbox row's summary, a test).</summary>
    public string Plain => string.Concat(Segs.Select(s => s.Text)).Trim();
}

/// <summary>
/// The composer's rules, WPF-free (Tests/Dares/DareSlipUiTests). Blank order, the options each blank
/// offers, how the sentence is put together, and the slip that goes on the wire. The option tables
/// themselves are <see cref="DareGrammar"/>; nothing here invents a choice the server would refuse.
/// </summary>
public static class DareSlipRules
{
    public static readonly IReadOnlyList<DareStakeKind> StakeKinds =
        new[] { DareStakeKind.Sp, DareStakeKind.Dare, DareStakeKind.Lock, DareStakeKind.Lockdown };

    /// <summary>The blanks this slip has, in order. Camera joins only once the kind is known and
    /// allows it (bubbles and custom dares never show a camera line).</summary>
    public static IReadOnlyList<DareStep> Steps(DareDraft d)
    {
        var list = new List<DareStep> { DareStep.Who, DareStep.How, DareStep.What, DareStep.When, DareStep.Mine, DareStep.Theirs };
        if (d.What != null && DareGrammar.CameraAllowed(d.What.Kind)) list.Add(DareStep.Camera);
        return list;
    }

    public static bool IsFilled(DareDraft d, DareStep s) => s switch
    {
        DareStep.Who => d.Who != null,
        DareStep.How => d.Type != null,
        DareStep.What => d.What != null,
        DareStep.When => d.Deadline != null,
        DareStep.Mine => d.Mine != null,
        DareStep.Theirs => d.Theirs != null,
        _ => d.Camera != null,
    };

    /// <summary>The first blank still open, or null when the slip is whole.</summary>
    public static DareStep? FirstOpen(DareDraft d)
    {
        foreach (var s in Steps(d)) if (!IsFilled(d, s)) return s;
        return null;
    }

    /// <summary>The blank in hand: the one being changed, else the first open one. Null = ready to send.</summary>
    public static DareStep? Current(DareDraft d) => d.Editing ?? FirstOpen(d);

    public static bool IsComplete(DareDraft d) => FirstOpen(d) == null;

    /// <summary>How far the sentence shows: every blank up to and including the first open one.</summary>
    public static bool Shows(DareDraft d, DareStep s)
    {
        var steps = Steps(d);
        var open = FirstOpen(d);
        int reach = open == null ? steps.Count - 1 : steps.ToList().IndexOf(open.Value);
        int i = steps.ToList().IndexOf(s);
        return i >= 0 && i <= reach;
    }

    // ---- presses ----------------------------------------------------------------------

    /// <summary>Tap a filled blank: it becomes the blank in hand. Its kind waits for an amount again
    /// when it has one, so the amount row shows at once.</summary>
    public static void Edit(DareDraft d, DareStep s)
    {
        if (!IsFilled(d, s)) { d.Editing = null; d.Pending = null; return; }
        d.Editing = s;
        d.Pending = s switch
        {
            DareStep.What when d.What != null && NeedsTarget(d.Type, d.What.Kind) => d.What.Kind,
            DareStep.Mine when d.Mine != null && NeedsAmount(d.Mine.Kind) => d.Mine.Kind,
            DareStep.Theirs when d.Theirs != null && NeedsAmount(d.Theirs.Kind) => d.Theirs.Kind,
            _ => null,
        };
    }

    /// <summary>A new friend; a Lockdown on their side goes when they cannot owe one.</summary>
    public static void SetWho(DareDraft d, DareFriend f, int? tier = null)
    {
        d.Who = f;
        d.WhoTier = tier;
        if (tier == 0 && d.Theirs?.Kind == DareStakeKind.Lockdown) d.Theirs = null;
        Done(d);
    }

    /// <summary>A different bet type clears what the bet is about: the kinds differ.</summary>
    public static void SetType(DareDraft d, DareType t)
    {
        if (d.Type != t) { d.What = null; d.Camera = null; }
        d.Type = t;
        Done(d);
    }

    /// <summary>A kind tile. Kinds with amounts wait for one (<see cref="DareDraft.Pending"/>); the rest fill at once.</summary>
    public static void PickKind(DareDraft d, DareKind k)
    {
        if (NeedsTarget(d.Type, k)) { d.Pending = k; return; }
        SetWhat(d, new DareWhat(k, null));
    }

    public static void PickTarget(DareDraft d, string target)
    {
        if (d.Pending is not DareKind k) return;
        if (!TargetsFor(d.Type, k).Contains(target)) return;
        SetWhat(d, new DareWhat(k, target));
    }

    private static void SetWhat(DareDraft d, DareWhat w)
    {
        bool before = d.What != null && DareGrammar.CameraAllowed(d.What.Kind);
        d.What = w;
        // The camera answer belongs to a kind that has one; a kind without drops it, a new kind asks again.
        if (!DareGrammar.CameraAllowed(w.Kind) || !before) d.Camera = null;
        Done(d);
    }

    public static void SetDeadline(DareDraft d, DareDeadline when) { d.Deadline = when; Done(d); }

    /// <summary>A stake tile on the blank in hand (Mine or Theirs).</summary>
    public static void PickStakeKind(DareDraft d, DareStakeKind k)
    {
        if (NeedsAmount(k)) { d.Pending = k; return; }
        SetStake(d, new DareStake(k));
    }

    /// <summary>An amount chip. <paramref name="balance"/> is my Sparkles (null = unknown): a Sparkles
    /// amount on my own stake above it cannot be picked (<see cref="AmountCovered"/>).</summary>
    public static void PickAmount(DareDraft d, int amount, int? balance = null)
    {
        if (d.Pending is not DareStakeKind k || !DareGrammar.AmountsFor(k).Contains(amount)) return;
        if (Current(d) is { } side && !AmountCovered(k, side, amount, balance)) return;
        SetStake(d, new DareStake(k, amount));
    }

    /// <summary>A Sparkles stake must be covered when it is put up: on MY stake an amount above my
    /// balance is greyed. The friend's stake stays open (the server checks theirs at accept), and an
    /// unknown balance leaves it to the server too.</summary>
    public static bool AmountCovered(DareStakeKind k, DareStep side, int amount, int? balance)
        => k != DareStakeKind.Sp || side != DareStep.Mine || balance == null || amount <= balance.Value;

    private static void SetStake(DareDraft d, DareStake s)
    {
        var at = Current(d);
        if (at == DareStep.Mine) d.Mine = s;
        else if (at == DareStep.Theirs) d.Theirs = s;
        else return;
        Done(d);
    }

    public static void SetCamera(DareDraft d, bool on) { d.Camera = on; Done(d); }

    private static void Done(DareDraft d) { d.Editing = null; d.Pending = null; }

    // ---- options ----------------------------------------------------------------------

    public static IReadOnlyList<DareKind> Kinds(DareType? t) => DareGrammar.KindsFor(t ?? DareType.Cant);

    /// <summary>Targets a kind offers: counts or difficulties in a "can't", none in a duel or for a dare.</summary>
    public static IReadOnlyList<string> TargetsFor(DareType? t, DareKind k)
        => t == DareType.Duel ? Array.Empty<string>() : DareGrammar.TargetsFor(k);

    public static bool NeedsTarget(DareType? t, DareKind k) => TargetsFor(t, k).Count > 0;

    public static bool NeedsAmount(DareStakeKind k) => DareGrammar.AmountsFor(k).Count > 0;

    /// <summary>How a stake tile stands. A dare anywhere needs Discord linked (its words are written
    /// there). A Lockdown is for subscribers only and stays VISIBLE to everyone as an ad (owner,
    /// 2026-11-18): on my own stake below Basic it is locked with the tier badge and a press opens
    /// the upgrade door (<see cref="DareTileLock.Upsell"/>); on the friend's stake when their tier
    /// is 0 it is shut with "needs a subscriber". An unknown friend tier (null) leaves it to the server.</summary>
    public static (DareTileLock Lock, string? Key) StakeLock(DareStakeKind k, DareStep side, DaresMe me, bool iAmSub, int? friendTier)
    {
        if (k == DareStakeKind.Dare && !me.Discord) return (DareTileLock.Shut, "dares_shut_discord");
        if (k == DareStakeKind.Lockdown)
        {
            if (side == DareStep.Mine && !iAmSub) return (DareTileLock.Upsell, "dares_lockdown_upsell");
            if (side == DareStep.Theirs && friendTier == 0) return (DareTileLock.Shut, "dares_shut_needs_sub");
        }
        return (DareTileLock.Open, null);
    }

    public static string? KindShut(DareKind k, DaresMe me) => k == DareKind.Dare && !me.Discord ? "dares_shut_discord" : null;

    // ---- numbering and the wire -------------------------------------------------------

    /// <summary>Every dare on the slip gets its number in order of appearance: what first, then my
    /// stake (from), then theirs (to). 0 = not a dare.</summary>
    public static (int What, int Mine, int Theirs) Numbers(DareDraft d)
    {
        int n = 0;
        int w = d.What?.Kind == DareKind.Dare ? ++n : 0;
        int m = d.Mine?.Kind == DareStakeKind.Dare ? ++n : 0;
        int t = d.Theirs?.Kind == DareStakeKind.Dare ? ++n : 0;
        return (w, m, t);
    }

    /// <summary>The slip as it goes to <c>CreateAsync</c>, or null while a blank is open.</summary>
    public static DareSlip? ToSlip(DareDraft d)
    {
        if (!IsComplete(d) || d.Type == null || d.What == null || d.Deadline == null || d.Mine == null || d.Theirs == null) return null;
        var (w, m, t) = Numbers(d);
        var what = d.What.Kind == DareKind.Dare ? new DareWhat(DareKind.Dare, w.ToString()) : d.What;
        var from = d.Mine.Kind == DareStakeKind.Dare ? d.Mine with { DareNumber = m } : d.Mine;
        var to = d.Theirs.Kind == DareStakeKind.Dare ? d.Theirs with { DareNumber = t } : d.Theirs;
        bool camera = d.Camera == true && DareGrammar.CameraAllowed(d.What.Kind);
        return new DareSlip(d.Type.Value, what, d.Deadline.Value, from, to, camera);
    }

    /// <summary>The slip has a dare on it somewhere (its words then go through Discord).</summary>
    public static bool HasDare(DareSlip s)
        => s.What.Kind == DareKind.Dare || s.FromStake.Kind == DareStakeKind.Dare || s.ToStake.Kind == DareStakeKind.Dare;

    // ---- the sentence -----------------------------------------------------------------

    /// <summary>The slip as it reads in the composer: only what is reached so far, a blank for the
    /// one in hand, chips for the rest. <paramref name="loc"/> is <c>Loc.Get</c> (the suite passes its own).</summary>
    public static IReadOnlyList<DareLine> Sentence(DareDraft d, Func<string, string> loc)
    {
        var (wn, mn, tn) = Numbers(d);
        var v = new View(d.Who?.Name, d.Type, d.What, wn, d.Deadline, d.Mine, mn, d.Theirs, tn,
            d.Camera, Receiver: false, Shows: s => Shows(d, s));
        return Build(v, loc);
    }

    /// <summary>A bet as it reads from my side: an offer, a running bet, the debt card.</summary>
    public static IReadOnlyList<DareLine> Sentence(DareBet b, Func<string, string> loc)
    {
        int Num(DareStake s) => s.Kind == DareStakeKind.Dare ? s.DareNumber : 0;
        int.TryParse(b.What.Kind == DareKind.Dare ? b.What.Target : null, out var wn);
        var v = new View(b.Friend.Name, b.Type, b.What, wn, b.Deadline, b.MyStake, Num(b.MyStake), b.TheirStake,
            Num(b.TheirStake), b.Camera, Receiver: b.Me == DareSide.To, Shows: _ => true);
        return Build(v, loc);
    }

    internal sealed record View(string? Who, DareType? Type, DareWhat? What, int WhatN, DareDeadline? When,
        DareStake? Mine, int MineN, DareStake? Theirs, int TheirsN, bool? Camera, bool Receiver, Func<DareStep, bool> Shows);

    private static IReadOnlyList<DareLine> Build(View v, Func<string, string> loc)
    {
        bool duel = v.Type == DareType.Duel;
        var lines = new List<DareLine>();
        var head = duel ? "dares_line_duel" : v.Receiver ? "dares_line_cant_to" : "dares_line_cant_from";
        lines.Add(new DareLine(DareMark.None, Fill(loc(head), v, loc, null)));

        // Outcome lines, my side first in the order the mockup reads: what I owe, then what they owe.
        // A receiver of a "can't" reads the yes line first (I make it: they owe), then the miss.
        if (v.Shows(DareStep.Mine) || v.Shows(DareStep.Theirs))
        {
            if (duel)
            {
                if (v.Shows(DareStep.Mine)) lines.Add(new DareLine(DareMark.Me, Fill(loc("dares_line_lose_me"), v, loc, DareStep.Mine)));
                if (v.Shows(DareStep.Theirs)) lines.Add(new DareLine(DareMark.Them, Fill(loc("dares_line_lose_them"), v, loc, DareStep.Theirs)));
            }
            else if (v.Receiver)
            {
                lines.Add(new DareLine(DareMark.Yes, Fill(loc("dares_line_make_me"), v, loc, DareStep.Theirs)));
                lines.Add(new DareLine(DareMark.No, Fill(loc("dares_line_miss_me"), v, loc, DareStep.Mine)));
            }
            else
            {
                if (v.Shows(DareStep.Mine)) lines.Add(new DareLine(DareMark.Yes, Fill(loc("dares_line_make_them"), v, loc, DareStep.Mine)));
                if (v.Shows(DareStep.Theirs)) lines.Add(new DareLine(DareMark.No, Fill(loc("dares_line_miss_them"), v, loc, DareStep.Theirs)));
            }
        }
        if (v.What != null && DareGrammar.CameraAllowed(v.What.Kind) && v.Shows(DareStep.Camera))
            lines.Add(new DareLine(DareMark.None, new[] { CameraSeg(v, loc) }));
        return lines;
    }

    private static readonly Regex Token = new(@"\{(who|how|what|when|stake)\}", RegexOptions.CultureInvariant);

    /// <summary>Splits a template on its named tokens and puts a chip (or a blank) where each stands.
    /// Named, not numbered, so a language can put them in its own order. Internal for the suite.</summary>
    internal static IReadOnlyList<DareSeg> Fill(string template, View v, Func<string, string> loc, DareStep? stakeStep)
    {
        var segs = new List<DareSeg>();
        int at = 0;
        foreach (Match m in Token.Matches(template))
        {
            if (m.Index > at) segs.Add(new DareSeg(DareSegKind.Text, template.Substring(at, m.Index - at)));
            at = m.Index + m.Length;
            var step = m.Groups[1].Value switch
            {
                "who" => DareStep.Who,
                "how" => DareStep.How,
                "what" => DareStep.What,
                "when" => DareStep.When,
                _ => stakeStep ?? DareStep.Mine,
            };
            if (!v.Shows(step)) continue;
            segs.Add(ChipFor(step, v, loc));
        }
        if (at < template.Length) segs.Add(new DareSeg(DareSegKind.Text, template.Substring(at)));
        return Tidy(segs);
    }

    /// <summary>Drops a space left dangling where a hidden blank was, so "I bet Moth  " reads "I bet Moth".</summary>
    private static IReadOnlyList<DareSeg> Tidy(List<DareSeg> segs)
    {
        for (int i = segs.Count - 1; i >= 0; i--)
        {
            if (segs[i].Kind != DareSegKind.Text) break;
            var t = segs[i].Text.TrimEnd();
            if (t.Length == 0) segs.RemoveAt(i);
            else { segs[i] = segs[i] with { Text = t }; break; }
        }
        return segs;
    }

    private static DareSeg ChipFor(DareStep step, View v, Func<string, string> loc)
    {
        switch (step)
        {
            case DareStep.Who:
                return v.Who == null ? Blank(step, loc) : new DareSeg(DareSegKind.Chip, v.Who, step, DareTone.Who, "dares_tip_who");
            case DareStep.How:
                return v.Type == null ? Blank(step, loc)
                    : v.Type == DareType.Duel
                        ? new DareSeg(DareSegKind.Chip, loc("dares_chip_duel"), step, DareTone.Duel, "dares_tip_duel")
                        : new DareSeg(DareSegKind.Chip, loc("dares_chip_cant"), step, DareTone.Cant, "dares_tip_cant");
            case DareStep.What:
                return v.What == null ? Blank(step, loc)
                    : new DareSeg(DareSegKind.Chip, WhatText(v.Type, v.What, v.WhatN, loc), step, ToneOf(v.What.Kind), TipOf(v.What.Kind));
            case DareStep.When:
                return v.When == null ? Blank(step, loc)
                    : new DareSeg(DareSegKind.Chip, loc(WhenKey(v.When.Value)), step, DareTone.When, "dares_tip_when");
            case DareStep.Mine:
                return v.Mine == null ? Blank(step, loc) : StakeSeg(step, v.Mine, v.MineN, loc);
            case DareStep.Theirs:
                return v.Theirs == null ? Blank(step, loc) : StakeSeg(step, v.Theirs, v.TheirsN, loc);
            default:
                return CameraSeg(v, loc);
        }
    }

    private static DareSeg CameraSeg(View v, Func<string, string> loc)
        => v.Camera == null ? Blank(DareStep.Camera, loc)
            : v.Camera == true
                ? new DareSeg(DareSegKind.Chip, loc("dares_chip_camera_on"), DareStep.Camera, DareTone.CameraOn, "dares_tip_camera_on")
                : new DareSeg(DareSegKind.Chip, loc("dares_chip_camera_off"), DareStep.Camera, DareTone.CameraOff, "dares_tip_camera_off");

    private static DareSeg StakeSeg(DareStep step, DareStake s, int n, Func<string, string> loc)
        => new(DareSegKind.Chip, StakeText(s, n, loc), step, ToneOf(s.Kind), TipOf(s.Kind));

    private static DareSeg Blank(DareStep s, Func<string, string> loc) => new(DareSegKind.Blank, loc(BlankKey(s)), s);

    public static string BlankKey(DareStep s) => s switch
    {
        DareStep.Who => "dares_blank_who",
        DareStep.How => "dares_blank_how",
        DareStep.What => "dares_blank_what",
        DareStep.When => "dares_blank_when",
        DareStep.Camera => "dares_blank_camera",
        _ => "dares_blank_stake",
    };

    /// <summary>The words of a "what" chip. A "can't" reads as the deed ("watch 500 pictures"), a
    /// duel as the race ("watches more pictures").</summary>
    public static string WhatText(DareType? t, DareWhat w, int dareNumber, Func<string, string> loc)
    {
        if (t == DareType.Duel)
            return loc(w.Kind switch
            {
                DareKind.Pics => "dares_chip_more_pics",
                DareKind.Minutes => "dares_chip_more_minutes",
                DareKind.Video => "dares_chip_more_video",
                _ => "dares_chip_more_bubbles",
            });
        return w.Kind switch
        {
            DareKind.Pics => Format(loc("dares_chip_pics"), w.Target),
            DareKind.Minutes => Format(loc("dares_chip_minutes"), w.Target),
            DareKind.Video => Format(loc("dares_chip_video"), w.Target),
            DareKind.Session => loc("dares_chip_session_" + (w.Target ?? "easy")),
            DareKind.Dare => Format(loc("dares_chip_do_dare"), dareNumber > 0 ? dareNumber.ToString() : "?"),
            _ => loc("dares_chip_more_bubbles"),
        };
    }

    public static string StakeText(DareStake s, int dareNumber, Func<string, string> loc) => s.Kind switch
    {
        DareStakeKind.Sp => Format(loc("dares_stake_sp"), s.Amount.ToString()),
        DareStakeKind.Lock => Format(loc("dares_stake_lock"), s.Amount.ToString()),
        DareStakeKind.Lockdown => Format(loc("dares_stake_lockdown"), s.Amount.ToString()),
        _ => Format(loc("dares_stake_dare"), dareNumber > 0 ? dareNumber.ToString() : "?"),
    };

    private static string Format(string template, string? arg)
    {
        try { return string.Format(template, arg ?? ""); }
        catch (FormatException) { return template; }
    }

    public static string WhenKey(DareDeadline d) => d switch
    {
        DareDeadline.ThreeHours => "dares_when_3h",
        DareDeadline.Today => "dares_when_today",
        DareDeadline.Weekend => "dares_when_weekend",
        _ => "dares_when_week",
    };

    public static DareTone ToneOf(DareKind k) => k switch
    {
        DareKind.Pics => DareTone.Pics,
        DareKind.Minutes => DareTone.Minutes,
        DareKind.Video => DareTone.Video,
        DareKind.Session => DareTone.Session,
        DareKind.Bubbles => DareTone.Bubbles,
        _ => DareTone.Dare,
    };

    public static DareTone ToneOf(DareStakeKind k) => k switch
    {
        DareStakeKind.Sp => DareTone.Sp,
        DareStakeKind.Lock => DareTone.Lock,
        DareStakeKind.Lockdown => DareTone.Lockdown,
        _ => DareTone.Dare,
    };

    public static string TipOf(DareKind k) => "dares_tip_" + DareGrammar.Wire(k);

    public static string TipOf(DareStakeKind k) => k == DareStakeKind.Dare ? "dares_tip_dare" : "dares_tip_" + DareGrammar.Wire(k);

    /// <summary>The tray heading for the blank in hand.</summary>
    public static string AskKey(DareStep s, DareType? t) => s switch
    {
        DareStep.Who => "dares_ask_who",
        DareStep.How => "dares_ask_how",
        DareStep.What => t == DareType.Duel ? "dares_ask_what_duel" : "dares_ask_what_cant",
        DareStep.When => "dares_ask_when",
        DareStep.Mine => t == DareType.Duel ? "dares_ask_mine_duel" : "dares_ask_mine_cant",
        DareStep.Theirs => t == DareType.Duel ? "dares_ask_theirs_duel" : "dares_ask_theirs_cant",
        _ => "dares_ask_camera",
    };

    /// <summary>The (?) help a blank carries, or null. Camera, Lockdown, numbered dares and video
    /// counting hide a rule (DARES-HANDOFF "Make it easy to understand").</summary>
    public static string? HelpFor(DareStep s, DareDraft d) => s switch
    {
        DareStep.Camera => "dares_help_camera",
        DareStep.What when d.Pending is DareKind.Video || d.What?.Kind == DareKind.Video => "dares_help_video",
        DareStep.What when d.What?.Kind == DareKind.Dare => "dares_help_numbers",
        DareStep.Mine or DareStep.Theirs when d.Pending is DareStakeKind.Lockdown => "dares_help_lockdown",
        DareStep.Mine or DareStep.Theirs when d.Pending is DareStakeKind.Dare => "dares_help_numbers",
        _ => null,
    };

    /// <summary>Tile words for each option.</summary>
    public static string TileKey(DareType t) => t == DareType.Duel ? "dares_tile_duel" : "dares_tile_cant";
    public static string TileKey(DareKind k) => "dares_tile_" + DareGrammar.Wire(k);
    public static string TileKey(DareStakeKind k) => k == DareStakeKind.Dare ? "dares_tile_dare" : "dares_tile_" + DareGrammar.Wire(k);
    public static string TargetLabelKey(string target) => "dares_tile_" + target;   // easy / medium / hard
}
