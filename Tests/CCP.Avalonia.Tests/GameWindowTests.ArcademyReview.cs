using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Sparkles.Claims;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void Arcademy_EscapeAndPanicResumeRespectSafety() => Run((shell, launcher) =>
    {
        var s = CoreSettings.Current;
        var old = (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll, s.AudioOnlySession);
        var w = new GameWindow(GameWindow.Games["arcademy"]);
        try
        {
            (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll, s.AudioOnlySession) = ("Escape", true, true, false);
            launcher.Hide(); w.Show(); w.Activate(); Dispatcher.UIThread.RunJobs();
            Assert.False(w.KeptEscapeReady);
            w.HandleMessage("{\"type\":\"ready\"}");
            var frames = new List<JObject>(); w.Posted += json => frames.Add(JObject.Parse(json));
            Assert.Same(w, GameWindow.EscapePauseInFront());
            var now = DateTime.Now;
            Assert.True(shell.TryGamePauseOnEscape(now));
            Assert.Contains(frames, f => (string?)f["type"] == "kept-escape");
            Assert.True(GameWindow.HandleArcademyPanic(now));
            Assert.Contains(frames, f => (string?)f["type"] == "suspend" && (bool?)f["on"] == true);
            s.AudioOnlySession = true;
            w.HandleMessage("{\"type\":\"resume-request\",\"reason\":\"panic\"}");
            Assert.DoesNotContain(frames, f => (string?)f["reason"] == "panic" && (bool?)f["on"] == false);
            s.AudioOnlySession = false;
            w.HandleMessage("{\"type\":\"resume-request\",\"reason\":\"panic\"}");
            Assert.Contains(frames, f => (string?)f["reason"] == "panic" && (bool?)f["on"] == false);
            Assert.True(GameWindow.HandleArcademyPanic(now.AddSeconds(4)));
            Assert.True(GameWindow.HandleArcademyPanic(now.AddSeconds(5)));
            Assert.Null(GameWindow.OpenForTests("arcademy"));
        }
        finally { w.Close(); (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll, s.AudioOnlySession) = old; }
    });

    [Fact]
    public void Arcademy_SettingWritesClampPersistAndEcho() => Run((_, _) =>
    {
        var w = new GameWindow(GameWindow.Games["arcademy"]);
        var settings = CoreSettings.Current;
        var old = (settings.ArcademySettingsJson, settings.ArcademyMasterIntensity, settings.ArcademyHideTutorial);
        var frames = new List<JObject>(); w.Posted += json => frames.Add(JObject.Parse(json));
        try
        {
            settings.ArcademySettingsJson = "{}";
            w.HandleMessage("{\"type\":\"ready\"}");
            w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"masterIntensity\",\"value\":9}");
            Assert.Equal(1, settings.ArcademyMasterIntensity);
            Assert.Equal(1, (double?)frames.Last()["value"]);
            w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"hideTutorial\",\"value\":true}");
            Assert.True(settings.ArcademyHideTutorial);
            Assert.True((bool?)frames.Last()["value"]);
            w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"dt_hard_mode\",\"value\":true}");
            Assert.True((bool?)JObject.Parse(settings.ArcademySettingsJson)["dt_hard_mode"]);
            Assert.True((bool?)frames.Last()["value"]);
            w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"unknown\",\"value\":true}");
            Assert.True((bool?)JObject.Parse(settings.ArcademySettingsJson)["unknown"]);
            Assert.True((bool?)frames.Last()["value"]);
            w.HandleMessage("{\"type\":\"set-setting\",\"key\":\"dt_hard_mode\",\"value\":{}}");
            Assert.True((bool?)frames.Last()["value"]);
        }
        finally { w.Close(); (settings.ArcademySettingsJson, settings.ArcademyMasterIntensity, settings.ArcademyHideTutorial) = old; }
    });

    [Fact]
    public void Arcademy_InitUsesActiveModAndConfiguredWords() => Run((_, _) =>
    {
        var root = Directory.CreateTempSubdirectory("arc-mod-");
        Directory.CreateDirectory(Path.Combine(root.FullName, "resources", "arcademy"));
        File.WriteAllText(Path.Combine(root.FullName, "resources", "arcademy", "lexicon.json"), "{\"class\":\"Lecon\",\"not_real\":\"bad\"}");
        var oldMod = CoreMods.ActiveModPackageProvider;
        var oldId = CoreMods.ActiveModIdProvider;
        var s = CoreSettings.Current; var oldWords = s.SubliminalPool;
        try
        {
            CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest { Id = "test-mod" }, root.FullName, false);
            CoreMods.ActiveModIdProvider = () => "test-mod";
            s.SubliminalPool = new Dictionary<string, bool> { ["  violet  "] = true, ["off"] = false };
            var w = new GameWindow(GameWindow.Games["arcademy"]);
            try
            {
                JObject? init = null; w.Posted += json => { var f = JObject.Parse(json); if ((string?)f["type"] == "init") init = f; };
                w.HandleMessage("{\"type\":\"ready\"}");
                Assert.Equal("test-mod", (string?)init!["modId"]);
                Assert.Equal("Lecon", (string?)init["lexicon"]?["class"]);
                Assert.Null(init["lexicon"]?["not_real"]);
                Assert.Equal(new[] { "violet" }, ((JArray)init["words"]!).Values<string>());
            }
            finally { w.Close(); }
        }
        finally { s.SubliminalPool = oldWords; CoreMods.ActiveModPackageProvider = oldMod; CoreMods.ActiveModIdProvider = oldId; root.Delete(true); }
    });

    [Fact]
    public void Arcademy_ClassAndCounterRepliesUseHostStores() => Run((_, _) =>
    {
        var s = CoreSettings.Current; var oldOffline = s.OfflineMode; var oldXp = CoreProgression.AddXPProvider;
        var oldSparkles = SparklesHead.Service;
        // The window's store writes the shared arcademy_meta.json; start clean and put it back after.
        var metaPath = Path.Combine(CorePaths.UserData, "arcademy_meta.json");
        var metaBytes = File.Exists(metaPath) ? File.ReadAllBytes(metaPath) : null;
        File.Delete(metaPath);
        var wire = new RewardWire();
        wire.Reply.SetResult(new(HttpStatusCode.OK) { Content = new StringContent("{ok:true,receipt:{credited:1,result:{kind:'medal',medal:'silver'}}}") });
        using var http = new HttpClient(wire);
        SparklesHead.Service = new SparklesClaimService(http, () => ("account", "test-token"), "http://localhost");
        var w = new GameWindow(GameWindow.Games["arcademy"]);
        var frames = new List<JObject>(); w.Posted += json => frames.Add(JObject.Parse(json));
        var awards = new List<double>();
        try
        {
            s.OfflineMode = true; CoreProgression.AddXPProvider = (amount, _) => awards.Add(amount);
            string key = "test_" + Guid.NewGuid().ToString("N");
            var start = new JObject { ["type"] = "class-started", ["gameKey"] = key };
            var end = new JObject { ["type"] = "class-ended", ["gameKey"] = key, ["gradeTier"] = 1, ["grade"] = "A" };
            w.HandleMessage(start.ToString()); w.HandleMessage(end.ToString());
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!w.SparklesPending.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
            Assert.Equal(1, wire.Calls);
            Assert.Equal("arcademy", (string?)wire.Body!["game"]);
            Assert.Equal("silver", (string?)wire.Body["result"]!["medal"]);
            Assert.Equal("A", (string?)wire.Body["detail"]?["grade"]);
            w.HandleMessage(end.ToString());
            Assert.Equal(new[] { 50d }, awards);
            var payouts = frames.Where(f => (string?)f["type"] == "payout-result").ToArray();
            Assert.Equal(2, payouts.Length); Assert.Equal(50, (double?)payouts[0]["xp"]); Assert.Equal(0, (double?)payouts[1]["xp"]);
            Assert.True((int?)payouts[0]["streak"] >= 1);
            var malformed = new JObject { ["type"] = "class-ended", ["gameKey"] = "bad_" + Guid.NewGuid().ToString("N"),
                ["gradeTier"] = (long)int.MaxValue + 1, ["grade"] = "A" };
            w.HandleMessage(malformed.ToString());
            Assert.Equal(50, (double?)frames.Last(f => (string?)f["type"] == "payout-result")["xp"]);
            var numeric = new JObject { ["type"] = "class-ended", ["gameKey"] = "num_" + Guid.NewGuid().ToString("N"),
                ["gradeTier"] = "3", ["flavorXp"] = "2", ["grade"] = "A" };
            w.HandleMessage(numeric.ToString());
            Assert.Equal(108.25, (double?)frames.Last(f => (string?)f["type"] == "payout-result")["xp"]);
            w.HandleMessage(new JObject { ["type"] = "enrollment-done", ["gameKey"] = key }.ToString());
            Assert.Contains(frames, f => (string?)f["type"] == "punchcard-result" && (string?)f["reason"] == "enrollment");
            w.HandleMessage("{\"type\":\"prize-buy\",\"sku\":\"not-a-prize\"}");
            Assert.Contains(frames, f => (string?)f["type"] == "wallet-result" && (bool?)f["ok"] == false);
            for (int i = 0; i < 20; i++) w.HandleMessage(end.ToString());
            w.HandleMessage("{\"type\":\"prize-buy\",\"sku\":\"id_frame_gold\"}");
            Assert.Contains(frames, f => (string?)f["type"] == "wallet-result" && (string?)f["sku"] == "id_frame_gold" && (bool?)f["ok"] == true);
            w.HandleMessage("{\"type\":\"prize-buy\",\"sku\":\"id_frame_gold\"}");
            Assert.False((bool?)frames.Last()["ok"]);
        }
        finally
        {
            w.Close(); s.OfflineMode = oldOffline; CoreProgression.AddXPProvider = oldXp; SparklesHead.Service = oldSparkles;
            if (metaBytes == null) File.Delete(metaPath); else File.WriteAllBytes(metaPath, metaBytes);
        }
    });

    [Fact]
    public void Arcademy_BankedPayoutReadsTheServerTill()
    {
        var till = GameWindow.ArcademyTill(JObject.Parse(
            "{tickets:-3,ticketBase:-1,ticketMult:0,payday:{mult:0},tokenMinted:true,lever:'honors',lateSlipUsed:false}"), "standard", true);
        Assert.Equal((0, 0, 1d, 1, true, "honors", false), (till.Tickets, till.Base, till.Mult, till.Payday, till.Token, till.Lever, till.Slip));
        till = GameWindow.ArcademyTill(JObject.Parse("{tickets:12,ticketBase:8,ticketMult:1.5,payday:{mult:2},lever:' '}"), "extra", true);
        Assert.Equal((12, 8, 1.5, 2, "extra", true), (till.Tickets, till.Base, till.Mult, till.Payday, till.Lever, till.Slip));
    }
}
