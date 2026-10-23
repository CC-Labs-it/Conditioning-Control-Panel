using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The words next to the leash level switch (Soft, Standard, Strict) are built from the
/// grammar. These pin the guide to the rules it explains, and to the figures the players were
/// told on 2026-10-10, so a change to either shows up here first. The last two draw the leashed
/// card and the ask card and check the guide sits under the switch, folded until asked for.</summary>
[Collection(CompanionWpfRenderCollection.Name)]
public sealed class LeashLevelGuideTests
{
    private static string LanguageDir()
    {
        return SourceRoots.LanguagesDirectory;
    }

    [Fact]
    public void EachLevel_AddsWhatTheGrammarFirstAllowsThere()
    {
        Assert.Equal(new[] { PunishKind.Lines, PunishKind.Pink }, LeashLevelGuide.Adds(LeashIntensity.Soft));
        Assert.Equal(new[] { PunishKind.Bubbles, PunishKind.Detention, PunishKind.Video }, LeashLevelGuide.Adds(LeashIntensity.Standard));
        Assert.Equal(new[] { PunishKind.Chaster }, LeashLevelGuide.Adds(LeashIntensity.Strict));

        // Every kind the wire knows sits in exactly one level, and the guide lists them as the sheet does.
        var all = Enum.GetValues<PunishKind>();
        Assert.Equal(all.OrderBy(k => k), LeashLevelGuide.Levels.SelectMany(LeashLevelGuide.Adds).OrderBy(k => k));
        Assert.Equal(LeashUiRules.PunishOrder, LeashLevelGuide.Order);
        foreach (var k in all)
            Assert.Contains(k, LeashLevelGuide.Adds(LeashGrammar.LowestIntensity(k)));
    }

    [Fact]
    public void EachLevel_KeepsEverythingFromTheOneBefore()
    {
        foreach (var level in LeashLevelGuide.Levels)
        {
            var allows = LeashLevelGuide.Allows(level);
            foreach (var k in Enum.GetValues<PunishKind>())
            {
                Assert.Equal(LeashGrammar.Allowed(k, level), allows.Contains(k));
                Assert.Equal(LeashUiRules.Allowed(k, level), allows.Contains(k));
            }
        }
        Assert.Equal(2, LeashLevelGuide.Allows(LeashIntensity.Soft).Count);
        Assert.Equal(5, LeashLevelGuide.Allows(LeashIntensity.Standard).Count);
        Assert.Equal(6, LeashLevelGuide.Allows(LeashIntensity.Strict).Count);
    }

    [Fact]
    public void Figures_AreTheGrammarsOwn()
    {
        Assert.Equal(new[] { 3, 5, 10 }, LeashLevelGuide.Figures(PunishKind.Lines));
        Assert.Equal(new[] { 10, 15, 20 }, LeashLevelGuide.Figures(PunishKind.Pink));
        Assert.Equal(new[] { 50, 100, 200 }, LeashLevelGuide.Figures(PunishKind.Bubbles));
        Assert.Equal(new[] { 10, 20, 30 }, LeashLevelGuide.Figures(PunishKind.Detention));
        Assert.Equal(new[] { 15, 30, 60 }, LeashLevelGuide.Figures(PunishKind.Chaster));   // minutes; 900/1800/3600 s on the wire
        Assert.Equal(new[] { LeashVideoCap.Max }, LeashLevelGuide.Figures(PunishKind.Video));

        foreach (var k in new[] { PunishKind.Lines, PunishKind.Pink, PunishKind.Bubbles, PunishKind.Detention })
        {
            Assert.Equal(LeashGrammar.PunishSizes(k), LeashLevelGuide.Figures(k));
            Assert.Equal(LeashUiRules.Sizes(k), LeashLevelGuide.Figures(k));
        }
        Assert.Equal(LeashGrammar.PunishSizes(PunishKind.Chaster).Select(s => s / 60), LeashLevelGuide.Figures(PunishKind.Chaster));

        Assert.Equal(3, LeashLevelGuide.PunishmentsPerDay);
        Assert.Equal(72, LeashLevelGuide.ExpiresAfterHours);
    }

    [Fact]
    public void Summary_NamesWhatTheLevelAdds()
    {
        Func<string, string> key = k => k;
        Assert.Equal("leash_pun_lines, leash_pun_pink", LeashLevelGuide.Summary(LeashIntensity.Soft, key));
        Assert.Equal("+ leash_pun_bubbles, leash_pun_detention, leash_pun_video", LeashLevelGuide.Summary(LeashIntensity.Standard, key));
        Assert.Equal("+ leash_pun_chaster", LeashLevelGuide.Summary(LeashIntensity.Strict, key));
    }

    [Fact]
    public void EveryLanguage_CarriesTheGuide_AndEveryLineTakesItsFigures()
    {
        var keys = LeashLevelGuide.AllKeys().Distinct().ToList();
        var files = Directory.GetFiles(LanguageDir(), "*.json");
        Assert.True(files.Length >= 9, "found only " + files.Length + " language files");
        foreach (var file in files)
        {
            var lang = Path.GetFileNameWithoutExtension(file);
            var json = JObject.Parse(File.ReadAllText(file));
            foreach (var k in keys)
            {
                var text = (string?)json[k];
                Assert.False(string.IsNullOrWhiteSpace(text), lang + " lacks " + k);
                // Copy voice: no exclamation marks, no em or en dashes, in any language.
                Assert.DoesNotContain("!", text);
                Assert.DoesNotContain("—", text);
                Assert.DoesNotContain("–", text);
            }
            string Loc(string k) => (string)json[k]!;
            foreach (var kind in LeashLevelGuide.Order)
            {
                var row = LeashLevelGuide.Row(kind);
                var line = LeashLevelGuide.Detail(row, Loc);
                foreach (var n in row.Figures) Assert.Contains(n.ToString(), line);
                Assert.DoesNotContain("{", line);
            }
            var limits = string.Format(Loc(LeashLevelGuide.LimitsKey), LeashLevelGuide.PunishmentsPerDay, LeashLevelGuide.ExpiresAfterHours);
            Assert.Contains("3", limits);
            Assert.Contains("72", limits);
            Assert.DoesNotContain("{", limits);
        }
    }

