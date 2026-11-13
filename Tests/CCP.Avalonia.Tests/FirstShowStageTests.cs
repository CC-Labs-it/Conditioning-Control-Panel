using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.FirstShow;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Fyp;
using Xunit;
using S = ConditioningControlPanel.Services.FirstShow.FirstShowScript;

namespace CCP.Avalonia.Tests;

/// <summary>EMI's welcome show (WPF Services/FirstShow): the stage opens with the real desk EMI presenting,
/// plays the script's beats on a stepped clock, and Stop, panic and Esc close it and hand her back.</summary>
public sealed partial class FirstShowStageTests
{
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
        Assert.Equal(EmiVox.WriteWav(EmiVox.RenderBurst(EmiVox.MakeScore(stage.SpeechText, "celebration"))), File.ReadAllBytes(voice.Path));
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
        Assert.Equal(!bundled, stage.Credit.Shown);   // WPF FirstShowDesktopWindow :236: "via Scrolller" only on fetched pictures
        t += S.FlashAt + .01; stage.Frame();
        Assert.True(played.Count(p => p == "chip_pop.mp3") >= 2, string.Join(",", played));   // the beat's cue + a picture flash
        t += S.Length; stage.Frame();
        Assert.False(stage.Credit.Shown);   // WPF :264: the mark goes when the show ends
    });

    /// <summary>WPF FirstShowDesktopWindow Start :236: a show of Scrolller pictures carries the "via Scrolller" credit
    /// (never the bundled one); the copies come from the device cache, carry the remote-copy prefix the 24 h sweep
    /// knows, and nothing is asked of Scrolller while the switch is off.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AScrolllerShowIsCreditedAndItsCopiesComeFromTheDeviceCache(bool bundled) => Run(async stage =>
    {
        var pics = Enumerable.Range(0, 4).Select(i => ContentLocator.Resolve(Path.Combine("Resources", "web", "backroom", "stations", "slot", "fallback", $"gif{i}.webp"))).ToArray();
        var real = FirstShowStageWindow.DeviceCacheFetch;
        int i = 0;
        FirstShowStageWindow.DeviceCacheFetch = (_, _, _) => Task.FromResult<string?>(pics[i++ % pics.Length]);
        try
        {
            stage.BundledPictures = () => pics;
            stage.FetchFeed = (_, _, _) => Task.FromResult<IReadOnlyList<FypAssetManifest.Entry>>(
                Enumerable.Range(0, 4).Select(n => new FypAssetManifest.Entry { Url = $"https://images.scrolller.com/x/{n}.webp" }).ToList());
            double t = S.GreetAfter + .1; stage.Clock = () => t; stage.Frame();
            await (bundled ? stage.Bundled() : stage.Load("censored"));
            stage.ChoiceButtons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // "ready"
            Assert.True(stage.Playing);
            Assert.Equal(!bundled, stage.CreditShown);
            t += S.Length + 1; stage.Frame();
            Assert.False(stage.CreditShown);
        }
        finally { FirstShowStageWindow.DeviceCacheFetch = real; }
    });

    /// <summary>The owner's fresh-profile run (Linux beta, 2026-11-06): the feed answers but the device cache refuses every
    /// picture at once (no remote-media consent yet, exactly as WPF RemoteMediaCache -> ScrolllerDiskCache.GetOrFetchAsync
    /// does). The card is WPF's error (FirstShowDesktopWindow.cs:190-191: the menu again, built-in pictures, later) and
    /// nothing else: a progress report the loader posted before it gave up must not bring back "0/8 pictures ready".</summary>
    [Fact]
    public Task EveryPictureRefusedShowsOnlyTheErrorAndItsThreeChoices() => Run(async stage =>
    {
        var real = FirstShowStageWindow.DeviceCacheFetch;
        int asked = 0;
        FirstShowStageWindow.DeviceCacheFetch = (_, _, _) => { asked++; return Task.FromResult<string?>(null); };   // refused
        try
        {
            ConditioningControlPanel.Services.Fyp.Online.ScrolllerPolicy.Apply(true, null);
            stage.FetchFeed = (_, _, _) => Task.FromResult<IReadOnlyList<FypAssetManifest.Entry>>(
                Enumerable.Range(0, 8).Select(n => new FypAssetManifest.Entry { Url = $"https://images.scrolller.com/x/{n}.webp" }).ToList());
            double t = S.GreetAfter + .1; stage.Clock = () => t; stage.Frame();
            await stage.Load("HypnoGoneWild");
            for (int i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }   // let any posted progress land
            Assert.Equal(16, asked);   // two attempts of eight, never a fetch around the device cache
            Assert.Equal(Loc.Get("first_show_error"), stage.SpeechText);
            Assert.False(stage.LoadingShown);
            Assert.Equal(new[] { Loc.Get("first_show_pickQuestion"), Loc.Get("first_show_bundled"), Loc.Get("first_show_later") },
                stage.ChoiceButtons.Select(b => ((TextBlock)b.Content!).Text).ToArray());
        }
        finally { FirstShowStageWindow.DeviceCacheFetch = real; }
    });

    /// <summary>Owner decision 2026-11-06 (option B), the same as WPF FirstShowDesktopWindow.Pick: picking a preset
    /// without remote-media consent first asks the Assets page's consent card, word for word. Yes records the consent
    /// and the mixed source, then loads that preset's feed; no plays the built-in pictures and changes nothing.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task APresetWithoutConsentAsksTheConsentCardFirst(bool yes) => Run(async stage =>
    {
        var s = CoreSettings.Current;
        s.MediaSource = "local"; s.RemoteMediaConsented = false; s.FypOnlineConsented = false;
        var fetched = new List<string>();
        var asked = new List<(string, string)>();
        await WithPictures(stage, fetched, async () =>
        {
            stage.ConsentOverride = (title, message) => { asked.Add((title, message)); return Task.FromResult(yes); };
            await stage.Pick("HypnoGoneWild");
        });
        Assert.Equal(new[] { ConditioningControlPanel.Avalonia.Views.Tabs.AssetsTabView.RemoteMediaConsentText() }, asked);
        Assert.Equal(yes, s.RemoteMediaConsented);
        Assert.False(s.FypOnlineConsented);
        Assert.Equal(yes ? "mixed" : "local", s.MediaSource);
        Assert.Equal(yes ? new[] { "HypnoGoneWild" } : Array.Empty<string>(), fetched.Distinct().ToArray());
        AssertWarningThenStart(stage, credited: yes);   // the built-in pictures carry no "via Scrolller"
    });

    /// <summary>Already consented with a fetching source: no card, the preset loads, the source stays the user's.</summary>
    [Fact]
    public Task AConsentedSourceLoadsThePresetWithoutAsking() => Run(async stage =>
    {
        var s = CoreSettings.Current;
        s.MediaSource = "mixed"; s.RemoteMediaConsented = true;
        var fetched = new List<string>();
        int asked = 0;
        await WithPictures(stage, fetched, async () =>
        {
            stage.ConsentOverride = (_, _) => { asked++; return Task.FromResult(false); };
            await stage.Pick("cosplay");
        });
        Assert.Equal(0, asked);
        Assert.Equal("mixed", s.MediaSource);
        Assert.Equal(new[] { "cosplay" }, fetched.Distinct().ToArray());
        AssertWarningThenStart(stage, credited: true);
    });

    /// <summary>Not consent, so not asked: the licence switch off, a spent share or Offline mode load as before
    /// (and then fall back), and leave consent and the source untouched.</summary>
    [Theory]
    [InlineData("licence")]
    [InlineData("spent")]
    [InlineData("offline")]
    public Task OtherBlockersAreNotAskedAbout(string blocker) => Run(async stage =>
    {
        var s = CoreSettings.Current;
        s.MediaSource = "local"; s.RemoteMediaConsented = false; s.FypOnlineConsented = false;
        s.OfflineMode = blocker == "offline";
        var fetched = new List<string>();
        int asked = 0;
        await WithPictures(stage, fetched, async () =>
        {
            FirstShowPresets.LicenceOn = () => blocker != "licence";
            FirstShowPresets.ShareSpent = () => blocker == "spent";
            stage.ConsentOverride = (_, _) => { asked++; return Task.FromResult(true); };
            await stage.Pick("sissyhypno");
        });
        s.OfflineMode = false;
        Assert.Equal(0, asked);
        Assert.False(s.HasRemoteMediaConsent);
        Assert.Equal("local", s.MediaSource);
        Assert.Equal(new[] { "sissyhypno" }, fetched.Distinct().ToArray());   // Load ran, exactly as before
    });

    /// <summary>A feed of local stand-in pictures, the licence on and the share open, around <paramref name="body"/>.</summary>
    private static async Task WithPictures(FirstShowStageWindow stage, List<string> fetched, Func<Task> body)
    {
        var (licence, spent) = (FirstShowPresets.LicenceOn, FirstShowPresets.ShareSpent);
        FirstShowPresets.LicenceOn = () => true; FirstShowPresets.ShareSpent = () => false;
        try
        {
            var pics = Enumerable.Range(0, 4).Select(i => ContentLocator.Resolve(Path.Combine("Resources", "web", "backroom", "stations", "slot", "fallback", $"gif{i}.webp"))).ToArray();
            stage.BundledPictures = () => pics;
            stage.FetchFeed = (preset, _, _) =>
            {
                fetched.Add(preset);
                return Task.FromResult<IReadOnlyList<FypAssetManifest.Entry>>(pics.Select(p => new FypAssetManifest.Entry { Url = p }).ToList());
            };
            stage.Download = (url, ct) => FirstShowStageWindow.SaveCapped(File.OpenRead(url), ct);
            double t = S.GreetAfter + .1; stage.Clock = () => t; stage.Frame();
            await body();
        }
        finally { FirstShowPresets.LicenceOn = licence; FirstShowPresets.ShareSpent = spent; }
    }

    private static void AssertWarningThenStart(FirstShowStageWindow stage, bool credited)
    {
        Assert.Equal(Loc.Get("first_show_warning") + S.PanicLabel(CoreSettings.Current.PanicKey) + " " + Loc.Get("first_show_panicHint"), stage.SpeechText);
        Click(stage, 0);   // "ready"
        Assert.True(stage.Playing);
        Assert.Equal(credited, stage.CreditShown);
    }

    [Fact]
    public Task AScrolllerCopyIsRefusedWhileTheSwitchIsOffAndPrefixedWhenOn() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var src = Path.Combine(Path.GetTempPath(), "ccp-fs-src-" + Guid.NewGuid().ToString("N") + ".jpg");
        File.WriteAllBytes(src, new byte[] { 0xFF, 0xD8, 0xFF, 0 });
        var real = FirstShowStageWindow.DeviceCacheFetch;
        int asked = 0;
        FirstShowStageWindow.DeviceCacheFetch = (_, max, _) => { asked++; Assert.Equal(FirstShowStageWindow.MaxPictureBytes, max); return Task.FromResult<string?>(src); };
        try
        {
            ConditioningControlPanel.Services.Fyp.Online.ScrolllerPolicy.Apply(false, null);
            Assert.Null(await FirstShowStageWindow.CopyFromDeviceCacheAsync("https://images.scrolller.com/a/1.jpg", default));
            Assert.Equal(0, asked);
            ConditioningControlPanel.Services.Fyp.Online.ScrolllerPolicy.Apply(true, null);
            var copy = await FirstShowStageWindow.CopyFromDeviceCacheAsync("https://images.scrolller.com/a/1.jpg", default);
            Assert.NotNull(copy);
            Assert.True(FirstShowStageWindow.IsScrolllerCopy(copy!));
            Assert.Equal(FirstShowStageWindow.TempDir, Path.GetDirectoryName(copy));
            File.Delete(copy!);
        }
        finally
        {
            ConditioningControlPanel.Services.Fyp.Online.ScrolllerPolicy.Apply(true, null);
            FirstShowStageWindow.DeviceCacheFetch = real;
            File.Delete(src);
        }
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
            StartupLadder.ModalWindowReady = () => shell?.IsVisible == true;
            shell = new MainShellWindow { OpenWelcomeShow = _ => { opened++; return true; } };
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            ((StartupLadder.TestClock)StartupLadder.Time).Advance(StartupLadder.ModalSettle);
            await Until(() => shell.OwnedWindows.OfType<FirstRunWizard>().Any());
            var wizard = shell.OwnedWindows.OfType<FirstRunWizard>().Single();
            Assert.Equal(0, opened);
            wizard.FindControl<CheckBox>("ChkAgeConfirm")!.IsChecked = true;
            wizard.FindControl<Button>("BtnNext")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            wizard.Close();
            for (int i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
            ((StartupLadder.TestClock)StartupLadder.Time).Advance(StartupLadder.ModalSettle);
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
    public Task DesktopEscapeStopsWithoutSpeechFocus() => Run(stage =>
    {
        stage.ReadDesktopInput = () => (new PixelPoint(0, 0), false, true);
        stage.Frame();
        Assert.False(FirstShowService.IsActive);
        Assert.False(EmiDeskService.Instance.Window!.PresentationActive);
        return Task.CompletedTask;
    });

    [Fact]
    public Task OnlyLiveBubbleCirclesTakeInputAndAPressPopsThem() => Run(async stage =>
    {
        stage.BundledPictures = () => Enumerable.Range(0, 4).Select(i => ContentLocator.Resolve(Path.Combine("Resources", "web", "backroom", "stations", "slot", "fallback", $"gif{i}.webp"))).ToArray();
        await stage.Bundled();
        double t = 0; stage.Clock = () => t;
        stage.Start();
        stage.Frame();
        Assert.Empty(stage.InputRects);
        var pushes = stage.InputPushes;
        stage.Frame();
        Assert.Equal(pushes, stage.InputPushes);   // empty stays empty: no region re-sent
        t = S.BubblesAt; stage.Frame();
        Assert.NotEmpty(stage.InputRects);
        var rows = stage.Effects!.BubbleInputRects(1);
        Assert.True(rows.Length < rows.Sum(r => r.Height), $"{rows.Length} rects, no rows merged");
        var center = rows[rows.Length / 2];
        var cues = new List<string>();
        CoreSettings.Current.MasterVolume = 50;
        stage.Audio.Play = (path, _, _) => { cues.Add(Path.GetFileName(path)); return () => { }; };
        var at = new Point(center.X + center.Width / 2.0, center.Y);
        stage.MouseDown(at, global::Avalonia.Input.MouseButton.Right);
        stage.MouseUp(at, global::Avalonia.Input.MouseButton.Right);
        Assert.NotEmpty(stage.InputRects);
        Assert.Empty(cues);
        // The native region feeds the same routed press as a real platform window.
        stage.MouseDown(new Point(center.X + center.Width / 2.0, center.Y), global::Avalonia.Input.MouseButton.Left);
        stage.MouseUp(new Point(center.X + center.Width / 2.0, center.Y), global::Avalonia.Input.MouseButton.Left);
        Assert.Empty(stage.InputRects);
        Assert.Contains("Pop3.mp3", cues);
        t += S.BubbleEvery; stage.Frame();
        Assert.NotEmpty(stage.InputRects);
        t = S.GatherAt; stage.Frame();
        Assert.Empty(stage.InputRects);
        pushes = stage.InputPushes;
        t += .1; stage.Frame();
        Assert.Equal(pushes, stage.InputPushes);
    });

    [Fact]
    public Task HelpReplayClosesItsPanelBeforeOpeningTheShow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var shell = new MainShellWindow();
        var overlay = shell.FindControl<Border>("MainTutorialOverlay")!;
        overlay.IsVisible = true;
        int opened = 0;
        shell.OpenWelcomeShow = owner => { Assert.Same(shell, owner); Assert.False(overlay.IsVisible); opened++; return true; };
        shell.FindControl<Button>("BtnFirstShowReplay")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, opened);
        shell.RequestExit();
        return Task.CompletedTask;
    });

    [Fact]
    public Task CuesUseModsAndHonorSuppressionAndMaster() => Run(stage =>
    {
        var path = Path.GetTempFileName();
        int played = 0;
        CoreSettings.Current.MasterVolume = 50;
        CoreModArt.AudioOverridePathProvider = _ => path;
        CoreAudio.IsOutputSuppressedProvider = () => false;
        stage.Audio.Play = (p, volume, tag) =>
        {
            Assert.Equal(path, p);
            Assert.Equal(.16f, volume);
            Assert.Equal("first-show-cue", tag);
            played++;
            return () => { };
        };
        try
        {
            stage.Audio.Cue("pop");
            Assert.Equal(1, played);
            CoreAudio.IsOutputSuppressedProvider = () => true;
            stage.Audio.Cue("pop");
            Assert.Equal(1, played);
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreSettings.Current.MasterVolume = 0;
            stage.Audio.Cue("pop");
            Assert.Equal(1, played);
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    });

    private static void Click(FirstShowStageWindow stage, int i) => stage.ChoiceButtons[i].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task Until(Func<bool> probe)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);   // P73: bounded by time
        while (!probe() && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
        Assert.True(probe(), "timed out");
    }

    private static void Setup() => AmbientBeatLoopsTests.Setup();


