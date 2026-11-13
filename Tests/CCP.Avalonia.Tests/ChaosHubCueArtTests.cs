using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosHubCueArtTests
{
    [Fact]
    public async Task Button_and_habit_toggle_play_cues() => await AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var cues = new List<string>();
        var old = ChaosHubWindow.PlayCue;
        ChaosHubWindow.PlayCue = (key, _) => cues.Add(key);
        ChaosHubWindow? hub = null;
        var slotPath = ChaosMetaStore.SlotFilePath(ConditioningControlPanel.CoreSettings.Current.ChaosActiveSlot);
        var original = File.Exists(slotPath) ? File.ReadAllBytes(slotPath) : null;
        try
        {
            hub = new ChaosHubWindow();
            Assert.Empty(cues);
            hub.Show();
            await Task.Delay(200);
            var button = hub.GetVisualDescendants().OfType<Button>().First();
            button.AddHandler(Button.ClickEvent, (_, e) => e.Handled = true);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains("ui_click", cues);
            cues.Clear();
            var id = ChaosUpgrades.All.First().Id;
            ChaosMeta.State.PurchasedUpgrades.Add(id);
            var toggle = new Button { Tag = id };
            var handler = typeof(ChaosHubWindow).GetMethod("HabitToggle_Click", BindingFlags.Instance | BindingFlags.NonPublic)!;
            handler.Invoke(hub, new object[] { toggle, new RoutedEventArgs() });
            Assert.Contains(ChaosMeta.IsUpgradeActive(id) ? "ui_equip" : "ui_unequip", cues);
            cues.Clear();
            handler.Invoke(hub, new object[] { toggle, new RoutedEventArgs() });
            Assert.Contains(ChaosMeta.IsUpgradeActive(id) ? "ui_equip" : "ui_unequip", cues);
        }
        finally
        {
            hub?.Close();
            ChaosHubWindow.PlayCue = old;
            if (original == null) File.Delete(slotPath); else File.WriteAllBytes(slotPath, original);
            ChaosMeta.Init();
        }
    });
    [Fact]
    public async Task Balance_roll_plays_ticks() => await AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var cues = new List<string>();
        var old = ChaosHubWindow.PlayCue;
        ChaosHubWindow.PlayCue = (key, _) => cues.Add(key);
        var hub = new ChaosHubWindow();
        try
        {
            hub.Show();
            for (var i = 0; i < 100 && !hub.IsLoaded; i++) await Task.Delay(25);
            Assert.True(hub.IsLoaded);
            typeof(ChaosHubWindow).GetMethod("AnimateBalance", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(hub, new object[] { new TextBlock(), 1, 100 });
            for (var i = 0; i < 100 && !cues.Contains("count_tick"); i++) await Task.Delay(25);
            Assert.Contains("count_tick", cues);
            hub.Close();
            Assert.Empty(BalanceTimers(hub));
            var afterClose = cues.Count;
            await Task.Delay(250);
            Assert.Equal(afterClose, cues.Count);
        }
        finally
        {
            hub.Close();
            foreach (var timer in BalanceTimers(hub).Values) timer.Stop();
            BalanceTimers(hub).Clear();
            ChaosHubWindow.PlayCue = old;
        }
    });

    private static Dictionary<TextBlock, DispatcherTimer> BalanceTimers(ChaosHubWindow hub) =>
        (Dictionary<TextBlock, DispatcherTimer>)typeof(ChaosHubWindow)
            .GetField("_balanceAnims", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hub)!;

    [Fact]
    public void Chaos_cue_prefers_mod_audio_then_uses_bundled_fallback()
    {
        var oldResolver = CoreModArt.AudioOverridePathProvider;
        var oldPlayer = CoreAudio.PlayOneShotProvider;
        var root = Path.Combine(Path.GetTempPath(), "ccp-chaos-cue-" + Guid.NewGuid());
        var modFile = Path.Combine(root, "resources", "sounds", "chaos", "ui_click.wav");
        var played = new List<string>();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(modFile)!);
            File.WriteAllText(modFile, "mod");
            CoreModArt.AudioOverridePathProvider = rel => CoreModArt.ModAudioFile(rel, root);
            CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => played.Add(path);
            ChaosOverlayWindow.ChaosSfx.Play("ui_click", 0.3f);
            Assert.Equal(modFile, Assert.Single(played));

            played.Clear();
            File.Delete(modFile);
            ChaosOverlayWindow.ChaosSfx.Play("ui_click", 0.3f);
            var bundled = ConditioningControlPanel.Services.ContentLocator.Resolve(Path.Combine("Resources", "sounds", "chaos", "ui_click.mp3"));
            Assert.True(File.Exists(bundled));
            Assert.Equal(bundled, Assert.Single(played));
        }
        finally
        {
            CoreModArt.AudioOverridePathProvider = oldResolver;
            CoreAudio.PlayOneShotProvider = oldPlayer;
            Directory.Delete(root, true);
        }
    }

}
