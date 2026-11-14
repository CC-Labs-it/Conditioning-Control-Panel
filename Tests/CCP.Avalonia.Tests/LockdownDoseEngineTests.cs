using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Safety;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class LockdownDoseEngineTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task ActivationStartsAnEnabledDoseAndGivesTheEngineBack()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            s.LockdownDoseKeeperEnabled = true;
            s.FlashEnabled = true;
            var count = s.TotalSessions;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                Assert.Contains("starts or restarts the engine", LockdownTabView.LockdownWarning(20, s, false));
                ld.Activate(TimeSpan.FromMinutes(20));
                Assert.False(CoreEngine.IsRunning);
                await Task.Delay(TimeSpan.FromSeconds(3)); // the real 2 second kickoff
                Assert.True(CoreEngine.IsRunning);
                Assert.Equal(count, s.TotalSessions); // system starts are not user sessions
                ld.Deactivate();
                Assert.False(CoreEngine.IsRunning);
            }
            finally { ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit(); }
        });
    }

    [Fact]
    public async Task RestartWaitsFourTicksAndRespectsSessionAndPanicHolds()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            s.LockdownDoseKeeperEnabled = true;
            s.FlashEnabled = true;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                ld.Activate(TimeSpan.FromMinutes(20));
                CoreSession.IsSessionRunningProvider = () => true;
                for (var i = 0; i < 5; i++) shell.EnforceLockdownDoseEngine(false);
                Assert.False(CoreEngine.IsRunning);
                CoreSession.IsSessionRunningProvider = () => false;
                PanicAftermath.ArmQuiet();
                for (var i = 0; i < 5; i++) shell.EnforceLockdownDoseEngine(false);
                Assert.False(CoreEngine.IsRunning);
                PanicAftermath.ReleaseEngineHold();
                for (var i = 0; i < 3; i++) shell.EnforceLockdownDoseEngine(false);
                Assert.False(CoreEngine.IsRunning);
                shell.EnforceLockdownDoseEngine(false);
                Assert.True(CoreEngine.IsRunning);
                CoreSession.IsSessionRunningProvider = () => true;
                ld.Deactivate();
                Assert.True(CoreEngine.IsRunning); // the session now owns it
            }
            finally
            {
                CoreSession.IsSessionRunningProvider = null; PanicAftermath.EndQuiet();
                ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit();
            }
            return Task.CompletedTask;
        });
    }

    // The trunk's portal seam (PortalPanicShortcut.BindForTests) holds the bind; #3975 used its own override.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task KeeperTickDoesNotSupersedeAUserStartWaitingForThePortal(bool keeperFirst, bool exitBeforePortal)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            s.LockdownDoseKeeperEnabled = s.PanicKeyEnabled = s.FlashEnabled = true;
            s.LockdownDisablePanicKey = false;
            var count = s.TotalSessions;
            var bound = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                PortalPanicShortcut.BindForTests = (_, _) => bound.Task;
                ld.Activate(TimeSpan.FromMinutes(20));
                if (keeperFirst) shell.EnforceLockdownDoseEngine(true);   // the keeper's own start waits too
                shell.StartEngineFromUser();
                Assert.False(CoreEngine.IsRunning);
                shell.EnforceLockdownDoseEngine(true);
                for (var i = 0; i < 5; i++) shell.EnforceLockdownDoseEngine(false);
                Assert.False(CoreEngine.IsRunning);
                if (exitBeforePortal) ld.Deactivate();   // the user's start is not the keeper's to take back
                bound.TrySetResult("F8");
                var until = System.Diagnostics.Stopwatch.StartNew();
                while (!CoreEngine.IsRunning && until.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
                Assert.True(CoreEngine.IsRunning);
                Assert.Equal(count + 1, s.TotalSessions);   // the user's start ran, not a system start
                if (!exitBeforePortal) ld.Deactivate();
                Assert.True(CoreEngine.IsRunning);
            }
            finally
            {
                bound.TrySetResult("F8");
                PortalPanicShortcut.BindForTests = null;
                ld.Dispose(); MainShellWindow.StopEngine(); LockdownService.Current = null; shell.RequestExit();
                MainShellWindow.ReleasePortalWhenUnused();
                await PortalPanicShortcut.UnbindAsync();
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [Theory]
    [InlineData("en", "Lockdown starts or restarts the engine while a feature is enabled")]
    [InlineData("de", "Lockdown startet den Motor, solange eine Funktion aktiviert ist, oder startet ihn erneut.")]
    [InlineData("es", "El bloqueo inicia o reinicia el motor mientras haya una función activada.")]
    [InlineData("fr", "Le verrouillage démarre ou redémarre le moteur tant qu'une fonction est activée.")]
    [InlineData("it", "Il lockdown avvia o riavvia il motore quando una funzione è attiva.")]
    [InlineData("ja", "機能が有効な間、ロックダウンはエンジンを起動または再起動します。")]
    [InlineData("ko", "기능이 켜져 있는 동안 락다운이 엔진을 시작하거나 다시 시작합니다.")]
    [InlineData("pt-BR", "O lockdown inicia ou reinicia o motor enquanto houver um recurso ativado.")]
    [InlineData("ru", "Пока включена функция, блокировка запускает или перезапускает движок.")]
    [InlineData("zh-CN", "只要有功能启用，封锁就会启动或重新启动引擎。")]
    public async Task DoseWarningUsesTheSelectedLanguageOnlyWhenEnabled(string language, string text)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var loc = LocalizationManager.Instance;
            var previous = loc.CurrentLanguage;
            try
            {
                loc.SetLanguage(language);
                var s = new ConditioningControlPanel.Models.AppSettings { LockdownDoseKeeperEnabled = true };
                Assert.Contains("- " + text + "\n", LockdownTabView.LockdownWarning(20, s, false));
                s.LockdownDoseKeeperEnabled = false;
                Assert.DoesNotContain("- " + text + "\n", LockdownTabView.LockdownWarning(20, s, false));
            }
            finally { loc.SetLanguage(previous); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task EmptyDoseAndDisabledKeeperStandDownAndPreexistingEngineIsLeftRunning()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            s.FlashEnabled = s.SubliminalEnabled = s.SpiralEnabled = s.PinkFilterEnabled = false;
            s.BouncingTextEnabled = s.BubblesEnabled = s.MandatoryVideosEnabled = s.MindWipeEnabled = false;
            s.LockCardEnabled = s.BubbleCountEnabled = s.BrainDrainEnabled = s.AudioOnlySession = s.PopQuizEnabled = false;
            s.CornerGifOverlays.Clear();
            s.LockdownDoseKeeperEnabled = true;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                ld.Activate(TimeSpan.FromMinutes(20));
                shell.EnforceLockdownDoseEngine(true);
                Assert.False(CoreEngine.IsRunning); // no feature selection on this head yet (#4061 re-cut)
                ld.Deactivate();
                s.FlashEnabled = true;
                s.LockdownDoseKeeperEnabled = false;
                Assert.DoesNotContain("starts or restarts the engine", LockdownTabView.LockdownWarning(20, s, false));
                ld.Activate(TimeSpan.FromMinutes(20));
                shell.EnforceLockdownDoseEngine(true);
                Assert.False(CoreEngine.IsRunning);
                ld.Deactivate();
                s.LockdownDoseKeeperEnabled = true;
                shell.StartEngine();
                Assert.True(CoreEngine.IsRunning);
                ld.Activate(TimeSpan.FromMinutes(20));
                MainShellWindow.StopEngine();
                shell.EnforceLockdownDoseEngine(true); // keeper restarts an engine that predated lockdown
                Assert.True(CoreEngine.IsRunning);
                ld.Deactivate();
                Assert.True(CoreEngine.IsRunning);
            }
            finally { ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit(); }
            return Task.CompletedTask;
        });
    }
}
