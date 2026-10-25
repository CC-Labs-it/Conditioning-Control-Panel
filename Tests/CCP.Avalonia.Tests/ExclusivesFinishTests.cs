using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
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
        var old = (settings.MotionLevel, settings.PerformanceMode);
        settings.MotionLevel = MotionLevel.Off;
        settings.PerformanceMode = false;
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
            Assert.Equal(AmbientFxCanvas.Env.AllowGlow(AmbientFxCanvas.Env.CurrentTier), glow.IsVisible);
            if (glow.IsVisible)
            {
                Assert.Equal(1, glow.BoxShadow.Count);
                Assert.Equal(0x8C, glow.BoxShadow[0].Color.A); // WPF opacity 0.55
            }
            card.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, card,
                new Pointer(1, PointerType.Mouse, true), card, default, 0, default, default));
            Assert.Equal(1, scale.GetBaseValue(ScaleTransform.ScaleXProperty));
            Assert.False(glow.IsVisible);
            settings.PerformanceMode = true;
            card.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, card,
                new Pointer(1, PointerType.Mouse, true), card, default, 0, default, default));
            Assert.False(glow.IsVisible);
            Assert.Equal(1.02, scale.ScaleX);
        }
        finally
        {
            host.Close();
            (settings.MotionLevel, settings.PerformanceMode) = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task HeaderUsesShippedVaultAndTierArt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureAvalonia();
        var view = new ExclusivesTabView();
        Assert.NotNull(view.FindControl<Image>("VaultHeroImage")?.Source);
        Assert.NotNull(view.FindControl<Image>("TierPlate1Art")?.Source);
        Assert.NotNull(view.FindControl<Image>("TierPlate2Art")?.Source);
        view.RefreshVault();
        Assert.NotNull(view.FindControl<Image>("VaultHeroImage")?.Source);
        return Task.CompletedTask;
    });

    private static void EnsureAvalonia()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }
}
