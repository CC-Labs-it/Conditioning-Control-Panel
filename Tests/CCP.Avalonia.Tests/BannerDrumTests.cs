using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BannerDrumTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void EffectiveMotionCapsKeepCrossfadesWithoutRolling(bool gentle, bool duringRoll)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var oldProbe = UiMotionPreference.OsProbe;
            var oldClock = BeatLoop.Time;
            BeatLoop.Time = new FrozenClock();
            var settings = CoreSettings.Current;
            var old = (settings.MotionLevel, settings.GentleVisuals);
            MainShellWindow? w = null;
            TextBlock? a = null, b = null;
            try
            {
                settings.MotionLevel = MotionLevel.Full;
                settings.GentleVisuals = false;
                UiMotionPreference.OsProbe = () => true;
                UiMotionPreference.CacheStamp = long.MinValue;
                w = new MainShellWindow();
                a = w.Named<TextBlock>("TxtBannerPrimary")!;
                b = w.Named<TextBlock>("TxtBannerSecondary")!;
                if (duringRoll)
                {
                    w.Crossfade(a, b);
                    Assert.IsType<TransformGroup>(b.RenderTransform);
                }
                settings.GentleVisuals = gentle;
                UiMotionPreference.OsProbe = () => gentle; // otherwise the OS cap alone
                UiMotionPreference.CacheStamp = long.MinValue;
                Assert.Equal(MotionLevel.Reduced, AmbientFxCanvas.Env.Level);
                if (duringRoll) BeatLoop.StepAll(); // no settings event: exercises the frame gate
                else w.Crossfade(a, b);
                Assert.Null(a.RenderTransform);
                Assert.Null(b.RenderTransform);
                Assert.DoesNotContain(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, w));
                foreach (var beat in new[] { a, b })
                {
                    var fade = Assert.IsType<global::Avalonia.Animation.DoubleTransition>(Assert.Single(beat.Transitions!));
                    Assert.Equal(Visual.OpacityProperty, fade.Property);
                    Assert.Equal(System.TimeSpan.FromMilliseconds(500), fade.Duration);
                }
                Assert.Equal(0, a.Opacity);
                Assert.Equal(1, b.Opacity);
                Assert.False(a.IsHitTestVisible);
                Assert.True(b.IsHitTestVisible);
                Assert.Equal(MotionLevel.Full, settings.MotionLevel);
            }
            finally
            {
                settings.MotionLevel = MotionLevel.Off;
                if (a is not null && b is not null) w!.RollBannerDrum(a, b);
                w?.Close();
                (settings.MotionLevel, settings.GentleVisuals) = old;
                BeatLoop.Time = oldClock;
                UiMotionPreference.OsProbe = oldProbe;
                UiMotionPreference.CacheStamp = long.MinValue;
            }
        });
    }

    private sealed class FrozenClock : System.TimeProvider
    {
        public override long GetTimestamp() => 0;
    }

    [Fact]
    public void ShellPaintAndRollUseTheLiveBannerControls()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var w = new MainShellWindow();
            try
            {
                var host = w.Named<Border>("HeaderBannerHost")!;
                Assert.Equal(31, host.Height);
                Assert.True(w.Named<Grid>("BannerDrum")!.ClipToBounds);
                foreach (var name in new[] { "BannerDrumShade", "BannerDrumLip", "BannerDrumFoot", "BannerGlass" })
                {
                    var layer = w.Named<Border>(name)!;
                    Assert.False(layer.IsHitTestVisible);
                    Assert.Null(layer.Effect);
                }
                var hue = Color.FromRgb(40, 120, 200);
                w.PaintBannerDepth(hue);
                var shade = Assert.IsType<LinearGradientBrush>(w.Named<Border>("BannerDrumShade")!.Background);
                Assert.Equal(4, shade.GradientStops.Count);
                Assert.Equal(DepthLaw.ShadowColor(hue.ToUInt32(), DepthLaw.WellTopAlpha), shade.GradientStops[0].Color.ToUInt32());
                Assert.Equal(0, shade.GradientStops[1].Color.A);
                Assert.Equal(0, shade.GradientStops[2].Color.A);
                Assert.Equal(DepthLaw.ShadowColor(hue.ToUInt32(), DepthLaw.WellLeftAlpha), shade.GradientStops[3].Color.ToUInt32());
                var lip = Assert.IsType<LinearGradientBrush>(w.Named<Border>("BannerDrumLip")!.Background);
                Assert.Equal(RelativeUnit.Absolute, lip.EndPoint.Unit);
                Assert.Equal(10, lip.EndPoint.Point.X);
                var a = w.Named<TextBlock>("TxtBannerPrimary")!;
                var b = w.Named<TextBlock>("TxtBannerSecondary")!;
                CoreSettings.Current.MotionLevel = MotionLevel.Full;
                w.Crossfade(a, b);
                var group = Assert.IsType<TransformGroup>(b.RenderTransform);
                Assert.Equal(13, Assert.IsType<TranslateTransform>(group.Children[1]).Y);
                CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
                w.Crossfade(a, b);
                Assert.Null(a.RenderTransform);
                Assert.Null(b.RenderTransform);
                Assert.NotEmpty(b.Transitions!);
                Assert.False(a.IsHitTestVisible);
                Assert.True(b.IsHitTestVisible);
                CoreSettings.Current.MotionLevel = MotionLevel.Off;
                w.Crossfade(b, a);
                Assert.Null(a.Transitions);
                Assert.Null(b.Transitions);
                Assert.Equal(1, a.Opacity);
                Assert.Equal(0, b.Opacity);
            }
            finally { w.Close(); }
        });
    }
}
