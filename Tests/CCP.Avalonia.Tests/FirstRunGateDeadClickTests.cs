using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
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
    public Task StartupRunsSilentSeasonBookkeeping() => AvaloniaTestDispatcher.RunAsync(() =>
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
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task StartupPersistsHeadValuesInTheRollover() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        var achievementProperty = typeof(global::ConditioningControlPanel.Avalonia.App).GetProperty("Achievements",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldAchievements = achievementProperty.GetValue(null);
        var path = System.IO.Path.Combine(CorePaths.UserData, "season-recaps", "2098-07.json");
        CoreSettings.ServiceProvider = () => service;
        MainShellWindow? shell = null;
        try
        {
            var engine = new AchievementEngine(new AchievementStore(System.IO.Path.Combine(CorePaths.UserData, "recap-test-achievements.json")));
            engine.Progress.LifetimeSkillPointsSpent = 12345;
            achievementProperty.SetValue(null, engine);
            CoreAccount.DisplayNameProvider = () => "Tester";
            CoreAccount.HasPremiumAccessProvider = () => true;
            MainShellWindow.NoteLeaderboardPage(new LeaderboardPage<LeaderboardEntryData> { YourRank = 17, YourTotal = 100 });
            service.Current.Welcomed = true;
            service.Current.HasAcceptedAgeVerification = true;
            service.Current.CurrentSeason = "2098-08";
            service.Current.LastSeasonResetSeen = "2098-07";
            service.Current.SeasonStatsSeason = "2098-07";
            service.Current.SeasonSessionsStarted = 3;
            shell = new MainShellWindow();
            var saved = SeasonRecapStore.Load("2098-07");
            Assert.NotNull(saved);
            Assert.Multiple(
                () => Assert.Equal("Tester", saved.Handle),
                () => Assert.True(saved.IsSupporter),
                () => Assert.Equal(12345, saved.LifetimePointsSpent),
                () => Assert.Equal(17, saved.Percentile),
                () => Assert.Equal(3, saved.SessionCount),
                () => Assert.Equal(0, service.Current.SeasonSessionsStarted));
            Assert.Empty(shell.OwnedWindows);
        }
        finally
        {
            shell?.RequestExit();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
            achievementProperty.SetValue(null, oldAchievements);
            MainShellWindow.NoteLeaderboardPage(new LeaderboardPage<LeaderboardEntryData>());
            System.IO.File.Delete(path);
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task BookkeepingFailureStillArmsTheGate(bool welcomed) => Run(welcomed, accepted: false, async shell =>
    {
        if (welcomed)
            Assert.NotNull(await Owned<ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>(shell));
        else
            Assert.NotNull(await Owned<FirstRunWizard>(shell));
    }, service =>
    {
        service.Current.CurrentSeason = "2026-08";
        service.Current.LastSeasonResetSeen = "2026-07";
        service.Current.SeasonStatsSeason = "";
        service.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(service.Current.SeasonStatsSeason))
                throw new System.InvalidOperationException("bookkeeping failure");
        };
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
        Assert.Equal("premium", shell.CurrentTab);

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

    private static Task Run(bool welcomed, bool accepted, System.Func<MainShellWindow, Task> body, System.Action<SettingsService>? arrange = null) => AvaloniaTestDispatcher.RunAsync(async () =>
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
            arrange?.Invoke(service);
            shell = new MainShellWindow();
            shell.Show();
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
