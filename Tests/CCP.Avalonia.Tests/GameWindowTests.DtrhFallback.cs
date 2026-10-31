using System;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // WPF DtrhHostService.cs:924-953: boot-error closes the web host, then first run
    // or one reusable classic hub. Launch (:140-195) retries the web engine next time.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Dtrh_BootFailure_ReachesClassicRunOrHub_AndKeepsGate(int completed)
    {
        RunDtrh(lab: true, (shell, _, _, denied) =>
        {
            var oldRendering = global::ConditioningControlPanel.Avalonia.RenderProof.Rendering;
            var oldFailed = GameWindow.DtrhBootFailedThisSession;
            var oldCompleted = ChaosMeta.State.RunsCompleted;
            var oldComposited = ChaosSkiaFxOverlay.Composited;
            var oldEnabled = CoreSettings.Current.ChaosSkiaFxEnabled;
            try
            {
                global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
                GameWindow.DtrhBootFailedThisSession = false;
                ChaosMeta.State.RunsCompleted = completed;
                CoreSettings.Current.ChaosSkiaFxEnabled = true;
                ChaosSkiaFxOverlay.Composited = () => true;
                ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
                Assert.True(ExclusivesTabView.IsOnThisBuild("dtrh"));
                shell.Show(); // the Premium shelf owns the sign-in dialog
                Dispatcher.UIThread.RunJobs();
                CoreAccount.IsLoggedInProvider = () => false;
                shell.OpenExclusiveFeature("dtrh");
                Assert.Null(GameWindow.OpenForTests("dtrh"));
                Dispatcher.UIThread.RunJobs();
                var login = Assert.Single(shell.OwnedWindows.OfType<LoginDialog>());
                login.Close();
                CoreAccount.IsLoggedInProvider = () => true;
                shell.OpenExclusiveFeature("dtrh");
                var web = GameWindow.OpenForTests("dtrh");
                Assert.NotNull(web);
                Assert.False(ChaosRunHost.IsActive);
                Assert.Null(GameWindow.DtrhClassicHub);
                web.HandleMessage("{\"type\":\"boot-error\",\"msg\":\"renderer unavailable\"}");
                Assert.Null(GameWindow.OpenForTests("dtrh"));
                Assert.True(GameWindow.DtrhBootFailedThisSession);
                if (completed == 0)
                {
                    Assert.True(ChaosRunHost.IsActive);
                    Assert.Equal(180, ChaosRunHost.State!.Config.DurationSec);
                    var overlay = ChaosRunHost.Overlay;
                    Assert.NotNull(overlay);
                    overlay.FinishCountdown();
                    Assert.NotNull(ChaosSkiaFxOverlay.Active); // actual classic FX path
                    var retry = GameWindow.Launch("DTRH");
                    Assert.NotNull(retry);
                    Assert.NotSame(web, retry);
                    Assert.True(retry.IsVisible);
                    Assert.Same(overlay, ChaosRunHost.Overlay);
                    retry.HandleMessage("{\"type\":\"boot-error\"}");
                    Assert.Same(overlay, ChaosRunHost.Overlay);
                    ChaosRunHost.ForceShutdown();
                    Assert.Null(ChaosSkiaFxOverlay.Active);
                }
                else
                {
                    var hub = GameWindow.DtrhClassicHub;
                    Assert.NotNull(hub);
                    Assert.True(hub.IsVisible);
                    var retry = GameWindow.Launch("DTRH");
                    Assert.NotNull(retry);
                    Assert.NotSame(web, retry);
                    Assert.True(retry.IsVisible);
                    retry.HandleMessage("{\"type\":\"boot-error\"}");
                    Assert.Same(hub, GameWindow.DtrhClassicHub);
                    hub.Close();
                    Assert.Null(GameWindow.DtrhClassicHub);
                    // A hub opened by the recap must be reused by the launcher too.
                    var recapHub = new ChaosHubWindow();
                    recapHub.Show();
                    var recapRetry = GameWindow.Launch("DTRH");
                    Assert.NotNull(recapRetry);
                    recapRetry.HandleMessage("{\"type\":\"boot-error\"}");
                    Assert.Same(recapHub, GameWindow.DtrhClassicHub);
                    Assert.Null(GameWindow.OpenForTests("dtrh"));
                    recapHub.Close();
                }
                Assert.Null(GameWindow.OpenForTests("dtrh"));
                // Retrying the web game still requires entitlement.
                CoreEntitlement.HasLabProvider = () => false;
                LauncherWindow.Destinations["dtrh"].Open(shell);
                Assert.NotEmpty(denied);
                Assert.False(ChaosRunHost.IsActive);
                Assert.Null(GameWindow.DtrhClassicHub);
                // A late duplicate message from the disposed web host cannot resurrect a game.
                web.HandleMessage("{\"type\":\"boot-error\"}");
                Assert.False(ChaosRunHost.IsActive);
                Assert.Null(GameWindow.DtrhClassicHub);
            }
            finally
            {
                GameWindow.DtrhClassicHub?.Close();
                ChaosRunHost.ForceShutdown();
                ChaosSkiaFxOverlay.CloseActive();
                ChaosSkiaFxOverlay.Composited = oldComposited;
                ChaosSkiaFxOverlay.SkipPlatformChecksForTests = false;
                CoreSettings.Current.ChaosSkiaFxEnabled = oldEnabled;
                ChaosMeta.State.RunsCompleted = oldCompleted;
                GameWindow.DtrhBootFailedThisSession = oldFailed;
                global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = oldRendering;
            }
        });
    }
}
