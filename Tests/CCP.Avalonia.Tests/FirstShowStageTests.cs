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
using Avalonia.Media;
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
using ConditioningControlPanel.Services.FirstShow;
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
            var helpAt = stage.GuideTarget.TranslatePoint(new Point(stage.GuideTarget.Bounds.Width / 2, stage.GuideTarget.Bounds.Height / 2), shell)!.Value;
            shell.MouseMove(helpAt);
            Assert.True(stage.GuideTarget.IsPointerOver);
            for (int i = 0; i < (int)(S.HoverSeconds / .05) - 1; i++) { t += .05; stage.Frame(); }
            Assert.Equal(Loc.Get("first_show_guideHelp"), stage.SpeechText);
            shell.MouseMove(new Point(1, 1)); // leaving resets the dwell
            t += .05; stage.Frame();
            shell.MouseMove(helpAt);
            for (int i = 0; i < (int)(S.HoverSeconds / .05) + 2; i++) { t += .05; stage.Frame(); }
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
    public Task SpeechWaitsForTheCardToSettleAndOnlySpeaksOnce() => Run(stage =>
    {
        var played = new List<(string Path, string Tag)>();
        stage.Audio.Master = () => 50;
        stage.Audio.Play = (path, _, tag) => { played.Add((path, tag)); return () => { }; };
        double t = S.GreetAfter + .1; stage.Clock = () => t;
        stage.Frame(); // motion off: the initial greeting is already settled
        played.Clear();
        Click(stage, 0); // the preset picker moves her to the centre
        stage.Frame();
        Assert.DoesNotContain(played, p => p.Tag == "first-show-voice");
        t += .2; stage.Frame();
        var voice = Assert.Single(played.Where(p => p.Tag == "first-show-voice"));
        Assert.Equal(FirstShowVoice.WriteWav(FirstShowVoice.RenderBurst(FirstShowVoice.MakeScore(stage.SpeechText, "celebration"))), File.ReadAllBytes(voice.Path));
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        t += .2; stage.Frame();
        Assert.Single(played.Where(p => p.Tag == "first-show-voice"));
        var transforms = (TransformGroup)EmiDeskService.Instance.Window!.FindControl<Grid>("BodyRoot")!.RenderTransform!;
        var bob = transforms.Children.OfType<TranslateTransform>().Last().Y;
        var ambientBob = Math.Sin(t * 2.5) * 3 + Math.Sin(t * 1.1) * 2;
        Assert.True(Math.Abs(bob - ambientBob) > .1); // WPF's 1.4 s speaking emphasis
        t += 1.5; stage.Frame();
        bob = transforms.Children.OfType<TranslateTransform>().Last().Y;
        Assert.Equal(Math.Sin(t * 2.5) * 3 + Math.Sin(t * 1.1) * 2, bob, 6);
        return Task.CompletedTask;
    });

    [Fact]
    public Task BubbleInputRegionsPopAndClearBeforeTheGuideAndEscapeStopsWithoutFocus() => Run(async stage =>
    {
        await stage.Bundled();
        var cues = new List<string>();
        stage.Audio.Master = () => 50;
        stage.Audio.Play = (path, _, tag) => { if (tag == "first-show-cue") cues.Add(Path.GetFileName(path)); return () => { }; };
        PixelRect[] regions = Array.Empty<PixelRect>();
        stage.ApplyInputRegions = (rects, count) => regions = rects.Take(count).ToArray();
        double t = 0; stage.Clock = () => t;
        stage.Start(); t = S.BubblesAt; stage.Frame(); t += .31; stage.Frame();
        var fx = stage.Effects!;
        var at = fx.BubbleCenter!.Value;
        var region = Assert.Single(regions);
        Assert.True(region.Contains(new PixelPoint((int)(at.X * stage.RenderScaling), (int)(at.Y * stage.RenderScaling))));
        stage.MouseDown(at, MouseButton.Left);
        stage.MouseUp(at, MouseButton.Left);
        Assert.Null(fx.BubbleCenter);
        Assert.Empty(regions);
        Assert.Equal(1, cues.Count(p => p == "Pop3.mp3"));
        t += S.BubbleEvery; stage.Frame(); t += .31; stage.Frame();
        Assert.Single(regions);
        t = S.GatherAt; stage.Frame();
        Assert.Empty(regions); // gathering bubbles cannot capture shell clicks
        stage.EscapeDown = () => true;
        stage.Frame();
        Assert.False(FirstShowService.IsActive);
        Assert.False(EmiDeskService.Instance.Window!.PresentationActive);
    });

    [Fact]
    public Task HelpAndBookReplayCloseTheirSurfaceBeforeOpeningTheShow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        var old = CoreSettings.ServiceProvider;
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        var shell = new MainShellWindow();
        var book = new EmiBookWindow();
        try
        {
            int opened = 0;
            shell.SetTutorialOverlay(true);
            shell.OpenWelcomeShow = owner =>
            {
                Assert.Same(shell, owner);
                Assert.False(shell.FindControl<Control>("MainTutorialOverlay")!.IsVisible);
                Assert.True(shell.FindControl<global::ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView>("SettingsTab")!.BrowserContainer.IsVisible);
                opened++; return true;
            };
            shell.FindControl<Button>("BtnFirstShowReplay")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, opened);
            book.Show();
            book.ReplayFirstShow = () => { Assert.False(book.IsVisible); opened++; return true; };
            book.FindControl<Button>("BtnFirstShow")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, opened);
        }
        finally
        {
            book.Close(); shell.RequestExit(); Dispatcher.UIThread.RunJobs();
            service.SaveImmediate(); CoreSettings.ServiceProvider = old;
        }
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
