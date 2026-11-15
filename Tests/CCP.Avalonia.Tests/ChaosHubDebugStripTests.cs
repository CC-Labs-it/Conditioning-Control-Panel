using System;
using System.Linq;
using System.IO;
using System.Reflection;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosHubDebugStripTests
{
    [Fact]
    public void Strip_is_flagged_and_actions_refresh_the_hub() => AvaloniaTestDispatcher.Run(() =>
    {
        var oldFlag = ChaosHubWindow.DebugStripEnabled;
        var oldProvider = CoreSettings.ServiceProvider;
        var settingsDir = Path.Combine(Path.GetTempPath(), "ccp-debug-strip-" + Guid.NewGuid());
        Directory.CreateDirectory(settingsDir);
        var settings = new SettingsService(Path.Combine(settingsDir, "settings.json"));
        var oldState = ChaosMeta.State;
        var slotPath = ChaosMetaStore.SlotFilePath(1);
        var slot = File.Exists(slotPath) ? File.ReadAllBytes(slotPath) : null;
        var tempPath = slotPath + ".tmp";
        var temp = File.Exists(tempPath) ? File.ReadAllBytes(tempPath) : null;
        ChaosHubWindow? hub = null;
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            ChaosMetaStore.Save(new ChaosMetaState { SeenIntroGuide = true, SeenDollhouse = true }, 1);
            CoreSettings.Current.ChaosActiveSlot = 1;
            ChaosHubWindow.DebugStripEnabled = () => false;
            hub = new ChaosHubWindow(); hub.Show();
            Assert.Null(hub.DebugStrip);
            hub.Close();
            ChaosHubWindow.DebugStripEnabled = () => true;
            hub = new ChaosHubWindow(); hub.Show();
            var strip = Assert.IsType<WrapPanel>(hub.DebugStrip);
            var boxes = strip.Children.OfType<TextBox>().ToArray();
            void Click(string text) => strip.Children.OfType<Button>().Single(b => (string?)b.Content == text)
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            boxes[0].Text = "-1"; Click("set runs"); Assert.Equal(0, ChaosMeta.State.RunsCompleted);
            boxes[0].Text = "10"; Click("set runs");
            Assert.Equal(10, ChaosMeta.State.RunsCompleted);
            Assert.Equal("Slipping", hub.FindControl<TextBlock>("MenuRank")!.Text);
            Click(Loc.Get("chaos_debug_add_sparks")); Assert.Equal(500, ChaosMeta.State.Sparks);
            Assert.Equal("500", hub.FindControl<TextBlock>("MenuSparks")!.Text);
            Click("+500 gold"); Assert.Equal(500, ChaosMeta.State.Gold);
            Assert.Equal("500", hub.FindControl<TextBlock>("MenuGold")!.Text);
            Click("complete lesson"); Assert.True(ChaosLessons.IsComplete("slow_fuses"));
            boxes[1].Text = "vibe_popping"; Click("complete lesson"); Assert.True(ChaosLessons.IsComplete("vibe_popping"));
            boxes[2].Text = "debug_test_reveal"; Click("force reveal");
            Assert.True(ChaosMeta.State.SeenReveals.Contains("debug_test_reveal") || ChaosMeta.State.PendingReveals.Contains("debug_test_reveal"));
            Click("reset meta"); Assert.Equal(0, ChaosMeta.State.RunsCompleted); Assert.Equal(0, ChaosMeta.State.Gold);
            Assert.Equal("0", hub.FindControl<TextBlock>("MenuGold")!.Text);
            Assert.NotEqual("Slipping", hub.FindControl<TextBlock>("MenuRank")!.Text);
            Assert.False(ChaosLessons.IsComplete("slow_fuses"));
        }
        finally
        {
            hub?.Close(); ChaosHubWindow.DebugStripEnabled = oldFlag;
            settings.SealForReset();
            CoreSettings.ServiceProvider = oldProvider;
            if (Directory.Exists(settingsDir)) Directory.Delete(settingsDir, true);
            if (slot == null) File.Delete(slotPath); else File.WriteAllBytes(slotPath, slot);
            if (temp == null) File.Delete(tempPath); else File.WriteAllBytes(tempPath, temp);
            typeof(ChaosMeta).GetProperty(nameof(ChaosMeta.State))!.SetValue(null, oldState);
        }
    });

    [Fact]
    public void Strip_test_preserves_save_and_settings_without_scheduling_a_save() => AvaloniaTestDispatcher.Run(() =>
    {
        var snapshot = ProcessStateSnapshot.Take(false);
        var previousProvider = CoreSettings.ServiceProvider;
        var tempPath = ChaosMetaStore.SlotFilePath(1) + ".tmp";
        var previousTemp = File.Exists(tempPath) ? File.ReadAllBytes(tempPath) : null;
        var settingsDir = Path.Combine(Path.GetTempPath(), "ccp-debug-settings-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(settingsDir);
            var settingsPath = Path.Combine(settingsDir, "settings.json");
            var settings = new SettingsService(settingsPath) { SaveDebounceDueTimeMilliseconds = System.Threading.Timeout.Infinite };
            settings.Current.ChaosActiveSlot = 2;
            Assert.True(settings.TrySaveImmediate());
            var settingsBytes = File.ReadAllBytes(settingsPath);
            CoreSettings.ServiceProvider = () => settings;
            ChaosMetaStore.Save(new ChaosMetaState { Gold = 123 }, 1);
            var slotBytes = File.ReadAllBytes(ChaosMetaStore.SlotFilePath(1));
            File.WriteAllText(tempPath, "pending save");
            var state = ChaosMeta.State;
            Strip_is_flagged_and_actions_refresh_the_hub();
            Assert.Equal(slotBytes, File.ReadAllBytes(ChaosMetaStore.SlotFilePath(1)));
            Assert.Equal("pending save", File.ReadAllText(tempPath));
            Assert.Equal(2, settings.Current.ChaosActiveSlot);
            Assert.Equal(settingsBytes, File.ReadAllBytes(settingsPath));
            Assert.False((bool)typeof(SettingsService).GetField("_savePending", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(settings)!);
            Assert.Same(state, ChaosMeta.State);
        }
        finally
        {
            SettingsService.DisarmAllPendingSaves();
            CoreSettings.ServiceProvider = previousProvider;
            snapshot.Restore();
            if (previousTemp == null) File.Delete(tempPath); else File.WriteAllBytes(tempPath, previousTemp);
            if (Directory.Exists(settingsDir)) Directory.Delete(settingsDir, true);
        }
    });

    [Theory]
    [InlineData("en", "+500 sparks")]
    [InlineData("de", "+500 Funken")]
    [InlineData("es", "+500 chispas")]
    [InlineData("fr", "+500 étincelles")]
    [InlineData("it", "+500 scintille")]
    [InlineData("ja", "+500 スパーク")]
    [InlineData("ko", "+500 스파크")]
    [InlineData("pt-BR", "+500 faíscas")]
    [InlineData("ru", "+500 искр")]
    [InlineData("zh-CN", "+500 火花")]
    public void Sparks_button_tracks_language(string language, string expected) => AvaloniaTestDispatcher.Run(() =>
    {
        var oldFlag = ChaosHubWindow.DebugStripEnabled;
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
        var oldRender = global::ConditioningControlPanel.Avalonia.RenderProof.Rendering;
        ChaosHubWindow? hub = null;
        try
        {
            ChaosHubWindow.DebugStripEnabled = () => true;
            // A render instance avoids loading or saving the profile in the constructor.
            global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
            hub = new ChaosHubWindow(); hub.Show();
            var button = Assert.IsType<WrapPanel>(hub.DebugStrip).Children.OfType<Button>().ElementAt(1);
            LocalizationManager.Instance.SetLanguage(language);
            Assert.Equal(expected, button.Content);
        }
        finally
        {
            hub?.Close();
            global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = oldRender;
            LocalizationManager.Instance.SetLanguage(oldLanguage);
            ChaosHubWindow.DebugStripEnabled = oldFlag;
        }
    });

}
