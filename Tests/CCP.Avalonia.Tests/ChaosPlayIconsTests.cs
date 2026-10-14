using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services.Chaos;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;
using IconVariant = FluentIcons.Common.IconVariant;

namespace CCP.Avalonia.Tests;

/// <summary>Fluent icons lane L8 (Chaos + Play): the state glyphs code swaps are icon Kinds now,
/// and the glyphs whose count or tier carries meaning keep it.</summary>
public sealed class ChaosPlayIconsTests
{
    private static void Boot()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task Hud_hero_swaps_play_and_pause_icons()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Boot();
            var hud = new ChaosHudWindow(new ChaosRunState(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 }));
            try
            {
                var hero = hud.FindControl<IconLabel>("TxtHero")!;
                hud.SetHeroMode(true);    // WPF "▶ FALL IN"
                Assert.Equal((IconKind.Play, "FALL IN"), (hero.Kind, hero.Text));
                hud.SetHeroMode(false);   // WPF "⏸ PAUSE"
                Assert.Equal((IconKind.Pause, "PAUSE"), (hero.Kind, hero.Text));
            }
            finally { hud.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Play_tab_lock_bands_keep_their_tier_icon()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Boot();
            var tab = new PlayTabView();
            var host = new Window { Content = tab };   // the band glyph's Variant comes from its ControlTheme
            host.Show();
            IconGlyph Glyph(string band) => tab.FindControl<Border>(band)!.GetLogicalDescendants().OfType<IconGlyph>().Single();
            // Tier vocabulary: a padlock (gold) on the Breakout band, a beaker (violet) on Gaze.
            Assert.Equal((IconKind.LockClosed, IconVariant.Filled), (Glyph("PlayLockBreakout").Kind, Glyph("PlayLockBreakout").Variant));
            Assert.Equal((IconKind.Beaker, IconVariant.Filled), (Glyph("PlayLockGaze").Kind, Glyph("PlayLockGaze").Variant));
            host.Close();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Rarity_pips_keep_their_count()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Boot();
                int Pips(ChaosRarity r) => ChaosOverlayWindow.IconPrefix(new TextBlock { Text = r.ToString(), FontSize = 11 },
                IconKind.Diamond, ChaosOverlayWindow.RarityDots(r)).Inlines!.OfType<InlineUIContainer>().Count();
            Assert.Equal((1, 2, 3), (Pips(ChaosRarity.Common), Pips(ChaosRarity.Uncommon), Pips(ChaosRarity.Rare)));   // ◆ / ◆◆ / ◆◆◆
            return Task.CompletedTask;
        });
    }
}
