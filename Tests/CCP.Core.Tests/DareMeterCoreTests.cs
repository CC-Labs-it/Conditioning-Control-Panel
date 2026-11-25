using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Dares;
using Xunit;

namespace ConditioningControlPanel.Tests.Dares;

/// <summary>The counting rules, the camera sums and the camera check.</summary>
public class DareMeterCoreTests
{
    internal static readonly DateTimeOffset T0 = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    internal static DareBet Bet(string id, DareKind kind, string? target = null, DareSide me = DareSide.To,
        DareType type = DareType.Cant, bool camera = false, DareState state = DareState.Running,
        DareStake? myStake = null, DareSide? winner = null, DareDebt? debt = null) => new(
        id, me, new DareFriend("u_f", "Fen", null), type, new DareWhat(kind, target), DareDeadline.ThreeHours,
        T0.AddHours(3), camera, me == DareSide.From ? myStake ?? new DareStake(DareStakeKind.Sp, 50) : new DareStake(DareStakeKind.Sp, 50),
        me == DareSide.To ? myStake ?? new DareStake(DareStakeKind.Sp, 50) : new DareStake(DareStakeKind.Sp, 50),
        Array.Empty<DareSeal>(), state, winner, 0, 0, null, null, debt, T0.AddMinutes(-5), T0, null, null);

    private sealed class MemStore : IDareMeterStore
    {
        public string? Text;
        public string? Read() => Text;
        public void Write(string text) => Text = text;
    }

    [Fact]
    public void Only_the_receiver_of_a_cant_and_both_duel_sides_report()
    {
        Assert.True(DareMeter.Reports(Bet("a", DareKind.Pics, "100", DareSide.To)));
        Assert.False(DareMeter.Reports(Bet("b", DareKind.Pics, "100", DareSide.From)));
        Assert.True(DareMeter.Reports(Bet("c", DareKind.Bubbles, null, DareSide.From, DareType.Duel)));
        Assert.False(DareMeter.Reports(Bet("d", DareKind.Dare, "1")));
        Assert.False(DareMeter.Reports(Bet("e", DareKind.Pics, "100", state: DareState.Sent)));
    }

    [Fact]
    public void Counts_land_only_inside_the_window_and_only_on_their_kind()
    {
        var m = new DareMeter();
        m.Track(new[] { Bet("p", DareKind.Pics, "100"), Bet("q", DareKind.Bubbles, null, DareSide.From, DareType.Duel) });
        m.NotePics(3, T0.AddMinutes(-1));       // before accepted_at
        m.NotePics(3, T0.AddMinutes(1));
        m.NoteBubbles(1, T0.AddMinutes(1));
        m.NotePics(5, T0.AddHours(4));          // after ends_at
        Assert.Equal(3, m.Reading("p")!.Value.Count);
        Assert.Equal(1, m.Reading("q")!.Value.Count);
    }

    [Fact]
    public void A_session_counts_at_its_difficulty_or_harder()
    {
        var m = new DareMeter();
        m.Track(new[] { Bet("s", DareKind.Session, "medium") });
        m.NoteSession(SessionDifficulty.Easy, T0.AddMinutes(1));
        Assert.Equal(0, m.Reading("s")!.Value.Count);
        m.NoteSession(SessionDifficulty.Extreme, T0.AddMinutes(2));
        Assert.Equal(1, m.Reading("s")!.Value.Count);
    }

    [Fact]
    public void Minutes_run_on_real_time_and_a_long_gap_is_capped()
    {
        var m = new DareMeter();
        m.Track(new[] { Bet("m", DareKind.Minutes, "30") });
        var on = new DareLive(Engine: true, Session: false, Video: false);
        for (var i = 1; i <= 90; i++) m.Sample(1, on, T0.AddSeconds(i));
        Assert.Equal(1, m.Reading("m")!.Value.Count);
        m.Sample(3600, on, T0.AddMinutes(70));   // a sleep: 15 s at most
        Assert.Equal(1, m.Reading("m")!.Value.Count);
        m.Sample(30, new DareLive(false, false, false), T0.AddMinutes(71));
        Assert.Equal(1, m.Reading("m")!.Value.Count);
    }

