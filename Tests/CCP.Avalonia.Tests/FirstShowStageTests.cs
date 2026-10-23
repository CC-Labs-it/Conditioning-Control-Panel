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

    /// <summary>P51 + alias: the panic key stored as "Enter" (Avalonia prints the key as "Return") closes the
    /// show even while a choice button is focused, and that button is not clicked.</summary>
    [Fact]
    public Task PanicKeyBoundToEnterBeatsAFocusedChoice() => Run(stage =>
    {
        double t = S.GreetAfter + .1; stage.Clock = () => t; stage.Frame();
        var yes = stage.ChoiceButtons[0];
        var clicked = false;
        yes.Click += (_, _) => clicked = true;
        yes.Focus();
        Assert.True(yes.IsFocused);
        stage.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "");
        Assert.False(clicked, "the focused choice was not activated");
        Assert.False(FirstShowService.IsActive);
        Assert.False(stage.IsVisible);
        return Task.CompletedTask;
    }, panicKey: "Enter");

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

    /// <summary>WPF FirstShowEffects.Tick/Render: sparks on every spawn, the brain-drain beat (11-15 s), the two
    /// floating words and the subliminal flash from 15 s, the spiral under the pink beat, the reveal burst.</summary>
    [Fact]
    public Task TheCarriedEffectsRunOnTheScript() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var service = new SettingsService();
        var old = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => service;
        var fx = new FirstShowEffects(_ => { }) { Width = 1600, Height = 900, Particles = () => true };
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            await fx.LoadAsync(Enumerable.Range(0, 4).Select(i => ContentLocator.Resolve(Path.Combine("Resources", "web", "backroom", "stations", "slot", "fallback", $"gif{i}.webp"))).ToArray());
            Assert.True(fx.HasSpiral);
            fx.Particles = () => false;
            fx.Tick(S.FlashAt, .016);
            Assert.Equal(0, fx.SparkCount);                        // MotionFx.AllowParticles off: a spawn, no sparks
            fx.Particles = () => true;
            fx.Tick(S.FlashAt + S.FastFlashEvery, .016);
            Assert.True(fx.SparkCount > 0);                        // the spawn burst
            Assert.Equal(0, fx.DrainAlpha);
            fx.Tick(S.DrainAt + 1, .016);
            Assert.True(fx.DrainAlpha > .2);
            Assert.Equal(0, fx.WordCount);
            fx.Tick(S.WordsAt, .016); fx.Tick(S.WordsAt + .1, .016);
            Assert.Equal(0, fx.DrainAlpha);
            Assert.Equal(2, fx.WordCount);
            Assert.Equal(.7, fx.SubliminalAlpha, 2);
            var host = new Window { Width = 1600, Height = 900, Content = fx };
            host.Show();
            Dispatcher.UIThread.RunJobs();
            using (var bmp = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(400, 225))) bmp.Render(fx);
            host.Content = null; host.Close();
            fx.Tick(S.SubliminalUntil + .5, .016);
            Assert.Equal(0, fx.SubliminalAlpha);
            fx.Tick(S.RevealAt, .016);
            Assert.True(fx.SparkCount > 60);                       // six bursts and the ripple
        }
        finally { fx.Dispose(); service.SaveImmediate(); CoreSettings.ServiceProvider = old; }
    });

    /// <summary>WPF Guide.cs: verdict -> "Show me around" walks Core WelcomeSteps on the real shell, rings the
    /// first unlocked feature card's help button (lit as a preview), and "sessions" closes on the Presets tab.</summary>
    [Fact]
    public Task TheGuideWalksTheRealShell() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var service = new SettingsService();
        var old = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => service;
        MainShellWindow? shell = null;
        try
        {
            CoreSettings.Current.Welcomed = true;
            CoreSettings.Current.HasAcceptedAgeVerification = true;
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            CoreSettings.Current.MasterVolume = 0;
            FirstShowService.RequireClickThrough = false;
            shell = new MainShellWindow { OpenWelcomeShow = _ => false };
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(FirstShowService.Open(shell));
            var stage = FirstShowService.Window!;
            double t = S.GreetAfter + .1; stage.Clock = () => t; stage.Frame();
            stage.Start(); t += S.Length + .1; stage.Frame();
            Click(stage, 0);                                       // "liked"
            Assert.Equal(Loc.Get("first_show_replyLiked"), stage.SpeechText);
            Click(stage, 0);                                       // "show me around"
            Assert.Equal(0, stage.GuideIndex);
            Assert.Equal("settings", shell.CurrentTab);
            Assert.Equal(Loc.Get("first_show_guideHelp"), stage.SpeechText);
            Assert.Single(stage.ChoiceButtons);                    // a hover step: only "finish"
            await Until(() => { t += .05; stage.Frame(); return stage.GuideTarget != null; });
            Assert.Equal("BtnHelp", stage.GuideTarget!.Name);
            var card = stage.PreviewCard!;
            Assert.True(card.TutorialPreview);
            t += .05; stage.Frame();
            Assert.NotNull(stage.HighlightTarget);
            stage.FoundHelp();
            Assert.Equal(Loc.Get("first_show_foundHelp"), stage.SpeechText);
            Click(stage, 0);                                       // "next"
            Assert.False(card.TutorialPreview);
            Assert.Equal("assets", shell.CurrentTab);
            Assert.Equal(Loc.Get("first_show_guideMedia"), stage.SpeechText);
            Click(stage, stage.ChoiceButtons.Count - 1);           // "finish the tour"
            Assert.Equal(Loc.Get("first_show_whereNext"), stage.SpeechText);
            Assert.Equal(3, stage.ChoiceButtons.Count);
            Click(stage, 0);                                       // "sessions"
            Assert.False(FirstShowService.IsActive);
            Assert.Equal("presets", shell.CurrentTab);
        }
        finally
        {
            FirstShowService.Stop();
            FirstShowService.RequireClickThrough = true;
            EmiDeskService.Instance.Window?.ShutDown();
            foreach (var o in shell?.OwnedWindows.ToList() ?? new()) o.Close();
            shell?.RequestExit();
            Dispatcher.UIThread.RunJobs();
            service.SaveImmediate(); CoreSettings.ServiceProvider = old;
        }
    });

    [Fact]
    public Task EachLandedLineSpeaksOnceAndReplayNeverDuplicatesTheStage() => Run(stage =>
    {
        CoreSettings.Current.MasterVolume = 50;
        var clips = new List<string>();
        stage.Audio.Play = (path, volume, tag) =>
        {
            if (tag == "first-show-voice")
            {
                Assert.Equal(.5f, volume);
                Assert.Equal(EmiVoxSynthesis.WriteWav(EmiVoxSynthesis.RenderBurst(EmiVoxSynthesis.MakeScore(stage.SpeechText, "celebration"))), File.ReadAllBytes(path));
                clips.Add(path);
            }
            return () => { };
        };
        double t = 1; stage.Clock = () => t;
        for (int i = 0; i < 4; i++) { stage.Frame(); t += .3; }
        Assert.Single(clips);
        Assert.True(FirstShowService.Open());
        Assert.Same(stage, FirstShowService.Window);
        stage.Start();
        double start = t;
        foreach (var beat in S.Beats)
        {
            t = start + beat; stage.Frame();
            for (int i = 0; i < 4; i++) { t += .2; stage.Frame(); }
        }
        Assert.Equal(1 + S.Beats.Length, clips.Count);
        Assert.Equal(clips.Count, clips.Distinct().Count());
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ClosingOrPanicStopsSpeechAndDeletesItsCache(bool panic) => Run(stage =>
    {
        CoreSettings.Current.MasterVolume = 50;
        int stopped = 0;
        stage.Audio.Play = (_, _, _) => () => stopped++;
        stage.Audio.Speak("hello", "idle");
        stage.Audio.Cue("flash");
        var dir = stage.Audio.CacheDirectory;
        Assert.Single(Directory.GetFiles(dir));
        if (panic) PanicSurfaces.StopAll("test"); else stage.Close();
        Assert.Equal(2, stopped);
        Assert.False(Directory.Exists(dir));
        return Task.CompletedTask;
    });

    [Fact]
    public Task LiveBubbleRegionsReachPopAndClearOnGatherAndClose() => Run(async stage =>
    {
        var regions = Array.Empty<PixelRect>();
        stage.SetInputRects = rects => regions = rects;
        await stage.Bundled();
        double t = 1; stage.Clock = () => t; stage.Start();
        t += S.BubblesAt + .1; stage.Frame();
        t += .1; stage.Frame();
        Assert.NotEmpty(regions);
        var bubble = regions[regions.Length / 2];
        Assert.False(ConditioningControlPanel.Avalonia.Platform.Win32Overlay.Hits(regions, regions.Length, 0, 0));
        Assert.True(ConditioningControlPanel.Avalonia.Platform.Win32Overlay.Hits(regions, regions.Length, bubble.X + bubble.Width / 2, bubble.Y));
        // Native input delivers this point to the transparent stage canvas, whose handler calls Pop.
        var before = regions.Length;
        stage.MouseDown(new Point(bubble.X + bubble.Width / 2, bubble.Y), MouseButton.Left);
        stage.MouseUp(new Point(bubble.X + bubble.Width / 2, bubble.Y), MouseButton.Left);
        Assert.True(regions.Length < before);
        t += .8; stage.Frame(); t += .1; stage.Frame();
        Assert.NotEmpty(regions);
        t = 1 + S.GatherAt; stage.Frame(); Assert.Empty(regions);
        stage.Close(); Assert.Empty(regions);
    });

    [Fact]
    public Task DrainUsesTheSharedVisualOnlyDuringTheSceneAndStopsOnClose() => Run(async stage =>
    {
        await stage.Bundled();
        double t = 1; stage.Clock = () => t; stage.Start();
        t += S.DrainAt + .4; stage.Frame();
        var first = stage.Drain.Pump!;
        Assert.NotNull(first);
        Assert.Equal(1, stage.Drain.Opacity, 5);
        Assert.True(stage.Drain.Veil.IsVisible); // headless/Wayland has no capture factory
        t = 1 + S.DrainDeepAt + .4; stage.Frame();
        Assert.True(first.Stopped);
        var deep = stage.Drain.Pump!;
        t = 1 + S.WordsAt; stage.Frame();
        Assert.True(deep.Stopped); Assert.Null(stage.Drain.Pump);
        Assert.Equal(0, stage.Drain.Opacity);
    });

    [Fact]
    public Task HelpAndBookReplayButtonsUseTheExistingService() => Run(stage =>
    {
        CoreSettings.Current.Welcomed = true;
        CoreSettings.Current.HasAcceptedAgeVerification = true;
        var shell = new MainShellWindow();
        EmiBookWindow? book = null;
        try
        {
            shell.Show(); shell.SetTutorialOverlay(true);
            shell.FindControl<Button>("BtnFirstShowReplay")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(shell.FindControl<Border>("MainTutorialOverlay")!.IsVisible);
            Assert.Same(stage, FirstShowService.Window);
            book = new EmiBookWindow(null); book.Show();
            book.FindControl<Button>("BtnFirstShow")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(book.IsVisible);
            Assert.Same(stage, FirstShowService.Window);
        }
        finally { book?.Kill(); shell.RequestExit(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task UnsupportedNativeInputRefusesTheStageBeforeBorrowingEmi() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        FirstShowService.RequireClickThrough = true;
        Assert.False(FirstShowService.Open()); // headless, like native Wayland, has no native input region seam
        Assert.False(FirstShowService.IsActive);
        return Task.CompletedTask;
    });

    private static void Click(FirstShowStageWindow stage, int i) => stage.ChoiceButtons[i].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

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

    private static Task Run(Func<FirstShowStageWindow, Task> body, string? panicKey = null) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var service = new SettingsService();
        var old = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => service;
        try
        {
            if (panicKey is not null) CoreSettings.Current.PanicKey = panicKey;   // read by the stage's ctor
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
