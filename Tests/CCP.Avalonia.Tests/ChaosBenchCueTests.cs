using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosBenchCueTests
{
    [Theory]
    [InlineData("short", "ui_denied", false)]
    [InlineData("owned", "", false)]
    [InlineData("rank", "", false)]
    [InlineData("hazy", "", false)]
    [InlineData("gift", "pocket_sewn", true)]
    [InlineData("pocket", "pocket_sewn", false)]
    [InlineData("ordinary", "ui_unlock", false)]
    public void Purchase_feedback_matches_wpf(string scenario, string expected, bool gift) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var oldSlot = settings.ChaosActiveSlot;
        var oldAudio = CoreAudio.PlayOneShotProvider;
        var oldEngine = BarkHead.Engine;
        var oldAvatar = settings.AvatarEnabled;
        var oldSuppression = settings.BarkChatSuppressionMs;
        var oldRotation = new Dictionary<string, List<string>>(settings.BarkVariantRotation);
        var item = ChaosBench.Items.Single(i => i.Id == BenchIds.AccPocket1);
        var oldGate = item.RevealGate;
        var sounds = new List<string>();
        ChaosHubWindow? hub = null;
        try
        {
            settings.AvatarEnabled = true;
            settings.BarkChatSuppressionMs = 0;
            var rule = new BarkRule { Id = "gift-test", Trigger = "ChaosGiftGiven", VariantPool = new() { new BarkVariant("gift") } };
            var engine = new BarkEngine(() => new BarkRuleSet(new[] { rule })) { Speak = _ => { } };
            BarkHead.Engine = engine;
            engine.Start();
            int notifications = 0;
            engine.BarkSpoken += (_, _) => notifications++;
            CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => sounds.Add(Path.GetFileNameWithoutExtension(path));
            for (int s = 1; s <= 3; s++) ChaosMetaStore.Delete(s);
            var state = new ChaosMetaState { Gold = scenario is "short" or "gift" ? 0 : 3000, RunsCompleted = scenario == "rank" ? 0 : 50 };
            if (scenario == "owned") state.BenchPurchases.Add(BenchIds.AccPocket1);
            if (scenario == "hazy") item.RevealGate = RevealIds.Diary;
            ChaosMetaStore.Save(state, 1);
            ChaosMetaStore.SetActiveSlot(1);
            hub = new ChaosHubWindow();
            string id = scenario switch { "gift" or "pocket" => BenchIds.ToyPocket1, "rank" => BenchIds.ToyPocket2, "ordinary" => BenchIds.StatsPanel, _ => BenchIds.AccPocket1 };
            typeof(ChaosHubWindow).GetMethod("BenchBuy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hub, new object[] { id });
            Assert.Equal(expected == "" ? Array.Empty<string>() : new[] { expected }, sounds);
            Assert.Equal(gift ? 1 : 0, notifications);
        }
        finally
        {
            hub?.Close();
            item.RevealGate = oldGate;
            BarkHead.Engine = oldEngine;
            CoreAudio.PlayOneShotProvider = oldAudio;
            settings.AvatarEnabled = oldAvatar;
            settings.BarkChatSuppressionMs = oldSuppression;
            settings.BarkVariantRotation = oldRotation;
            for (int s = 1; s <= 3; s++) ChaosMetaStore.Delete(s);
            settings.ChaosActiveSlot = oldSlot;
            ChaosMeta.Init();
        }
    });
}
