using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media.Imaging;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ExclusivesFinishTests
{
    [Fact]
    public Task CardsLiftAndBloomThenResetEvenWhenUnavailable() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode);
        settings.MotionLevel = MotionLevel.Off;
        settings.PerformanceMode = false;
        settings.AutoPerformanceMode = false; // Full tier, so the bloom assert always runs
        var view = new ExclusivesTabView();
        var host = new Window { Width = 1400, Height = 900, Content = view };
        try
        {
            host.Show();
            Dispatcher.UIThread.RunJobs();
            var card = view.GetVisualDescendants().OfType<Border>().First(b =>
                b.Name == "ExCard" && b.DataContext is ExclusiveCardRow r && r.Feature.Key == "backroom");
            card.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, card,
                new Pointer(1, PointerType.Mouse, true), card, default, 0, default, default));
            var scale = Assert.IsType<ScaleTransform>(card.RenderTransform);
            Assert.Equal(1.02, scale.GetBaseValue(ScaleTransform.ScaleXProperty));
            Assert.Null(scale.Transitions); // Motion Off snaps the same lift, WPF MotionFx.HoverLift
            Assert.Null(card.Effect);
            var glow = card.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "HoverGlow");
            Assert.True(glow.IsVisible);
            Assert.Equal(1, glow.BoxShadow.Count);
            Assert.Equal(0x8C, glow.BoxShadow[0].Color.A); // WPF opacity 0.55
            card.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, card,
                new Pointer(1, PointerType.Mouse, true), card, default, 0, default, default));
            Assert.Equal(1, scale.GetBaseValue(ScaleTransform.ScaleXProperty));
            Assert.False(glow.IsVisible);
            settings.PerformanceMode = true;
            card.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, card,
                new Pointer(1, PointerType.Mouse, true), card, default, 0, default, default));
            Assert.False(glow.IsVisible);
            Assert.Equal(1.02, scale.ScaleX);
            // Motion on: WPF MotionFx.HoverLift eases 150 ms, quadratic ease-out, on both axes.
            settings.MotionLevel = MotionLevel.Full;
            card.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, card,
                new Pointer(1, PointerType.Mouse, true), card, default, 0, default, default));
            var lifts = Assert.IsAssignableFrom<Transitions>(scale.Transitions).Cast<DoubleTransition>().ToList();
            Assert.Equal(new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty }, lifts.Select(t => t.Property));
            Assert.All(lifts, t =>
            {
                Assert.Equal(TimeSpan.FromMilliseconds(150), t.Duration);
                Assert.IsType<QuadraticEaseOut>(t.Easing);
            });
        }
        finally
        {
            host.Close();
            (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode) = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task HeaderUsesShippedVaultAndTierArt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var old = (CoreMods.ActiveModIdProvider, CoreModArt.OverridePathProvider);
        try
        {
            CoreModArt.OverridePathProvider = null;
            CoreMods.ActiveModIdProvider = null; // CCP default
            var view = new ExclusivesTabView();
            var hero = Assert.IsType<ImageBrush>(view.FindControl<Border>("VaultHero")!.Background);
            Assert.Equal(AlignmentX.Right, hero.AlignmentX); // WPF crops from the right edge
            Assert.NotNull(view.FindControl<Image>("TierPlate1Art")?.Source);
            Assert.NotNull(view.FindControl<Image>("TierPlate2Art")?.Source);
            var plain = Pixels(hero.Source);
            Assert.Equal(Pixels(ModArt.TryLoadBuiltIn("features/vault.png", 480)), plain);
            // WPF ModTileVariant: a built-in mod paints its themed face.
            CoreMods.ActiveModIdProvider = () => BuiltInMods.BambiSleepId;
            view.RefreshVault();
            var bambi = Pixels(hero.Source);
            Assert.Equal(Pixels(ModArt.TryLoadBuiltIn("features/vault_bambi.png", 480)), bambi);
            Assert.NotEqual(plain, bambi);
        }
        finally
        {
            (CoreMods.ActiveModIdProvider, CoreModArt.OverridePathProvider) = old;
        }
        return Task.CompletedTask;
    });

    private static byte[] Pixels(object? source)
    {
        var bitmap = Assert.IsAssignableFrom<Bitmap>(source);
        var stride = bitmap.PixelSize.Width * 4;
        var bytes = new byte[stride * bitmap.PixelSize.Height];
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pin.AddrOfPinnedObject(), bytes.Length, stride); }
        finally { pin.Free(); }
        return bytes;
    }

    private static void EnsureAvalonia()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }
}
