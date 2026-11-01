using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ExclusivesDoorsTests
{
    private static void Run(Action<MainShellWindow> test) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.Welcomed = true;
        settings.Current.HasAcceptedAgeVerification = true;
        settings.Current.WhatMovedCardShown = 1;
        settings.Current.FocusGazeEnabled = false;
        var shell = new MainShellWindow { Width = 1400, Height = 900 };
        try
        {
            shell.Show();
            shell.ShowTab("premium");
            Settle();
            test(shell);
        }
        finally
        {
            GameWindow.CloseAllForPanic();
            foreach (var child in shell.OwnedWindows.ToList()) child.Close();
            shell.Close();
            Settle();
            CoreSettings.ServiceProvider = null;
        }
    });

    [Fact]
    public void FocusGazeCardShowsPlayAndScrollsToTheSwitch() => Run(shell =>
    {
        var play = shell.Named<PlayTabView>("PlayTab")!;
        var scroll = play.FindControl<ScrollViewer>("WallScroll")!;
        var slot = play.FindControl<Control>("SlotFocusGaze")!;
        ClickCard(shell, "focusgaze");
        Settle();
        Assert.Equal("play", shell.CurrentTab);
        Assert.True(scroll.Offset.Y > 0);
        var top = slot.TranslatePoint(default, scroll)!.Value.Y;
        Assert.InRange(top, -slot.Bounds.Height, scroll.Bounds.Height);
        Assert.False(CoreSettings.Current.FocusGazeEnabled); // navigation never turns the switch on
    });

    [Fact]
    public void DtrhCardAsksForLoginThenUsesTheGameGateAndHost() => Run(shell =>
    {
        var root = Directory.CreateTempSubdirectory("vault-dtrh-").FullName;
        using var server = new WebAssetServer(root);
        var (oldServer, oldLogin, oldLab, oldFree, oldDenied) = (GameWindow.Server, CoreAccount.IsLoggedInProvider,
            CoreEntitlement.HasLabProvider, CoreEntitlement.IsFreeTodayProvider, CoreEntitlement.ShowDeniedHandler);
        try
        {
            GameWindow.Server = () => server;
            CoreAccount.IsLoggedInProvider = () => false;
            CoreEntitlement.HasLabProvider = () => false;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            var denied = 0;
            CoreEntitlement.ShowDeniedHandler = _ => denied++;
            ClickCard(shell, "dtrh");
            Settle();
            Assert.Single(shell.OwnedWindows.OfType<LoginDialog>(), w => w.IsVisible);
            Assert.Equal(0, denied);
            Assert.False(GameWindow.IsAnyOpen());
            foreach (var dialog in shell.OwnedWindows.OfType<LoginDialog>().ToList()) dialog.Close();
            Settle();

            CoreAccount.IsLoggedInProvider = () => true;
            ClickCard(shell, "dtrh");
            Settle();
            Assert.Equal(1, denied);
            Assert.False(GameWindow.IsAnyOpen());

            CoreEntitlement.HasLabProvider = () => true;
            ClickCard(shell, "dtrh");
            Settle();
            Assert.True(GameWindow.IsAnyOpen());
            var game = GameWindow.Launch("dtrh")!;
            Assert.Contains("dtrh/index.html", game.PageUrl!.AbsolutePath);
            Assert.Equal("premium", shell.CurrentTab);
        }
        finally
        {
            GameWindow.CloseAllForPanic();
            (GameWindow.Server, CoreAccount.IsLoggedInProvider, CoreEntitlement.HasLabProvider,
                CoreEntitlement.IsFreeTodayProvider, CoreEntitlement.ShowDeniedHandler) = (oldServer, oldLogin, oldLab, oldFree, oldDenied);
            server.Dispose();
            Directory.Delete(root, true);
        }
    });

    private static void ClickCard(MainShellWindow shell, string key)
    {
        var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
        var card = view.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "ExCard"
            && c.DataContext is ExclusiveCardRow row && row.Feature.Key == key);
        var model = (ExclusiveCardRow)card.DataContext!;
        Assert.True(model.IsAvailable);
        Assert.Null(model.UnavailableTip);
        card.BringIntoView();
        Settle();
        var point = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), shell)!.Value;
        shell.MouseMove(point);
        shell.MouseDown(point, MouseButton.Left);
        shell.MouseUp(point, MouseButton.Left);
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
