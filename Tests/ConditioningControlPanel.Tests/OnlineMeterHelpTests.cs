using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The online media meter's "?" slip (owner, 2026-10-19): the shares and the cache size come from
/// the live figures, formatted plainly; every help key is in all ten languages with its slots;
/// the copy never names the provider.
/// </summary>
public class OnlineMeterHelpTests
{
    private const long MB = 1024L * 1024;

    private static readonly (string Key, int Slots)[] HelpKeys =
    {
        (OnlineMeterRules.HelpTitleKey, 0), ("online_help_share", 4), ("online_help_counts", 0),
        ("online_help_cache", 1), ("online_help_spent", 0), ("online_help_local", 0),
    };

    [Theory]
    [InlineData(300 * MB, "300 MB")]
    [InlineData(500 * MB, "500 MB")]
    [InlineData(2048 * MB, "2 GB")]
    [InlineData(6144 * MB, "6 GB")]
    [InlineData(7987 * MB, "7.8 GB")]
    [InlineData(1536 * MB, "1.5 GB")]
    [InlineData(0, "0 MB")]
    public void Sizes_read_as_whole_mb_or_gb_with_one_decimal(long bytes, string expected)
        => Assert.Equal(expected, OnlineMeterRules.FormatSize(bytes));

    [Fact]
    public void The_english_slip_with_the_shipped_figures()
    {
        var o = JObject.Parse(File.ReadAllText(Path.Combine(OnlineMeterCopyTests.LanguageDir(), "en.json")));
        string Loc(string k) => (string)o[k]!;
        var lines = OnlineMeterRules.HelpParagraphs(Loc,
            ScrolllerAllowance.DefaultFreeBytes, ScrolllerAllowance.DefaultFreeFirstDayBytes,
            ScrolllerAllowance.DefaultBasicBytes, ScrolllerAllowance.DefaultPrimeBytes,
            ScrolllerDiskCache.DefaultCapBytes);
        Assert.Equal("How online media works", Loc(OnlineMeterRules.HelpTitleKey));
        Assert.Equal(new[]
        {
            "Each plan gets a daily share: Free 300 MB, or 500 MB on your first day; Basic 2 GB; Prime 6 GB. It is counted per account on this PC, and signed out has a share of its own.",
            "Every picture, GIF and clip that comes fresh from the online feed counts. The share resets at midnight, your local time.",
            "Online media you have seen is usually kept on this PC, up to about 2 GB. When that is full, what you played longest ago goes first. Playing a kept file again is free and does not count.",
            "When the share is used up, kept media keeps playing and new online media waits until midnight. Your own files are never affected.",
            "The meter is counted on this PC only. Your usage is never tracked or sent anywhere.",
        }, lines);
    }

    [Fact]
    public void Every_help_key_is_in_every_language_with_its_slots_and_never_names_the_provider()
    {
        var files = Directory.GetFiles(OnlineMeterCopyTests.LanguageDir(), "*.json");
        Assert.Equal(10, files.Length);
        foreach (var f in files)
        {
            var o = JObject.Parse(File.ReadAllText(f));
            foreach (var (key, slots) in HelpKeys)
            {
                var v = (string?)o[key];
                Assert.False(string.IsNullOrWhiteSpace(v), $"{Path.GetFileName(f)} has no {key}");
                for (int i = 0; i < 4; i++)
                    Assert.True(v!.Contains("{" + i + "}") == (i < slots), $"{Path.GetFileName(f)} {key} slot {i}");
                Assert.DoesNotContain("scrolller", v!, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("—", v!);
                Assert.DoesNotContain("!", v!);
            }
            // Every format fills without a throw.
            var lines = OnlineMeterRules.HelpParagraphs(k => (string)o[k]!, 300 * MB, 500 * MB, 2048 * MB, 6144 * MB, 2048 * MB);
            Assert.Equal(5, lines.Length);
            Assert.All(lines, l => Assert.DoesNotContain("{", l));
        }
    }
}

/// <summary>The slip itself builds on the paper look with no slot left unfilled, and the "?" that
/// opens it carries its tag.</summary>
[Collection(CompanionWpfRenderCollection.Name)]
public sealed class OnlineMeterHelpSlipRenderTests
{
    [Fact]
    public void The_slip_renders_every_paragraph_filled()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            var slip = new ConditioningControlPanel.Controls.OnlineMediaHelpSlip();
            slip.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            var texts = new System.Collections.Generic.List<string>();
            void Walk(System.Windows.DependencyObject d)
            {
                if (d is System.Windows.Controls.TextBlock t) texts.Add(t.Text);
                foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(d))
                    if (c is System.Windows.DependencyObject dc) Walk(dc);
            }
            Walk(slip);
            Assert.Equal(6, texts.Count);   // the title and five paragraphs
            Assert.All(texts, s => Assert.DoesNotContain("{", s));
            Assert.Equal(320, slip.Width);

            var q = ConditioningControlPanel.Controls.Leash.LeashHelpButton.Create(() => new ConditioningControlPanel.Controls.OnlineMediaHelpSlip(), "online-help");
            Assert.Equal("online-help", q.Tag);
        });
    }
}
