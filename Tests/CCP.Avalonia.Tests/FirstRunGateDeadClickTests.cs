using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
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
    public Task AgeGateThatNeverGetsItsTurnAsksAnyway() => Run(welcomed: true, accepted: false, async shell =>
    {
        // WPF App.xaml.cs:4092 onAbandoned: a tray start hides the shell past the 5-minute wait;
        // the gate is then asked anyway, never dropped (the app must not run unverified).
        ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog? asked = null;
        using var seen = Window.WindowOpenedEvent.AddClassHandler<ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>((d, _) => asked = d);
        var closed = false;
        shell.Closed += (_, _) => closed = true;
        shell.Hide();
        Dispatcher.UIThread.RunJobs();
        ((StartupLadder.TestClock)StartupLadder.Time).Advance(System.TimeSpan.FromMinutes(5) + System.TimeSpan.FromSeconds(1));
        await WaitFor(() => asked != null);
        Assert.Equal(MainShellWindow.AgeGateBody, asked!.FindControl<TextBlock>("TxtMessage")!.Text);
        asked.FindControl<Button>("BtnOk")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitFor(() => CoreSettings.Current.HasAcceptedAgeVerification);
        Assert.False(closed);
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
            // Startup modals wait on the ladder's stepped clock (750 ms settle), never wall time.
            ((StartupLadder.TestClock)StartupLadder.Time).Advance(System.TimeSpan.FromMilliseconds(250));
            await Task.Delay(20);
        }
        Assert.True(condition(), "timed out");
    }

    private static Task Run(bool welcomed, bool accepted, System.Func<MainShellWindow, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
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
