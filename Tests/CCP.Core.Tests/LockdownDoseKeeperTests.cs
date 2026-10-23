using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics;
using Xunit;
namespace CCP.Core.Tests;
[Collection(SessionStatics.Name)]
public class CoreLockdownDoseKeeperTests
{
    private static readonly string[] Starter = { "flash", "subliminal", "spiral", "pinkfilter", "bouncingtext", "bubbles" };
    private static readonly string[] Escalation = { "video" };
    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    [InlineData(9, 4)]
    public void WantedFor_TwoThenOneMorePerRound_CapsAtFour(int round, int expected)
        => Assert.Equal(expected, CoreLockdownDoseKeeper.WantedFor(round));
    [Theory]
    [InlineData(0, 6)]
    [InlineData(1, 4)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(10, 2)]
    public void DoseGrace_ShrinksPerRound_FloorsAtTwoSeconds(int roundsSoFar, int expected)
        => Assert.Equal(expected, CoreLockdownDoseKeeper.DoseGraceFor(roundsSoFar));
    [Fact]
    public void Round1_PicksTwoStarters_WhenTheUserHadNothingOn()
    {
        var picks = CoreLockdownDoseKeeper.PickConscripts(1, Array.Empty<string>(), Starter, Escalation,
            Array.Empty<string>(), new Random(7));
        Assert.Equal(2, picks.Count);
        Assert.All(picks, k => Assert.Contains(k, Starter));
        Assert.Equal(picks.Count, picks.Distinct().Count());
    }
    [Fact]
    public void Round1_TurnsTheUsersOwnFeaturesBackOnFirst()
    {
        var picks = CoreLockdownDoseKeeper.PickConscripts(1, new[] { "bubbles", "flash" }, Starter, Escalation,
            Array.Empty<string>(), new Random(3));
        Assert.Equal(2, picks.Count);
        Assert.Contains("flash", picks);
        Assert.Contains("bubbles", picks);
    }
    [Fact]
    public void NeverPicksWhatIsAlreadyOn()
    {
        var on = new[] { "flash", "subliminal" };
        var picks = CoreLockdownDoseKeeper.PickConscripts(2, Array.Empty<string>(), Starter, Escalation, on, new Random(1));
        Assert.Equal(3, picks.Count);
        Assert.DoesNotContain("flash", picks);
        Assert.DoesNotContain("subliminal", picks);
    }
    [Fact]
    public void Round1_NeverReachesTheEscalationPool()
    {
        for (int seed = 0; seed < 50; seed++)
        {
            var picks = CoreLockdownDoseKeeper.PickConscripts(1, Array.Empty<string>(), Starter, Escalation,
                Array.Empty<string>(), new Random(seed));
            Assert.DoesNotContain("video", picks);
        }
    }
    [Fact]
    public void Round2Plus_CanReachTheEscalationPool_WhenStartersRunOut()
    {
        var picks = CoreLockdownDoseKeeper.PickConscripts(2, Array.Empty<string>(), Starter, Escalation, Starter, new Random(5));
        Assert.Equal(new[] { "video" }, picks);
    }
    [Fact]
    public void UnknownPreviouslyOnKeys_AreIgnored()
    {
        var picks = CoreLockdownDoseKeeper.PickConscripts(1, new[] { "braindrain", "nope" }, Starter, Escalation,
            Array.Empty<string>(), new Random(2));
        Assert.DoesNotContain("braindrain", picks);
        Assert.DoesNotContain("nope", picks);
        Assert.Equal(2, picks.Count);
    }
    [Fact]
    public void NothingLeftToPick_ReturnsEmpty_NotAnException()
    {
        var all = Starter.Concat(Escalation).ToArray();
        var picks = CoreLockdownDoseKeeper.PickConscripts(3, all, Starter, Escalation, all, new Random(0));
        Assert.Empty(picks);
    }
    [Fact]
    public void Deterministic_UnderTheSameSeed()
    {
        var a = CoreLockdownDoseKeeper.PickConscripts(2, new[] { "spiral" }, Starter, Escalation, Array.Empty<string>(), new Random(42));
        var b = CoreLockdownDoseKeeper.PickConscripts(2, new[] { "spiral" }, Starter, Escalation, Array.Empty<string>(), new Random(42));
        Assert.Equal(a, b);
    }
    [Fact]
    public void Catalog_TierZeroIsTheStarterMix_AndEveryKeyIsAWallKey()
    {
        var tier0 = CoreLockdownDoseKeeper.Catalog.Where(f => f.Tier == 0).Select(f => f.Key).ToArray();
        Assert.Equal(Starter.OrderBy(k => k), tier0.OrderBy(k => k));
        var wallKeys = new HashSet<string> { "flash", "video", "subliminal", "spiral", "pinkfilter", "bubbles",
            "lockcard", "bubblecount", "bouncingtext", "mindwipe", "braindrain" };
        Assert.All(CoreLockdownDoseKeeper.Catalog, f => Assert.Contains(f.Key, wallKeys));
    }
    private static AppSettings QuietRoom() => new()
    {
        FlashEnabled = false,
        SubliminalEnabled = false,
        SpiralEnabled = false,
        PinkFilterEnabled = false,
        BouncingTextEnabled = false,
        BubblesEnabled = false,
        MandatoryVideosEnabled = false,
        MindWipeEnabled = false,
        LockCardEnabled = false,
        BubbleCountEnabled = false,
        BrainDrainEnabled = false,
        PopQuizEnabled = false,
        AudioOnlySession = false,
        AutonomyModeEnabled = false,
        AutonomyConsentGiven = false,
        CornerGifOverlays = new List<CornerGifOverlaySetting>(),
    };
    [Fact]
    public void RememberedTakeoverConsentAloneDoesNotCountAsADose()
    {
        var s = QuietRoom();
        s.AutonomyModeEnabled = s.AutonomyConsentGiven = true;
        Assert.True(CoreLockdownDoseKeeper.DoseIsEmpty(s, false));
        Assert.False(CoreLockdownDoseKeeper.DoseIsEmpty(s, true));
    }
    [Fact]
    public void PopQuizAlone_IsNotAnEmptyRoom()
    {
        var s = QuietRoom();
        Assert.True(CoreLockdownDoseKeeper.DoseIsEmpty(s));
        s.PopQuizEnabled = true;
        Assert.False(CoreLockdownDoseKeeper.DoseIsEmpty(s));
        Assert.True(CoreLockdownDoseKeeper.CountsAsOffWallDose(s));
    }
    [Fact]
    public void PopQuiz_CountsButIsNeverConscriptable()
    {
        Assert.DoesNotContain(CoreLockdownDoseKeeper.Catalog,
            f => string.Equals(f.Key, "popquiz", StringComparison.OrdinalIgnoreCase));
        var picks = CoreLockdownDoseKeeper.PickConscripts(3, new[] { "popquiz" }, Starter, Escalation,
            Array.Empty<string>(), new Random(11));
        Assert.DoesNotContain("popquiz", picks);
    }
    [Theory]
    [InlineData("clip.mp4", true)]
    [InlineData("CLIP.MP4", true)]
    [InlineData("a.mov", true)]
    [InlineData("a.avi", true)]
    [InlineData("a.wmv", true)]
    [InlineData("a.mkv", true)]
    [InlineData("a.webm", true)]
    [InlineData("Thumbs.db", false)]
    [InlineData("desktop.ini", false)]
    [InlineData("clip.ccpenh.json", false)]
    [InlineData("cover.jpg", false)]
    [InlineData("notes", false)]
    public void OnlyFilesVideoServiceCouldPlay_CountAsVideoAssets(string name, bool playable)
    {
        Assert.Equal(playable, CoreLockdownDoseKeeper.IsPlayableVideoFile(name));
    }
    [Theory]
    [InlineData("activation")]
    [InlineData("refill")]
    [InlineData("restart")]
    [InlineData("session")]
    [InlineData("panic")]
    [InlineData("disabled")]
    [InlineData("restore-failure")]
    public void RuntimeKeepsTheDoseAndGivesBackWhatItBorrowed(string scenario)
    {
        var s = CoreSettings.Current;
        var quiet = QuietRoom();
        var saved = (s.FlashEnabled, s.SubliminalEnabled, s.SpiralEnabled, s.PinkFilterEnabled, s.BouncingTextEnabled,
            s.BubblesEnabled, s.MandatoryVideosEnabled, s.MindWipeEnabled, s.LockCardEnabled, s.BubbleCountEnabled,
            s.BrainDrainEnabled, s.PopQuizEnabled, s.AudioOnlySession, s.AutonomyModeEnabled, s.CornerGifOverlays,
            s.LockdownDoseKeeperEnabled, s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownTripwiresEnabled);
        var prev = LockdownService.Current;
        var takeover = CoreLockdownDoseKeeper.TakeoverRunningProvider;
        var dir = Directory.CreateTempSubdirectory("ccp-dose-").FullName;
        var recovery = Path.Combine(dir, "lockdown-dose.json");
        var ld = LockdownService.Current = new LockdownService();
        var engine = false;
        var session = false;
        var failRestore = false;
        var starves = 0;
        ld.EscapeAttempted += a => { if (a.Kind == "starve") starves++; };
        var flips = new List<(string Key, bool On)>();
        var clock = new PopQuizSchedulerTests.FakeClock();
        var barks = new List<(string Names, int Round, bool Engine)>();
        var host = new LockdownDoseHost
        {
            IsEngineRunning = () => engine,
            IsSessionFeatureLockActive = () => session,
            StartEngine = () => engine = true,
            StopEngine = () => engine = false,
            SetWallFeature = (key, on) =>
            {
                if (failRestore && !on) throw new InvalidOperationException("restore unavailable");
                lock (flips) flips.Add((key, on));
                switch (key)
                {
                    case "flash": s.FlashEnabled = on; break;
                    case "subliminal": s.SubliminalEnabled = on; break;
                    case "spiral": s.SpiralEnabled = on; break;
                    case "pinkfilter": s.PinkFilterEnabled = on; break;
                    case "bouncingtext": s.BouncingTextEnabled = on; break;
                    case "bubbles": s.BubblesEnabled = on; break;
                    case "video": s.MandatoryVideosEnabled = on; break;
                }
            },
            Bark = (names, round, started) => { lock (barks) barks.Add((names, round, started)); },
            Time = clock,
            RecoveryPath = () => recovery,
            AssetsPath = () => dir,
        };
        using var keeper = new CoreLockdownDoseKeeper(ld, host);
        // Install's own hooks drive it: the kickoff timer on the stepped clock, CountdownTick via RestartTimer.
        // Only the test's own ticks run; the service's real 1 s timer posts from the pool and is dropped.
        var post = CoreDispatch.PostProvider;
        var me = Environment.CurrentManagedThreadId;
        CoreDispatch.PostProvider = a => { if (Environment.CurrentManagedThreadId == me) a(); };
        void Tick() => ld.RestartTimer("test");
        try
        {
            CoreLockdownDoseKeeper.TakeoverRunningProvider = () => false;
            (s.FlashEnabled, s.SubliminalEnabled, s.SpiralEnabled, s.PinkFilterEnabled, s.BouncingTextEnabled,
                s.BubblesEnabled, s.MandatoryVideosEnabled, s.MindWipeEnabled, s.LockCardEnabled, s.BubbleCountEnabled,
                s.BrainDrainEnabled, s.PopQuizEnabled, s.AudioOnlySession, s.AutonomyModeEnabled, s.CornerGifOverlays) =
                (false, false, false, false, false, false, false, false, false, false, false, false, false, false, quiet.CornerGifOverlays);
            s.LockdownDoseKeeperEnabled = s.LockdownTripwiresEnabled = true;
            keeper.Install();
            ld.Activate(TimeSpan.FromMinutes(20));
            Assert.True(keeper.IsArmed);
            clock.Advance(TimeSpan.FromMilliseconds(1999));
            Assert.False(engine);
            clock.Advance(TimeSpan.FromMilliseconds(1));            // WPF's 2 s kickoff
            Assert.True(engine);                                   // the engine was started for the user
            Assert.Equal(1, keeper.Round);
            Assert.Equal(2, CoreLockdownDoseKeeper.KeysOn(s).Count);   // round one conscripts two starters
            Assert.True(File.Exists(recovery));                    // a killed lockdown can still give them back
            var bark = Assert.Single(barks);
            Assert.True(bark.Engine && bark.Round == 1 && bark.Names.Contains(" and "));
            Tick(); // the next countdown observes the newly supplied dose
            if (scenario == "restart" || scenario == "panic")
            {
                engine = false;
                if (scenario == "panic") keeper.HoldAfterPanic();
                for (var i = 0; i < 3; i++) Tick();
                Assert.False(engine);
                Tick();
                Assert.Equal(scenario == "restart", engine);
                if (scenario == "restart") Assert.Equal(("something", 1, true), barks[^1]);   // WPF BarkService.cs:422
                keeper.ReleasePanicHold();                         // the user's START (WPF PanicAftermath.EndQuiet)
                for (var i = 0; i < 4; i++) Tick();
                Assert.True(engine);
            }
            if (scenario == "refill" || scenario == "session" || scenario == "disabled")
            {
                foreach (var key in CoreLockdownDoseKeeper.KeysOn(s)) host.SetWallFeature(key, false);
                session = scenario == "session";
                s.LockdownDoseKeeperEnabled = scenario != "disabled";
                for (var i = 0; i < 3; i++) Tick();
                Assert.Empty(CoreLockdownDoseKeeper.KeysOn(s));
                Assert.Equal(scenario == "refill" ? 1 : 0, starves);
                session = false;
                Tick();
                Assert.Equal(scenario == "refill" ? 3 : 0, CoreLockdownDoseKeeper.KeysOn(s).Count);
                if (scenario == "refill")
                {
                    foreach (var key in CoreLockdownDoseKeeper.KeysOn(s)) host.SetWallFeature(key, false);
                    Tick();
                    Assert.Empty(CoreLockdownDoseKeeper.KeysOn(s));
                    Tick();
                    Assert.Equal(4, CoreLockdownDoseKeeper.KeysOn(s).Count);
                }
                if (scenario == "session") Assert.Equal(1, starves); // stand-down preserved the edge
            }
            failRestore = scenario == "restore-failure";
            ld.Deactivate();
            if (failRestore)
            {
                Assert.True(File.Exists(recovery));
                CoreLockdownDoseKeeper.RecoverIfNeeded(recovery);
            }
            Assert.False(keeper.IsArmed);
            Assert.Empty(CoreLockdownDoseKeeper.KeysOn(s));            // every borrowed toggle is back off
            Assert.False(engine);                                  // and the engine it started is stopped
            Assert.False(File.Exists(recovery));
        }
        finally
        {
            CoreDispatch.PostProvider = post;
            if (ld.IsActive) ld.Deactivate();
            LockdownService.Current = prev;
            CoreLockdownDoseKeeper.TakeoverRunningProvider = takeover;
            (s.FlashEnabled, s.SubliminalEnabled, s.SpiralEnabled, s.PinkFilterEnabled, s.BouncingTextEnabled,
                s.BubblesEnabled, s.MandatoryVideosEnabled, s.MindWipeEnabled, s.LockCardEnabled, s.BubbleCountEnabled,
                s.BrainDrainEnabled, s.PopQuizEnabled, s.AudioOnlySession, s.AutonomyModeEnabled, s.CornerGifOverlays,
                s.LockdownDoseKeeperEnabled, s.StrictLockEnabled, s.PanicKeyEnabled, s.LockdownTripwiresEnabled) = saved;
            try { Directory.Delete(dir, true); } catch { }
        }
    }
    [Fact]
    public void AnInterruptedLockdownsTogglesAreSwitchedBackOffAtTheNextLaunch()
    {
        var s = CoreSettings.Current;
        var saved = (s.FlashEnabled, s.SpiralEnabled);
        var dir = Directory.CreateTempSubdirectory("ccp-dose-").FullName;
        var recovery = Path.Combine(dir, "lockdown-dose.json");
        try
        {
            (s.FlashEnabled, s.SpiralEnabled) = (true, true);
            File.WriteAllText(recovery, "{\"Flipped\":[\"flash\"]}");
            CoreLockdownDoseKeeper.RecoverIfNeeded(recovery);
            Assert.False(s.FlashEnabled);
            Assert.True(s.SpiralEnabled);     // not in the record: the user's own
            Assert.False(File.Exists(recovery));
        }
        finally
        {
            (s.FlashEnabled, s.SpiralEnabled) = saved;
            CoreSettings.SaveImmediate();
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
