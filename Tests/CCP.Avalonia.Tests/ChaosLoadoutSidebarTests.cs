using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using Avalonia.Threading;
using Avalonia.Controls.Primitives;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosLoadoutSidebarTests
{
    private static void Click(ChaosHubWindow hub, string name) =>
        hub.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task Dollhouse_shows_and_tracks_loadout_and_back_closes_it() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var render = ConditioningControlPanel.Avalonia.RenderProof.Rendering;
        var companion = ChaosHubWindow.SetCompanionActive;
        var oldState = ChaosMeta.State;
        var slotPath = ChaosMetaStore.SlotFilePath(ChaosMetaStore.ActiveSlot);
        var oldFile = File.Exists(slotPath) ? File.ReadAllBytes(slotPath) : null;
        ChaosHubWindow? hub = null;
        try
        {
            ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
            ChaosHubWindow.SetCompanionActive = _ => { };
            ChaosMeta.DebugResetState();
            var boon = ChaosLifetimeBoons.InCategory(ChaosBoonCategory.Skill).First();
            var habit = ChaosUpgrades.All.First();
            var charm = ChaosLifetimeBoons.InCategory(ChaosBoonCategory.Utility).First();
            ChaosMeta.State.ToyPockets = 2;
            ChaosMeta.State.AccessoryPockets = 1;
            ChaosMeta.State.LifetimeBoonLevels[boon.Id] = 1;
            ChaosMeta.State.LifetimeBoonLevels[charm.Id] = 1;
            ChaosMeta.SetBoonActive(charm.Id, true);
            ChaosMeta.State.PurchasedUpgrades.Add(habit.Id);
            ChaosMeta.SetBoonActive(boon.Id, true);
            hub = new ChaosHubWindow();
            hub.Show();
            Click(hub, "BtnMenuDollhouse");
            var state = Assert.IsType<ChaosRunState>(ChaosRunHost.PreState);
            Assert.Equal("FALL IN", ChaosRunHost.PreHud!.FindControl<IconLabel>("TxtHero")!.Text);
            Assert.Contains(state.ActiveSidebarToys, b => b.Id == boon.Id);
            Assert.Single(state.ActiveSidebarToys, b => b.IsEmptySlot);
            Assert.Single(state.ActiveSidebarAccessories, b => b.IsEmptySlot);
            Assert.Contains(state.RunModifiers, b => b.Name == habit.Name);
            Assert.Contains(state.RunModifiers, b => b.Id == charm.Id);
            ChaosMeta.SetBoonActive(boon.Id, false);
            ChaosMeta.SetUpgradeActive(habit.Id, false);
            Assert.All(state.ActiveSidebarToys, b => Assert.True(b.IsEmptySlot));
            Assert.DoesNotContain(state.RunModifiers, b => b.Name == habit.Name);
            Click(hub, "BtnBackToMenu");
            Assert.Null(ChaosRunHost.PreHud);
            Click(hub, "BtnMenuDollhouse");
            PanicSurfaces.All.Single(s => s.Id == "chaos").Stop(null);
            Assert.Null(ChaosRunHost.PreHud);
            Click(hub, "BtnBackToMenu");
            Click(hub, "BtnMenuDollhouse");
            ChaosRunHost.StartRun();
            Assert.Null(ChaosRunHost.PreHud);
            ChaosRunHost.ForceShutdown();
            Click(hub, "BtnBackToMenu");
            Click(hub, "BtnMenuDollhouse");
            hub.Close();
            Assert.Null(ChaosRunHost.PreHud);
        }
        finally
        {
            hub?.Close();
            ChaosRunHost.ForceShutdown();
            ChaosRunHost.CloseLoadoutSidebar();
            if (oldFile == null) File.Delete(slotPath); else File.WriteAllBytes(slotPath, oldFile);
            typeof(ChaosMeta).GetProperty(nameof(ChaosMeta.State))!.SetValue(null, oldState);
            ChaosHubWindow.SetCompanionActive = companion;
            ConditioningControlPanel.Avalonia.RenderProof.Rendering = render;
        }
        return Task.CompletedTask;
    });
    private static Task InPreview(Action<ChaosHubWindow, ChaosHudWindow, string> action) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var render = ConditioningControlPanel.Avalonia.RenderProof.Rendering;
        var companion = ChaosHubWindow.SetCompanionActive;
        var oldState = ChaosMeta.State;
        var path = ChaosMetaStore.SlotFilePath(ChaosMetaStore.ActiveSlot);
        var oldFile = File.Exists(path) ? File.ReadAllBytes(path) : null;
        ChaosHubWindow? hub = null;
        try
        {
            ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
            ChaosHubWindow.SetCompanionActive = _ => { };
            ChaosMeta.DebugResetState();
            var boon = ChaosLifetimeBoons.InCategory(ChaosBoonCategory.Skill).First();
            ChaosMeta.State.ToyPockets = 2;
            ChaosMeta.State.LifetimeBoonLevels[boon.Id] = 1;
            ChaosMeta.SetBoonActive(boon.Id, true);
            hub = new ChaosHubWindow();
            hub.Show();
            Click(hub, "BtnMenuDollhouse");
            action(hub, ChaosRunHost.PreHud!, boon.Id);
        }
        finally
        {
            Dispatcher.UIThread.RunJobs();
            ChaosRunHost.ForceShutdown();
            hub?.Close();
            if (oldFile == null) File.Delete(path); else File.WriteAllBytes(path, oldFile);
            typeof(ChaosMeta).GetProperty(nameof(ChaosMeta.State))!.SetValue(null, oldState);
            ChaosHubWindow.SetCompanionActive = companion;
            ConditioningControlPanel.Avalonia.RenderProof.Rendering = render;
        }
        return Task.CompletedTask;
    });

    private static void ClickHud(ChaosHudWindow hud, string name) =>
        hud.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void PressPocket(ChaosHudWindow hud, string id, bool left = true)
    {
        hud.UpdateLayout();
        var tile = hud.GetVisualDescendants().OfType<Grid>().First(g =>
            g.DataContext is ChaosSidebarBoon && Equals(g.Tag, id) && g.Width == 57);
        tile.RaiseEvent(new PointerPressedEventArgs(tile, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true), hud,
            new Point(), 0, new PointerPointProperties(left ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton,
                left ? PointerUpdateKind.LeftButtonPressed : PointerUpdateKind.RightButtonPressed), KeyModifiers.None, 1));
    }

    [Fact]
    public Task Filled_pocket_unequips_and_refreshes_hub() => InPreview((hub, hud, id) =>
    {
        Assert.Equal("1/2 equipped", hub.FindControl<TextBlock>("TxtSkillCount")!.Text);
        PressPocket(hud, id, left: false);
        Assert.True(ChaosMeta.IsBoonActive(id));
        PressPocket(hud, id);
        Assert.Equal("0/2 equipped", hub.FindControl<TextBlock>("TxtSkillCount")!.Text);
        Assert.False(ChaosMeta.IsBoonActive(id));
        Assert.DoesNotContain(ChaosRunHost.PreState!.ActiveSidebarToys, b => b.Id == id);
        Assert.Contains(ChaosRunHost.PreState.RecentEvents, line => line.Contains(ChaosLifetimeBoons.ById(id)!.Name));
    });

    [Fact]
    public Task Empty_pocket_opens_toybox() => InPreview((hub, hud, id) =>
    {
        PressPocket(hud, "");
        Assert.True(hub.FindControl<ToggleButton>("TabEnhance")!.IsChecked);
    });

    [Fact]
    public Task Close_button_closes_warren_and_preview() => InPreview((hub, hud, id) =>
    {
        ClickHud(hud, "BtnCloseMode");
        Assert.False(hub.IsVisible);
        Assert.Null(ChaosRunHost.PreHud);
    });

    [Fact]
    public Task Hero_saves_setup_and_starts_run_through_warren() => InPreview((hub, hud, id) =>
    {
        var value = CoreSettings.Current.ChaosEffectIntensity > 0.8 ? 0.5 : 1.2;
        hub.FindControl<Slider>("SldEffect")!.Value = value;
        ClickHud(hud, "BtnHero");
        Assert.Equal(value, CoreSettings.Current.ChaosEffectIntensity);
        Assert.False(hub.IsVisible);
        Assert.Null(ChaosRunHost.PreHud);
        Dispatcher.UIThread.RunJobs();
        Assert.True(ChaosRunHost.IsActive);
    });

    [Fact]
    public async Task Preview_test_restores_slot_and_in_memory_state()
    {
        var oldState = ChaosMeta.State;
        var path = ChaosMetaStore.SlotFilePath(ChaosMetaStore.ActiveSlot);
        var oldFile = File.Exists(path) ? File.ReadAllBytes(path) : null;
        await InPreview((_, _, id) => ChaosMeta.SetBoonActive(id, false));
        Assert.Same(oldState, ChaosMeta.State);
        if (oldFile == null) Assert.False(File.Exists(path));
        else Assert.Equal(oldFile, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("en")][InlineData("de")][InlineData("es")][InlineData("fr")][InlineData("it")]
    [InlineData("pt-BR")][InlineData("ru")][InlineData("ja")][InlineData("ko")][InlineData("zh-CN")]
    public Task Empty_pocket_copy_uses_selected_language(string language) => InPreview((hub, hud, id) =>
    {
        var previous = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            LocalizationManager.Instance.SetLanguage(language);
            ChaosMeta.SetBoonActive(id, false);
            var pocket = ChaosRunHost.PreState!.ActiveSidebarToys.First();
            Assert.Equal(Loc.Get("chaos_sidebar_empty_toy"), pocket.Name);
            Assert.Equal(Loc.Get("chaos_sidebar_empty_desc"), pocket.Desc);
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", language + ".json")));
            foreach (var key in new[] { "chaos_sidebar_empty_toy", "chaos_sidebar_empty_accessory", "chaos_sidebar_empty_desc", "chaos_sidebar_left_behind" })
                Assert.Equal(json.RootElement.GetProperty(key).GetString(), Loc.Get(key));
        }
        finally { LocalizationManager.Instance.SetLanguage(previous); }
    });
}
