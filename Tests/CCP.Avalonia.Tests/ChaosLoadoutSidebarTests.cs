using System.Linq;
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
            ChaosMeta.Init();
            ChaosHubWindow.SetCompanionActive = companion;
            ConditioningControlPanel.Avalonia.RenderProof.Rendering = render;
        }
        return Task.CompletedTask;
    });
}
