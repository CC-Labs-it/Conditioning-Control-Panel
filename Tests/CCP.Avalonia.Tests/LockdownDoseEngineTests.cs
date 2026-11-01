using System;
using System.Threading.Tasks;
using System.Linq;
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

    [Fact]
    public async Task AUserStartWhileTheKeeperWaitsForThePortalIsNotTakenBack()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            s.LockdownDoseKeeperEnabled = s.PanicKeyEnabled = s.FlashEnabled = true;
            s.LockdownDisablePanicKey = false;
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var allowed = typeof(MainShellWindow).GetField("_portalAllowed", flags)!;
            var failed = typeof(MainShellWindow).GetField("_portalFailed", flags)!;
            var bind = typeof(MainShellWindow).GetField("_portalBind", flags)!;
            var oldAllowed = allowed.GetValue(null); var oldFailed = failed.GetValue(null); var oldBind = bind.GetValue(null);
            var oldWayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            var pending = new TaskCompletionSource();
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "test");
                allowed.SetValue(null, true); failed.SetValue(null, false); bind.SetValue(null, pending.Task);
                ld.Activate(TimeSpan.FromMinutes(20));
                shell.EnforceLockdownDoseEngine(true);
                Assert.False(CoreEngine.IsRunning);
                PortalPanicShortcut.SetSessionForTests("/test/session", null);
                shell.StartEngineFromUser();
                Assert.True(CoreEngine.IsRunning);
                ld.Deactivate();
                Assert.True(CoreEngine.IsRunning);
                pending.SetResult();
                Dispatcher.UIThread.RunJobs();
                Assert.True(CoreEngine.IsRunning);
            }
            finally
            {
                PortalPanicShortcut.SetSessionForTests(null, null);
                allowed.SetValue(null, oldAllowed); failed.SetValue(null, oldFailed); bind.SetValue(null, oldBind);
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", oldWayland);
                ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit();
            }
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task KeeperTickDoesNotSupersedeAUserStartWaitingForThePortal(bool keeperFirst, bool exitBeforePortal)
    {
        Assert.SkipWhen(!OperatingSystem.IsLinux(), "The GlobalShortcuts portal is Linux-only.");
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            s.LockdownDoseKeeperEnabled = s.PanicKeyEnabled = s.FlashEnabled = true;
            s.LockdownDisablePanicKey = false;
            var count = s.TotalSessions;
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var allowed = typeof(MainShellWindow).GetField("_portalAllowed", flags)!;
            var failed = typeof(MainShellWindow).GetField("_portalFailed", flags)!;
            var bind = typeof(MainShellWindow).GetField("_portalBind", flags)!;
            var oldAllowed = allowed.GetValue(null); var oldFailed = failed.GetValue(null); var oldBind = bind.GetValue(null);
            var oldWayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
            var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "test");
                PortalPanicShortcut.SetSessionForTests(null, null);
                allowed.SetValue(null, true); failed.SetValue(null, false); bind.SetValue(null, pending.Task);
                ld.Activate(TimeSpan.FromMinutes(20));
                if (keeperFirst) shell.EnforceLockdownDoseEngine(true);
                shell.StartEngineFromUser();
                Assert.False(CoreEngine.IsRunning);
                shell.EnforceLockdownDoseEngine(true);
                for (var i = 0; i < 4; i++) shell.EnforceLockdownDoseEngine(false);
                Assert.False(CoreEngine.IsRunning);
                if (exitBeforePortal) ld.Deactivate();
                pending.SetResult();
                var until = System.Diagnostics.Stopwatch.StartNew();
                while (!CoreEngine.IsRunning && until.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(20);
                Assert.True(CoreEngine.IsRunning);
                Assert.Equal(count + 1, s.TotalSessions);
                if (!exitBeforePortal) ld.Deactivate();
                Assert.True(CoreEngine.IsRunning);
            }
            finally
            {
                pending.TrySetResult();
                ld.Dispose(); MainShellWindow.StopEngine(); LockdownService.Current = null; shell.RequestExit();
                PortalPanicShortcut.SetSessionForTests(null, null);
                allowed.SetValue(null, oldAllowed); failed.SetValue(null, oldFailed); bind.SetValue(null, oldBind);
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", oldWayland);
            }
        });
    }

    [Theory]
    [InlineData("en", "Lockdown starts or restarts the engine, picks features when none are on, and switches them back on if you turn them all off.")]
    [InlineData("de", "Lockdown startet oder startet den Motor erneut, wählt Funktionen, wenn keine aktiv ist, und schaltet sie wieder ein, wenn du alle ausschaltest.")]
    [InlineData("es", "El bloqueo inicia o reinicia el motor, elige funciones si no hay ninguna activa y las vuelve a activar si las apagas todas.")]
    [InlineData("fr", "Le verrouillage démarre ou redémarre le moteur, choisit des fonctions si aucune n'est active et les réactive si vous les désactivez toutes.")]
    [InlineData("it", "Il lockdown avvia o riavvia il motore, sceglie funzioni se nessuna è attiva e le riattiva se le disattivi tutte.")]
    [InlineData("ja", "ロックダウンはエンジンを起動または再起動し、機能がなければ選び、すべてオフにすると再びオンにします。")]
    [InlineData("ko", "락다운은 엔진을 시작하거나 다시 시작하고 켜진 기능이 없으면 기능을 선택하며 모두 끄면 다시 켭니다.")]
    [InlineData("pt-BR", "O lockdown inicia ou reinicia o motor, escolhe recursos se nenhum estiver ativo e os reativa se você desligar todos.")]
    [InlineData("ru", "Блокировка запускает или перезапускает движок, выбирает функции, если они выключены, и снова включает их, если вы выключите все.")]
    [InlineData("zh-CN", "封锁会启动或重启引擎，在没有启用的功能时选择功能，并在全部关闭后重新开启。")]
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
                Assert.True(CoreEngine.IsRunning);
                Assert.Equal(2, new[] { s.FlashEnabled, s.SubliminalEnabled, s.SpiralEnabled, s.PinkFilterEnabled,
                    s.BouncingTextEnabled, s.BubblesEnabled }.Count(on => on));
                ld.Deactivate();
                Assert.False(CoreEngine.IsRunning);
                Assert.False(s.FlashEnabled || s.SubliminalEnabled || s.SpiralEnabled || s.PinkFilterEnabled
                    || s.BouncingTextEnabled || s.BubblesEnabled);
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
    private static void EmptyDose(ConditioningControlPanel.Models.AppSettings s)
    {
        s.FlashEnabled = s.SubliminalEnabled = s.SpiralEnabled = s.PinkFilterEnabled = false;
        s.BouncingTextEnabled = s.BubblesEnabled = s.MandatoryVideosEnabled = s.MindWipeEnabled = false;
        s.LockCardEnabled = s.BubbleCountEnabled = s.BrainDrainEnabled = s.AudioOnlySession = s.PopQuizEnabled = false;
        s.CornerGifOverlays.Clear();
        s.AutonomyModeEnabled = false;
    }

    private static int StarterCount(ConditioningControlPanel.Models.AppSettings s) =>
        new[] { s.FlashEnabled, s.SubliminalEnabled, s.SpiralEnabled, s.PinkFilterEnabled,
            s.BouncingTextEnabled, s.BubblesEnabled }.Count(on => on);

    [Fact]
    public async Task EmptyEdgeWaitsForSessionThenRefillsWithShrinkingGraceAndRestoresSnapshot()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            EmptyDose(s); s.FlashEnabled = s.LockdownDoseKeeperEnabled = s.LockdownTripwiresEnabled = true;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow(); shell.Show();
            var attempts = 0;
            ld.EscapeAttempted += a => { if (a.Kind == ConditioningControlPanel.Services.Possession.EscapeKinds.Starve) attempts++; };
            try
            {
                shell.StartEngine();
                ld.Activate(TimeSpan.FromMinutes(20));
                // Drive ticks explicitly here; the real kickoff has its own timed test above.
                ((DispatcherTimer)typeof(MainShellWindow).GetField("_doseEngineKickoff",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(shell)!).Stop();
                CoreSession.IsSessionRunningProvider = () => true;
                s.FlashEnabled = false;
                for (var i = 0; i < 8; i++) shell.EnforceLockdownDoseEngine(false);
                Assert.Equal(0, attempts);
                Assert.Equal(0, StarterCount(s));
                CoreSession.IsSessionRunningProvider = () => false;
                for (var i = 0; i < 5; i++) shell.EnforceLockdownDoseEngine(false);
                Assert.Equal(1, attempts);
                Assert.Equal(0, StarterCount(s));
                shell.EnforceLockdownDoseEngine(false);
                Assert.Equal(2, StarterCount(s));
                Assert.True(s.FlashEnabled); // the user's activation choice is picked first
                shell.EnforceLockdownDoseEngine(false); // observe the dose before it goes empty again
                EmptyDose(s);
                for (var i = 0; i < 3; i++) shell.EnforceLockdownDoseEngine(false);
                Assert.Equal(2, attempts);
                Assert.Equal(0, StarterCount(s));
                shell.EnforceLockdownDoseEngine(false);
                Assert.Equal(3, StarterCount(s));
                Assert.True(s.FlashEnabled);
                shell.EnforceLockdownDoseEngine(false);
                EmptyDose(s);
                shell.EnforceLockdownDoseEngine(false);
                Assert.Equal(0, StarterCount(s));
                shell.EnforceLockdownDoseEngine(false);
                Assert.Equal(4, StarterCount(s));
                Assert.True(s.FlashEnabled);
                ld.Deactivate();
                Assert.Equal(1, StarterCount(s));
                Assert.True(s.FlashEnabled);
                Assert.True(CoreEngine.IsRunning); // engine was already running at activation
            }
            finally
            {
                CoreSession.IsSessionRunningProvider = null;
                ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task InterruptedDoseRecoversOnlyBorrowedTogglesBeforeNextStart()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            EmptyDose(s); s.LockdownDoseKeeperEnabled = true;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow(); shell.Show();
            var path = System.IO.Path.Combine(CorePaths.UserData, "lockdown-dose.json");
            try
            {
                ld.Activate(TimeSpan.FromMinutes(20));
                shell.EnforceLockdownDoseEngine(true);
                Assert.Equal(2, StarterCount(s));
                Assert.True(System.IO.File.Exists(path));
                s.PopQuizEnabled = true; // never borrowed
                MainShellWindow.RecoverLockdownDose();
                Assert.Equal(0, StarterCount(s));
                Assert.True(s.PopQuizEnabled);
                Assert.False(System.IO.File.Exists(path));
            }
            finally { ld.Deactivate(); ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit(); }
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("clip.mp4", true)]
    [InlineData("clip.ccpenh.json", false)]
    public async Task PreviousVideoIsPickedOnlyWhenPlayableAssetsExist(string filename, bool playable)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            EmptyDose(s); s.MandatoryVideosEnabled = s.LockdownDoseKeeperEnabled = true;
            var dir = System.IO.Path.Combine(CorePaths.UserData, "dose-assets-" + Guid.NewGuid());
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, "videos", "category"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "videos", "category", filename), "");
            var previous = CorePaths.EffectiveAssetsProvider;
            CorePaths.EffectiveAssetsProvider = () => dir;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow(); shell.Show();
            try
            {
                ld.Activate(TimeSpan.FromMinutes(20));
                s.MandatoryVideosEnabled = false;
                shell.EnforceLockdownDoseEngine(true);
                Assert.Equal(playable, s.MandatoryVideosEnabled);
                Assert.Equal(playable ? 1 : 2, StarterCount(s));
                ld.Deactivate();
                Assert.Equal(playable, s.MandatoryVideosEnabled); // only a picked prior choice returns
                Assert.Equal(0, StarterCount(s));
            }
            finally
            {
                ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit();
                CorePaths.EffectiveAssetsProvider = previous;
                System.IO.Directory.Delete(dir, true);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task PanicHoldDoesNotPickFeaturesOrRestartAnEmptyDose()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            EmptyDose(s); s.LockdownDoseKeeperEnabled = true;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow(); shell.Show();
            try
            {
                ld.Activate(TimeSpan.FromMinutes(20));
                PanicAftermath.ArmQuiet();
                shell.EnforceLockdownDoseEngine(true);
                for (var i = 0; i < 5; i++) shell.EnforceLockdownDoseEngine(false);
                Assert.False(CoreEngine.IsRunning);
                Assert.Equal(0, StarterCount(s));
                PanicAftermath.ReleaseEngineHold();
                shell.EnforceLockdownDoseEngine(true);
                Assert.Equal(2, StarterCount(s));
                Assert.True(CoreEngine.IsRunning);
                ld.Deactivate();
            }
            finally { PanicAftermath.EndQuiet(); ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit(); }
            return Task.CompletedTask;
        });
    }

}
