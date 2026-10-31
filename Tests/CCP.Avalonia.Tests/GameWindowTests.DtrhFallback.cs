using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // WPF DtrhHostService.cs:923-954: only a failed boot takes this path.
    [Theory]
    [InlineData(0, true)]
    [InlineData(4, true)]
    [InlineData(4, false)]
    public void Dtrh_boot_failure_closes_web_then_opens_first_run_or_owned_single_hub(int runs, bool visibleOwner) =>
        RunDtrh(lab: true, (shell, _, _, _) =>
        {
            ChaosMetaStore.Save(new ChaosMetaState { RunsCompleted = runs }, ChaosMetaStore.ActiveSlot);
            ChaosMeta.State.RunsCompleted = runs == 0 ? 4 : 0; // route from disk, not stale memory
            var oldOwner = ChaosHubWindow.OwnerWindow;
            ChaosHubWindow.OwnerWindow = () => shell;
            if (visibleOwner) shell.Show();
            try
            {
                var web = GameWindow.Launch("dtrh")!;
                Assert.Null(ChaosHubWindow.Current);
                Assert.False(ChaosRunHost.IsActive);
                web.HandleMessage("{\"type\":\"boot-error\"}");
                web.HandleMessage("{\"type\":\"boot-error\"}"); // duplicate must not enqueue a second door
                Assert.False(web.IsVisible);
                Assert.Null(ChaosHubWindow.Current); // teardown precedes fallback on the dispatcher
                Dispatcher.UIThread.RunJobs();
                if (runs == 0)
                {
                    Assert.True(ChaosRunHost.IsActive);
                    Assert.True(ChaosRunHost.State!.Config.ScriptedFirstRun);
                    Assert.True(ChaosRunHost.Overlay!.FindControl<Viewbox>("CountdownBox")!.IsVisible);
                    Assert.Null(ChaosHubWindow.Current);
                    ChaosHubWindow.OpenAt(); // an active run refuses a hub
                    Assert.Null(ChaosHubWindow.Current);
                }
                else
                {
                    var hub = ChaosHubWindow.Current!;
                    Assert.NotNull(hub);
                    Assert.True(hub.IsVisible);
                    if (visibleOwner) Assert.Same(shell, hub.Owner);
                    else Assert.Null(hub.Owner);
                    Assert.False(ChaosRunHost.IsActive);
                    var recap = ChaosOverlayWindow.ForRun();
                    recap.Show();
                    recap.FindControl<Button>("BtnAdjust")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.False(recap.IsVisible);
                    Dispatcher.UIThread.RunJobs();
                    Assert.Same(hub, ChaosHubWindow.Current);
                    Assert.True(hub.FindControl<ScrollViewer>("PanelRun")!.IsVisible);
                    hub.Close();
                    Assert.Null(ChaosHubWindow.Current);
                    ChaosHubWindow.OpenAt();
                    Assert.NotSame(hub, ChaosHubWindow.Current);
                    Assert.True(ChaosHubWindow.Current!.IsVisible);
                }
            }
            finally
            {
                ChaosHubWindow.Current?.Close();
                ChaosRunHost.ForceShutdown();
                ChaosHubWindow.OwnerWindow = oldOwner;
            }
        });

    [Fact]
    public void Dtrh_queued_fallback_does_not_interrupt_a_new_run() => RunDtrh(lab: true, (_, _, _, _) =>
    {
        ChaosMetaStore.Save(new ChaosMetaState { RunsCompleted = 4 }, ChaosMetaStore.ActiveSlot);
        ChaosMeta.State.RunsCompleted = 4;
        var meta = ChaosMeta.State;
        try
        {
            var web = GameWindow.Launch("dtrh")!;
            web.HandleMessage("{\"type\":\"boot-error\"}");
            ChaosRunHost.StartRun(new ChaosRunConfig());
            var overlay = ChaosRunHost.Overlay;
            Dispatcher.UIThread.RunJobs();
            Assert.True(ChaosRunHost.IsActive);
            Assert.Same(overlay, ChaosRunHost.Overlay);
            Assert.Same(meta, ChaosMeta.State); // a queued door must not reload a live run's profile
            Assert.Null(ChaosHubWindow.Current);
        }
        finally { ChaosRunHost.ForceShutdown(); }
    });
}
