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
        var s = CoreSettings.Current;
        var moves = new List<(IntPtr, IntPtr)>();
        var drain = new IntPtr(1); var picture = new IntPtr(2);
        var band = new List<BrainDrainKeepClear.BandEntry> { new(drain, true, true), new(picture, true, false) };
        BrainDrainOverlay.ShowOverride = (_, _) => true;
        BrainDrainOverlay.Schedule = (_, _) => { };
        BrainDrainOverlay.Band = () => band;
        BrainDrainOverlay.Restack = (h, after) => moves.Add((h, after));
        try
        {
            s.BrainDrainBlurStrength = 50; s.BrainDrainKeepPicturesClear = true;
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
        finally { BrainDrainOverlay.Stop(); }
        return Task.CompletedTask;
    });
}
