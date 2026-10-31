using System;
using System.Linq;
using System.IO;
using System.Reflection;
using Avalonia.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
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
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir.FullName, "achievements.json")));
        try
        {
            property.SetValue(null, engine);
            AvApp.SeedCompanionRecall();
            AvApp.SeedCompanionRecall(); // repeated composition must not add listeners
            var entitlementChecks = 0;
            ConditioningControlPanel.CoreEntitlement.HasPremiumProvider = () => { entitlementChecks++; return false; };
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
}