#if DEBUG
    [Fact]
    public Task DebugPreviewOwnsTheShowAndClosesEmi() => Run(_ =>
    {
        FirstShowService.Stop();
        EmiDeskService.Instance.Window?.ShutDown();
        var desktop = new global::Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime
        {
            Args = new[] { "--first-show-preview" },
        };
        try
        {
            Assert.True(ConditioningControlPanel.Avalonia.App.TryStartFirstShowPreview(desktop));
            var stage = Assert.IsType<FirstShowStageWindow>(desktop.MainWindow);
            Assert.Same(FirstShowService.Window, stage);
            Assert.True(stage.IsVisible);
            Assert.Equal(ShutdownMode.OnMainWindowClose, desktop.ShutdownMode);
            Assert.True(EmiDeskService.Instance.Window!.PresentationActive);
            // Keep the suite's shared dispatcher alive while testing the owned-window cleanup.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            stage.Close();
            Assert.False(FirstShowService.IsActive);
            Assert.Null(EmiDeskService.Instance.Window);
            Assert.False(EmiDeskService.Instance.IsOut);
        }
        finally { desktop.MainWindow = null; desktop.Dispose(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task DebugPreviewGlobalEscapeStopsTheShowAndItsHook() => Run(_ =>
    {
        FirstShowService.Stop();
        EmiDeskService.Instance.Window?.ShutDown();
        using var desktop = new global::Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime
        {
            Args = new[] { "--first-show-preview" },
        };
        int starts = 0, stops = 0;
        FirstShowStageWindow.TestWindowsKeys = true;
        try
        {
            Assert.True(ConditioningControlPanel.Avalonia.App.TryStartFirstShowPreview(desktop,
                () => ConditioningControlPanel.Avalonia.App.StartFirstShowPreviewHook(true, () => false,
                    () => { starts++; return true; }, () => stops++)));
            Assert.Equal(1, starts);
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ConditioningControlPanel.Avalonia.Platform.Win32PanicKey.OnDown(0x41, DateTime.UtcNow);
            Dispatcher.UIThread.RunJobs();
            Assert.True(FirstShowService.IsActive);
            ConditioningControlPanel.Avalonia.Platform.Win32PanicKey.OnDown(0x1B, DateTime.UtcNow);
            Dispatcher.UIThread.RunJobs();
            Assert.False(FirstShowService.IsActive);
            Assert.Equal(1, stops);
            Assert.Null(EmiDeskService.Instance.Window);
            ConditioningControlPanel.Avalonia.Platform.Win32PanicKey.OnDown(0x1B, DateTime.UtcNow);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, stops);
        }
        finally
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            FirstShowService.Stop();
            FirstShowStageWindow.TestWindowsKeys = false;
            desktop.MainWindow = null;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void PreviewDoesNotOwnAnExistingHookOrStartOneOffWindows()
    {
        int starts = 0, stops = 0;
        Action? Start(bool windows, bool listening) => ConditioningControlPanel.Avalonia.App.StartFirstShowPreviewHook(
            windows, () => listening, () => { starts++; return true; }, () => stops++);
        Assert.Null(Start(false, false));
        Assert.Null(Start(true, true));
        Assert.Equal(0, starts);
        Assert.Equal(0, stops);
    }

    [Theory]
    [InlineData("--first-show-preview-extra")]
    [InlineData("--FIRST-SHOW-PREVIEW")]
    [InlineData("--smoke")]
    public Task OtherArgumentsDoNotStartPreview(string arg) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        using var desktop = new global::Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime
        {
            Args = new[] { arg },
        };
        FirstShowService.RequireClickThrough = false;
        try
        {
            Assert.False(ConditioningControlPanel.Avalonia.App.TryStartFirstShowPreview(desktop));
            Assert.Null(desktop.MainWindow);
            Assert.False(FirstShowService.IsActive);
        }
        finally
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            FirstShowService.Stop();
            EmiDeskService.Instance.Window?.ShutDown();
            FirstShowService.RequireClickThrough = true;
        }
        return Task.CompletedTask;
    });
#endif

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
