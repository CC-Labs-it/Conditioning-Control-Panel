using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // WPF DtrhHostService.cs:923-953: actual launcher -> failed page -> native recovery.
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void Dtrh_boot_error_recovers_once_and_panic_cancels_pending_recovery(bool returning, bool panic, bool visibleOwner)
        => RunDtrh(lab: true, (shell, _, _, _) =>
    {
        int slot = CoreSettings.Current.ChaosActiveSlot;
        var oldOwner = ChaosHubWindow.HubOwner;
        ChaosHubWindow.HubOwner = () => shell; // desktop lifetime is absent under SetupWithoutStarting
        try
        {
            ChaosMetaStore.Save(new ChaosMetaState { RunsCompleted = returning ? 1 : 0 }, 1);
            ChaosMetaStore.SetActiveSlot(1);
            if (visibleOwner) shell.Show();
            var page = GameWindow.Launch(GameWindow.DtrhId)!;
            Assert.True(page.IsVisible);
            page.HandleMessage("{\"type\":\"boot-error\"}");
            page.HandleMessage("{\"type\":\"boot-error\"}"); // no second hub/run
            if (panic) GameWindow.CloseAllForPanic();
            Dispatcher.UIThread.RunJobs();
            Assert.False(page.IsVisible);
            if (panic)
            {
                Assert.Null(ChaosHubWindow.Current);
                Assert.False(ChaosRunHost.IsActive);
            }
            else if (returning)
            {
                var hub = ChaosHubWindow.Current;
                Assert.NotNull(hub);
                Assert.True(hub.IsVisible);
                Assert.Same(visibleOwner ? shell : null, hub.Owner);
                Assert.NotNull(ChaosIntroWindow.Current); // first-open guide on the real path
                Assert.False(ChaosRunHost.IsActive);
                ChaosIntroWindow.Current!.Close();
                Dispatcher.UIThread.RunJobs();
                Assert.True(ChaosMetaStore.Load(1).SeenIntroGuide);
                Assert.Same(hub, ChaosHubWindow.OpenAt("run"));
                Assert.True(hub.FindControl<ScrollViewer>("PanelRun")!.IsVisible);
                hub.Close();
                Assert.Null(ChaosHubWindow.Current);
            }
            else
            {
                Assert.Null(ChaosHubWindow.Current);
                Assert.True(ChaosRunHost.IsActive);
                Assert.True(ChaosRunHost.State!.Config.ScriptedFirstRun);
                Assert.False(ChaosRunHost.IsDescending); // countdown, not a premature run
                Assert.Null(ChaosHubWindow.OpenAt()); // active run guards every hub door
                ChaosRunHost.Overlay!.FinishCountdown();
                Assert.True(ChaosRunHost.IsDescending);
            }
        }
        finally
        {
            ChaosHubWindow.HubOwner = oldOwner;
            ChaosIntroWindow.Current?.Close();
            ChaosHubWindow.Current?.Close();
            ChaosRunHost.ForceShutdown();
            ChaosMetaStore.Delete(1);
            CoreSettings.Current.ChaosActiveSlot = slot;
            ChaosMeta.Init();
        }
    });

    // WPF ChaosOverlayWindow.xaml.cs:867-885: recap closes first; registry reused, run tab selected.
    [Fact]
    public void Chaos_recap_adjust_reuses_hub_and_clears_run_before_navigation()
        => RunDtrh(lab: true, (_, _, _, _) =>
    {
        int slot = CoreSettings.Current.ChaosActiveSlot;
        try
        {
            ChaosMetaStore.Save(new ChaosMetaState { SeenIntroGuide = true, RunsCompleted = 1 }, 1);
            ChaosMetaStore.SetActiveSlot(1);
            ChaosMeta.Init();
            var hub = ChaosHubWindow.OpenAt()!;
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.RequestStop();
            var recap = ChaosRunHost.Overlay!;
            recap.FindControl<Button>("BtnAdjust")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(ChaosRunHost.IsActive);
            Assert.False(recap.IsVisible);
            Assert.Same(hub, ChaosHubWindow.Current);
            Assert.True(hub.FindControl<ScrollViewer>("PanelRun")!.IsVisible);
            hub.Close();
            Assert.Null(ChaosHubWindow.Current);
        }
        finally
        {
            ChaosHubWindow.Current?.Close();
            ChaosRunHost.ForceShutdown();
            ChaosMetaStore.Delete(1);
            CoreSettings.Current.ChaosActiveSlot = slot;
            ChaosMeta.Init();
        }
    });
}
