using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Controls.Leash;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The holder's "?" slips (ink slips, 2026-10-17): every holder command and every sheet
/// row has one, each stamp is the lowest level the grammar allows, every key is in all ten
/// languages, and the holder card carries a "?" per button and per row without pressing them.</summary>
[Collection(CompanionWpfRenderCollection.Name)]
public sealed class LeashHelpSlipTests
{
    [Fact]
    public void Every_holder_button_and_let_go_has_a_slip()
    {
        foreach (var id in LeashHelpCatalog.HolderButtons.Append(LeashHelpCatalog.LetGoId))
            Assert.NotNull(LeashHelpCatalog.Get(id));
        Assert.Equal(LeashHelpCatalog.All.Count, LeashHelpCatalog.All.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void Every_sheet_row_has_a_slip()
    {
        foreach (var k in Enum.GetValues<PunishKind>()) Assert.Equal(LeashHelpCatalog.Id(k), LeashHelpCatalog.ForPunish(k).Id);
        foreach (var k in Enum.GetValues<AssignKind>()) Assert.Equal("assign", LeashHelpCatalog.ForAssign(k).Id);
        var rewards = Enum.GetValues<RewardKind>().Select(k => LeashHelpCatalog.ForReward(k).Id).ToList();
        Assert.Equal(rewards.Count, rewards.Distinct().Count());
        Assert.Equal(new[] { "credit", "pardon", "praise", "sticker" }, rewards.OrderBy(x => x));
    }

    [Fact]
    public void Stamps_match_the_grammar()
    {
        foreach (var k in Enum.GetValues<PunishKind>())
            Assert.Equal(LeashGrammar.LowestIntensity(k), LeashHelpCatalog.ForPunish(k).Stamp);
        Assert.Equal(LeashIntensity.Soft, LeashHelpCatalog.Get("punish")!.Stamp);
        Assert.Equal(Enum.GetValues<PunishKind>().Select(LeashGrammar.LowestIntensity).Min(), LeashHelpCatalog.Get("punish")!.Stamp);
        // Assign, tug, let go and every reward work at any level.
        foreach (var id in new[] { "assign", "tug", "letgo", "reward", "sticker", "praise", "pardon", "credit" })
            Assert.Null(LeashHelpCatalog.Get(id)!.Stamp);
        Assert.Equal(LeashIntensity.Strict, LeashHelpCatalog.ForPunish(PunishKind.Chaster).Stamp);
    }

    [Fact]
    public void Stamp_text_reads_like_the_mockup()
    {
        string Loc(string k) => k switch
        {
            "leash_slip_any" => "ANY LEVEL",
            "leash_level_soft" => "Soft",
            "leash_level_standard" => "Standard",
            "leash_level_strict" => "Strict",
            _ => k,
        };
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal("ANY LEVEL", LeashHelpCatalog.StampText(null, Loc, inv));
        Assert.Equal("SOFT+", LeashHelpCatalog.StampText(LeashIntensity.Soft, Loc, inv));
        Assert.Equal("STANDARD+", LeashHelpCatalog.StampText(LeashIntensity.Standard, Loc, inv));
        Assert.Equal("STRICT", LeashHelpCatalog.StampText(LeashIntensity.Strict, Loc, inv));
        Assert.Equal("+30 min", LeashHelpCatalog.YouLabel(LeashHelpCatalog.ForPunish(PunishKind.Chaster), _ => "{0} min"));
        Assert.Equal("-30 min", LeashHelpCatalog.YouLabel(LeashHelpCatalog.ForReward(RewardKind.Credit), _ => "{0} min"));
        Assert.Equal("100", LeashHelpCatalog.YouLabel(LeashHelpCatalog.ForPunish(PunishKind.Bubbles), k => k));
    }

    [Fact]
    public void Every_pictogram_is_drawn()
    {
        foreach (var e in LeashHelpCatalog.All) Assert.Contains(e.Pictogram, LeashHelpCatalog.Pictograms);
        WpfRenderHarness.OnStaThread(() =>
        {
            foreach (var p in LeashHelpCatalog.Pictograms)
                Assert.True(LeashHelpSlip.Pictogram(p).Children.Count >= 2, p);
        });
    }

    [Fact]
    public void Every_slip_key_is_in_all_ten_languages()
    {
        var dir = SourceRoots.LanguagesDirectory;
        var keys = LeashHelpCatalog.AllKeys().Distinct().ToList();
        Assert.True(keys.Count > 40, "found only " + keys.Count);
        foreach (var lang in new[] { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" })
        {
            var json = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(dir, lang + ".json")));
            foreach (var k in keys) Assert.True(json[k] != null, $"{lang}.json is missing {k}");
            foreach (var e in LeashHelpCatalog.All.Where(e => e.CaptionNamesThem))
                Assert.Contains("{0}", (string)json[e.CaptionKey]!);
            foreach (var e in LeashHelpCatalog.All.Where(e => !e.CaptionNamesThem))
                Assert.DoesNotContain("{0}", (string)json[e.CaptionKey]!);
        }
    }

    [Fact]
    public void Slip_accents_follow_the_card_and_stay_readable_on_paper()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            Assert.Equal(System.Windows.Media.Color.FromRgb(0xFF, 0xCF, 0x6B), LeashHelpSlip.AccentFor("assign").Bright);
            Assert.Equal(LeashHelpSlip.MintAccent, LeashHelpSlip.AccentFor("reward"));
            Assert.Equal(LeashHelpSlip.RedAccent, LeashHelpSlip.AccentFor("punish"));
            foreach (var k in Enum.GetValues<PunishKind>()) Assert.Equal(LeashHelpSlip.RedAccent, LeashHelpSlip.AccentFor(LeashHelpCatalog.Id(k)));
            Assert.Equal(LeashHelpSlip.PinkAccent, LeashHelpSlip.AccentFor("praise"));
            Assert.Equal(LeashHelpSlip.LilacAccent, LeashHelpSlip.AccentFor("pardon"));
            Assert.Equal(LeashHelpSlip.MintAccent, LeashHelpSlip.AccentFor("credit"));
            foreach (var e in LeashHelpCatalog.All)
            {
                var a = LeashHelpSlip.AccentFor(e.Id);
                Assert.True(LeashHelpSlip.Contrast(a.Deep, LeashHelpSlip.PaperColor) >= 4.5, e.Id + " stamp ink is too light on paper");
            }
        });
    }

    [Fact]
    public void Slip_copy_says_what_the_player_sees()
    {
        var en = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(SourceRoots.LanguagesDirectory, "en.json")));
        foreach (var e in LeashHelpCatalog.All)
            foreach (var k in new[] { e.CaptionKey, e.LimitsKey })
                Assert.DoesNotContain("gate", ((string)en[k]!).ToLowerInvariant());
    }

    [Fact]
    public void Row_help_badge_costs_no_width()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var icon = new Border { Width = 13, Height = 13 };
            var b = LeashHelpButton.Badged(icon, LeashHelpCatalog.Get("lines")!, () => "Mira");
            b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.Equal(13, b.DesiredSize.Width, 1);
            Assert.Equal("leash-slip-help:lines", b.Tag);
        });
    }

    [Fact]
    public void Slip_names_the_other_player()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var slip = new LeashHelpSlip(LeashHelpCatalog.Get("tug")!, "Vex");
            Assert.Contains("Vex", slip.CaptionText);
            Assert.DoesNotContain("{0}", slip.CaptionText);
        });
    }

    [Fact]
    public void Holder_card_has_a_help_per_button_and_row()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            LeashFx.ForceStill = true;
            try
            {
                var svc = FakeLeashService.Sample();
                var card = new LeashHolderCard(svc.Snapshot.Holding[0], () => svc);
                foreach (var b in LeashHelpCatalog.HolderButtons)
                    Assert.NotNull(Find(card, t => t == "leash-slip-help:" + b));
                // The general explainer stays reachable from the head.
                Assert.NotNull(Find(card, t => t == "leash-help:holder"));

                card.ToggleSheet("punish");
                foreach (var k in LeashUiRules.VisiblePunishments(svc.Snapshot.Holding[0].Intensity, svc.Snapshot.Holding[0].Report?.ChasterLinked == true))
                    Assert.NotNull(Find(card, t => t == "leash-slip-help:" + LeashHelpCatalog.Id(k)));

                card.ToggleSheet("reward");
                foreach (var id in new[] { "sticker", "praise", "pardon" })
                    Assert.NotNull(Find(card, t => t == "leash-slip-help:" + id));

                // Pressing a "?" pins its slip and never opens or sends anything.
                var before = card.OpenSheet;
                var q = (Button)Find(card, t => t == "leash-slip-help:reward")!;
                q.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, q));
                Assert.Equal(before, card.OpenSheet);
                Assert.Null(card.ResultText);
            }
            finally { LeashFx.ForceStill = false; }
        });
    }

    /// <summary>With CCP_LEASH_SHOTS set to a folder, saves every slip and the holder card's
    /// button row there as PNGs (the look the owner approved, checked by eye).</summary>
    [Fact]
    public void Slips_render()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var dir = Environment.GetEnvironmentVariable("CCP_LEASH_SHOTS");
            foreach (var e in LeashHelpCatalog.All)
            {
                var slip = new LeashHelpSlip(e, "Mira");
                var frame = new Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x10, 0x0A, 0x1E)), Padding = new Thickness(14), Child = slip };
                frame.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                frame.Arrange(new Rect(frame.DesiredSize));
                frame.UpdateLayout();
                Assert.True(slip.ActualHeight > 120, e.Id);
                if (string.IsNullOrEmpty(dir)) continue;
                Directory.CreateDirectory(dir);
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(frame.ActualWidth * 2), (int)Math.Ceiling(frame.ActualHeight * 2), 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(frame);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                using var fs = File.Create(Path.Combine(dir, "slip-" + e.Id + ".png"));
                enc.Save(fs);
            }
        });
    }

    private static FrameworkElement? Find(DependencyObject root, Func<string, bool> tag)
    {
        if (root is FrameworkElement fe && fe.Tag is string s && tag(s)) return fe;
        foreach (var c in LogicalTreeHelper.GetChildren(root))
            if (c is DependencyObject d && Find(d, tag) is { } hit) return hit;
        return null;
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "ConditioningControlPanel", "Localization"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
