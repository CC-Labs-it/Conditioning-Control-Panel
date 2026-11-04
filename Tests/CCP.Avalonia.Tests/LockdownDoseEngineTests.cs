using System;
using System.Threading.Tasks;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Remote;
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

    private static MainShellWindow NewDoseShell() => new() { DoseOverlaySupportForTests = true, DoseVideoReadyForTests = true };
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
            var shell = NewDoseShell();
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
            var shell = NewDoseShell();
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
            var shell = NewDoseShell();
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
            var shell = NewDoseShell();
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
    [InlineData("en", "When available, Lockdown starts or restarts the engine and picks features if all are off.")]
    [InlineData("de", "Wenn Funktionen verfügbar sind, startet Lockdown den Motor und wählt Funktionen, falls alle aus sind.")]
    [InlineData("es", "Cuando hay funciones disponibles, el bloqueo inicia o reinicia el motor y elige funciones si todas están apagadas.")]
    [InlineData("fr", "Si des fonctions sont disponibles, le verrouillage démarre ou redémarre le moteur et en choisit si toutes sont désactivées.")]
    [InlineData("it", "Se sono disponibili funzioni, il lockdown avvia o riavvia il motore e ne sceglie alcune se sono tutte disattivate.")]
    [InlineData("ja", "利用できる機能がある場合、ロックダウンはエンジンを起動または再起動し、すべてオフなら機能を選びます。")]
    [InlineData("ko", "사용 가능한 기능이 있으면 락다운은 엔진을 시작하거나 다시 시작하고 모든 기능이 꺼져 있으면 기능을 선택합니다.")]
    [InlineData("pt-BR", "Quando há recursos disponíveis, o lockdown inicia ou reinicia o motor e escolhe recursos se todos estiverem desligados.")]
    [InlineData("ru", "Если доступны функции, блокировка запускает или перезапускает движок и выбирает функции, когда все выключены.")]
    [InlineData("zh-CN", "有可用功能时，封锁会启动或重启引擎，并在所有功能关闭时选择功能。")]
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
            var shell = NewDoseShell();
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
    private static void EmptyDose(ConditioningControlPanel.Models.AppSettings s) {
        s.FlashEnabled = s.SubliminalEnabled = s.SpiralEnabled = s.PinkFilterEnabled = false;
        s.BouncingTextEnabled = s.BubblesEnabled = s.MandatoryVideosEnabled = s.MindWipeEnabled = false;
        s.LockCardEnabled = s.BubbleCountEnabled = s.BrainDrainEnabled = s.AudioOnlySession = s.PopQuizEnabled = false; s.CornerGifOverlays.Clear(); s.AutonomyModeEnabled = false;
    }
    private static int StarterCount(ConditioningControlPanel.Models.AppSettings s) =>
        new[] { s.FlashEnabled, s.SubliminalEnabled, s.SpiralEnabled, s.PinkFilterEnabled,
            s.BouncingTextEnabled, s.BubblesEnabled }.Count(on => on);
    [Fact]
    public async Task EmptyEdgeWaitsForSessionThenRefillsWithShrinkingGraceAndRestoresSnapshot() {
        await AvaloniaTestDispatcher.RunAsync(() => {
            EnsureApp();
            var s = CoreSettings.Current;
            EmptyDose(s); s.FlashEnabled = s.LockdownDoseKeeperEnabled = s.LockdownTripwiresEnabled = true;
            var ld = LockdownService.Current = new LockdownService(); var shell = NewDoseShell(); shell.Show(); var attempts = 0;
            ld.EscapeAttempted += a => { if (a.Kind == ConditioningControlPanel.Services.Possession.EscapeKinds.Starve) attempts++; };
            try {
                shell.StartEngine(); ld.Activate(TimeSpan.FromMinutes(20));
                // Drive ticks explicitly here; the real kickoff has its own timed test above.
                ((DispatcherTimer)typeof(MainShellWindow).GetField("_doseEngineKickoff",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(shell)!).Stop();
                CoreSession.IsSessionRunningProvider = () => true;
                s.FlashEnabled = false;
                for (var i = 0; i < 8; i++) shell.EnforceLockdownDoseEngine(false);
                Assert.Equal(0, attempts); Assert.Equal(0, StarterCount(s));
                CoreSession.IsSessionRunningProvider = () => false;
                for (var i = 0; i < 5; i++) shell.EnforceLockdownDoseEngine(false);
                Assert.Equal(1, attempts); Assert.Equal(0, StarterCount(s)); shell.EnforceLockdownDoseEngine(false); Assert.Equal(2, StarterCount(s));
                Assert.True(s.FlashEnabled); // the user's activation choice is picked first
                shell.EnforceLockdownDoseEngine(false); // observe the dose before it goes empty again
                EmptyDose(s);
                for (var i = 0; i < 3; i++) shell.EnforceLockdownDoseEngine(false);
                Assert.Equal(2, attempts); Assert.Equal(0, StarterCount(s)); shell.EnforceLockdownDoseEngine(false); Assert.Equal(3, StarterCount(s));
                Assert.True(s.FlashEnabled); shell.EnforceLockdownDoseEngine(false);
                EmptyDose(s);
                shell.EnforceLockdownDoseEngine(false); Assert.Equal(0, StarterCount(s)); shell.EnforceLockdownDoseEngine(false); Assert.Equal(4, StarterCount(s));
                Assert.True(s.FlashEnabled); ld.Deactivate(); Assert.Equal(1, StarterCount(s)); Assert.True(s.FlashEnabled);
                Assert.True(CoreEngine.IsRunning); // engine was already running at activation
            } finally {
                CoreSession.IsSessionRunningProvider = null;
                ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit();
            }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task InterruptedDoseRecoversOnlyBorrowedTogglesBeforeNextStart() {
        await AvaloniaTestDispatcher.RunAsync(() => {
            EnsureApp();
            var s = CoreSettings.Current;
            EmptyDose(s); s.LockdownDoseKeeperEnabled = true;
            var ld = LockdownService.Current = new LockdownService(); var shell = NewDoseShell(); shell.Show();
            var path = System.IO.Path.Combine(CorePaths.UserData, "lockdown-dose.json");
            try {
                ld.Activate(TimeSpan.FromMinutes(20)); shell.EnforceLockdownDoseEngine(true); Assert.Equal(2, StarterCount(s)); Assert.True(System.IO.File.Exists(path));
                s.PopQuizEnabled = true; // never borrowed
                MainShellWindow.RecoverLockdownDose();
                Assert.Equal(0, StarterCount(s)); Assert.True(s.PopQuizEnabled); Assert.False(System.IO.File.Exists(path));
            } finally { ld.Deactivate(); ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit(); }
            return Task.CompletedTask;
        });
    }
    [Theory]
    [InlineData("clip.mp4", true, false)]
    [InlineData("clip.m4v", true, false)]
    [InlineData("clip.mp4", false, true)]
    [InlineData("clip.ccpenh.json", false, false)]
    public async Task PreviousVideoIsPickedOnlyWhenPlayableAssetsExist(string filename, bool playable, bool excluded) {
        await AvaloniaTestDispatcher.RunAsync(() => {
            EnsureApp();
            var s = CoreSettings.Current;
            EmptyDose(s); s.MandatoryVideosEnabled = s.LockdownDoseKeeperEnabled = true;
            var dir = System.IO.Path.Combine(CorePaths.UserData, "dose-assets-" + Guid.NewGuid());
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, "videos", "category"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "videos", "category", filename), "");
            if (excluded) s.DisabledAssetPaths.Add("videos/category/" + filename);
            var previous = CorePaths.EffectiveAssetsProvider; var previousVideo = CoreEngine.Video;
            CorePaths.EffectiveAssetsProvider = () => dir;
            CoreEngine.Video = MandatoryVideoOverlay.Instance.Scheduler; var ld = LockdownService.Current = new LockdownService(); var shell = NewDoseShell(); shell.Show();
            try {
                ld.Activate(TimeSpan.FromMinutes(20)); s.MandatoryVideosEnabled = false; shell.EnforceLockdownDoseEngine(true);
                Assert.Equal(playable, s.MandatoryVideosEnabled); Assert.Equal(playable ? 1 : 2, StarterCount(s)); ld.Deactivate();
                Assert.Equal(playable, s.MandatoryVideosEnabled); // only a picked prior choice returns
                Assert.Equal(0, StarterCount(s));
            } finally {
                ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit();
                CorePaths.EffectiveAssetsProvider = previous;
                CoreEngine.Video = previousVideo;
                System.IO.Directory.Delete(dir, true);
            }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task PanicHoldDoesNotPickFeaturesOrRestartAnEmptyDose() {
        await AvaloniaTestDispatcher.RunAsync(() => {
            EnsureApp();
            var s = CoreSettings.Current;
            EmptyDose(s); s.LockdownDoseKeeperEnabled = true;
            var ld = LockdownService.Current = new LockdownService(); var shell = NewDoseShell(); shell.Show();
            try {
                ld.Activate(TimeSpan.FromMinutes(20)); PanicAftermath.ArmQuiet(); shell.EnforceLockdownDoseEngine(true);
                for (var i = 0; i < 5; i++) shell.EnforceLockdownDoseEngine(false);
                Assert.False(CoreEngine.IsRunning); Assert.Equal(0, StarterCount(s)); PanicAftermath.ReleaseEngineHold(); shell.EnforceLockdownDoseEngine(true);
                Assert.Equal(2, StarterCount(s)); Assert.True(CoreEngine.IsRunning); ld.Deactivate();
            } finally { PanicAftermath.EndQuiet(); ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit(); }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task UnsupportedOverlayBackendWaitsForRunnableVideo() {
        await AvaloniaTestDispatcher.RunAsync(() => {
            EnsureApp();
            var s = CoreSettings.Current;
            EmptyDose(s); s.SpiralEnabled = s.PinkFilterEnabled = s.LockdownDoseKeeperEnabled = true;
            var dir = System.IO.Path.Combine(CorePaths.UserData, "dose-wayland-" + Guid.NewGuid());
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, "videos"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "videos", "clip.m4v"), "");
            var previousAssets = CorePaths.EffectiveAssetsProvider; var previousVideo = CoreEngine.Video;
            CorePaths.EffectiveAssetsProvider = () => dir; CoreEngine.Video = null;
            var ld = LockdownService.Current = new LockdownService();
            var shell = new MainShellWindow { DoseVideoReadyForTests = false }; shell.Show(); // native Wayland has no overlay backend
            try {
                ld.Activate(TimeSpan.FromMinutes(20)); shell.EnforceLockdownDoseEngine(true); Assert.False(CoreEngine.IsRunning);
                Assert.False(s.MandatoryVideosEnabled || s.FlashEnabled || s.SubliminalEnabled || s.BubblesEnabled || s.BouncingTextEnabled);
                Assert.True(s.SpiralEnabled && s.PinkFilterEnabled); // pre-existing but not runnable
                CoreEngine.Video = MandatoryVideoOverlay.Instance.Scheduler;
                shell.EnforceLockdownDoseEngine(true); Assert.False(CoreEngine.IsRunning); // host unavailable
                shell.DoseVideoReadyForTests = true; s.DisabledAssetPaths.Add("videos/clip.m4v");
                shell.EnforceLockdownDoseEngine(true); Assert.False(CoreEngine.IsRunning); // excluded
                s.DisabledAssetPaths.Remove("videos/clip.m4v"); shell.EnforceLockdownDoseEngine(true); Assert.True(s.MandatoryVideosEnabled);
                Assert.True(CoreEngine.IsRunning); ld.Deactivate(); Assert.False(s.MandatoryVideosEnabled); Assert.True(s.SpiralEnabled && s.PinkFilterEnabled);
                s.MandatoryVideosEnabled = true; ld.Activate(TimeSpan.FromMinutes(20));
                shell.EnforceLockdownDoseEngine(true); Assert.True(CoreEngine.IsRunning); // video-only census
                ld.Deactivate(); System.IO.File.Delete(System.IO.Path.Combine(dir, "videos", "clip.m4v")); var packDir = System.IO.Path.Combine(dir, ".packs", "test", "content");
                System.IO.Directory.CreateDirectory(packDir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, ".packs", "test", ".manifest.enc"), "");
                System.IO.File.WriteAllText(System.IO.Path.Combine(packDir, "v.enc"), "");
                var store = new ContentPackStore(() => dir, () => s, () => { },
                    loadManifest: _ => "{\"PackId\":\"p1\",\"Files\":[{\"OriginalName\":\"v.mp4\",\"ObfuscatedName\":\"v.enc\",\"FileType\":\"video\"}]}",
                    decryptFile: _ => new byte[] { 1 });
                CoreEngine.Video = new MandatoryVideoScheduler(null!, library: () => Array.Empty<string>())
                    { PackVideos = new PackMediaPool(ContentPackStore.VideoType, store: () => store) };
                Assert.True(System.Threading.SpinWait.SpinUntil(CoreEngine.Video.HasPlayableContent, TimeSpan.FromSeconds(10)));
                ld.Activate(TimeSpan.FromMinutes(20)); shell.EnforceLockdownDoseEngine(true);
                Assert.True(CoreEngine.IsRunning); Assert.True(s.MandatoryVideosEnabled); // pack-only video
                ld.Deactivate();
            } finally {
                ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit();
                CoreEngine.Video = previousVideo; CorePaths.EffectiveAssetsProvider = previousAssets;
                System.IO.Directory.Delete(dir, true);
            }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public void RemoteAndDoseRecoveryRestoreTheOriginalValueInOrder() {
        var s = CoreSettings.Current;
        s.SpiralEnabled = false; // controller wrote off over a Dose-borrowed on
        var remote = RemoteHoldings.Take(f => f == RemoteHeld.Spiral ? 1 : RemoteHoldings.Read(s, f), 1);
        remote.NoteWrite(RemoteHeld.Spiral, 1, 0);
        RemoteHoldings.Save(CorePaths.UserData, remote);
        var dosePath = System.IO.Path.Combine(CorePaths.UserData, "lockdown-dose.json"); System.IO.File.WriteAllText(dosePath, "{\"Flipped\":[\"spiral\"]}");
        global::ConditioningControlPanel.Avalonia.App.RecoverRemoteAndDose();
        Assert.False(s.SpiralEnabled); Assert.False(System.IO.File.Exists(dosePath));
        Assert.False(System.IO.File.Exists(System.IO.Path.Combine(CorePaths.UserData, RemoteHoldings.FileName)));
        System.IO.File.WriteAllText(dosePath, "{"); MainShellWindow.RecoverLockdownDose();
        Assert.True(System.IO.File.Exists(dosePath)); // unreadable debt remains for retry
        System.IO.File.Delete(dosePath);
    }
    [Fact]
    public async Task MalformedRecoveryBlocksActivationAndShowsError() {
        await AvaloniaTestDispatcher.RunAsync(() => {
            EnsureApp(); var old = CoreEntitlement.HasPremiumProvider; CoreEntitlement.HasPremiumProvider = () => true;
            var ld = LockdownService.Current = new LockdownService(); var shell = NewDoseShell(); var path = System.IO.Path.Combine(CorePaths.UserData, "lockdown-dose.json");
            System.IO.File.WriteAllText(path, "{"); CoreSettings.Current.LockdownDoseKeeperEnabled = true;
            ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog? shown = null;
            using var hook = Window.WindowOpenedEvent.AddClassHandler<ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>((w, _) => shown = w);
            try {
                shell.Show(); var tab = shell.Named<LockdownTabView>("LockdownTab")!;
                tab.CmbLockdownDuration.SelectedIndex = 0; tab.BtnActivateLockdown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs(); Assert.NotNull(shown); Assert.False(ld.IsActive); Assert.Equal("{", System.IO.File.ReadAllText(path));
            } finally { shown?.Close(); System.IO.File.Delete(path); ld.Dispose(); LockdownService.Current = null; shell.RequestExit(); CoreEntitlement.HasPremiumProvider = old; }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task RetryAfterFailedRestorePreservesLaterUserChoice() {
        await AvaloniaTestDispatcher.RunAsync(() => {
            EnsureApp(); var old = CoreSettings.ServiceProvider;
            var settings = new SettingsService { SaveDebounceDueTimeMilliseconds = 60000 }; CoreSettings.ServiceProvider = () => settings;
            var s = settings.Current; EmptyDose(s); s.LockdownDoseKeeperEnabled = true;
            var ld = LockdownService.Current = new LockdownService(); var shell = NewDoseShell(); shell.Show();
            try {
                ld.Activate(TimeSpan.FromMinutes(20)); shell.EnforceLockdownDoseEngine(true);
                var prop = new[] { "FlashEnabled", "SubliminalEnabled", "SpiralEnabled", "PinkFilterEnabled", "BouncingTextEnabled", "BubblesEnabled" }
                    .Select(n => typeof(ConditioningControlPanel.Models.AppSettings).GetProperty(n)!).First(p => (bool)p.GetValue(s)!);
                settings.SealForReset(); ld.Deactivate(); settings.UnsealAfterFailedReset();
                prop.SetValue(s, true); ld.Activate(TimeSpan.FromMinutes(20));
                Assert.True((bool)prop.GetValue(s)!);
            } finally { settings.UnsealAfterFailedReset(); ld.Deactivate(); ld.Dispose(); CoreEngine.Stop(); LockdownService.Current = null; shell.RequestExit(); CoreSettings.ServiceProvider = old; }
            return Task.CompletedTask;
        });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExitFlushesBeforeDiscardingRecoveryAndKeepsItOnSaveFailure(bool failSave) {
        await AvaloniaTestDispatcher.RunAsync(() => {
            EnsureApp();
            var oldProvider = CoreSettings.ServiceProvider; var settings = new SettingsService { SaveDebounceDueTimeMilliseconds = 60000 };
            CoreSettings.ServiceProvider = () => settings;
            var s = settings.Current;
            EmptyDose(s); s.LockdownDoseKeeperEnabled = true;
            var ld = LockdownService.Current = new LockdownService(); var shell = NewDoseShell(); shell.Show();
            var path = System.IO.Path.Combine(CorePaths.UserData, "lockdown-dose.json");
            try {
                ld.Activate(TimeSpan.FromMinutes(20)); shell.EnforceLockdownDoseEngine(true); Assert.Equal(2, StarterCount(s));
                settings.SaveImmediate(); // the interrupted process left borrowed flags on disk
                if (failSave) settings.SealForReset();
                ld.Deactivate();
                var restarted = new SettingsService(); // no debounce has elapsed
                Assert.Equal(failSave ? 2 : 0, StarterCount(restarted.Current)); Assert.Equal(failSave, System.IO.File.Exists(path));
                if (failSave) {
                    ld.Activate(TimeSpan.FromMinutes(20));
                    Assert.True(System.IO.File.Exists(path)); // a second activation cannot discard the debt
                    settings.UnsealAfterFailedReset(); CoreSettings.ServiceProvider = () => restarted;
                    typeof(MainShellWindow).GetField("_doseRecoveryApplied", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.SetValue(null, false);
                    MainShellWindow.RecoverLockdownDose(); // restart while the second lockdown is active
                    Assert.False(System.IO.File.Exists(path)); Assert.Equal(0, StarterCount(new SettingsService().Current)); ld.Deactivate();
                }
            } finally {
                settings.UnsealAfterFailedReset(); ld.Dispose(); CoreEngine.Stop();
                LockdownService.Current = null; shell.RequestExit(); CoreSettings.ServiceProvider = oldProvider;
            }
            return Task.CompletedTask;
        });
    }
}
