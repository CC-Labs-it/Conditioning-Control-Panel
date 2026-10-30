using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The For You feed's rules, lifted from WPF Services/Fyp/FypHostService.cs into Core FypRules (one copy,
/// both heads). The page is never trusted: remote needs consent AND a non-library source, settings frames are
/// whitelisted, a probe commits only a real answer.</summary>
[Collection(SessionStatics.Name)]
public sealed class FypRulesTests
{
    [Fact]
    public void Gaze_TransformsMixedDpiAndMonitorOrigins_ClampsOutside()
    {
        var p = FypRules.GazePoint(300, 200, 2, -1920, 100, true, -1520, 300, 1.25, 800, 400);
        Assert.Equal(0.2, p.X, 8);
        Assert.Equal(0.4, p.Y, 8);
        Assert.True(p.Inside);
        p = FypRules.GazePoint(-10, 500, 99, 9999, 9999, false, 0, 0, 1, 100, 100);
        Assert.Equal(0, p.X);
        Assert.Equal(1, p.Y);
        Assert.False(p.Inside);
    }

    private static AppSettings Fresh() => new();

    [Fact]
    public void RemoteNeedsConsentAndANonLibrarySource()
    {
        var s = Fresh();
        Assert.Equal("library", FypRules.EffectiveFeedSource(s));
        Assert.False(FypRules.RemoteAllowed(s));
        Assert.False(FypRules.RemoteAllowed(null));
        // The app-wide source carries over only with consent.
        s.MediaSource = "online";
        Assert.Equal("library", FypRules.EffectiveFeedSource(s));
        Assert.False(FypRules.RemoteAllowed(s));
        // A page cannot talk its way past the consent card.
        Assert.Equal(FypRules.SettingEffect.None, FypRules.ApplySetting(s, "source", "online"));
        Assert.Equal("library", s.FypSource);
        Assert.False(FypRules.RemoteAllowed(s));
        // Consent is one-way and only on a literal true.
        FypRules.ApplySetting(s, "onlineConsented", false);
        Assert.False(s.HasRemoteMediaConsent);
        FypRules.ApplySetting(s, "onlineConsented", true);
        Assert.True(s.HasRemoteMediaConsent);
        FypRules.ApplySetting(s, "onlineConsented", false);
        Assert.True(s.HasRemoteMediaConsent);
        Assert.Equal("online", FypRules.EffectiveFeedSource(s));   // the app-wide source, now consented
        Assert.True(FypRules.RemoteAllowed(s));
        // The feed's own picker wins once it has left "library", ratio included.
        s.RemoteMediaRatio = 70;
        Assert.Equal(70, FypRules.EffectiveOnlineRatio(s));
        FypRules.ApplySetting(s, "source", "mixed");
        FypRules.ApplySetting(s, "onlineRatio", 20);
        Assert.Equal("mixed", FypRules.EffectiveFeedSource(s));
        Assert.Equal(20, FypRules.EffectiveOnlineRatio(s));
        // Local app-wide + library feed = nothing remote, consent or not.
        s.MediaSource = "local";
        FypRules.ApplySetting(s, "source", "library");
        Assert.False(FypRules.RemoteAllowed(s));
    }

