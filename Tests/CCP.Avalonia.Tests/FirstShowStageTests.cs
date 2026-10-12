using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using S = ConditioningControlPanel.Services.FirstShow.FirstShowScript;

namespace CCP.Avalonia.Tests;

/// <summary>EMI's welcome show (WPF Services/FirstShow): the stage opens with the real desk EMI presenting,
/// plays the script's beats on a stepped clock, and Stop, panic and Esc close it and hand her back.</summary>
public sealed class FirstShowStageTests
{
    [Fact]
    public Task OpensPlaysTheScriptAndStopHandsEmiBack() => Run(stage =>
    {
        var emi = EmiDeskService.Instance.Window!;
        Assert.True(stage.IsVisible);
        Assert.True(emi.PresentationActive);
        double t = 0; stage.Clock = () => t;
        t = S.GreetAfter + .1; stage.Frame();
        Assert.Equal(Loc.Get("first_show_greeting"), stage.SpeechText);
        Assert.Equal(2, stage.ChoiceButtons.Count);

        stage.Start();
        t += S.Beats[3]; stage.Frame();
        Assert.Equal(Loc.Get("first_show_beat3"), stage.SpeechText);
        Assert.Single(stage.ChoiceButtons);   // WPF: "stop" stays up through every beat
        t += S.RevealAt + S.RevealSeconds - S.Beats[3]; stage.Frame();
        Assert.True(stage.LogoOpacity > .9);
        t += S.Length; stage.Frame();
        Assert.False(stage.Playing);
        Assert.Equal(Loc.Get("first_show_verdict"), stage.SpeechText);

        Assert.True(FirstShowService.Stop());
        Assert.False(FirstShowService.IsActive);
        Assert.False(stage.IsVisible);
        Assert.False(emi.PresentationActive);
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task PanicAndEscapeCloseTheShow(bool panic) => Run(stage =>
    {
        if (panic) PanicSurfaces.StopAll("test");
        else stage.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.False(FirstShowService.IsActive);
        Assert.False(stage.IsVisible);
        Assert.False(EmiDeskService.Instance.Window!.PresentationActive);
    });

    /// <summary>WPF MainWindow.xaml.cs:546-553: the show follows the first-run wizard once the age gate is
    /// accepted, and only on the launch that claimed the first run.</summary>
    [Fact]
    public Task ShownOnceAfterTheFirstRunWizard() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var service = new SettingsService();
        var old = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => service;
        MainShellWindow? shell = null, second = null;
        int opened = 0;
        try
        {
            CoreSettings.Current.Welcomed = false;
            CoreSettings.Current.HasAcceptedAgeVerification = false;
            shell = new MainShellWindow { OpenWelcomeShow = _ => { opened++; return true; } };
            shell.Show();
            await Until(() => shell.OwnedWindows.OfType<FirstRunWizard>().Any());
            var wizard = shell.OwnedWindows.OfType<FirstRunWizard>().Single();
            Assert.Equal(0, opened);
            wizard.FindControl<CheckBox>("ChkAgeConfirm")!.IsChecked = true;
            wizard.FindControl<Button>("BtnNext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            wizard.Close();
            await Until(() => opened > 0);
            Assert.Equal(1, opened);

            second = new MainShellWindow { OpenWelcomeShow = _ => { opened++; return true; } };
            second.Show();
            for (int i = 0; i < 20; i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
            Assert.Empty(second.OwnedWindows.OfType<FirstRunWizard>());
            Assert.Equal(1, opened);
        }
        finally
        {
            foreach (var w in new[] { shell, second }) { foreach (var o in w?.OwnedWindows.ToList() ?? new()) o.Close(); w?.RequestExit(); }
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate(); CoreSettings.ServiceProvider = old;
        }
    });

    private static async Task Until(Func<bool> probe)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);   // P73: bounded by time
        while (!probe() && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
        Assert.True(probe(), "timed out");
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static Task Run(Action<FirstShowStageWindow> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        var old = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => service;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;   // no entrance summon: she is "arrived" at once
            CoreSettings.Current.MasterVolume = 0;
            FirstShowService.RequireClickThrough = false;
            Assert.True(FirstShowService.Open());
            Assert.True(FirstShowService.IsActive);
            body(FirstShowService.Window!);
            return Task.CompletedTask;
        }
        finally
        {
            FirstShowService.Stop();
            FirstShowService.RequireClickThrough = true;
            EmiDeskService.Instance.Window?.ShutDown();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate(); CoreSettings.ServiceProvider = old;
        }
    });
}
