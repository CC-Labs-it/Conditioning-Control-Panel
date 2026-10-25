using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Haptics;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class StartupServiceWiringTests
{
    [Fact]
    public void HapticClockForwardsSeeksAndDetachesTheExactSubscriber()
    {
        var prior = FunScriptService.SubscribePlaybackTime;
        Action? detach = null;
        try
        {
            FunScriptService.SubscribePlaybackTime = null;
            App.SeedHapticPlaybackTime();
            long time = -1;
            detach = FunScriptService.SubscribePlaybackTime!(ms => time = ms);
            var video = MandatoryVideoOverlay.Instance;
            video.ReportPlaybackTime(4200);
            Assert.Equal(4200, time);
            video.ReportPlaybackTime(600); // a backwards seek is still an authoritative clock
            Assert.Equal(600, time);
            detach!();
            video.ReportPlaybackTime(9000);
            Assert.Equal(600, time);
        }
        finally { detach?.Invoke(); FunScriptService.SubscribePlaybackTime = prior; }
    }

    [Fact]
    public Task CountdownRetriesWithoutAnAvatarThenDeliversScriptedPriorityCopy() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        AvatarTubeWindow? tube = null;
        using var countdown = App.CreateDescentCountdown();
        try
        {
            Assert.NotNull(countdown.Speaker);
            Assert.False(countdown.Speaker!("not yet"));
            tube = new AvatarTubeWindow();
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(App.SpeakCountdownLine("startup scripted line", tube));
            Assert.Contains("startup scripted line", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            tube.Close();
            Assert.False(countdown.Speaker("gone"));
        }
        finally { tube?.Close(); }
        return Task.CompletedTask;
    });
}
