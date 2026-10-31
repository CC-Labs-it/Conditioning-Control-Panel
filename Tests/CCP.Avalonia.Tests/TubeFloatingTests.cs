using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TubeFloatingTests
{
    private static Task Run(Action<AvatarTubeWindow> test) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var tube = new AvatarTubeWindow(null);
        try { tube.Show(); test(tube); }
        finally { tube.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LegacyAvatarBobsWithoutReplacingTheReplyBounce() => Run(tube =>
    {
        var image = tube.FindControl<Image>("ImgAvatar")!;
        var bob = Assert.IsType<global::Avalonia.Media.TranslateTransform>(image.RenderTransform);
        // Start at a known show boundary, as WPF ShowTube restarts the phase.
        var previousTick = Assert.IsType<Action>(tube.FloatingFrame);
        tube.Hide();
        tube.Show();
        Assert.Equal(0, bob.Y);
        previousTick();
        Assert.Equal(0, bob.Y);
        var timer = Assert.IsType<DispatcherTimer>(typeof(AvatarTubeWindow).GetField("_floatTimer",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(tube));
        Assert.Equal(TimeSpan.FromMilliseconds(16), timer.Interval);
        var tick = Assert.IsType<Action>(tube.FloatingFrame);
        for (int i = 1; i <= 126; i++)
        {
            tick();
            Assert.Equal(Math.Sin(i * 0.05) * 4, bob.Y, 8);
        }
        tube.ChatClock = () => TimeSpan.Zero;
        tube.PlayDoubleBounce();
        tick();
        Assert.Same(bob, image.RenderTransform);
        Assert.NotNull(tube.FindControl<Grid>("AvatarBounceHost")!.RenderTransform);
        Assert.Equal(Math.Sin(127 * 0.05) * 4, bob.Y, 8);
    });

    [Fact]
    public Task ClosingStopsTheBobAndLateTicksCannotMoveTheAvatar() => Run(tube =>
    {
        var bob = Assert.IsType<global::Avalonia.Media.TranslateTransform>(
            tube.FindControl<Image>("ImgAvatar")!.RenderTransform);
        var tick = Assert.IsType<Action>(tube.FloatingFrame);
        tick();
        Assert.NotEqual(0, bob.Y);
        tube.Close();
        Assert.Null(tube.FloatingFrame);
        Assert.Equal(0, bob.Y);
        tick(); // a dispatcher callback captured before close must stay inert
        Assert.Equal(0, bob.Y);
    });

}
