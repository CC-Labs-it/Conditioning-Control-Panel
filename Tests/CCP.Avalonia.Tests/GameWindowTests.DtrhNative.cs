using System;
using System.Collections;
using System.Reflection;
using System.Linq;
using System.Threading;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics.Core;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Haptics;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void Dtrh_HapticsStopOnCloseDuringRun() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var w = GameWindow.Launch("dtrh")!;
        Assert.True(DtrhHapticDirector.Snapshot.Active);
        w.HandleMessage("{\"type\":\"run-started\"}");
        Assert.True(DtrhHapticDirector.Snapshot.RunActive);
        w.HandleMessage("{\"type\":\"run-ended\"}");
        Assert.False(DtrhHapticDirector.Snapshot.RunActive);
        w.HandleMessage("{\"type\":\"run-started\"}");
        Assert.True(DtrhHapticDirector.Snapshot.RunActive);
        w.Close();
        Assert.False(DtrhHapticDirector.Snapshot.Active);
        Assert.False(DtrhHapticDirector.Snapshot.RunActive);
    });

    // Use the real mixer and its mock provider, including the 400 ms panic mute. Counting its
    // sequences catches a wave queued to resume after the mute, even with no physical toy.
    private static int DtrhSequences(HapticService h)
    {
        var type = typeof(HapticMixer);
        var gate = type.GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(h.Mixer)!;
        lock (gate)
            return ((IDictionary)type.GetField("_sequences", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(h.Mixer)!).Count;
    }

    private static void WithDtrhToy(Action<HapticService> body)
    {
        var old = CoreHaptics.Service;
        var settings = CoreSettings.Current.Haptics;
        settings.Enabled = true;
        using var h = new HapticService(settings);
        settings.V2.Rule(HapticEventKind.DtrhAccent).Enabled = true;
        var mock = h.DeviceManager.Providers.OfType<MockProviderV2>().Single();
        mock.ConnectAsync(CancellationToken.None).GetAwaiter().GetResult();
        CoreHaptics.Service = h;
        try { body(h); }
        finally { DtrhHapticDirector.OnClosed(); CoreHaptics.Service = old; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dtrh_PanicDoesNotQueueFarewellAfterDelayedResponseOrWatchdog(bool responds) => RunDtrh(lab: true, (_, _, _, _) =>
        WithDtrhToy(h =>
        {
            var w = GameWindow.Launch("dtrh")!;
            w.HandleMessage("{\"type\":\"ready\"}");
            w.HandleMessage("{\"type\":\"run-started\"}");
            w.HandleMessage("{\"type\":\"run-progress\",\"score\":10,\"durationSec\":180}");
            Assert.True(DtrhHapticDirector.IsReady);
            Assert.True(DtrhSequences(h) > 0);
            h.PanicStop();
            GameWindow.CloseAllForPanic();
            Assert.False(DtrhHapticDirector.Snapshot.RunActive);
            Thread.Sleep(500); // beyond the mixer's panic mute, still before the 1200 ms watchdog
            if (responds)
            {
                w.HandleMessage("{\"type\":\"run-started\"}"); // a late frame cannot restart the toy
                w.HandleMessage("{\"type\":\"run-ended\",\"score\":10}");
                Assert.Equal(0, DtrhSequences(h));
                w.HandleMessage("{\"type\":\"exit-done\"}");
            }
            else
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                w.Closed += (_, _) => deadline.Cancel();
                Dispatcher.UIThread.MainLoop(deadline.Token);
                Assert.False(GameWindow.IsAnyOpen());
            }
            Assert.Equal(0, DtrhSequences(h));
        }));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Dtrh_PanicOrCloseCancelsPendingFarewell(bool panic) => RunDtrh(lab: true, (_, _, _, _) =>
        WithDtrhToy(h =>
        {
            var w = GameWindow.Launch("dtrh")!;
            w.HandleMessage("{\"type\":\"ready\"}");
            w.HandleMessage("{\"type\":\"run-started\"}");
            w.HandleMessage("{\"type\":\"run-ended\"}");
            Assert.True(SpinWait.SpinUntil(() => DtrhSequences(h) > 0, 500)); // normal completion keeps its farewell
            if (panic) GameWindow.CloseAllForPanic();
            else w.Close();
            Assert.Equal(0, DtrhSequences(h));
            if (panic) w.Close();
        }));
}
