using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TubeFloatingTests
{
    private static Task Run(Action<AvatarTubeWindow> test, double? avatarScale = null) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var previousOverrides = settings.TubeLayoutOverridesByMod;
        var overrides = settings.TubeLayoutOverridesByMod ??= new();
        var id = CoreMods.ActiveModId;
        var hadOverride = overrides.TryGetValue(id, out var previous);
        if (avatarScale.HasValue) overrides[id] = new ModTubeLayout { AvatarScale = avatarScale.Value };
        var tube = new AvatarTubeWindow(null);
        try { tube.Show(); test(tube); }
        finally
        {
            tube.Close();
            if (hadOverride) overrides[id] = previous!;
            else overrides.Remove(id);
            settings.TubeLayoutOverridesByMod = previousOverrides;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LegacyAvatarBobsWithoutReplacingTheReplyBounce() => Run(tube =>
    {
        var image = tube.FindControl<Image>("ImgAvatar")!;
        var bob = Assert.IsType<TranslateTransform>(Assert.IsType<TransformGroup>(image.RenderTransform).Children[1]);
        // Start at a known show boundary, as WPF ShowTube restarts the phase.
        var previousTick = Assert.IsType<Action<double>>(tube.FloatingFrame);
        tube.Hide();
        tube.Show();
        Assert.Equal(0, bob.Y);
        previousTick(1);
        Assert.Equal(0, bob.Y);
        var loop = Assert.IsType<BeatLoop>(typeof(AvatarTubeWindow).GetField("_floatLoop",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(tube));
        Assert.True(loop.OnFrameBeat);
        var tick = Assert.IsType<Action<double>>(tube.FloatingFrame);
        for (int i = 1; i <= 126; i++)
        {
            tick(i * 0.016);
            Assert.Equal(Math.Sin(i * 0.05) * 4, bob.Y, 8);
        }
        tube.ChatClock = () => TimeSpan.Zero;
        tube.PlayDoubleBounce();
        tick(127 * 0.016);
        Assert.Same(bob, Assert.IsType<TransformGroup>(image.RenderTransform).Children[1]);
        Assert.NotNull(tube.FindControl<Grid>("AvatarBounceHost")!.RenderTransform);
        Assert.Equal(Math.Sin(127 * 0.05) * 4, bob.Y, 8);
    });

    [Fact]
    public Task ClosingStopsTheBobAndLateTicksCannotMoveTheAvatar() => Run(tube =>
    {
        var bob = Assert.IsType<TranslateTransform>(Assert.IsType<TransformGroup>(
            tube.FindControl<Image>("ImgAvatar")!.RenderTransform).Children[1]);
        var tick = Assert.IsType<Action<double>>(tube.FloatingFrame);
        tick(0.016);
        Assert.NotEqual(0, bob.Y);
        tube.Close();
        Assert.Null(tube.FloatingFrame);
        Assert.Equal(0, bob.Y);
        tick(0.016); // a dispatcher callback captured before close must stay inert
        Assert.Equal(0, bob.Y);
    });

    [Fact]
    public Task OpeningPreservesConfiguredScaleWhileBobbing() => Run(tube =>
    {
        var image = tube.FindControl<Image>("ImgAvatar")!;
        var tick = Assert.IsType<Action<double>>(tube.FloatingFrame);
        tick(0.016);
        var at = Assert.IsAssignableFrom<global::Avalonia.Media.ITransform>(image.RenderTransform)
            .Value.Transform(new Point(10, 20));
        Assert.Equal(16.5, at.X, 8);
        Assert.Equal(33 + Math.Sin(0.05) * 4, at.Y, 8);
    }, avatarScale: 1.65);

    [Fact]
    public Task LayoutRefreshAndAttachmentPreserveTheRunningBob() => Run(tube =>
    {
        var image = tube.FindControl<Image>("ImgAvatar")!;
        var tick = Assert.IsType<Action<double>>(tube.FloatingFrame);
        tick(0.016);
        CoreSettings.Current.TubeLayoutOverridesByMod![CoreMods.ActiveModId].AvatarScale = 1.35;
        tube.RefreshTubeLayout();
        tube.Detach();
        tube.Attach();
        var at = Assert.IsAssignableFrom<global::Avalonia.Media.ITransform>(image.RenderTransform)
            .Value.Transform(new Point(10, 20));
        Assert.Equal(13.5, at.X, 8);
        Assert.Equal(27 + Math.Sin(0.05) * 4, at.Y, 8);
        Assert.Same(tick, tube.FloatingFrame);
        tick(0.032);
        at = image.RenderTransform!.Value.Transform(new Point(10, 20));
        Assert.Equal(27 + Math.Sin(0.1) * 4, at.Y, 8);
    }, avatarScale: 1.65);

    [Fact]
    public Task FloatingUsesTheSharedBeatAndParksWithTheWindow() => Run(tube =>
    {
        var previousTime = BeatLoop.Time;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        try
        {
            tube.Hide();
            tube.Show();
            var loop = Assert.Single(BeatLoop.Running.Where(x => x.Owner == tube));
            Assert.True(loop.OnFrameBeat);
            var image = tube.FindControl<Image>("ImgAvatar")!;
            var bob = Assert.IsType<TranslateTransform>(Assert.IsType<TransformGroup>(image.RenderTransform).Children[1]);
            time.Advance(TimeSpan.FromMilliseconds(16));
            BeatLoop.StepAll();
            Assert.Equal(Math.Sin(0.05) * 4, bob.Y, 8);
            tube.WindowState = WindowState.Minimized;
            Assert.False(loop.IsTicking);
            tube.WindowState = WindowState.Normal;
            Assert.True(loop.OnFrameBeat);
            tube.Hide();
            Assert.False(loop.IsTicking);
            tube.Show();
            Assert.False(loop.IsRunning);
            Assert.Equal(0, bob.Y);
            var restarted = Assert.Single(BeatLoop.Running.Where(x => x.Owner == tube));
            Assert.True(restarted.OnFrameBeat);
            tube.Close();
            Assert.False(restarted.IsRunning);
            Assert.DoesNotContain(restarted, BeatLoop.Running);
            time.Advance(TimeSpan.FromSeconds(1));
            BeatLoop.StepAll();
            Assert.Equal(0, bob.Y);
        }
        finally { BeatLoop.Time = previousTime; }
    });

}
