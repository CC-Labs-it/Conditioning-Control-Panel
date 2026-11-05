using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

// The FunScript playhead seed (#3288's other half) lands with #3566 in the effects lane.
public sealed class StartupServiceWiringTests
{
    [Fact]
    public Task CountdownRetriesWithoutAnAvatarThenDeliversScriptedPriorityCopy() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var builder = global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp();
        if (Application.Current is null)
            builder.UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        else
            builder.AfterSetupCallback(builder); // register shipped fonts if an earlier test omitted them
        AvatarTubeWindow? tube = null, live = null;
        // The finder stands in for AvatarTubeWindow.Live: a headless test cannot install a desktop
        // lifetime (Application.ApplicationLifetime is locked after init), so Live is always null here.
        using var countdown = App.CreateDescentCountdown(() => live);
        try
        {
            Assert.NotNull(countdown.Speaker);
            Assert.False(countdown.Speaker!("not yet"));
            tube = new AvatarTubeWindow();
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            live = tube;
            Assert.True(countdown.Speaker("startup scripted line"));
            Assert.Contains("startup scripted line", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            Assert.False(tube.FindControl<Border>("AiBadge")!.IsVisible);
            tube.Close();
            live = null;
            Assert.False(countdown.Speaker("gone"));
        }
        finally { tube?.Close(); }
        return Task.CompletedTask;
    });
}
