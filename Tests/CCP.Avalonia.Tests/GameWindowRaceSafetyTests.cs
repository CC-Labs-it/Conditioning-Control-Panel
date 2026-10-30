using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Race;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // WPF CaucusHostService.cs:383,435: both paths stop the file clock.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Race_RunEndAndExit_StopTheAudio(bool exit) => RunRace("rt.original.03", (_, _, _) =>
        WithTrackFiles((_, _) => Tone(1), paths =>
    {
        var player = new FakeTrackPlayer();
        try
        {
            var w = GameWindow.Launch("race")!;
            w.RacePickFile = () => Task.FromResult<string?>(paths[0]);
            w.RaceNewPlayer = () => player;
            w.HandleMessage("{\"type\":\"track-pick\"}");
            Settle(w);
            w.HandleMessage("{\"type\":\"track-play\"}");
            w.HandleMessage("{\"type\":\"run-started\"}");
            w.HandleMessage(exit ? "{\"type\":\"exit\"}" : "{\"type\":\"run-ended\"}");
            Assert.False(player.IsPlaying);
            Assert.False(w.RaceTrackClockRunning);
        }
        finally { GameWindow.CloseAllForPanic(); }
    }));

    // WPF CaucusHostService.cs:1108-1115 and CloseActive:235-248.
    [Fact]
    public void Race_OwnershipRefreshAndLoss_UnsubscribesOnPanic() => RunRace("rt.original.03", (_, _, _) =>
    {
        var settings = CoreSettings.Current;
        var old = (settings.UnifiedId, settings.OfflineMode, RacingAccess.IsGrantedProvider, CoreSecrets.RetrieveProvider);
        try
        {
            (settings.UnifiedId, settings.OfflineMode) = ("race-test", false);
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "test-token" : null;   // P02: never the keyring
            RacingAccess.IsGrantedProvider = PrizeOwnership.Held;
            PrizeOwnership.ApplySnapshot("race-test", 1, new[] { "rt.original.03" });
            var w = Assert.IsType<GameWindow>(GameWindow.Launch("race"));
            w.HandleMessage("{\"type\":\"ready\"}");   // WPF CloseActive asks a ready page to exit
            Dispatcher.UIThread.RunJobs();
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            PrizeOwnership.ApplySnapshot("race-test", 2, new[] { "rt.original.03", "rt.original.04" });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "race-ownership" }, sent.Select(o => (string?)o["type"]));
            Assert.Equal(new[] { 3, 4 }, sent[0]["tracks"]!.Values<int>());
            sent.Clear();
            PrizeOwnership.ApplySnapshot("race-test", 1, Array.Empty<string>()); // stale
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(sent);
            PrizeOwnership.ApplySnapshot("race-test", 3, new[] { "rt.original.04" });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("exit-request", (string?)Assert.Single(sent)["type"]);
            GameWindow.CloseAllForPanic();
            sent.Clear();
            PrizeOwnership.ApplySnapshot("race-test", 4, new[] { "rt.original.05" });
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(sent);
            PrizeOwnership.ApplySnapshot("race-test", 5, new[] { "rt.original.03" });
            var loading = GameWindow.Launch("race")!;   // never ready: WPF DisposeAll at once, no 1200 ms wait
            loading.Posted += json => sent.Add(JObject.Parse(json));
            PrizeOwnership.ApplySnapshot("race-test", 6, Array.Empty<string>());
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
            Assert.Empty(sent);
        }
        finally
        {
            (settings.UnifiedId, settings.OfflineMode, RacingAccess.IsGrantedProvider, CoreSecrets.RetrieveProvider) = old;
            PrizeOwnership.ResetHeld();
        }
    });

    private sealed class RaceVideoHost : IMandatoryVideoHost
    {
        public void Show(string path, bool strict) { }
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => then();
    }

    private sealed class RaceVideoClock : TimeProvider
    {
        public Action? Due;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period)
            => new Timer(this, () => callback(state));
        private sealed class Timer(RaceVideoClock clock, Action tick) : ITimer
        {
            public bool Change(TimeSpan due, TimeSpan period)
            {
                clock.Due = due == Timeout.InfiniteTimeSpan ? null : tick;
                return true;
            }
            public void Dispose() => clock.Due = null;
            public ValueTask DisposeAsync() { Dispose(); return default; }
        }
    }

    // WPF CaucusHostService.cs:558-592 pauses while the mandatory clip owns focus.
    [Fact]
    public void Race_VideoPausesResumes_AndStopsPostingAfterPanic() => RunRace("rt.original.03", (_, _, _) =>
    {
        var old = CoreEngine.Video;
        var clock = new RaceVideoClock();
        var video = new MandatoryVideoScheduler(new RaceVideoHost(), clock, () => new[] { "test.mp4" });
        try
        {
            CoreEngine.Video = video;
            var w = GameWindow.Launch("race")!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            video.Trigger();
            clock.Due!();
            Dispatcher.UIThread.RunJobs();
            Assert.True((bool)Assert.Single(sent)["on"]!);
            video.End();
            Dispatcher.UIThread.RunJobs();
            Assert.False((bool)sent[1]["on"]!);
            GameWindow.CloseAllForPanic();
            sent.Clear();
            video.Trigger();
            clock.Due!();
            video.End();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(sent);
        }
        finally { video.Stop(); CoreEngine.Video = old; }
    });

}