    [Fact]
    public void SettingsFramesAreWhitelistedAndSanitized()
    {
        var s = Fresh();
        FypRules.ApplySetting(s, "layout", "trio");
        FypRules.ApplySetting(s, "layout", "sideways");   // not a layout: the property keeps its default
        FypRules.ApplySetting(s, "muted", true);
        FypRules.ApplySetting(s, "volume", 40);
        Assert.Equal("duo", s.FypLayout);
        Assert.True(s.FypMuted);
        Assert.Equal(40, s.FypVolume);
        // Unknown keys, null keys and null values change nothing and never throw.
        Assert.Equal(FypRules.SettingEffect.None, FypRules.ApplySetting(s, "AuthToken", "x"));
        Assert.Equal(FypRules.SettingEffect.None, FypRules.ApplySetting(s, null, "x"));
        Assert.Equal(FypRules.SettingEffect.None, FypRules.ApplySetting(s, "layout", null));
        Assert.Equal(FypRules.SettingEffect.None, FypRules.ApplySetting(s, "volume", "loud"));
        Assert.Equal(40, s.FypVolume);
        // Niches: catalog ids only, no duplicates.
        var real = FypOnlineCoordinator.Catalog[0].Id;
        FypRules.ApplySetting(s, "onlineNiches", new JArray(real, "not-a-niche", real));
        Assert.Equal(new[] { real }, s.FypOnlineNiches);
        // Custom subs: sanitized, de-duplicated, capped at 20; each kept in the library.
        var many = new JArray(Enumerable.Range(0, 30).Select(i => (object)("sub" + i)).ToArray()) { "sub0", "bad name!", "" };
        FypRules.ApplySetting(s, "onlineCustomSubs", many);
        Assert.Equal(20, s.FypOnlineCustomSubs.Count);
        Assert.All(s.FypOnlineCustomSubs, name => Assert.NotNull(FypOnlineCoordinator.SanitizeSub(name)));
        Assert.Contains(s.BuildRemoteSubLibraryView(), r => r.Name == "sub0" && r.Selected);
    }

    [Fact]
    public void GhostAndEyeFramesReportTheirEffect_NeverStoreGhost()
    {
        var s = Fresh();
        Assert.Equal(FypRules.SettingEffect.GhostOn, FypRules.ApplySetting(s, "clickThrough", true));
        Assert.Equal(FypRules.SettingEffect.GhostOff, FypRules.ApplySetting(s, "clickThrough", false));
        Assert.Equal(FypRules.SettingEffect.GhostOn, FypRules.ApplySetting(null, "clickThrough", true));   // settings-free
        Assert.Equal(FypRules.SettingEffect.EyeControlOn, FypRules.ApplySetting(s, "eyeControl", true));
        Assert.True(s.FypEyeControl);
        Assert.Equal(FypRules.SettingEffect.EyeControlOff, FypRules.ApplySetting(s, "eyeControl", false));
        Assert.Equal(FypRules.SettingEffect.EyeGazeChanged, FypRules.ApplySetting(s, "eyeGaze", true));
        Assert.Equal(FypRules.SettingEffect.OpacityChanged, FypRules.ApplySetting(s, "windowOpacity", 0.4));
        Assert.Equal(0.4, s.FypWindowOpacity, 3);
    }

    [Fact]
    public void InitCarriesSettingsCatalogAndLibrary_AndNeverAnEyeToggleTheHeadCannotDrive()
    {
        var s = Fresh();
        s.FypEyeControl = true;
        s.FypEyeGaze = true;
        s.TryAddLibrarySub("hypno");
        var noStats = Path.Combine(Path.GetTempPath(), "fyp-nostats-" + Guid.NewGuid().ToString("N") + ".json");
        var init = FypRules.BuildInit(s, Array.Empty<FypAssetManifest.Entry>(), eyeControl: false, noStats);
        Assert.Equal("init", (string?)init["type"]);
        Assert.Empty((JArray)init["assets"]!);
        Assert.False((bool)init["settings"]!["eyeControl"]!);
        Assert.False((bool)init["settings"]!["eyeGaze"]!);
        Assert.Equal("library", (string?)init["settings"]!["source"]);
        Assert.False((bool)init["settings"]!["onlineConsented"]!);
        Assert.Equal(FypOnlineCoordinator.Catalog.Length, ((JArray)init["online"]!["niches"]!).Count);
        Assert.Contains((JArray)init["online"]!["library"]!, r => (string?)r["name"] == "hypno");
        Assert.Equal(JTokenType.Null, init["stats"]!.Type);
        var on = FypRules.BuildInit(s, Array.Empty<FypAssetManifest.Entry>(), eyeControl: true, noStats);
        Assert.True((bool)on["settings"]!["eyeControl"]!);
    }

