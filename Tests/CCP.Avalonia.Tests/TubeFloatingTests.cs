using System;
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
        Assert.Same(bob, Assert.IsType<TransformGroup>(image.RenderTransform).Children[1]);
        Assert.NotNull(tube.FindControl<Grid>("AvatarBounceHost")!.RenderTransform);
        Assert.Equal(Math.Sin(127 * 0.05) * 4, bob.Y, 8);
    });

    [Fact]
    public Task ClosingStopsTheBobAndLateTicksCannotMoveTheAvatar() => Run(tube =>
    {
        var bob = Assert.IsType<TranslateTransform>(Assert.IsType<TransformGroup>(
            tube.FindControl<Image>("ImgAvatar")!.RenderTransform).Children[1]);
        var tick = Assert.IsType<Action>(tube.FloatingFrame);
        tick();
        Assert.NotEqual(0, bob.Y);
        tube.Close();
        Assert.Null(tube.FloatingFrame);
        Assert.Equal(0, bob.Y);
        tick(); // a dispatcher callback captured before close must stay inert
        Assert.Equal(0, bob.Y);
    });

    [Fact]
    public Task OpeningPreservesConfiguredScaleWhileBobbing() => Run(tube =>
    {
        var image = tube.FindControl<Image>("ImgAvatar")!;
        var tick = Assert.IsType<Action>(tube.FloatingFrame);
        tick();
        var at = Assert.IsAssignableFrom<global::Avalonia.Media.ITransform>(image.RenderTransform)
            .Value.Transform(new Point(10, 20));
        Assert.Equal(16.5, at.X, 8);
        Assert.Equal(33 + Math.Sin(0.05) * 4, at.Y, 8);
    }, avatarScale: 1.65);

    [Fact]
    public Task LayoutRefreshAndAttachmentPreserveTheRunningBob() => Run(tube =>
    {
        var image = tube.FindControl<Image>("ImgAvatar")!;
        var tick = Assert.IsType<Action>(tube.FloatingFrame);
        tick();
        CoreSettings.Current.TubeLayoutOverridesByMod![CoreMods.ActiveModId].AvatarScale = 1.35;
        tube.RefreshTubeLayout();
        tube.Detach();
        tube.Attach();
        var at = Assert.IsAssignableFrom<global::Avalonia.Media.ITransform>(image.RenderTransform)
            .Value.Transform(new Point(10, 20));
        Assert.Equal(13.5, at.X, 8);
        Assert.Equal(27 + Math.Sin(0.05) * 4, at.Y, 8);
        Assert.Same(tick, tube.FloatingFrame);
        tick();
        at = image.RenderTransform!.Value.Transform(new Point(10, 20));
        Assert.Equal(27 + Math.Sin(0.1) * 4, at.Y, 8);
    }, avatarScale: 1.65);

}
