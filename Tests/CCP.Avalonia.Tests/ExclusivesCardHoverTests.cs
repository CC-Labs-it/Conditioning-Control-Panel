using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF OnExclusiveCardHover (MainWindow.Exclusives.cs:914): HoverLift 1.02 plus a 0.55 glow bloom
/// (blur min(24, tier cap)) where the performance tier allows glow, both gone on leave.</summary>
public sealed class ExclusivesCardHoverTests
{
    [Fact]
    public Task HoverLiftsAndBloomsTheCardAndLeaveSettlesIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.MotionLevel, s.PerformanceMode);
        try
        {
            // Motion off snaps the lift (MotionFx.HoverLift's !AllowTransitions arm), so the value is read at once.
            (s.MotionLevel, s.PerformanceMode) = (MotionLevel.Off, false);
            var card = new Border();
            ExclusivesTabView.CardHover(card, true);
            var lift = Assert.IsType<ScaleTransform>(card.RenderTransform);
            Assert.Equal(1.02, lift.ScaleX, 3);
            Assert.Equal(1.02, lift.ScaleY, 3);
            Assert.Equal(1, card.BoxShadow.Count);
            var bloom = card.BoxShadow[0];
            var c = ExclusiveCardRow.Accent;
            Assert.Equal(Color.FromArgb(140, c.R, c.G, c.B), bloom.Color);
            Assert.Equal(24, bloom.Blur);

            ExclusivesTabView.CardHover(card, false);
            Assert.Equal(1.0, lift.ScaleX, 3);
            Assert.Equal(0, card.BoxShadow.Count);

            // Performance tier forbids glow: the lift stays, the bloom does not come.
            s.PerformanceMode = true;
            ExclusivesTabView.CardHover(card, true);
            Assert.Equal(1.02, lift.ScaleX, 3);
            Assert.Equal(0, card.BoxShadow.Count);
        }
        finally { (s.MotionLevel, s.PerformanceMode) = old; }
        return Task.CompletedTask;
    });
}
