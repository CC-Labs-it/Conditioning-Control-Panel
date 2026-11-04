using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosEntryTests
{
    private sealed class LogCapture : Serilog.Core.ILogEventSink, System.IDisposable
    {
        private readonly Serilog.ILogger _old = Serilog.Log.Logger;
        internal string Details = "";
        internal LogCapture() => Serilog.Log.Logger = new Serilog.LoggerConfiguration().MinimumLevel.Debug().WriteTo.Sink(this).CreateLogger();
        public void Emit(Serilog.Events.LogEvent e) => Details += e.RenderMessage() + " " + e.Exception + "\n";
        public void Dispose() => Serilog.Log.Logger = _old;
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task BootErrorAndNextAppEntryOpenFirstRunOrHub(bool returning, bool bootError)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            using var log = new LogCapture();
            var previous = ChaosMeta.State;
            ChaosMeta.Init();
            ChaosMeta.State.RunsCompleted = returning ? 1 : 0;
            ChaosMeta.State.SeenIntroGuide = true;
            ChaosMeta.Save();
            ChaosMeta.State.RunsCompleted = returning ? 0 : 1; // stale in-memory state must not choose the door
            CoreEntitlement.HasPremiumProvider = () => true;
            GameWindow.DtrhBootFailedThisSession = !bootError;
            try
            {
                if (bootError)
                {
                    var game = new GameWindow(GameWindow.Games[GameWindow.DtrhId]);
                    game.Show();
                    game.HandleMessage("{\"type\":\"boot-error\",\"msg\":\"gpu\"}");
                    Assert.False(game.IsVisible);
                    Assert.True(GameWindow.DtrhBootFailedThisSession);
                }
                else
                    Assert.Null(GameWindow.Launch(GameWindow.DtrhId));

                if (returning)
                {
                    Assert.True(ChaosHubWindow.Current != null, log.Details);
                    var hub = ChaosHubWindow.Current!;
                    Assert.True(hub.IsVisible);
                    Assert.Null(GameWindow.Launch(GameWindow.DtrhId));
                    Assert.Same(hub, ChaosHubWindow.Current);
                }
                else
                {
                    Assert.True(ChaosRunHost.IsActive, log.Details);
                    Assert.True(ChaosRunHost.State!.Config.ScriptedFirstRun);
                    Assert.Equal(180, ChaosRunHost.State.Config.DurationSec);
                    Assert.Null(GameWindow.Launch(GameWindow.DtrhId));
                    Assert.Null(ChaosHubWindow.Current);
                }
            }
            finally
            {
                ChaosHubWindow.Current?.Close();
                ChaosRunHost.ForceShutdown();
                GameWindow.CloseAllForPanic();
                ChaosMetaStore.Save(previous);
                ChaosMeta.Init();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task PoppingATreatInTheLegacyRunShowsItsWord()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            using var log = new LogCapture();
            ChaosPopText.PlatformCanShow = () => true;
            ChaosPopText.Prepare = (w, r) => { w.Position = r.Position; return true; };
            ChaosRunHost.PopText = null;
            CoreSettings.Current.ChaosAnnouncerEnabled = true;
            try
            {
                ChaosRunHost.StartRun(DtrhRunWire.FirstRunConfig());
                ChaosRunHost.Overlay!.FinishCountdown();
                var spec = new EffectBubbleSpec { VariantId = "flash", Payload = new FlashPayload { Strength = 0 } };
                ChaosRunHost.OnBubblePopped(new AmbientBubble { Spec = spec });
                Dispatcher.UIThread.RunJobs();
                Assert.True(System.Linq.Enumerable.Any(ChaosPopText.Pool.All, w => w.IsVisible && w.LabelText == "FLASH"), log.Details);
            }
            finally { ChaosRunHost.ForceShutdown(); ChaosPopText.ShutdownPool(); }
            return Task.CompletedTask;
        });
    }
}