    // ---- the guide in the leash UI -----------------------------------------------------

    private static List<FrameworkElement> Tagged(DependencyObject root, string prefix)
    {
        var list = new List<FrameworkElement>();
        void Walk(DependencyObject d)
        {
            if (d is FrameworkElement fe && fe.Tag is string s && s.StartsWith(prefix, StringComparison.Ordinal)) list.Add(fe);
            foreach (var c in LogicalTreeHelper.GetChildren(d)) if (c is DependencyObject x) Walk(x);
        }
        Walk(root);
        return list;
    }

    private static string TextOf(DependencyObject root)
    {
        var parts = new List<string>();
        void Walk(DependencyObject d)
        {
            if (d is TextBlock t) parts.Add(t.Text);
            foreach (var c in LogicalTreeHelper.GetChildren(d)) if (c is DependencyObject x) Walk(x);
        }
        Walk(root);
        return string.Join(" ", parts);
    }

    private static LeashLevelHelp HelpOf(DependencyObject root) => (LeashLevelHelp)Tagged(root, "leash-guide").Single(e => e is LeashLevelHelp);

    private static void Run(Action body) => WpfRenderHarness.OnStaThread(() =>
    {
        LeashFx.ForceStill = true;
        LeashLevelHelp.Open = false;
        try { body(); }
        finally { LeashFx.ForceStill = false; LeashLevelHelp.Open = false; }
    });

    [Fact]
    public void SelfCard_SaysWhatEachLevelAllows_UnderTheSwitch_AndUnfoldsTheTable()
    {
        Run(() =>
        {
            var svc = FakeLeashService.Sample();
            var me = svc.Snapshot.Me! with { Intensity = LeashIntensity.Standard };
            var card = new LeashSelfCard(me, () => svc);
            var lines = Tagged(card, "leash-guide-line:");
            Assert.Equal(new[] { "leash-guide-line:soft", "leash-guide-line:standard:on", "leash-guide-line:strict" }, lines.Select(l => (string)l.Tag));
            // Each line names what the level adds, from the grammar.
            foreach (var level in LeashLevelGuide.Levels)
                foreach (var kind in LeashLevelGuide.Adds(level))
                    Assert.Contains(Loc.Get(LeashLevelGuide.NameKey(kind)), TextOf(lines[(int)level]));
            Assert.DoesNotContain(Loc.Get(LeashLevelGuide.NameKey(PunishKind.Chaster)), TextOf(lines[0]));

            // The table is one click away, never open by itself.
            var help = HelpOf(card);
            Assert.False(help.TableShown);
            var toggle = (Button)Tagged(card, "leash-guide-toggle").Single();
            toggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.True(help.TableShown);
            Assert.Equal(LeashLevelGuide.Order.Select(k => "leash-guide-row:" + k.ToString().ToLowerInvariant()),
                Tagged(help, "leash-guide-row:").Select(r => (string)r.Tag));
            Assert.Equal(3, Tagged(help, "leash-guide-level:").Count);
            Assert.Equal(LeashLevelGuide.AlwaysKeys.Count, Tagged(help, "leash-guide-rule:").Count);
            var limits = (TextBlock)Tagged(help, "leash-guide-limits").Single();
            Assert.Equal(Loc.GetF(LeashLevelGuide.LimitsKey, LeashLevelGuide.PunishmentsPerDay, LeashLevelGuide.ExpiresAfterHours), limits.Text);

            // Picking a level redraws the card: the table stays open and the lit line moves.
            var strict = new LeashSelfCard(me with { Intensity = LeashIntensity.Strict }, () => svc);
            Assert.True(HelpOf(strict).TableShown);
            Assert.Single(Tagged(strict, "leash-guide-line:strict:on"));
        });
    }

    [Fact]
    public void AskCard_SaysThePickedLevelInAFewWords_AndOffersTheSameTable()
    {
        Run(() =>
        {
            var svc = FakeLeashService.Sample();
            var ask = new LeashAskCard(svc.Snapshot.Offers[0], () => svc);
            ask.SetLevel(LeashIntensity.Standard);
            var tagline = (TextBlock)Tagged(ask, "leash-guide-tagline").Single();
            Assert.Equal(Loc.Get(LeashLevelGuide.TaglineKey(LeashIntensity.Standard)), tagline.Text);
            Assert.False(HelpOf(ask).TableShown);

            HelpOf(ask).Toggle();
            ask.SetLevel(LeashIntensity.Strict);
            var help = HelpOf(ask);
            Assert.True(help.TableShown);
            Assert.Equal(Loc.Get(LeashLevelGuide.TaglineKey(LeashIntensity.Strict)), ((TextBlock)Tagged(ask, "leash-guide-tagline").Single()).Text);
            Assert.Equal(6, Tagged(help, "leash-guide-row:").Count);
        });
    }
}
