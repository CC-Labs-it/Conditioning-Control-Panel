using System;
using System.IO;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void DesktopStartupInitializesChaosAfterSettings()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../CCP.Avalonia/App.axaml.cs")));
        var startup = source.IndexOf("private async Task StartDesktop(", StringComparison.Ordinal);
        var settings = source.IndexOf("CoreSettings.ServiceProvider = () => Settings;", startup, StringComparison.Ordinal);
        var chaos = source.IndexOf("InitializeChaos();", startup, StringComparison.Ordinal);
        Assert.True(startup >= 0 && settings > startup && chaos > settings);
    }

    [Fact]
    public void Dtrh_StartupLoadsSaveAndConsumesCrashSentinel() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        var slot = CoreSettings.Current.ChaosActiveSlot;
        var oldState = ChaosMeta.State;
        var path = ChaosMetaStore.SlotFilePath(2);
        var oldFile = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var sentinel = Path.Combine(CorePaths.UserData, "logs", "chaos_session.active");
        var oldSentinel = File.Exists(sentinel) ? File.ReadAllBytes(sentinel) : null;
        try
        {
            CoreSettings.Current.ChaosActiveSlot = 2;
            ChaosMetaStore.Save(new ChaosMetaState { Sparks = 100, RunsCompleted = 3 }, 2);
            Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
            File.WriteAllText(sentinel, "prior run");
            App.InitializeChaos();
            Assert.False(File.Exists(sentinel));
            GameWindow.DtrhBankSparks = (input, counts) => ChaosMeta.AwardRunRewards(input, counts);
            var w = GameWindow.Launch("dtrh")!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"request-run\"}");
            Assert.False((bool)Assert.Single(sent)["runConfig"]!["scriptedFirstRun"]!);
            sent.Clear();
            w.HandleMessage("{\"type\":\"run-started\",\"difficulty\":\"Easy\"}");
            w.HandleMessage("{\"type\":\"run-ended\",\"score\":300,\"durationSec\":180}");
            var earned = (int)Assert.Single(sent)["sparksEarned"]!;
            var saved = ChaosMetaStore.Load(2);
            Assert.True(earned > 0);
            Assert.Equal(100 + earned, saved.Sparks);
            Assert.Equal(4, saved.RunsCompleted);
        }
        finally
        {
            CoreSettings.Current.ChaosActiveSlot = slot;
            if (oldFile == null) File.Delete(path); else File.WriteAllBytes(path, oldFile);
            if (oldSentinel == null) File.Delete(sentinel); else File.WriteAllBytes(sentinel, oldSentinel);
            typeof(ChaosMeta).GetProperty(nameof(ChaosMeta.State))!.SetValue(null, oldState);
        }
    });
}
