using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiIdleBeatsTests
{
    [Fact]
    public Task RestartTicksAndStopManageBothClocks() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var desk = new EmiDeskWindow();
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        try
        {
            desk.Show();
            desk.RestartIdleBeats();
            var idle = (DispatcherTimer)typeof(EmiDeskWindow).GetField("_idleTimer", flags)!.GetValue(desk)!;
            var sway = (DispatcherTimer)typeof(EmiDeskWindow).GetField("_swayTimer", flags)!.GetValue(desk)!;
            Assert.True(idle.IsEnabled);
            Assert.True(sway.IsEnabled);
            Assert.InRange(idle.Interval.TotalMilliseconds, EmiAlive.BlinkEveryMs - EmiAlive.BlinkJitterMs, EmiAlive.BlinkEveryMs + EmiAlive.BlinkJitterMs);
            typeof(EmiDeskWindow).GetMethod("OnIdleTick", flags)!.Invoke(desk, new object?[] { null, EventArgs.Empty });
            Assert.Equal(EmiAlive.BlinkFace, desk.Face);
            typeof(EmiDeskWindow).GetMethod("OnSwayTick", flags)!.Invoke(desk, new object?[] { null, EventArgs.Empty });
            Assert.Equal(1, (int)typeof(EmiDeskWindow).GetField("_swayAt", flags)!.GetValue(desk)!);
            desk.StopIdleBeats();
            Assert.False(idle.IsEnabled);
            Assert.False(sway.IsEnabled);
            Assert.Null(typeof(EmiDeskWindow).GetField("_idleTimer", flags)!.GetValue(desk));
            Assert.Null(typeof(EmiDeskWindow).GetField("_swayTimer", flags)!.GetValue(desk));
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });
}
