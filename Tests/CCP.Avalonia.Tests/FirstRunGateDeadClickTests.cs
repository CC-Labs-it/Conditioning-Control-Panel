using System.Linq;
using System.IO;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF's 18+ gate on the first run, and the dashboard/Library clicks that used to do nothing.</summary>
public sealed class FirstRunGateDeadClickTests
{
    [Fact]
    public async Task StartupBookkeepingRestoresTheSharedProfile()
    {
        var path = Path.Combine(CorePaths.UserData, "settings.json");
        var backup = Path.GetTempFileName();
        var existed = File.Exists(path);
        if (existed) File.Copy(path, backup, true);
        const string sentinel = "{\"Welcomed\":true,\"SeasonStatsSeason\":\"2024-01\"}";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, sentinel);
            await StartupRunsSilentSeasonBookkeeping();
            Assert.Equal(sentinel, File.ReadAllText(path));
        }
        finally
        {
            if (existed) File.Copy(backup, path, true); else File.Delete(path);
            File.Delete(backup);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SeasonAdvanceDuringProfileAdoptionRollsTheLiveBucket(bool syncReply) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var settingsPath = Path.Combine(CorePaths.UserData, "settings.json");
        var backup = Path.GetTempFileName();
        var existed = File.Exists(settingsPath);
        if (existed) File.Copy(settingsPath, backup, true);
        var snapshotPath = Path.Combine(CorePaths.UserData, "season-recaps", "2098-07.json");
        var snapshotBackup = Path.GetTempFileName();
        var snapshotExisted = File.Exists(snapshotPath);
        if (snapshotExisted) File.Copy(snapshotPath, snapshotBackup, true);
        var nextSnapshotPath = Path.Combine(CorePaths.UserData, "season-recaps", "2098-08.json");
        var nextBackup = Path.GetTempFileName();
        var nextExisted = File.Exists(nextSnapshotPath);
        if (nextExisted) File.Copy(nextSnapshotPath, nextBackup, true);
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        MainShellWindow? shell = null;
        try
        {
            var s = service.Current;
            s.Welcomed = true;
            s.HasAcceptedAgeVerification = true;
            s.CurrentSeason = s.LastSeasonResetSeen = s.SeasonStatsSeason = "2098-07";
            s.SeasonSessionsStarted = 3;
            shell = new MainShellWindow();
            if (syncReply)
                ProfileAdopt.ApplySyncResponse(s, JObject.Parse("{\"user\":{\"current_season\":\"2098-08\"}}"), System.DateTime.UtcNow);
            else
                ProfileAdopt.AdoptReadBeforeWrite(s, new V2AuthService.V2User { CurrentSeason = "2098-08" });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("2098-08", s.SeasonStatsSeason);
            Assert.Equal("2098-08", s.LastSeasonResetSeen);
            Assert.Equal(0, s.SeasonSessionsStarted);
            Assert.Equal(3, SeasonRecapStore.Load("2098-07")!.SessionCount);
            s.SeasonSessionsStarted++;
            Assert.Equal("2098-08", s.SeasonStatsSeason);

            // A queued nudge from the old account must not roll the next account's bucket.
            s.UnifiedId = "old-account";
            ProfileAdopt.AdoptReadBeforeWrite(s, new V2AuthService.V2User { CurrentSeason = "2098-09" });
            s.UnifiedId = "new-account";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("2098-08", s.SeasonStatsSeason);
            Assert.Equal(1, s.SeasonSessionsStarted);
        }
        finally
        {
            shell?.RequestExit();
            Dispatcher.UIThread.RunJobs();
            service.SealForReset();
            CoreSettings.ServiceProvider = oldSettings;
            if (existed) File.Copy(backup, settingsPath, true); else File.Delete(settingsPath);
            File.Delete(backup);
            if (snapshotExisted) File.Copy(snapshotBackup, snapshotPath, true); else File.Delete(snapshotPath);
            File.Delete(snapshotBackup);
            if (nextExisted) File.Copy(nextBackup, nextSnapshotPath, true); else File.Delete(nextSnapshotPath);
            File.Delete(nextBackup);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task StartupRunsSilentSeasonBookkeeping() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settingsPath = Path.Combine(CorePaths.UserData, "settings.json");
        var backup = Path.GetTempFileName();
        var existed = File.Exists(settingsPath);
        if (existed) File.Copy(settingsPath, backup, true);
        var service = new SettingsService();
        var oldSettings = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => service;
        MainShellWindow? shell = null;
        try
        {
            service.Current.Welcomed = true;
            service.Current.HasAcceptedAgeVerification = true;
            service.Current.CurrentSeason = "2026-08";
            service.Current.LastSeasonResetSeen = "2026-07";
            service.Current.SeasonStatsSeason = "";
            shell = new MainShellWindow();
            Assert.Equal("2026-08", service.Current.SeasonStatsSeason);
            Assert.Equal("2026-08", service.Current.LastSeasonResetSeen);
            Assert.Empty(shell.OwnedWindows);
        }
        finally
        {
            shell?.RequestExit();
            Dispatcher.UIThread.RunJobs();
            service.SealForReset();
            CoreSettings.ServiceProvider = oldSettings;
            if (existed) File.Copy(backup, settingsPath, true); else File.Delete(settingsPath);
            File.Delete(backup);
        }
        return Task.CompletedTask;
    });


    [Fact]
    public Task ClosingTheGateUnticked_HandsBackAndExits() => Run(welcomed: false, accepted: false, async shell =>
    {
        var wizard = await Owned<FirstRunWizard>(shell);
        var next = wizard.FindControl<Button>("BtnNext")!;
        var box = wizard.FindControl<CheckBox>("ChkAgeConfirm")!;
        Assert.False(next.IsEnabled);
        box.IsChecked = true;
        Assert.True(next.IsEnabled);
        box.IsChecked = false;
        Assert.False(next.IsEnabled);

        var closed = false;
        shell.Closed += (_, _) => closed = true;
        wizard.Close();
        await WaitFor(() => closed);
        Assert.False(CoreSettings.Current.HasAcceptedAgeVerification);
        Assert.False(CoreSettings.Current.Welcomed);   // handed back to the next launch
    });

    [Fact]
    public Task WizardTimeoutHandsBackAndClosesTheUngatedShell() => Run(welcomed: false, accepted: false, async shell =>
    {
        var closed = false;
        shell.Closed += (_, _) => closed = true;
        var clock = (StartupLadder.TestClock)StartupLadder.Time;
        for (var i = 0; i < 5; i++) await Task.Yield();
        for (var i = 0; i < (int)(StartupLadder.ModalMaxWait / StartupLadder.ModalPoll); i++)
        { clock.Advance(StartupLadder.ModalPoll); for (var j = 0; j < 5; j++) await Task.Yield(); }
        Assert.True(closed);
        Assert.False(CoreSettings.Current.Welcomed);
        Assert.False(CoreSettings.Current.HasAcceptedAgeVerification);
    }, blocked: true);

    [Fact]
    public Task EnterWithTheBoxTicked_RecordsAcceptanceAndCarriesOn() => Run(welcomed: false, accepted: false, async shell =>
    {
        var wizard = await Owned<FirstRunWizard>(shell);
        wizard.FindControl<CheckBox>("ChkAgeConfirm")!.IsChecked = true;
        wizard.FindControl<Button>("BtnNext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(CoreSettings.Current.HasAcceptedAgeVerification);
        Assert.True(wizard.FindControl<Grid>("Step2")!.IsVisible);

        var closed = false;
        shell.Closed += (_, _) => closed = true;
        wizard.Close();
        await WaitFor(() => !wizard.IsVisible);
        Dispatcher.UIThread.RunJobs();
        Assert.False(closed);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task WelcomedButNeverAccepted_AsksTheAppLevelGate(bool answer) => Run(welcomed: true, accepted: false, async shell =>
    {
        var closed = false;
        shell.Closed += (_, _) => closed = true;
        var dialog = await Owned<ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>(shell);
        Assert.Equal(MainShellWindow.AgeGateBody, dialog.FindControl<TextBlock>("TxtMessage")!.Text);
        dialog.FindControl<Button>(answer ? "BtnOk" : "BtnCancel")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitFor(() => answer ? CoreSettings.Current.HasAcceptedAgeVerification : closed);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(!answer, closed);
        Assert.Equal(answer, CoreSettings.Current.HasAcceptedAgeVerification);
    });

    [Fact]
    public Task DashboardAndLibraryClicksGoWhereWpfGoes() => Run(welcomed: true, accepted: true, shell =>
    {
        var dash = shell.GetLogicalDescendants().OfType<SettingsTabView>().First();
        FeatureCard Card(string n) => dash.FindControl<FeatureCard>(n)!;

        Card("CardFlash").RaiseEvent(new RoutedEventArgs(FeatureCard.ClickEvent));
        Assert.Equal("studio", shell.CurrentTab);

        var was = MainShellWindow.IsWallFeatureOn("flash");
        Card("CardFlash").RaiseEvent(new RoutedEventArgs(FeatureCard.ToggleRequestedEvent));
        Assert.NotEqual(was, MainShellWindow.IsWallFeatureOn("flash"));

        var pink = MainShellWindow.IsWallFeatureOn("pinkfilter");
        dash.FindControl<SplitFeatureCard>("ComboSpiralPink")!.RaiseEvent(new RoutedEventArgs(SplitFeatureCard.ToggleBEvent));
        Assert.NotEqual(pink, MainShellWindow.IsWallFeatureOn("pinkfilter"));

        shell.ShowTab("settings");
        Card("CardVault").RaiseEvent(new RoutedEventArgs(FeatureCard.ClickEvent));
        Assert.Equal("appsettings", shell.CurrentTab);
        Assert.Equal("account", shell.Named<AppSettingsTabView>("AppSettingsTab")!.CheckedSectionKey);

        shell.OpenNavSection("library");   // the Media Log is a Library launcher pill now
        shell.PageStrip!.PillFor("medialog")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("assets", shell.CurrentTab);   // a window: the page under it stays
        Assert.Contains(shell.OwnedWindows, w => w is MediaHistoryWindow);
        return Task.CompletedTask;
    });

    private static async Task<T> Owned<T>(MainShellWindow shell) where T : Window
    {
        await WaitFor(() => shell.OwnedWindows.OfType<T>().Any());
        return shell.OwnedWindows.OfType<T>().Single();
    }

    private static async Task WaitFor(System.Func<bool> condition)
    {
        var deadline = System.DateTime.UtcNow.AddSeconds(5);
        while (!condition() && System.DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Assert.True(condition(), "timed out");
    }

    private static Task Run(bool welcomed, bool accepted, System.Func<MainShellWindow, Task> body, bool blocked = false) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        MainShellWindow? shell = null;
        try
        {
            CoreSettings.Current.Welcomed = welcomed;
            CoreSettings.Current.HasAcceptedAgeVerification = accepted;
            StartupLadder.ModalWindowReady = () => !blocked && shell?.IsVisible == true;
            shell = new MainShellWindow();
            shell.Show();
            if (!welcomed || !accepted)
            {
                Dispatcher.UIThread.RunJobs();
                ((StartupLadder.TestClock)StartupLadder.Time).Advance(StartupLadder.ModalSettle);
            }
            await body(shell);
        }
        finally
        {
            foreach (var w in shell?.OwnedWindows.ToList() ?? new()) w.Close();
            shell?.RequestExit();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();   // cancels the 500ms debounce so it cannot land in a later test's file
            CoreSettings.ServiceProvider = null;
        }
    });
}
