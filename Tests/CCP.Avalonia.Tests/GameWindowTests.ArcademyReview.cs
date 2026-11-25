using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Models;
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
        var w = new GameWindow(GameWindow.ArcademySpec);
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
        var w = new GameWindow(GameWindow.ArcademySpec);
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
            var w = new GameWindow(GameWindow.ArcademySpec);
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
}
