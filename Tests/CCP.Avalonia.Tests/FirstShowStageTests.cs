using System;
using System.Collections.Generic;
using System.IO;
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
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Fyp;
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
        var home = (EmiState.Current.WinLeftPx, EmiState.Current.WinTopPx);
        emi.SavePlacement();   // WPF EmiDeskWindow.xaml.cs:787: the show's spot is never saved as her home
        Assert.Equal(home, (EmiState.Current.WinLeftPx, EmiState.Current.WinTopPx));

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
        return Task.CompletedTask;
    });

    /// <summary>WPF Load :145 / Bundled :206 -> Confirm :222 -> Start: pictures load through the seams, the
    /// warning names the stop key, "ready" starts the show, and the flash beat plays its cue.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PicturesThenWarningThenTheShow(bool bundled) => Run(async stage =>
    {
        var pics = Enumerable.Range(0, 4).Select(i => ContentLocator.Resolve(Path.Combine("Resources", "web", "backroom", "stations", "slot", "fallback", $"gif{i}.webp"))).ToArray();
        stage.BundledPictures = () => pics;
        stage.FetchFeed = (_, _, _) => Task.FromResult<IReadOnlyList<FypAssetManifest.Entry>>(pics.Select(p => new FypAssetManifest.Entry { Url = p }).ToList());
        stage.Download = (url, ct) => FirstShowStageWindow.SaveCapped(File.OpenRead(url), ct);
        var played = new List<string>();
        stage.Audio.Play = (path, _, _) => { played.Add(Path.GetFileName(path)); return () => { }; };
        CoreSettings.Current.MasterVolume = 50;
        double t = S.GreetAfter + .1; stage.Clock = () => t; stage.Frame();
        await (bundled ? stage.Bundled() : stage.Load("censored"));
        Assert.Equal(Loc.Get("first_show_warning") + S.PanicLabel(CoreSettings.Current.PanicKey) + " " + Loc.Get("first_show_panicHint"), stage.SpeechText);
        stage.ChoiceButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // "ready"
        Assert.True(stage.Playing);
        t += S.FlashAt + .01; stage.Frame();
        Assert.True(played.Count(p => p == "chip_pop.mp3") >= 2, string.Join(",", played));   // the beat's cue + a picture flash
    });

    [Fact]
    public Task APictureOverTheCapIsDroppedAndLeavesNoFile() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var before = Directory.Exists(FirstShowStageWindow.TempDir) ? Directory.GetFiles(FirstShowStageWindow.TempDir).Length : 0;
        Assert.Null(await FirstShowStageWindow.SaveCapped(new MemoryStream(new byte[FirstShowStageWindow.MaxPictureBytes + 1]), default));
        Assert.Equal(before, Directory.GetFiles(FirstShowStageWindow.TempDir).Length);
        var ok = await FirstShowStageWindow.SaveCapped(new MemoryStream(new byte[16]), default);
        Assert.True(File.Exists(ok)); File.Delete(ok!);
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
        return Task.CompletedTask;
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

    private static Task Run(Func<FirstShowStageWindow, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
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
            await body(FirstShowService.Window!);
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
