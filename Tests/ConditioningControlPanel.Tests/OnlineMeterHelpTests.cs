using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The online media meter's "?" slip (owner, 2026-10-19): words only, every help key in all ten
/// languages, never the provider's name, and (owner, 21:30) never a size anywhere in the online
/// media UI: the meter shows a percentage and "used up", nothing in MB or GB.
/// </summary>
public class OnlineMeterHelpTests
{
    [Fact]
    public void The_english_slip()
    {
        var o = JObject.Parse(File.ReadAllText(Path.Combine(OnlineMeterCopyTests.LanguageDir(), "en.json")));
        string Loc(string k) => (string)o[k]!;
        Assert.Equal("How online media works", Loc(OnlineMeterRules.HelpTitleKey));
        Assert.Equal(new[]
        {
            "Each plan gets a daily share. Basic gets more than Free, and Prime gets the most. A free account gets a little extra on its first day. The share is counted per account on this PC, and signed out has a share of its own.",
            "Every picture, GIF and clip that comes fresh from the online feed counts toward the share. It resets at midnight, your local time.",
            "A good amount of the online media you have seen is kept on this PC. When that space is full, what was used longest ago goes first. Playing a kept file again is free and does not count toward the share.",
            "When the share is used up, kept media keeps playing and new online media waits until midnight. Your own files are never affected.",
            "The meter is counted on this PC only. Your usage is never tracked or sent anywhere.",
        }, OnlineMeterRules.HelpParagraphs(Loc));
    }

    [Fact]
    public void Every_help_key_is_in_every_language_with_no_slot_and_never_names_the_provider()
    {
        var files = Directory.GetFiles(OnlineMeterCopyTests.LanguageDir(), "*.json");
        Assert.Equal(10, files.Length);
        var keys = OnlineMeterRules.HelpParagraphKeys.Prepend(OnlineMeterRules.HelpTitleKey).ToArray();
        foreach (var f in files)
        {
            var o = JObject.Parse(File.ReadAllText(f));
            foreach (var key in keys)
            {
                var v = (string?)o[key];
                Assert.False(string.IsNullOrWhiteSpace(v), $"{Path.GetFileName(f)} has no {key}");
                Assert.DoesNotContain("{", v!);
                Assert.DoesNotContain("scrolller", v!, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("—", v!);
                Assert.DoesNotContain("!", v!);
            }
        }
    }

    [Fact]
    public void No_online_media_string_shows_a_size()
    {
        foreach (var f in Directory.GetFiles(OnlineMeterCopyTests.LanguageDir(), "*.json"))
        {
            var o = JObject.Parse(File.ReadAllText(f));
            foreach (var p in o.Properties())
            {
                if (!p.Name.StartsWith("online_", StringComparison.Ordinal) || p.Value.Type != JTokenType.String) continue;
                var v = (string)p.Value!;
                Assert.False(v.Contains("MB") || v.Contains("GB"), $"{Path.GetFileName(f)} {p.Name} shows a size: {v}");
            }
        }
    }
}

/// <summary>The slip itself builds on the paper look with every paragraph filled, and the "?"
/// that opens it carries its tag.</summary>
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
