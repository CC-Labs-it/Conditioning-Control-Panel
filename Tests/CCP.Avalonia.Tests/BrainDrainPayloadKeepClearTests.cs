using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
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
        var oldShow = BrainDrainOverlay.ShowOverride;
        var oldSchedule = BrainDrainOverlay.Schedule;
        var oldBand = BrainDrainOverlay.Band;
        var oldRestack = BrainDrainOverlay.Restack;
        var moves = new List<(IntPtr, IntPtr)>();
        var drain = new IntPtr(1); var picture = new IntPtr(2);
        var band = new List<BrainDrainKeepClear.BandEntry> { new(drain, true, true), new(picture, true, false) };
        try
        {
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
                BrainDrainOverlay.ShowOverride = oldShow;
                BrainDrainOverlay.Schedule = oldSchedule;
                BrainDrainOverlay.Band = oldBand;
                BrainDrainOverlay.Restack = oldRestack;
                s.BrainDrainBlurStrength = oldStrength;
                s.BrainDrainKeepPicturesClear = oldKeepClear;
            }
        }
    }

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
