using System;
using System.Linq;
using System.IO;
using System.Reflection;
using Avalonia.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion.Asks;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class CompanionBrainHostTests
{
    [Fact]
    public Task RecallSignalUnlocksOnlyWithEntitlement() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var property = typeof(AvApp).GetProperty("Achievements", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = property.GetValue(null);
        var entitlement = ConditioningControlPanel.CoreEntitlement.HasPremiumProvider;
        var recall = ConditioningControlPanel.Services.Companion.Brain.CompanionBrain.MemoryRecalled;
        var dir = Directory.CreateTempSubdirectory("ccp-brain-recall-");
        var entitlementChecks = 0;
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir.FullName, "achievements.json")),
            () => { entitlementChecks++; return ConditioningControlPanel.CoreEntitlement.HasPremium; });
        try
        {
            property.SetValue(null, engine);
            AvApp.SeedCompanionRecall();
            AvApp.SeedCompanionRecall(); // repeated composition must not add listeners
            ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = () => false;
            // Other UI work can read the same global provider while the recall is dispatched.
            Dispatcher.UIThread.Post(() => { _ = ConditioningControlPanel.CoreEntitlement.HasPremium; });
            ConditioningControlPanel.Services.Companion.Brain.CompanionBrain.MemoryRecalled!();
            Dispatcher.UIThread.RunJobs();
            Assert.False(engine.Progress.IsUnlocked("she_remembers"));
            Assert.Equal(1, entitlementChecks); // each subscribed handler attempts the denied unlock
            ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = () => true;
            ConditioningControlPanel.Services.Companion.Brain.CompanionBrain.MemoryRecalled!();
            Dispatcher.UIThread.RunJobs();
            Assert.True(engine.Progress.IsUnlocked("she_remembers"));
        }
        finally
        {
            property.SetValue(null, previous);
            ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = entitlement;
            ConditioningControlPanel.Services.Companion.Brain.CompanionBrain.MemoryRecalled = recall;
            dir.Delete(true);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task BackgroundSendCreditsAchievementOnUiThread() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var property = typeof(AvApp).GetProperty("Achievements", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previous = property.GetValue(null);
        var dir = Directory.CreateTempSubdirectory("ccp-brain-ach-");
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir.FullName, "achievements.json")));
        try
        {
            property.SetValue(null, engine);
            await Task.Run(AvApp.NotifyUserMessageSent);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, engine.Progress.CompanionMessages);
            Assert.True(engine.Progress.IsUnlocked("pleased_to_meet_you"));
        }
        finally { property.SetValue(null, previous); dir.Delete(true); }
    });

    [Fact]
    public Task SessionOffersRecheckCurrentCatalogueAvailability() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var page = new PresetsTabView();
        var options = page.CompanionSessionOptions();
        Assert.NotEmpty(options);
        Assert.All(options, option => Assert.True(option.IsAllowed));
        Assert.Null(page.RevealSession("missing-session"));
        // Refreshing the catalogue invalidates options captured from the old rack, just as WPF's pool re-read does.
        var manager = new ConditioningControlPanel.Services.SessionManager();
        page.UseSessionManager(manager);
        Assert.Empty(page.CompanionSessionOptions());
        Assert.All(options, option => Assert.False(option.IsAllowed));
        return Task.CompletedTask;
    });

    [Fact]
    public Task BackgroundAskReadsTheShownSessionRack() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        var previousBrain = AvApp.Brain;
        var dir = Directory.CreateTempSubdirectory("ccp-background-ask-");
        using var brain = new ConditioningControlPanel.Services.Companion.Brain.CompanionBrain(
            new ConditioningControlPanel.Services.AiService("http://127.0.0.1:9"),
            memory: new ConditioningControlPanel.Services.Companion.Brain.MemoryStore(Path.Combine(dir.FullName, "memory.json")),
            store: new ConditioningControlPanel.Services.Companion.Brain.CompanionSessionStore(
                Path.Combine(dir.FullName, "session.json"), Path.Combine(dir.FullName, "legacy.json")));
        try
        {
            ConditioningControlPanel.CoreSettings.Current.AvatarEnabled = true;
            ConditioningControlPanel.CoreSettings.Current.CompanionAsksEnabled = true;
            AvApp.Brain = brain;
            shell.Show();
            AvApp.SeedCompanionTubeSeams();
            // Headless has no desktop MainWindow lifetime. Use the same target on this shown shell.
            CompanionAskService.SessionOptions = shell.CompanionSessionOptions;
            CompanionAskService.BusyProvider = () => false;
            ConditioningControlPanel.Services.Companion.Brain.CompanionBrain.ActivitiesProvider =
                () => Array.Empty<ConditioningControlPanel.Services.Companion.CompanionActivity>();
            Assert.NotEmpty(await Task.Run(shell.CompanionSessionOptions));
            Assert.True(await Task.Run(() => CompanionAskService.Instance.CanOfferFor("what can I do")));
        }
        finally
        {
            CompanionAskService.Instance.Stop();
            shell.Close();
            AvApp.Brain = previousBrain;
            brain.Dispose();
            dir.Delete(true);
        }
    });

    [Fact]
    public Task SessionOfferUsesConfirmationAndRefusesWhileRunning() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = AvApp.Sessions;
        var runner = new SessionRunner(new SessionLogService());
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        try
        {
            AvApp.Sessions = runner;
            shell.Show();
            AvApp.SeedCompanionTubeSeams();
            var option = shell.CompanionSessionOptions().First();
            Assert.False(shell.StartSessionFromBillboard("missing-session"));
            Assert.True(shell.StartSessionFromBillboard(option.Id));
            Dispatcher.UIThread.RunJobs();
            var confirm = Assert.Single(shell.OwnedWindows.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>());
            Assert.False(runner.IsRunning);
            confirm.Close();
            // A running rack is busy even before CoreSession has a current session to report.
            typeof(SessionRunner).GetProperty("IsRunning")!.SetValue(runner, true);
            Assert.True(CompanionAskService.BusyProvider!());
            Assert.False(option.IsAllowed);
            Assert.False(shell.StartSessionFromBillboard(option.Id));
        }
        finally
        {
            typeof(SessionRunner).GetProperty("IsRunning")!.SetValue(runner, false);
            foreach (var dialog in shell.OwnedWindows.ToArray()) dialog.Close();
            shell.Close();
            CompanionAskService.Instance.Stop();
            AvApp.Sessions = previous;
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task RemoteAndGraceSuppressAskCards(bool remote) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var overlay = ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.Instance;
        var grace = overlay.GetType().GetField("_gracePaused", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var oldRelay = RemoteControlTabView.Relay;
        using var relay = new ConditioningControlPanel.Services.RemoteRelay(
            () => null, () => null, "test", (_, _) => null, _ => { });
        try
        {
            RemoteControlTabView.Relay = new Lazy<ConditioningControlPanel.Services.RemoteRelay>(() => relay);
            AvApp.Mantra.EndSession();
            ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
            grace.SetValue(overlay, false);
            AvApp.SeedCompanionTubeSeams();
            Assert.False(CompanionAskService.BusyProvider!());
            if (remote)
                relay.GetType().GetProperty("ControllerConnected")!.SetValue(RemoteControlTabView.Relay.Value, true);
            else grace.SetValue(overlay, true);
            Assert.True(CompanionAskService.BusyProvider!());
            Assert.False(CompanionAskService.Instance.CanOfferFor("what can I do"));
        }
        finally
        {
            grace.SetValue(overlay, false);
            RemoteControlTabView.Relay = oldRelay;
            CompanionAskService.Instance.Stop();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task MantraAndStartupQuietSuppressAskCards() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var busy = CompanionAskService.BusyProvider;
        var sessions = CompanionAskService.SessionOptions;
        var start = CompanionAskService.StartSession;
        try
        {
            AvApp.SeedCompanionTubeSeams();
            Assert.NotNull(CompanionAskService.StartSession);
            AvApp.Mantra.StartSession(5);
            Assert.True(CompanionAskService.BusyProvider!());
            Assert.False(CompanionAskService.Instance.CanOfferFor("what can I do"));
            AvApp.Mantra.EndSession();
            ConditioningControlPanel.Avalonia.Platform.StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(1));
            Assert.True(CompanionAskService.BusyProvider!());
            Assert.False(CompanionAskService.Instance.CanOfferFor("what can I do"));
        }
        finally
        {
            AvApp.Mantra.EndSession();
            ConditioningControlPanel.Avalonia.Platform.StartupLadder.ResetForTests();
            CompanionAskService.Instance.Stop();
            CompanionAskService.BusyProvider = busy;
            CompanionAskService.SessionOptions = sessions;
            CompanionAskService.StartSession = start;
        }
        return Task.CompletedTask;
    });
    private static Action SaveAskSeams()
    {
        var saved = (CompanionAskService.BrainProvider, CompanionAskService.ShowCardSurface, CompanionAskService.SaySurface,
            CompanionAskService.BusyProvider, CompanionAskService.SessionOptions, CompanionAskService.StartSession,
            CompanionAskService.OpenLink, ConditioningControlPanel.Services.Companion.ConversationDelivery.AskCardsShown,
            ConditioningControlPanel.Services.Companion.Brain.PromptAssembler.NoticeSurface);
        var timer = typeof(CompanionAskService).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var stopped = typeof(CompanionAskService).GetField("_stopped", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var instance = CompanionAskService.Instance;
        var wasRunning = timer.GetValue(instance) != null;
        var wasStopped = stopped.GetValue(instance);
        return () =>
        {
            if (!wasRunning) { instance.Stop(); stopped.SetValue(instance, wasStopped); }
            (CompanionAskService.BrainProvider, CompanionAskService.ShowCardSurface, CompanionAskService.SaySurface,
             CompanionAskService.BusyProvider, CompanionAskService.SessionOptions, CompanionAskService.StartSession,
             CompanionAskService.OpenLink, ConditioningControlPanel.Services.Companion.ConversationDelivery.AskCardsShown,
             ConditioningControlPanel.Services.Companion.Brain.PromptAssembler.NoticeSurface) = saved;
        };
    }

    [Fact]
    public Task AskSeamCleanupRestoresCallbacksAndTimer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var outerRestore = SaveAskSeams();
        var instance = CompanionAskService.Instance;
        var timer = typeof(CompanionAskService).GetField("_timer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var stopped = typeof(CompanionAskService).GetField("_stopped", BindingFlags.NonPublic | BindingFlags.Instance)!;
        void Check()
        {
            CompanionAskService.BrainProvider = null;
            CompanionAskService.ShowCardSurface = null;
            CompanionAskService.SaySurface = null;
            CompanionAskService.BusyProvider = null;
            CompanionAskService.SessionOptions = null;
            CompanionAskService.StartSession = null;
            CompanionAskService.OpenLink = null;
            ConditioningControlPanel.Services.Companion.ConversationDelivery.AskCardsShown = null;
            ConditioningControlPanel.Services.Companion.Brain.PromptAssembler.NoticeSurface = null;
            var oldTimer = timer.GetValue(instance);
            var oldStopped = stopped.GetValue(instance);
            var restore = SaveAskSeams();
            try { AvApp.SeedCompanionTubeSeams(); }
            finally { restore(); }
            Assert.Null(CompanionAskService.BrainProvider);
            Assert.Null(CompanionAskService.ShowCardSurface);
            Assert.Null(CompanionAskService.SaySurface);
            Assert.Null(CompanionAskService.BusyProvider);
            Assert.Null(CompanionAskService.SessionOptions);
            Assert.Null(CompanionAskService.StartSession);
            Assert.Null(CompanionAskService.OpenLink);
            Assert.Null(ConditioningControlPanel.Services.Companion.ConversationDelivery.AskCardsShown);
            Assert.Null(ConditioningControlPanel.Services.Companion.Brain.PromptAssembler.NoticeSurface);
            Assert.Same(oldTimer, timer.GetValue(instance));
            Assert.Equal(oldStopped, stopped.GetValue(instance));
        }
        try
        {
            if (timer.GetValue(instance) == null) Check();
            instance.Start();
            Check();
        }
        finally { outerRestore(); }
        return Task.CompletedTask;
    });

    private sealed class AskBusyAudio : ConditioningControlPanel.Avalonia.Views.Deeper.IDeeperLocalAudio
    {
        public double DurationSeconds => 95;
        public double? PlaybackDurationSeconds => null;
        public double PositionSeconds { get; set; }
        public int Volume { get; set; }
        public bool Playing { get; private set; }
        public event Action? Ended { add { } remove { } }
        public event Action? Failed { add { } remove { } }
        public void Play() => Playing = true;
        public void Pause() => Playing = false;
        public void Dispose() => Playing = false;
    }

    [Fact]
    public Task CompanionAskBusyDeeperPlayer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var restore = SaveAskSeams();
        var open = ConditioningControlPanel.Avalonia.Views.Deeper.DeeperLocalAudio.Open;
        var audio = new AskBusyAudio();
        ConditioningControlPanel.Avalonia.Views.Deeper.DeeperLocalAudio.Open = _ => Task.FromResult<ConditioningControlPanel.Avalonia.Views.Deeper.IDeeperLocalAudio?>(audio);
        var player = new ConditioningControlPanel.Avalonia.Views.Deeper.EnhancementPlayerWindow(null, null);
        player.LoadWaveformPeaks = DeeperPlayerAudioTests.NoPeaks;
        void Click(string name) => player.FindControl<global::Avalonia.Controls.Button>(name)!.RaiseEvent(
            new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
        void Tick() => player.GetType().GetMethod("UiTimer_Tick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(player, new object?[] { null, EventArgs.Empty });
        try
        {
            player.Show();
            AvApp.SeedCompanionTubeSeams();
            player.OpenLocalMediaFile("plain.mp3");
            Tick();
            Assert.True(audio.Playing);
            Assert.False(CompanionAskService.BusyProvider!());
            player.LoadEnhancementFromMemory(new ConditioningControlPanel.Models.Deeper.Enhancement
                { MediaType = ConditioningControlPanel.Models.Deeper.MediaTypes.Audio, MediaSource = "*" }, "test");
            Tick();
            Assert.True(CompanionAskService.BusyProvider!());
            Click("BtnPlayPause");
            Assert.False(audio.Playing);
            Assert.True(CompanionAskService.BusyProvider!());
            Assert.False(CompanionAskService.Instance.CanOfferFor("what can I do"));
            Click("BtnStop");
            Assert.False(CompanionAskService.BusyProvider!());
        }
        finally
        {
            player.Close();
            ConditioningControlPanel.Avalonia.Views.Deeper.DeeperLocalAudio.Open = open;
            restore();
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CompanionAskBusyVideoBeforePlayback(bool local) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var restore = SaveAskSeams();
        var player = new ConditioningControlPanel.Avalonia.Views.Deeper.EnhancementPlayerWindow(null, null);
        var web = player.FindControl<ConditioningControlPanel.Avalonia.Views.Controls.WebHost>("VideoBrowser")!;
        web.ScriptInvoker = _ => Task.FromResult<string?>("ok");
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");
        File.WriteAllText(path, "");
        try
        {
            player.Show();
            AvApp.SeedCompanionTubeSeams();
            player.OpenLocalMediaFile(path);
            Assert.False(CompanionAskService.BusyProvider!());
            player.LoadEnhancementFromMemory(new ConditioningControlPanel.Models.Deeper.Enhancement
                { MediaType = ConditioningControlPanel.Models.Deeper.MediaTypes.Video, MediaSource = local ? path : "https://hypnotube.com/video/test" }, "test");
            Assert.True(CompanionAskService.BusyProvider!());
            Assert.False(CompanionAskService.Instance.CanOfferFor("what can I do"));
            player.FindControl<Button>("BtnStop")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.False(CompanionAskService.BusyProvider!());
            player.FindControl<Button>("BtnPlayPause")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(CompanionAskService.BusyProvider!());
            ConditioningControlPanel.Avalonia.Views.Deeper.EnhancementPlayerWindow.PanicPauseAll();
            Assert.False(CompanionAskService.BusyProvider!());
        }
        finally { player.Close(); File.Delete(path); restore(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task VideoEnhancementBindsAtZeroAfterMidPlaybackSwitch() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var open = ConditioningControlPanel.Avalonia.Views.Deeper.DeeperLocalAudio.Open;
        var audio = new AskBusyAudio();
        ConditioningControlPanel.Avalonia.Views.Deeper.DeeperLocalAudio.Open = _ => Task.FromResult<ConditioningControlPanel.Avalonia.Views.Deeper.IDeeperLocalAudio?>(audio);
        var player = new ConditioningControlPanel.Avalonia.Views.Deeper.EnhancementPlayerWindow(null, null);
        player.LoadWaveformPeaks = DeeperPlayerAudioTests.NoPeaks;
        player.FindControl<ConditioningControlPanel.Avalonia.Views.Controls.WebHost>("VideoBrowser")!.ScriptInvoker = _ => Task.FromResult<string?>("0|95|0");
        var speaks = 0;
        player.SpeakSessionFactory = (_, _) => { speaks++; throw new InvalidOperationException("speak"); };
        void Tick() => player.GetType().GetMethod("UiTimer_Tick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(player, new object?[] { null, EventArgs.Empty });
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mp4");
        File.WriteAllText(path, "");
        try
        {
            player.Show();
            player.OpenLocalMediaFile("plain.mp3");
            audio.PositionSeconds = 50;
            Tick();
            Assert.True(audio.Playing);
            player.LoadEnhancementFromMemory(new ConditioningControlPanel.Models.Deeper.Enhancement
            {
                MediaType = ConditioningControlPanel.Models.Deeper.MediaTypes.Video, MediaSource = path, TimelineItems = {
                    new ConditioningControlPanel.Models.Deeper.TimelineItem { Id = "band", Kind = ConditioningControlPanel.Models.Deeper.TimelineItemKind.Rule, Start = 40, Duration = 20,
                        Trigger = new ConditioningControlPanel.Models.Deeper.TimeReachedTrigger { Time = 41 },
                        Action = new ConditioningControlPanel.Models.Deeper.TriggerEffectAction { EffectType = ConditioningControlPanel.Models.Deeper.EffectTypes.Haptic, PatternName = "pulse" } },
                    new ConditioningControlPanel.Models.Deeper.TimelineItem { Kind = ConditioningControlPanel.Models.Deeper.TimelineItemKind.Rule, Duration = double.MaxValue,
                        Trigger = new ConditioningControlPanel.Models.Deeper.RegionExitedTrigger { RegionId = "band" },
                        Action = new ConditioningControlPanel.Models.Deeper.TriggerEffectAction { EffectType = ConditioningControlPanel.Models.Deeper.EffectTypes.Speak, SpeakTarget = "yes" } } }
            }, "test");
            Tick(); // the new video's first poll reads 0: the previous media's 50 s must not count as leaving the band
            Assert.Equal(0, speaks);
        }
        finally
        {
            player.Close();
            ConditioningControlPanel.Avalonia.Views.Deeper.DeeperLocalAudio.Open = open;
            File.Delete(path);
        }
        return Task.CompletedTask;
    });
    [Fact]
    public Task CompanionAskBusyBrowserEnhancement() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var restore = SaveAskSeams();
        var oldSetting = ConditioningControlPanel.CoreSettings.Current.BrowserEnhanceIfPossible;
        var lifetimeField = typeof(Application).GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .Single(f => typeof(global::Avalonia.Controls.ApplicationLifetimes.IApplicationLifetime).IsAssignableFrom(f.FieldType));
        var oldLifetime = Application.Current!.ApplicationLifetime;
        using var lifetime = new global::Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime
            { ShutdownMode = global::Avalonia.Controls.ShutdownMode.OnExplicitShutdown };
        ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow? shell = null;
        string? path = null;
        try
        {
            lifetimeField.SetValue(Application.Current, lifetime);
            ConditioningControlPanel.CoreSettings.Current.BrowserEnhanceIfPossible = true;
            shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
            lifetime.MainWindow = shell;
            shell.Show();
            AvApp.SeedCompanionTubeSeams();
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var web = tab.FindControl<ConditioningControlPanel.Avalonia.Views.Controls.WebHost>("BrowserWebHost")!;
            web.ScriptInvoker = _ => Task.FromResult<string?>(null);
            var library = shell.BrowserEnhancementLibrary!;
            var id = Guid.NewGuid().ToString("N");
            var url = "https://hypnotube.com/watch/" + id;
            path = Path.Combine(library.LibraryFolder, id + ".ccpenh.json");
            File.WriteAllText(path, "{\"$schema\":\"ccp-enhancement/v1\",\"version\":1,\"media_type\":\"video\",\"media_source\":\"" + url + "\",\"metadata\":{\"name\":\"Ask busy test\",\"creator\":\"test\"}}");
            Assert.False(CompanionAskService.BusyProvider!());
            web.OnNavigationCompleted(new Uri(url));
            Assert.True(tab.BrowserEnhanceHost.IsRunning);
            Assert.True(CompanionAskService.BusyProvider!());
            Assert.False(CompanionAskService.Instance.CanOfferFor("what can I do"));
            web.OnNavigationCompleted(new Uri("https://example.com/unrelated"));
            Assert.False(tab.BrowserEnhanceHost.IsRunning);
            Assert.False(CompanionAskService.BusyProvider!());
        }
        finally
        {
            shell?.Close();
            if (path != null) File.Delete(path);
            ConditioningControlPanel.CoreSettings.Current.BrowserEnhanceIfPossible = oldSetting;
            lifetimeField.SetValue(Application.Current, oldLifetime);
            restore();
        }
        return Task.CompletedTask;
    });

}
