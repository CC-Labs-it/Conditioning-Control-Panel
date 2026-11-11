using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
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
            await Task.Delay(200);
            typeof(ChaosHubWindow).GetMethod("AnimateBalance", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(hub, new object[] { new TextBlock(), 1, 100 });
            await Task.Delay(250);
            Assert.Contains("count_tick", cues);
        }
        finally { hub.Close(); ChaosHubWindow.PlayCue = old; }
    });

}
