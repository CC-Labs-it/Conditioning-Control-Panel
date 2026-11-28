using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class StartHereReviewTests
{
    private static Task WithShell(Action<MainShellWindow, SettingsService> check) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var oldSession = CoreSession.IsSessionRunningProvider;
        var settings = new SettingsService();
        settings.Current.Welcomed = settings.Current.HasAcceptedAgeVerification = true;
        settings.Current.StartHereShown = false;
        CoreSettings.ServiceProvider = () => settings;
        StartupLadder.ResetForTests();
        var shell = new MainShellWindow { OpenWelcomeShow = _ => false };
        shell.Show();
        try { check(shell, settings); }
        finally
        {
            shell.Close();
            StartupLadder.ResetForTests();
            CoreSession.IsSessionRunningProvider = oldSession;
            CoreSettings.ServiceProvider = previous;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RefusedShowFilesOfferWithoutSpendingFlagUntilOpened() => WithShell((shell, settings) =>
    {
        CoreSession.IsSessionRunningProvider = () => true;
        shell.OfferWelcomeShowAfterFirstRun();
        Dispatcher.UIThread.RunJobs();
        var row = Assert.Single(StartupLadder.Inbox.Items.Where(i => i.Key == "intro:starthere"));
        Assert.False(settings.Current.StartHereShown);
        StartupLadder.Inbox.Open(row);
        Assert.True(settings.Current.StartHereShown);
        var card = Assert.Single(shell.OwnedWindows.Where(w => w.Name == "StartHereCard"));
        card.Close();
    });

    [Fact]
    public Task OfferWaitsForTheShowToEndAndQueuesOnlyOnce() => WithShell((shell, settings) =>
    {
        CoreSession.IsSessionRunningProvider = () => true;
        Action? ended = null;
        shell.OpenWelcomeShow = owner =>
        {
            ended = typeof(MainShellWindow).GetField("_firstShowEnded", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner) as Action;
            return true;
        };
        shell.OfferWelcomeShowAfterFirstRun();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(ended);
        Assert.False(StartupLadder.Inbox.Contains("intro:starthere"));
        ended!();
        ended();
        Dispatcher.UIThread.RunJobs();
        Assert.Single(StartupLadder.Inbox.Items.Where(i => i.Key == "intro:starthere"));
        Assert.False(settings.Current.StartHereShown);
    });

    [Fact]
    public Task ReplayUsesBorderlessGlassAndCloseButton() => WithShell((shell, _) =>
    {
        var replay = shell.FindControl<Button>("BtnHelpStartHere");
        Assert.NotNull(replay);
        replay!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, replay));
        var card = Assert.Single(shell.OwnedWindows.Where(w => w.Name == "StartHereCard"));
        Assert.Equal("None", card.SystemDecorations.ToString());
        var root = card.FindControl<Border>("CardRoot");
        Assert.NotNull(root);
        Assert.IsType<LinearGradientBrush>(root!.Background);
        var close = card.FindControl<Button>("BtnX");
        Assert.NotNull(close);
        close!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, close));
        Assert.False(card.IsVisible);
    });
}
