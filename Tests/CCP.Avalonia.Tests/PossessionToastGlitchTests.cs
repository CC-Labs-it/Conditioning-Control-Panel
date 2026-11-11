using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionToastGlitchTests
{
    private sealed class Host : IPossessionHost
    {
        public IReadOnlyList<PossessionTarget> Targets => Array.Empty<PossessionTarget>();
        public bool IsUsable => true;
    }
    private sealed class Attribution : IPossessionAttribution
    {
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) => Task.CompletedTask;
        public IDisposable Possess(object target) => new CancellationTokenSource();
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
    }
    private static PossessionContext Context(PossessionIntensity intensity, bool photosafe) => new()
    {
        Host = new Host(), Attribution = new Attribution(), Rung = PossessionRung.ItKnows,
        Intensity = intensity, Photosafe = photosafe, Rng = new Random(1), ElapsedFraction = 1,
        Remaining = TimeSpan.Zero, Name = (_, _) => { }
    };

    [Fact]
    public void CatalogIncludesPortraitWithFullDokiFlickerMetadata()
    {
        var effect = Assert.Single(PossessionAvaloniaHead.CreateEffects().Where(e => e.Id == "glitchportrait"));
        Assert.Equal(PossessionRung.ItKnows, effect.MinRung);
        Assert.Equal(PossessionIntensity.FullDoki, effect.MinIntensity);
        Assert.True(effect.UsesFlicker);
    }

    [Fact]
    public void PortraitRequiresFullDokiAndNoPhotosafeAndUndoClearsTheTube() => AvaloniaTestDispatcher.Run(() =>
    {
        var tube = new AvatarTubeWindow(null);
        try
        {
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var effect = new GlitchPortraitEffect(() => tube);
            Assert.False(effect.CanApply(Context(PossessionIntensity.Gentle, false), null));
            Assert.False(effect.CanApply(Context(PossessionIntensity.Eerie, false), null));
            Assert.False(effect.CanApply(Context(PossessionIntensity.FullDoki, true), null));
            var ctx = Context(PossessionIntensity.FullDoki, false);
            Assert.True(effect.CanApply(ctx, null));

            using var bitmap = new WriteableBitmap(new PixelSize(20, 20), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            var portrait = tube.FindControl<Image>("ImgAvatar")!;
            portrait.Source = bitmap;
            portrait.IsVisible = true;
            portrait.Opacity = 1;
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            effect.ApplyAsync(ctx, null, CancellationToken.None).GetAwaiter().GetResult();
            var layer = tube.FindControl<Grid>("PossessionGlitchLayer")!;
            Assert.True(layer.IsVisible);
            Assert.Equal(3, layer.Children.Count);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.False(layer.IsVisible);
            Assert.Empty(layer.Children);
        }
        finally { tube.Close(); }
    });
}