    [Fact]
    public void StatsRoundTripVerbatim()
    {
        var file = Path.Combine(Path.GetTempPath(), "fyp-stats-" + Guid.NewGuid().ToString("N"), "fyp_stats.json");
        try
        {
            Assert.Null(FypRules.LoadStats(file));
            FypRules.SaveStats(file, JObject.Parse("{\"clips\":12,\"nested\":{\"a\":[1,2]}}"));
            Assert.Equal(12, (int)FypRules.LoadStats(file)!["clips"]!);
            File.WriteAllText(file, "{broken");
            Assert.Null(FypRules.LoadStats(file));   // a bad file never throws
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(file)!, true); } catch { } }
    }
    [Fact]
    public void ClipXpIsCappedPerRollingMinute()
    {
        FypRules.ResetClipXpForTests();
        try
        {
            var t = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < FypRules.MaxClipXpPerMinute; i++) Assert.True(FypRules.AllowClipXp(t.AddSeconds(i)));
            Assert.False(FypRules.AllowClipXp(t.AddSeconds(30)));
            Assert.True(FypRules.AllowClipXp(t.AddSeconds(61)));    // the oldest left the window
        }
        finally { FypRules.ResetClipXpForTests(); }
    }
    [Fact]
    public void AnEmptyBatchStillSendsItsStatus()   // the page clears its spinner or shows "offline"
    {
        var dry = FypRules.BatchFrames(new FypOnlineCoordinator.FeedBatch(new(), "offline", true, 0)).ToList();
        Assert.Equal("online-status", (string?)dry.Single()["type"]);
        Assert.False((bool)dry[0]["ok"]!);
        Assert.Equal("offline", (string?)dry[0]["error"]);
        Assert.True((bool)dry[0]["dry"]!);
    }
    [Fact]
    public void AProbeIsRememberedOnlyWhenItAnswered_AndAFoundSubJoinsTheFeed()
    {
        var s = Fresh();
        var failed = FypRules.CommitProbe(s, "hypno", new SubProbe { Ok = false, Error = "network" });
        Assert.Equal("network", (string?)failed["error"]);
        Assert.Empty(s.FypOnlineSubVerdicts);            // a transport failure taught us nothing
        var missing = FypRules.CommitProbe(s, "nosuchsub", new SubProbe { Ok = false });
        Assert.False((bool)missing["ok"]!);
        Assert.False(s.FypOnlineSubVerdicts["nosuchsub"].Ok);
        Assert.Empty(s.FypOnlineCustomSubs);
        var found = FypRules.CommitProbe(s, "hypno", new SubProbe { Ok = true, VideoCount = 42 });
        Assert.Equal("sub-probe", (string?)found["type"]);
        Assert.Equal(42, (int)found["videoCount"]!);
        Assert.Contains("hypno", s.FypOnlineCustomSubs);
        FypRules.CommitProbe(s, "hypno", new SubProbe { Ok = true, VideoCount = 42 });
        Assert.Single(s.FypOnlineCustomSubs);            // never twice
        // Deselecting a pill is not forgetting it: a library sub keeps its verdict, a name nothing keeps loses it.
        s.FypOnlineSubVerdicts["gone"] = new RemoteSubVerdict { Ok = true };
        FypRules.ApplySetting(s, "onlineCustomSubs", new JArray());
        Assert.Empty(s.FypOnlineCustomSubs);
        Assert.True(s.FypOnlineSubVerdicts.ContainsKey("hypno"));
        Assert.False(s.FypOnlineSubVerdicts.ContainsKey("gone"));
        Assert.Equal("invalid", (string?)FypRules.InvalidProbeFrame("bad name!")["error"]);
        Assert.Equal("library", (string?)FypRules.LibraryFrame(s)["type"]);
    }
}
