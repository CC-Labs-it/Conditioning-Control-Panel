using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosHubArtTests
{
    [Theory]
    [InlineData("slot")]
    [InlineData("settings")]
    public Task Art_test_preserves_slots_settings_and_live_state(string part) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var snapshot = ProcessStateSnapshot.Take(repairLateWrites: false);
        var originalState = ChaosMeta.State;
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string slot = ChaosMetaStore.SlotFilePath(1);
            const string slotContents = "{\"Sparks\":987}";
            File.WriteAllText(slot, slotContents);
            string settings = Path.Combine(root, "settings.json");
            var service = new ConditioningControlPanel.Services.SettingsService(settings);
            service.Current.ChaosActiveSlot = 3;
            Assert.True(service.TrySaveImmediate());
            var settingsBytes = File.ReadAllBytes(settings);
            CoreSettings.ServiceProvider = null;
            var fallback = CoreSettings.Current;
            var fallbackBefore = (fallback.ChaosActiveSlot, fallback.ChaosAccessoryKey1, fallback.ChaosAccessoryKey2);
            CoreSettings.ServiceProvider = () => service;

            await Art_is_shown_on_shelves_bag_diary_and_howto_with_fallbacks("upgrade");

            if (part == "slot") Assert.Equal(slotContents, File.Exists(slot) ? File.ReadAllText(slot) : null);
            if (part == "settings")
            {
                Assert.Same(service, CoreSettings.Service);
                Assert.Equal(3, service.Current.ChaosActiveSlot);
                Assert.False(service.HasPendingSave);
                Assert.Equal(settingsBytes, File.ReadAllBytes(settings));
                Assert.Equal(fallbackBefore, (fallback.ChaosActiveSlot, fallback.ChaosAccessoryKey1, fallback.ChaosAccessoryKey2));
            }
            Assert.Same(originalState, ChaosMeta.State);
        }
        finally
        {
            snapshot.Restore();
            typeof(ChaosMeta).GetProperty(nameof(ChaosMeta.State))!.SetValue(null, originalState);
            Directory.Delete(root, true);
        }
    });

    [Theory]
    [InlineData("upgrade")]
    [InlineData("boon")]
    [InlineData("mystery")]
    [InlineData("empty")]
    [InlineData("codex")]
    [InlineData("banner")]
    [InlineData("backdrop")]
    [InlineData("howto")]
    [InlineData("pocket")]
    [InlineData("fallback")]
    public Task Art_is_shown_on_shelves_bag_diary_and_howto_with_fallbacks(string part) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string[] files = { "upgrades/slow_fuses.png", "boons/blank_eyes.png", "boons/vibe_popping.png", "hub/tile_unknown.png", "hub/pockets_empty.png", "hub/backdrop.png", "bubbles/tease.png", "howto/howto_1.png", "banner.png" };
        foreach (string file in files)
        {
            string path = Path.Combine(root, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var bitmap = new WriteableBitmap(new PixelSize(6, 6), new Vector(96, 96));
            bitmap.Save(path);
        }
        bool oldRendering = global::ConditioningControlPanel.Avalonia.RenderProof.Rendering;
        var stateProperty = typeof(ChaosMeta).GetProperty(nameof(ChaosMeta.State))!;
        var oldState = ChaosMeta.State;
        var oldProvider = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = null;
        var fallback = CoreSettings.Current;
        var oldKeys = (fallback.ChaosAccessoryKey1, fallback.ChaosAccessoryKey2);
        var oldSlot = fallback.ChaosActiveSlot;
        ChaosHubWindow? hub = null;
        try
        {
            ChaosArt.RootsOverride = () => new[] { root };
            // RenderProof avoids Init, so the art test can use a private in-memory state.
            stateProperty.SetValue(null, new ChaosMetaState { RunsCompleted = 2, LifetimeBoonLevels = new Dictionary<string, int> { ["blank_eyes"] = 1 }, DiscoveredCodexIds = new HashSet<string> { "bubble:tease" } });
            global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
            hub = new ChaosHubWindow();
            bool HasArt(Control host, string kind, string id)
            {
                var source = ChaosArt.Resolve(kind, id);
                return source != null && host.GetLogicalDescendants().OfType<Image>().Any(i => ReferenceEquals(i.Source, source));
            }
            if (part == "upgrade")
            {
                var host = hub.FindControl<StackPanel>("HabitsHost")!;
                Assert.True(HasArt(host, "upgrades", "slow_fuses"));
                var image = host.GetLogicalDescendants().OfType<Image>().Single(i => ReferenceEquals(i.Source, ChaosArt.Resolve("upgrades", "slow_fuses")));
                var frame = Assert.IsType<Grid>(image.Parent);
                Assert.IsType<RectangleGeometry>(frame.Clip);
                Assert.Contains(frame.Children.OfType<Border>(), b => b.BorderThickness.Left == 3);
            }
            if (part == "boon") Assert.True(HasArt(hub.FindControl<Panel>("TilesHabits")!, "boons", "blank_eyes"));
            var charm = hub.FindControl<Panel>("TilesHabits")!.Children.OfType<StackPanel>()
                .First(c => (ToolTip.GetTip(c) as string)?.StartsWith("? ? ?\n") == true);
            if (part == "mystery") Assert.True(HasArt(charm, "hub", "tile_unknown"));
            if (part == "empty") Assert.True(HasArt(hub.FindControl<StackPanel>("PocketSlotsHost")!, "hub", "pockets_empty"));
            if (part == "codex") Assert.True(HasArt(hub.FindControl<StackPanel>("DiaryHost")!, "bubbles", "tease"));
            if (part == "banner")
            {
                Assert.Same(ChaosArt.MenuImage("banner.png"), hub.FindControl<Image>("BannerImage")!.Source);
                Assert.True(hub.FindControl<Image>("BannerImage")!.IsVisible);
            }
            if (part == "backdrop")
            {
                Assert.Same(ChaosArt.Resolve("hub", "backdrop"), hub.FindControl<Image>("HubBackdrop")!.Source);
                Assert.True(hub.FindControl<Image>("HubBackdrop")!.IsVisible);
            }
            typeof(ChaosHubWindow).GetMethod("HowToShow", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(hub, null);
            var box = hub.FindControl<Border>("HowToImageBox")!;
            if (part == "howto")
            {
                Assert.True(box.IsVisible);
                Assert.Same(ChaosArt.Resolve("howto", "howto_1"), ((ImageBrush)((Border)((Grid)box.Child!).Children[0]).Background!).Source);
            }
            hub.Close(); hub = null;

            // An equipped toy appears both in the BAG and its pocket.
            ChaosMeta.State.ToyPockets = 1;
            ChaosMeta.State.LifetimeBoonLevels["vibe_popping"] = 1;
            ChaosMeta.State.ActiveLifetimeBoons.Add("vibe_popping");
            hub = new ChaosHubWindow();
            if (part == "pocket")
            {
                Assert.True(HasArt(hub.FindControl<StackPanel>("PocketSlotsHost")!, "boons", "vibe_popping"));
                Assert.True(HasArt(hub.FindControl<Panel>("TilesSkills")!, "boons", "vibe_popping"));
            }
            hub.Close(); hub = null;

            foreach (string file in files) File.Delete(Path.Combine(root, file));
            ChaosArt.ResetForTest();
            ChaosArt.RootsOverride = () => new[] { root };
            hub = new ChaosHubWindow();
            if (part == "fallback")
            {
                Assert.Contains(hub.FindControl<StackPanel>("HabitsHost")!.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "⏳");
                Assert.False(hub.FindControl<Image>("BannerImage")!.IsVisible);
                Assert.False(hub.FindControl<Image>("HubBackdrop")!.IsVisible);
            }
            typeof(ChaosHubWindow).GetMethod("HowToShow", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(hub, null);
            if (part == "fallback") Assert.False(hub.FindControl<Border>("HowToImageBox")!.IsVisible);
        }
        finally
        {
            try { hub?.Close(); }
            finally
            {
                global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = oldRendering;
                ChaosArt.ResetForTest();
                stateProperty.SetValue(null, oldState);
                (fallback.ChaosAccessoryKey1, fallback.ChaosAccessoryKey2) = oldKeys;
                fallback.ChaosActiveSlot = oldSlot;
                CoreSettings.ServiceProvider = oldProvider;
                Directory.Delete(root, true);
            }
        }
        return Task.CompletedTask;
    });
}
