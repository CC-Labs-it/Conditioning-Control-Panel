using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.FirstShow;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Owner decision 2026-11-06 (option B), the same as Avalonia FirstShowStageWindow.Pick: a first-show preset's
/// pictures are Scrolller's, which ScrolllerDiskCache refuses without remote-media consent and a non-local source.
/// Picking one then asks the Assets page's consent card first (MainWindow.AskRemoteMediaConsent's words). Yes records
/// the consent and the mixed source (own files plus the feed) and loads the preset; no plays the built-in pictures.
/// </summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class FirstShowRemoteConsentTests
{
    private sealed record Outcome(List<(string Title, string Message)> Asked, List<string> Loaded, int Bundled);

    private static Outcome PickWith(AppSettings s, bool? answer, string preset = "HypnoGoneWild",
        bool licence = true, bool spent = false)
    {
        var outcome = new Outcome(new(), new(), 0);
        int bundled = 0;
        var (oldLicence, oldSpent) = (FirstShowPresets.LicenceOn, FirstShowPresets.ShareSpent);
        FirstShowPresets.LicenceOn = () => licence;
        FirstShowPresets.ShareSpent = () => spent;
        try
        {
            WpfRenderHarness.OnStaThread(() =>
            {
                var emi = new EmiDeskWindow { Left = -10000, Top = -10000, Opacity = 0 };
                var show = new FirstShowDesktopWindow(true, emi, null)
                {
                    CurrentSettings = () => s,
                    ConsentOverride = (title, message) =>
                    {
                        outcome.Asked.Add((title, message));
                        return answer ?? throw new InvalidOperationException("must not ask");
                    },
                    LoadOverride = p => { outcome.Loaded.Add(p); return Task.CompletedTask; },
                    BundledOverride = () => { bundled++; return Task.CompletedTask; },
                };
                try
                {
                    var pick = show.Pick(preset);
                    Assert.True(pick.IsCompleted);   // every seam answers at once
                    pick.GetAwaiter().GetResult();
                }
                finally { show.Close(); emi.Close(); }
            });
        }
        finally { FirstShowPresets.LicenceOn = oldLicence; FirstShowPresets.ShareSpent = oldSpent; }
        return outcome with { Bundled = bundled };
    }

    private static AppSettings Fresh() => new() { MediaSource = "local", RemoteMediaConsented = false, FypOnlineConsented = false };

    [Fact]
    public void Yes_records_consent_and_the_mixed_source_then_loads_the_preset()
    {
        var s = Fresh();
        var o = PickWith(s, answer: true);
        Assert.Equal(new[] { MainWindow.RemoteMediaConsentText() }, o.Asked);
        Assert.True(s.RemoteMediaConsented);
        Assert.False(s.FypOnlineConsented);
        Assert.Equal("mixed", s.MediaSource);
        Assert.Equal(new[] { "HypnoGoneWild" }, o.Loaded);
        Assert.Equal(0, o.Bundled);
    }

    [Fact]
    public void No_plays_the_built_in_pictures_and_changes_nothing()
    {
        var s = Fresh();
        var o = PickWith(s, answer: false);
        Assert.Single(o.Asked);
        Assert.False(s.HasRemoteMediaConsent);
        Assert.Equal("local", s.MediaSource);
        Assert.Empty(o.Loaded);
        Assert.Equal(1, o.Bundled);
    }

    [Theory]
    [InlineData("online")]
    [InlineData("mixed")]
    public void Already_consented_loads_the_preset_without_asking(string source)
    {
        var s = new AppSettings { MediaSource = source, RemoteMediaConsented = true };
        var o = PickWith(s, answer: null, preset: "cosplay");
        Assert.Empty(o.Asked);
        Assert.Equal(source, s.MediaSource);
        Assert.Equal(new[] { "cosplay" }, o.Loaded);
    }

    /// <summary>The For You card's yes counts as consent (HasRemoteMediaConsent) but the source is still local: the
    /// card is asked, because only a yes there moves the source.</summary>
    [Fact]
    public void Consent_from_the_feed_card_with_a_local_source_still_asks_before_moving_the_source()
    {
        var s = new AppSettings { MediaSource = "local", FypOnlineConsented = true };
        var o = PickWith(s, answer: true);
        Assert.Single(o.Asked);
        Assert.Equal("mixed", s.MediaSource);
        Assert.Equal(new[] { "HypnoGoneWild" }, o.Loaded);
    }

    /// <summary>The card's two keys are in every language file, and en.json is the code's fallback word for word, so
    /// a build that falls back and one that reads en.json ask exactly the same question.</summary>
    [Fact]
    public void The_consent_card_is_translated_in_every_language_and_English_matches_the_fallback()
    {
        foreach (var lang in new[] { "en", "de", "es", "fr", "it", "ja", "ko", "pt-BR", "ru", "zh-CN" })
        {
            var file = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(
                System.IO.Path.Combine(SourceRoots.LanguagesDirectory, lang + ".json")));
            foreach (var key in new[] { "title_remote_media_consent", "msg_remote_media_consent" })
                Assert.False(string.IsNullOrWhiteSpace((string?)file[key]), $"{lang}.json lacks {key}");
            if (lang != "en") continue;
            // Read from source, not through Loc: other suites switch the live language.
            foreach (var head in new[] { "ConditioningControlPanel/MainWindow/MainWindow.Assets.cs", "CCP.Avalonia/Views/Tabs/AssetsTabView.MediaPicker.cs" })
            {
                var source = System.IO.File.ReadAllText(System.IO.Path.Combine(SourceRoots.RepoRoot, head));
                Assert.Equal((string?)file["title_remote_media_consent"], Fallback(source, "title_remote_media_consent"));
                Assert.Equal((string?)file["msg_remote_media_consent"], Fallback(source, "msg_remote_media_consent"));
            }
        }
    }

    /// <summary>The English a <c>LocOr("key", "..." + "...")</c> call falls back to, unescaped.</summary>
    private static string Fallback(string source, string key)
    {
        int start = source.IndexOf("LocOr(\"" + key + "\",", StringComparison.Ordinal);
        Assert.True(start >= 0, "no LocOr for " + key);
        start += key.Length + 9;
        int end = source.IndexOf(')', start);
        var parts = System.Text.RegularExpressions.Regex.Matches(source[start..end], "\"((?:[^\"\\\\]|\\\\.)*)\"");
        return string.Concat(System.Linq.Enumerable.Select(parts, m => System.Text.RegularExpressions.Regex.Unescape(m.Groups[1].Value)));
    }

    /// <summary>Not consent, so not asked: the licence switch off, a spent share or Offline mode load as before
    /// (and fall back as before), leaving consent and the source alone.</summary>
    [Theory]
    [InlineData("licence")]
    [InlineData("spent")]
    [InlineData("offline")]
    public void Other_blockers_are_not_asked_about(string blocker)
    {
        var s = Fresh();
        s.OfflineMode = blocker == "offline";
        var o = PickWith(s, answer: null, licence: blocker != "licence", spent: blocker == "spent");
        Assert.Empty(o.Asked);
        Assert.False(s.HasRemoteMediaConsent);
        Assert.Equal("local", s.MediaSource);
        Assert.Equal(new[] { "HypnoGoneWild" }, o.Loaded);
    }
}
