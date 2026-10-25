using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Hub purchases announce themselves on the non-clickable unlock-card layer the way WPF
/// ChaosHubWindow.ShowUnlockCard does: top-center, queued, slide/fade timeline, 4.5 s dwell.</summary>
public sealed class ChaosUnlockCardLayerTests
{
    [Fact]
    public async Task Hub_purchases_queue_unlock_cards_on_the_layer()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var old = CoreSettings.Current.ChaosActiveSlot;
            ChaosHubWindow? hub = null;
            try
            {
                for (int s = 1; s <= 3; s++) ChaosMetaStore.Delete(s);
                ChaosMetaStore.Save(new ChaosMetaState { RunsCompleted = 10, Sparks = 321, Gold = 55 }, 1);
                ChaosMetaStore.SetActiveSlot(1);

                hub = new ChaosHubWindow();
                double now = 0;
                hub.UnlockCards.Now = () => now;   // stepped clock, no real frames (P08)
                var layer = hub.FindControl<Grid>("UnlockCardLayer")!;
                Assert.False(layer.IsHitTestVisible);   // shopping continues underneath

                Click(hub, "blank_eyes", "Unlock|Sparkle|120");
                Click(hub, BenchIds.ToyPocket1, "buy|CoinMultiple|50");

                var first = Assert.Single(layer.Children.OfType<Border>());
                var text = Texts(first);
                Assert.Equal(ChaosUnlockCardRules.ForBoonUnlock("blank_eyes")!.Ribbon, text[0]);
                Assert.Contains(ChaosLifetimeBoons.ById("blank_eyes")!.Name, text);
                Assert.Equal(1, hub.UnlockCards.Queued);   // the pocket card waits its turn
                Assert.Equal(0, first.Opacity);
                Assert.Equal(-14, ((global::Avalonia.Media.TranslateTransform)first.RenderTransform!).Y);

                now = ChaosUnlockCardLayer.InMs + ChaosUnlockCardLayer.SlideMs;
                hub.UnlockCards.Advance();
                Assert.Equal(1, first.Opacity);
                Assert.Equal(0, ((global::Avalonia.Media.TranslateTransform)first.RenderTransform!).Y, 6);

                now = ChaosUnlockCardLayer.HoldMs + ChaosUnlockCardLayer.OutMs / 2;   // fading
                hub.UnlockCards.Advance();
                Assert.InRange(first.Opacity, 0.4, 0.6);

                now = ChaosUnlockCardLayer.HoldMs + ChaosUnlockCardLayer.OutMs;        // gone -> next
                hub.UnlockCards.Advance();
                var second = Assert.Single(layer.Children.OfType<Border>());
                Assert.NotSame(first, second);
                Assert.Equal(ChaosUnlockCardRules.PocketRibbon, Texts(second)[0]);
                Assert.Equal("first toy pocket", Texts(second)[2]);   // ribbon, glyph tile, title

                now += ChaosUnlockCardLayer.HoldMs + ChaosUnlockCardLayer.OutMs;
                hub.UnlockCards.Advance();
                Assert.Empty(layer.Children);
                Assert.Null(hub.UnlockCards.Current);
            }
            finally
            {
                hub?.Close();
                for (int s = 1; s <= 3; s++) ChaosMetaStore.Delete(s);
                ChaosMeta.Init();
                CoreSettings.Current.ChaosActiveSlot = old;
            }
            return Task.CompletedTask;
        });
    }

    private static void Click(Control root, string tag, string label)
    {
        var button = root.GetLogicalDescendants().OfType<Button>()
            .First(b => b.Tag as string == tag && Label(b.Content) == label);
        button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }

    private static string Label(object? content) => content is StackPanel p
        ? string.Join("|", p.Children.Select(c => c is IconGlyph g ? g.Kind.ToString() : (c as TextBlock)?.Text))
        : (content as TextBlock)?.Text ?? "";

    private static List<string> Texts(Control root) =>
        root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
}
