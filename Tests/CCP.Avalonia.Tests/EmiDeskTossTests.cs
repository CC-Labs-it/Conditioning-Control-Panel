using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF EmiDeskWindow.Toss.cs on the Avalonia desk: a real drag picks her up (lift cue,
/// o_o), a fast one lands as a throw (counted, thud, squash), a slow one as a drop (bump).</summary>
public sealed class EmiDeskTossTests
{
    private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task DragPicksHerUpAThrowLandsAsAThudAndADropAsABump() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var played = new List<string>();
        var oldProvider = CoreAudio.PlayOneShotProvider;
        int oldVolume = CoreSettings.Current.MasterVolume;
        var clock = new SteppedClock();
        var st = EmiState.Current;
        int flings = st.FlingsTotal;
        var (left, top, monitor) = (st.WinLeftPx, st.WinTopPx, st.Monitor);
        EmiDeskWindow? desk = null;
        try
        {
            CoreAudio.PlayOneShotProvider = (path, _, tag, _, _) => played.Add(tag + ":" + System.IO.Path.GetFileName(path));
            CoreSettings.Current.MasterVolume = 50;
            EmiSfx.Clock = clock;
            EmiDeskWindow.TossClock = clock;

            desk = new EmiDeskWindow();
            desk.Show();
            Dispatcher.UIThread.RunJobs();
            var art = (Image)typeof(EmiDeskWindow).GetField("_bodyImage", Priv)!.GetValue(desk)!;
            var idleArt = art.Source;
            Assert.NotNull(idleArt);
            var body = (Control)typeof(EmiDeskWindow).GetField("_bodyRoot", Priv)!.GetValue(desk)!;
            var c = body.TranslatePoint(new Point(body.Bounds.Width / 2, body.Bounds.Height / 2), desk)!.Value;
            var toss = typeof(EmiDeskWindow).GetMethod("NoteTossFrame", Priv)!;

            // Pick her up: past the drag threshold is the lift cue and the startled face. A wobble
            // tick under load may already have swapped it for a flight face, so record the frames.
            var faces = new List<string>();
            desk.FaceChanged += (_, f) => faces.Add(f);
            desk.MouseDown(c, MouseButton.Left);
            desk.MouseMove(c + new Point(40, 0));
            Assert.Equal(new[] { "emi-sfx-toss:ui_equip.mp3" }, played);
            Assert.Contains(EmiTossRules.PickupFace, faces);

            // The wobble tick's frames, flying fast for longer than FLING_MS, then let go.
            toss.Invoke(desk, new object[] { 0.016, 5000.0 });
            clock.Now += TimeSpan.FromMilliseconds(130).Ticks;
            toss.Invoke(desk, new object[] { 0.016, 5000.0 });
            desk.MouseUp(c + new Point(40, 0), MouseButton.Left);
            Assert.Equal(flings + 1, st.FlingsTotal);
            Assert.Equal("emi-sfx-toss:thud.mp3", played[^1]);
            var squash = (CancellationTokenSource?)typeof(EmiDeskWindow).GetField("_squashAnim", Priv)!.GetValue(desk);
            Assert.NotNull(squash);                                  // the landing owns the squash slot

            Assert.Equal("@_@", desk.Face);
            foreach (var face in new[] { "=_=", "@_@", "x_x", "0_0" })
            {
                clock.Fire();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(face, desk.Face);
            }
            Assert.False(desk.ChainLive);

            // A slow drag is a drop: not counted, the bump cue.
            clock.Now += TimeSpan.TicksPerSecond;
            desk.MouseDown(c, MouseButton.Left);
            desk.MouseMove(c + new Point(40, 0));
            toss.Invoke(desk, new object[] { 0.016, 200.0 });
            desk.MouseUp(c + new Point(40, 0), MouseButton.Left);
            Assert.Equal(flings + 1, st.FlingsTotal);
            Assert.Equal("emi-sfx-toss:shield_thunk.mp3", played[^1]);
            Assert.NotSame(squash, typeof(EmiDeskWindow).GetField("_squashAnim", Priv)!.GetValue(desk));

            // WPF's LandChain: ^_^, then her rest face, not the startled pickup face forever.
            Assert.Equal("^_^", desk.Face);
            Assert.NotSame(idleArt, art.Source);
            Assert.NotNull(art.Source);
            Assert.Equal("celebration", typeof(EmiDeskWindow).GetField("_pose", Priv)!.GetValue(desk));
            Assert.True(desk.ChainLive);
            Assert.Equal(TimeSpan.FromMilliseconds(EmiDeskWindow.LandFaceMs), clock.Due);
            clock.Fire();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("0_0", desk.Face);
            Assert.True(desk.ChainLive); // the final rest frame holds for 160 ms
            clock.Fire();
            Dispatcher.UIThread.RunJobs();
            Assert.False(desk.ChainLive);
            Assert.Equal("idle", typeof(EmiDeskWindow).GetField("_pose", Priv)!.GetValue(desk));
            Assert.Same(idleArt, art.Source);
            var sheet = EmiChains.BodyPath("celebration")!;
            System.IO.File.Move(sheet, sheet + ".test-hidden");
            try
            {
                desk.SetPose("celebration");
                Assert.Same(idleArt, art.Source); // WPF xaml.cs:833-838 retains prior art
            }
            finally { System.IO.File.Move(sheet + ".test-hidden", sheet); }
            desk.SetPose("celebration");
            var celebration = art.Source;
            desk.SetPose("idle");
            desk.SetPose("celebration");
            Assert.Same(celebration, art.Source); // a repeat uses the cache
            desk.SetPose("idle");
            desk.PlayChain("dizzy");
            desk.CancelChain();
            clock.Fire();
            Dispatcher.UIThread.RunJobs();
            Assert.False(desk.ChainLive);
            Assert.Equal("@_@", desk.Face); // cancellation never runs the next frame or Done
            desk.PlayChain("dizzy");
            desk.Close();
            clock.Fire();
            Dispatcher.UIThread.RunJobs();
            Assert.False(desk.ChainLive); // no queued frame survives the window

        }
        finally
        {
            desk?.Close();
            CoreAudio.PlayOneShotProvider = oldProvider;
            CoreSettings.Current.MasterVolume = oldVolume;
            EmiSfx.Clock = TimeProvider.System;
            EmiDeskWindow.TossClock = TimeProvider.System;
            st.FlingsTotal = flings;
            st.WinLeftPx = left; st.WinTopPx = top; st.Monitor = monitor;
        }
        return Task.CompletedTask;
    });

    private sealed class SteppedClock : TimeProvider
    {
        public long Now = TimeSpan.TicksPerDay;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(Now, TimeSpan.Zero);

        // One pending one-shot timer, fired by the test instead of waiting for it.
        private TimerCallback? _cb;
        private object? _state;
        public TimeSpan Due;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            (_cb, _state, Due) = (callback, state, dueTime);
            return new NoTimer();
        }
        public void Fire() => _cb?.Invoke(_state);

        private sealed class NoTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
