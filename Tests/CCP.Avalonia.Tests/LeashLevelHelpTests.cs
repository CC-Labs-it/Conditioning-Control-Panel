using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using Xunit;
using AppA = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>What Soft, Standard and Strict allow, said at the level switch (players asked,
/// 2026-10-10): one line per level always on show, the table one click away, on both cards that
/// carry the switch.</summary>
[Collection(RunsAloneCollection.Name)]   // LeashLevelHelp.Open and the ask card's explainer seam are process-wide
public sealed class LeashLevelHelpTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AppA>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static readonly LeashPerson Vex = new("h1", "Vex", null);

    private static MyLeash Me(LeashIntensity level) => new(
        Vex, level, DateTimeOffset.UtcNow.AddDays(-2), 3, null, default,
        Array.Empty<Punishment>(), null, 0, Array.Empty<Sticker>());

    private static Control[] Tagged(Control root, string prefix) =>
        root.GetLogicalDescendants().OfType<Control>().Where(c => (c.Tag as string)?.StartsWith(prefix, StringComparison.Ordinal) == true).ToArray();

    private static string TextOf(Control c) =>
        string.Join(" ", c.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));

    [Fact]
    public async Task SelfCard_SaysWhatEachLevelAllows_UnderTheSwitch_AndUnfoldsTheTable()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            LeashFx.ForceStill = true;
            LeashLevelHelp.Open = false;
            try
            {
                var card = new LeashSelfCard(Me(LeashIntensity.Standard), () => null);
                var lines = Tagged(card, "leash-guide-line:");
                Assert.Equal(new[] { "leash-guide-line:soft", "leash-guide-line:standard:on", "leash-guide-line:strict" }, lines.Select(l => (string)l.Tag!));
                // Each line names what the level adds, from the Core rules.
                foreach (var level in LeashLevelGuide.Levels)
                    foreach (var kind in LeashLevelGuide.Adds(level))
                        Assert.Contains(Loc.Get(LeashLevelGuide.NameKey(kind)), TextOf(lines[(int)level]));
                Assert.DoesNotContain(Loc.Get(LeashLevelGuide.NameKey(PunishKind.Chaster)), TextOf(lines[0]));

                // The table is one click away, never open by itself.
                var help = card.GetLogicalDescendants().OfType<LeashLevelHelp>().Single();
                Assert.False(help.TableShown);
                Assert.Single(Tagged(card, "leash-guide-toggle"));
                help.Toggle();
                Assert.True(help.TableShown);
                Assert.Equal(LeashLevelGuide.Order.Select(k => "leash-guide-row:" + k.ToString().ToLowerInvariant()),
                    Tagged(help, "leash-guide-row:").Select(r => (string)r.Tag!));
                Assert.Equal(3, Tagged(help, "leash-guide-level:").Length);
                Assert.Equal(LeashLevelGuide.AlwaysKeys.Count, Tagged(help, "leash-guide-rule:").Length);
                var limits = Tagged(help, "leash-guide-limits").Single();
                Assert.Contains("3", ((TextBlock)limits).Text);
                Assert.Contains("72", ((TextBlock)limits).Text);

                // Picking a level redraws the card: the table stays open and the lit line moves.
                var strict = new LeashSelfCard(Me(LeashIntensity.Strict), () => null);
                Assert.True(strict.GetLogicalDescendants().OfType<LeashLevelHelp>().Single().TableShown);
                Assert.Single(Tagged(strict, "leash-guide-line:strict:on"));
            }
            finally { LeashLevelHelp.Open = false; }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AskCard_SaysThePickedLevelInAFewWords_AndOffersTheSameTable()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            LeashFx.ForceStill = true;
            LeashLevelHelp.Open = false;
            var oldFactory = LeashAskCard.ExplainerFactory;
            LeashAskCard.ExplainerFactory = _ => new Border();
            try
            {
                var ask = new LeashAskCard(new LeashOffer(Vex, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7)), () => null);
                var tagline = (TextBlock)Tagged(ask, "leash-guide-tagline").Single();
                Assert.Equal(Loc.Get(LeashLevelGuide.TaglineKey(LeashIntensity.Standard)), tagline.Text);
                Assert.False(ask.GetLogicalDescendants().OfType<LeashLevelHelp>().Single().TableShown);

                ask.GetLogicalDescendants().OfType<LeashLevelHelp>().Single().Toggle();
                ask.SetLevel(LeashIntensity.Strict);
                var help = ask.GetLogicalDescendants().OfType<LeashLevelHelp>().Single();
                Assert.True(help.TableShown);
                Assert.Equal(Loc.Get(LeashLevelGuide.TaglineKey(LeashIntensity.Strict)), ((TextBlock)Tagged(ask, "leash-guide-tagline").Single()).Text);
                Assert.Equal(6, Tagged(help, "leash-guide-row:").Length);
            }
            finally
            {
                LeashAskCard.ExplainerFactory = oldFactory;
                LeashLevelHelp.Open = false;
            }
            return Task.CompletedTask;
        });
    }
}
