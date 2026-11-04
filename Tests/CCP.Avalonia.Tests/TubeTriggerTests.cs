using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services.Safety;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class TubeTriggerTests
{
    [Fact]
    public Task TimerFollowsSettingsAndTicksOnlyWhenEnabled() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null) AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (enabled, interval, triggers, muted) = (s.TriggerModeEnabled, s.TriggerIntervalSeconds, s.CustomTriggers, s.AvatarMuted);
        s.TriggerModeEnabled = false; s.AvatarMuted = true; s.CustomTriggers = new() { "test" };
        var tube = new AvatarTubeWindow(null);
        var pulses = 0;
        tube.TriggerHaptic = _ => pulses++;
        try
        {
            PanicAftermath.EndQuiet(); tube.StartTriggerMode();
            Assert.Null(tube.TriggerInterval);
            tube.TriggerTick(); Assert.Equal(0, pulses);
            s.TriggerIntervalSeconds = 23; s.TriggerModeEnabled = true;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal  (23, tube.TriggerInterval);
            tube.TriggerTick(); Assert.Equal(1, pulses);
            s.TriggerIntervalSeconds = 37; Dispatcher.UIThread.RunJobs();
            Assert.Equal(37, tube.TriggerInterval);
            s.TriggerModeEnabled = false; Dispatcher.UIThread.RunJobs();
            Assert.Null(tube.TriggerInterval);
            tube.TriggerTick(); Assert.Equal(1, pulses);
        }
        finally
        {
            tube.StopTriggerMode(); tube.Close(); PanicAftermath.EndQuiet();
            s.TriggerModeEnabled = enabled; s.TriggerIntervalSeconds = interval; s.CustomTriggers = triggers; s.AvatarMuted = muted;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicSuppressesTriggerHapticsEvenWhenMuted() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null) AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var old = settings.AvatarMuted;
        settings.AvatarMuted = true;
        var tube = new AvatarTubeWindow(null);
        var pulses = 0;
        tube.TriggerHaptic = _ => pulses++;
        try
        {
            PanicAftermath.EndQuiet();
            tube.ShowTrigger("test");
            Assert.Equal(1, pulses);
            PanicAftermath.ArmQuiet();
            tube.ShowTrigger("test");
            Assert.Equal(1, pulses);
        }
        finally { PanicAftermath.EndQuiet(); settings.AvatarMuted = old; tube.Close(); }
        return Task.CompletedTask;
    });
}
