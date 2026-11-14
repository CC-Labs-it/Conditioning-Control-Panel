using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrainDrainPayloadKeepClearTests
{
    [Fact]
    public Task SweepAndSettingAndStop() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Exercise();
        return Task.CompletedTask;
    });

    private static void Exercise(Action? afterSetup = null)
    {
        var s = CoreSettings.Current;
        var oldStrength = s.BrainDrainBlurStrength;
        var oldKeepClear = s.BrainDrainKeepPicturesClear;
        var quiet = typeof(PanicAftermath).GetField("_quietUntilMs", BindingFlags.Static | BindingFlags.NonPublic)!;
        var held = typeof(PanicAftermath).GetField("_engineHeld", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldQuiet = quiet.GetValue(null); var oldHeld = held.GetValue(null);
        var oldRunning = BrainDrainOverlay.UserDrainRunning;
        var oldShow = BrainDrainOverlay.ShowOverride;
        var oldSchedule = BrainDrainOverlay.Schedule;
        var oldBand = BrainDrainOverlay.Band;
        var oldRestack = BrainDrainOverlay.Restack;
        var moves = new List<(IntPtr, IntPtr)>();
        var drain = new IntPtr(1); var picture = new IntPtr(2);
        var band = new List<BrainDrainKeepClear.BandEntry> { new(drain, true, true), new(picture, true, false) };
        try
        {
            BrainDrainOverlay.Stop();
            PanicAftermath.EndQuiet();
            BrainDrainOverlay.UserDrainRunning = () => false;
            BrainDrainOverlay.ShowOverride = (_, _) => true;
            BrainDrainOverlay.Schedule = (_, _) => { };
            BrainDrainOverlay.Band = () => band;
            BrainDrainOverlay.Restack = (h, after) => moves.Add((h, after));
            s.BrainDrainBlurStrength = 50; s.BrainDrainKeepPicturesClear = true;
            afterSetup?.Invoke();
            BrainDrainOverlay.FirePayload(new BrainDrainMeltPayload());
            Assert.Equal((drain, picture), Assert.Single(moves));
            moves.Clear(); band.Reverse();
            BrainDrainOverlay.KeepClearTick(null, EventArgs.Empty);
            Assert.Empty(moves);
            s.BrainDrainKeepPicturesClear = false;
            Assert.Equal((drain, new IntPtr(-1)), Assert.Single(moves));
            moves.Clear(); s.BrainDrainKeepPicturesClear = true; moves.Clear();
            BrainDrainOverlay.Stop(); band.Reverse();
            BrainDrainOverlay.KeepClearTick(null, EventArgs.Empty);
            Assert.Empty(moves);
            BrainDrainOverlay.FirePayload(new BrainDrainMeltPayload()); moves.Clear();
            MainShellWindow.StopEngine();
            BrainDrainOverlay.KeepClearTick(null, EventArgs.Empty);
            Assert.Empty(moves);
        }
        finally
        {
            try { BrainDrainOverlay.Stop(); }
            finally
            {
                quiet.SetValue(null, oldQuiet); held.SetValue(null, oldHeld);
                BrainDrainOverlay.UserDrainRunning = oldRunning;
                BrainDrainOverlay.ShowOverride = oldShow;
                BrainDrainOverlay.Schedule = oldSchedule;
                BrainDrainOverlay.Band = oldBand;
                BrainDrainOverlay.Restack = oldRestack;
                s.BrainDrainBlurStrength = oldStrength;
                s.BrainDrainKeepPicturesClear = oldKeepClear;
            }
        }
    }

    private sealed class Frames : IFrameSource
    {
        private Action<TimeSpan>? _onFrame;
        public void Subscribe(Action<TimeSpan> onFrame) => _onFrame += onFrame;
        public void Unsubscribe(Action<TimeSpan> onFrame) => _onFrame -= onFrame;
        internal bool Subscribed => _onFrame != null;
        internal void Frame(TimeSpan at) => _onFrame?.Invoke(at);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(60)]
    public Task CaptureFramesUseSharedClockAndStop(int fps) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var source = new Frames(); var clock = new FrameClock(source); int ticks = 0;
        clock.Tick += (_, _) => ticks++;
        try
        {
            BrainDrainOverlay.Stop();
            var interval = TimeSpan.FromSeconds(1.0 / fps);
            BrainDrainOverlay.StartFrameClock(clock, interval);
            Assert.True(source.Subscribed);
            Assert.Equal(interval, clock.Interval);
            source.Frame(TimeSpan.FromMilliseconds(100));
            Assert.Equal(1, ticks);
            BrainDrainOverlay.Stop();
            Assert.False(source.Subscribed);
            source.Frame(TimeSpan.FromMilliseconds(200));
            Assert.Equal(1, ticks);
        }
        finally { BrainDrainOverlay.Stop(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SweepWorksAfterPriorPanic() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var quiet = typeof(PanicAftermath).GetField("_quietUntilMs", BindingFlags.Static | BindingFlags.NonPublic)!;
        var held = typeof(PanicAftermath).GetField("_engineHeld", BindingFlags.Static | BindingFlags.NonPublic)!;
        var panics = typeof(PanicAftermath).GetField("_panics", BindingFlags.Static | BindingFlags.NonPublic)!;
        var oldQuiet = quiet.GetValue(null); var oldHeld = held.GetValue(null); var oldPanics = panics.GetValue(null);
        var oldRunning = BrainDrainOverlay.UserDrainRunning;
        Func<bool> priorRunning = () => true;
        try
        {
            BrainDrainOverlay.UserDrainRunning = priorRunning;
            PanicAftermath.ArmQuiet();
            Assert.True(PanicAftermath.TriggersSilenced());
            Exercise();
            Assert.True(PanicAftermath.TriggersSilenced());
            Assert.Same(priorRunning, BrainDrainOverlay.UserDrainRunning);
        }
        finally
        {
            BrainDrainOverlay.UserDrainRunning = oldRunning;
            quiet.SetValue(null, oldQuiet); held.SetValue(null, oldHeld); panics.SetValue(null, oldPanics);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task CleanupRestoresStateWhenBodyThrows() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var s = CoreSettings.Current;
        var strength = s.BrainDrainBlurStrength;
        var keepClear = s.BrainDrainKeepPicturesClear;
        var show = BrainDrainOverlay.ShowOverride;
        var schedule = BrainDrainOverlay.Schedule;
        var band = BrainDrainOverlay.Band;
        var restack = BrainDrainOverlay.Restack;
        try
        {
            s.BrainDrainBlurStrength = 27;
            s.BrainDrainKeepPicturesClear = false;
            Assert.Throws<InvalidOperationException>(() => Exercise(() => throw new InvalidOperationException()));
            Assert.Same(show, BrainDrainOverlay.ShowOverride);
            Assert.Same(schedule, BrainDrainOverlay.Schedule);
            Assert.Same(band, BrainDrainOverlay.Band);
            Assert.Same(restack, BrainDrainOverlay.Restack);
            Assert.Equal(27, s.BrainDrainBlurStrength);
            Assert.False(s.BrainDrainKeepPicturesClear);
        }
        finally
        {
            BrainDrainOverlay.ShowOverride = show;
            BrainDrainOverlay.Schedule = schedule;
            BrainDrainOverlay.Band = band;
            BrainDrainOverlay.Restack = restack;
            s.BrainDrainBlurStrength = strength;
            s.BrainDrainKeepPicturesClear = keepClear;
        }
        return Task.CompletedTask;
    });
}