    [Fact]
    public void Video_is_one_clock_whatever_plays()
    {
        var m = new DareMeter();
        m.Track(new[] { Bet("v", DareKind.Video, "15") });
        // Three floating videos at once are still one "a video is playing" per step.
        for (var i = 1; i <= 120; i++) m.Sample(1, new DareLive(false, false, Video: true), T0.AddSeconds(i));
        Assert.Equal(2, m.Reading("v")!.Value.Count);
    }

    [Fact]
    public void The_camera_counts_only_while_the_bets_own_counting_is_live()
    {
        var m = new DareMeter();
        m.Track(new[] { Bet("c", DareKind.Video, "15", camera: true) });
        var playing = new DareLive(false, false, true);
        Assert.False(m.CameraWanted(T0.AddMinutes(1), default));
        Assert.True(m.CameraWanted(T0.AddMinutes(1), playing));
        for (var i = 0; i < 10; i++) m.CameraSample(0.5, seen: i < 8, playing, T0.AddMinutes(1));
        m.CameraSample(0.5, true, default, T0.AddMinutes(1));   // nothing live: not counted
        var r = m.Reading("c")!.Value;
        Assert.Equal(5, r.CountedSeconds);
        Assert.Equal(4, r.SeenSeconds);
        Assert.True(r.Camera);
    }

    [Fact]
    public void Totals_survive_a_restart_and_drop_with_the_bet_or_the_account()
    {
        var store = new MemStore();
        var m = new DareMeter(store);
        m.SetAccount("u_me");
        m.Track(new[] { Bet("p", DareKind.Pics, "100") });
        m.NotePics(7, T0.AddMinutes(1));
        m.Save();
        var again = new DareMeter(store);
        again.SetAccount("u_me");
        again.Track(new[] { Bet("p", DareKind.Pics, "100") });
        Assert.Equal(7, again.Reading("p")!.Value.Count);
        again.Track(Array.Empty<DareBet>());
        Assert.Null(again.Reading("p"));
        again.Track(new[] { Bet("p", DareKind.Pics, "100") });
        again.NotePics(1, T0.AddMinutes(1));
        again.SetAccount("u_other");
        Assert.Null(again.Reading("p"));
    }

    private sealed class GatedStore : IDareMeterStore
    {
        public string? Text;
        public bool Fail;
        public readonly System.Threading.ManualResetEventSlim Entered = new(), Release = new(true);
        private int _writes;
        public string? Read() => Text;
        public void Write(string text)
        {
            Entered.Set();
            if (System.Threading.Interlocked.Increment(ref _writes) == 1) Release.Wait();   // only the first write stalls
            if (Fail) throw new System.IO.IOException("disk full");
            Text = text;
        }
    }

    [Fact]
    public void Overlapping_saves_never_land_an_older_snapshot_last()
    {
        var store = new GatedStore();
        var m = new DareMeter(store);
        m.Track(new[] { Bet("p", DareKind.Pics, "100") });
        m.NotePics(1, T0.AddMinutes(1));
        store.Release.Reset();
        var first = System.Threading.Tasks.Task.Run(m.Save);   // holds the old snapshot inside Write
        Assert.True(store.Entered.Wait(5000));
        m.NotePics(4, T0.AddMinutes(1));
        var second = System.Threading.Tasks.Task.Run(m.Save);
        Assert.False(second.Wait(200));   // waits for the first write instead of racing it
        store.Release.Set();
        System.Threading.Tasks.Task.WaitAll(first, second);
        var again = new DareMeter(store);
        again.Track(new[] { Bet("p", DareKind.Pics, "100") });
        Assert.Equal(5, again.Reading("p")!.Value.Count);
    }

    [Fact]
    public void A_failed_write_keeps_the_counts_unsaved()
    {
        var store = new GatedStore { Fail = true };
        var m = new DareMeter(store);
        m.Track(new[] { Bet("p", DareKind.Pics, "100") });
        m.NotePics(2, T0.AddMinutes(1));
        m.Save();
        Assert.Null(store.Text);
        store.Fail = false;
        m.Save();   // nothing new moved, the retry still writes
        var again = new DareMeter(store);
        again.Track(new[] { Bet("p", DareKind.Pics, "100") });
        Assert.Equal(2, again.Reading("p")!.Value.Count);
    }
}
