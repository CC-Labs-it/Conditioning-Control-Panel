using System;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class ProfileBubbleParityTests : IDisposable
{
    // Capture head statics only after the suite owns Dispatcher.UIThread.
    static ProfileBubbleParityTests() => AvaloniaTestDispatcher.Run(() => { });

    private readonly SettingsService _settings = new();
    private readonly Func<SettingsService?>? _settingsBefore = CoreSettings.ServiceProvider;
    private readonly Func<bool>? _labBefore = CoreAccount.HasLabAccessProvider;
    private readonly Func<bool>? _premiumBefore = CoreAccount.HasPremiumAccessProvider;
    private readonly Func<bool>? _loginBefore = CoreAccount.IsLoggedInProvider;
    private readonly Func<string>? _nameBefore = CoreAccount.DisplayNameProvider;
    private readonly Func<string, bool> _openBefore = MainShellWindow.OpenProfileLink;
    private readonly Func<string, bool> _allowedBefore = MainShellWindow.ProfileLinkAllowed;

    private MainShellWindow Shell(MotionLevel motion = MotionLevel.Off)
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont().With(AppFonts.Options(systemEmoji: false))
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        CoreSettings.ServiceProvider = () => _settings;
        CoreAccount.HasLabAccessProvider = CoreAccount.HasPremiumAccessProvider = CoreAccount.IsLoggedInProvider = () => true;
        CoreAccount.DisplayNameProvider = () => "Bubble Test";
        _settings.Current.ShareProfilePicture = false;
        _settings.Current.ProfileCosmetics ??= new ProfileCosmetics();
        _settings.Current.AvatarEnabled = false;
        _settings.Current.MotionLevel = motion;
        _settings.Current.PerformanceMode = motion != MotionLevel.Full;
        _settings.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        var shell = new MainShellWindow();
        shell.Show();
        shell.Activate();
        Dispatcher.UIThread.RunJobs();
        return shell;
    }

    public void Dispose()
    {
        CoreSettings.ServiceProvider = _settingsBefore;
        CoreAccount.HasLabAccessProvider = _labBefore;
        CoreAccount.HasPremiumAccessProvider = _premiumBefore;
        CoreAccount.IsLoggedInProvider = _loginBefore;
        CoreAccount.DisplayNameProvider = _nameBefore;
        MainShellWindow.OpenProfileLink = _openBefore;
        MainShellWindow.ProfileLinkAllowed = _allowedBefore;
    }

    [Fact]
    public Task EquippedPresetWinsOverInitialsAndInvalidPresetFallsBack() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = Shell();
        try
        {
            _settings.Current.ProfileCosmetics.AvatarId = "avatar_bambi_1";
            shell.RefreshProfileBubble();
            var face = shell.FindControl<Ellipse>("ProfileBubbleFill")!;
            var initials = shell.FindControl<TextBlock>("ProfileBubbleInitials")!;
            Assert.IsType<ImageBrush>(face.Fill);
            Assert.False(initials.IsVisible);
            _settings.Current.ProfileCosmetics.AvatarId = "unknown-preset";
            shell.RefreshProfileBubble();
            Assert.IsNotType<ImageBrush>(face.Fill);
            Assert.True(initials.IsVisible);
            Assert.Equal("BT", initials.Text);
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task HoverShowsTheTierArtToTheLeftAndExitOrTierLossClosesIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = Shell();
        try
        {
            var badge = shell.FindControl<Image>("ProfileBubbleTierBadge")!;
            var position = badge.TranslatePoint(new Point(badge.Bounds.Width / 2, badge.Bounds.Height / 2), shell)!.Value;
            shell.MouseMove(position);
            Dispatcher.UIThread.RunJobs();
            var popup = shell.FindControl<Popup>("ProfileTierBadgePopup");
            Assert.NotNull(popup);
            Assert.True(popup!.IsOpen);
            Assert.Equal(PlacementMode.Left, popup.Placement);
            Assert.Equal(-4, popup.HorizontalOffset);
            Assert.Equal(-46, popup.VerticalOffset);
            Assert.False(popup.IsHitTestVisible);
            var big = shell.FindControl<Image>("ProfileTierBadgeBig")!;
            Assert.Same(badge.Source, big.Source);
            Assert.Equal(72, big.Height);
            Assert.Equal(1, ((ScaleTransform)big.RenderTransform!).ScaleX);
            shell.MouseMove(new Point(10, 10));
            Dispatcher.UIThread.RunJobs();
            Assert.False(popup.IsOpen);
            shell.MouseMove(position);
            Dispatcher.UIThread.RunJobs();
            Assert.True(popup.IsOpen);
            CoreAccount.HasLabAccessProvider = CoreAccount.HasPremiumAccessProvider = () => false;
            shell.RefreshProfileBubble();
            Assert.False(popup.IsOpen);
            Assert.False(badge.IsVisible);
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task OpeningTheMenuRestacksTheEnlargedTierArtOnTop() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = Shell();
        try
        {
            var badge = shell.FindControl<Image>("ProfileBubbleTierBadge")!;
            shell.MouseMove(badge.TranslatePoint(new Point(badge.Bounds.Width / 2, badge.Bounds.Height / 2), shell)!.Value);
            Dispatcher.UIThread.RunJobs();
            var tier = shell.FindControl<Popup>("ProfileTierBadgePopup")!;
            Assert.True(tier.IsOpen);
            int reopened = 0;
            tier.Opened += (_, _) => reopened++;
            shell.FindControl<Popup>("ProfileBubblePopup")!.IsOpen = true;
            Dispatcher.UIThread.RunJobs();
            // WPF RaiseProfileTierPopup: the later menu must not cover the badge; a reopen puts it on top.
            Assert.Equal(1, reopened);
            Assert.True(tier.IsOpen);
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FullMotionHoverThenCloseDoesNotThrow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = Shell(MotionLevel.Full);
        var badge = shell.FindControl<Image>("ProfileBubbleTierBadge")!;
        var position = badge.TranslatePoint(new Point(badge.Bounds.Width / 2, badge.Bounds.Height / 2), shell)!.Value;
        shell.MouseMove(position);
        Dispatcher.UIThread.RunJobs();
        Assert.True(shell.FindControl<Popup>("ProfileTierBadgePopup")!.IsOpen);
        // Closed disposes the grow token, then hides the popup (which cancels it): must not throw.
        shell.Close();
        Dispatcher.UIThread.RunJobs();
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(MotionLevel.Full, true)]
    [InlineData(MotionLevel.Reduced, false)]
    public Task ARealLevelUpBurstsOnlyWhenParticlesAreAllowed(MotionLevel motion, bool emitted) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = Shell(motion);
        try
        {
            _settings.Current.OfflineMode = true;
            _settings.Current.OfflineUsername = "bubble-parity";
            _settings.Current.PlayerLevel = 3;
            _settings.Current.PlayerXP = 0;
            Assert.True(shell.IsActive);
            ProgressionBank.Add(XpCurve.GetXPForLevel(3, XpCurve.EpochOf(_settings.Current)), "Quest");
            Dispatcher.UIThread.RunJobs();
            // The shared EventBurstLayer (560px, WPF EventFxAllowed gates) fired once on the bubble.
            Assert.Equal(emitted ? 1 : 0, shell._profileBubbleBurst.Count);
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TierWobbleUsesTheSharedBeatAndReducedMotionParksIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var previousTime = BeatLoop.Time;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var shell = Shell(MotionLevel.Full);
        try
        {
            var badge = shell.FindControl<Image>("ProfileBubbleTierBadge")!;
            var tilt = (RotateTransform)badge.RenderTransform!;
            Assert.Equal(-12, tilt.Angle);
            // WPF: an 8 s timer, and no frames between wobbles.
            Assert.True(shell._profileTierTimer!.IsEnabled);
            Assert.Equal(TimeSpan.FromSeconds(8), shell._profileTierTimer.Interval);
            Assert.DoesNotContain(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, badge));
            shell.ProfileTierWobble();
            Assert.Contains(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, badge));
            time.Advance(TimeSpan.FromSeconds(0.105));
            BeatLoop.StepAll();
            Assert.Equal(-3, tilt.Angle, 3);
            time.Advance(TimeSpan.FromTicks(5_075_000)); // FromSeconds truncates 0.5075 to one tick short
            BeatLoop.StepAll();
            // WPF's final SineEase EaseOut (linear would be -12.9).
            Assert.Equal(-12.527207793864215, tilt.Angle, 6);
            time.Advance(TimeSpan.FromTicks(875_000)); // lands on exactly 700 ms
            BeatLoop.StepAll();
            Assert.Equal(-12, tilt.Angle, 3);
            // The 700 ms one-shot stops its own frame clock.
            Assert.DoesNotContain(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, badge));
            _settings.Current.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(shell._profileTierTimer.IsEnabled);
            shell.ProfileTierWobble();
            Assert.DoesNotContain(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, badge));
            Assert.Equal(-12, tilt.Angle);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = previousTime;
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF OnBubbleFlashDisplayed / OnBubbleSubliminalDisplayed: a flash tilts the bubble, a subliminal dips it.
    /// Motion Off: WPF MotionFx.AllowTransitions gate, nothing tilts or dims.</summary>
    [Theory]
    [InlineData(MotionLevel.Full, true)]
    [InlineData(MotionLevel.Off, false)]
    public Task FlashWobblesAndSubliminalShimmersTheBubble(MotionLevel motion, bool animates) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = Shell(motion);
        try
        {
            var visual = shell.FindControl<Grid>("ProfileBubbleVisual")!;
            double Angle() => visual.RenderTransform is TransformGroup g
                ? g.Children.OfType<RotateTransform>().Single().Angle
                : (visual.RenderTransform as RotateTransform)?.Angle ?? 0;
            double maxTilt = 0, minOpacity = 1;
            void Run()
            {
                for (int i = 0; i < 60; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    maxTilt = Math.Max(maxTilt, Math.Abs(Angle()));
                    minOpacity = Math.Min(minOpacity, visual.Opacity);
                }
            }

            DisplayFlash(shell);
            Run();
            if (animates) Assert.True(maxTilt > 1, $"a flash never tilted the bubble (max {maxTilt})");
            else Assert.Equal(0, maxTilt);
            Assert.Equal(1, minOpacity);

            maxTilt = 0;
            DisplaySubliminal();
            Run();
            if (animates) Assert.True(minOpacity < 0.95, $"a subliminal never dipped the bubble (min {minOpacity})");
            else Assert.Equal(1, minOpacity);
            Assert.Equal(0, maxTilt);
        }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            SubliminalOverlay.CloseAll();
            shell.Close(); Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    // WPF MainWindow.ProfileBubble.cs:623-649: independent 2.5 s / 4 s throttles.
    [Theory]
    [InlineData("flash", "_profileBubbleLastWobble", 2500)]
    [InlineData("subliminal", "_profileBubbleLastShimmer", 4000)]
    public Task FeatureReactionsThrottleIndependentlyAndUnsubscribeOnClose(string feature, string fieldName, int interval) =>
        AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = Shell();
        var field = typeof(MainShellWindow).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)!;
        void Fire()
        {
            if (feature == "flash") DisplayFlash(shell); else DisplaySubliminal();
            Dispatcher.UIThread.RunJobs();
        }
        try
        {
            Fire();
            var first = (DateTime)field.GetValue(shell)!;
            Assert.True(first > DateTime.UtcNow.AddSeconds(-1));
            Fire();
            Assert.Equal(first, field.GetValue(shell));
            // Just inside the throttle stays suppressed; just outside is accepted.
            var recent = DateTime.UtcNow.AddMilliseconds(-interval + 100);
            field.SetValue(shell, recent);
            Fire();
            Assert.Equal(recent, field.GetValue(shell));
            field.SetValue(shell, DateTime.UtcNow.AddMilliseconds(-interval - 100));
            Fire();
            var accepted = (DateTime)field.GetValue(shell)!;
            Assert.True(accepted > DateTime.UtcNow.AddSeconds(-1));
            shell.Close();
            field.SetValue(shell, DateTime.MinValue);
            Fire();
            Assert.Equal(DateTime.MinValue, field.GetValue(shell));
        }
        finally { FlashOverlay.CloseAll(final: false); SubliminalOverlay.CloseAll(); shell.Close(); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    // WPF FlashService.cs:1645-1674 and MainWindow.ProfileBubble.cs:623: arrival, not request.
    [Theory]
    [InlineData("shown")]
    [InlineData("canceled")]
    [InlineData("refused")]
    [InlineData("empty")]
    [InlineData("cap")]
    public Task FlashReactionWaitsForAnAdmittedDisplay(string outcome) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = Shell();
        var field = typeof(MainShellWindow).GetField("_profileBubbleLastWobble", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var timers = new List<(Action Then, TimeSpan Delay)>();
        var saved = (FlashOverlay.After, FlashOverlay.Present);
        var closed = typeof(FlashOverlay).GetField("_closed", BindingFlags.NonPublic | BindingFlags.Static)!;
        var wasClosed = closed.GetValue(null);
        closed.SetValue(null, false); // a live app, not the final-teardown state left by a panic test
        FlashOverlay.After = (then, delay) => timers.Add((then, delay));
        FlashOverlay.Present = (_, _, _) => outcome != "refused";
        try
        {
            global::ConditioningControlPanel.Avalonia.App.NoteFeatureUsed("flash");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(DateTime.MinValue, field.GetValue(shell));
            var pictures = new List<(Bitmap, PixelRect, string, Screen)>();
            if (outcome != "empty") pictures.Add((new RenderTargetBitmap(new PixelSize(1, 1)),
                new PixelRect(0, 0, 1, 1), "test.png", shell.Screens.All[0]));
            if (outcome == "shown") pictures.Add((new RenderTargetBitmap(new PixelSize(1, 1)),
                new PixelRect(2, 0, 1, 1), "second.png", shell.Screens.All[0]));
            FlashOverlay.Schedule(pictures, 1000, 300, FlashOverlay.Generation, outcome == "cap" ? 0 : 30,
                1, TimeSpan.Zero, TimeSpan.FromSeconds(30), false, false, 0, null);
            Assert.Equal(DateTime.MinValue, field.GetValue(shell));
            if (outcome == "empty") Assert.Empty(timers);
            else Assert.Equal(TimeSpan.FromSeconds(1), timers[0].Delay);
            if (outcome == "shown") Assert.Equal(TimeSpan.FromMilliseconds(1300), timers[1].Delay);
            if (outcome == "canceled") FlashOverlay.CloseAll(final: false);
            if (timers.Count > 0) timers[0].Then();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(outcome == "shown", (DateTime)field.GetValue(shell)! != DateTime.MinValue);
            if (outcome == "shown")
            {
                field.SetValue(shell, DateTime.MinValue); // prove batch semantics independently of the throttle
                timers[1].Then(); Dispatcher.UIThread.RunJobs();
                Assert.Equal(2, FlashOverlay.Active.Count);
                Assert.Equal(DateTime.MinValue, field.GetValue(shell));
            }
        }
        finally
        {
            FlashOverlay.CloseAll(final: false);
            (FlashOverlay.After, FlashOverlay.Present) = saved;
            closed.SetValue(null, wasClosed);
            shell.Close(); Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    // WPF SubliminalService.cs:609-629: delayed arrivals honor Stop, including Freeze and Reset.
    [Theory]
    [InlineData("freeze")]
    [InlineData("phrase")]
    [InlineData("cancel-delay")]
    [InlineData("cancel-queued")]
    [InlineData("skip")]
    public Task SubliminalReactionFollowsGuardedWhisperFreezeAndResetDisplays(string outcome) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = Shell();
        var field = typeof(MainShellWindow).GetField("_profileBubbleLastShimmer", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var timers = new List<(int Ms, Action Then)>();
        var drawn = new List<string>();
        var saved = (SubliminalWhisperShow.DrawNow, SubliminalWhisperShow.After, SubliminalWhisperShow.Roll,
            CoreAudio.PlayStoppableProvider, CoreSubliminal.BambiFreezeProvider);
        SubliminalWhisperShow.DrawNow = text =>
        {
            if (outcome == "skip") SubliminalOverlay.Show(shell, text);
            else { drawn.Add(text); SubliminalOverlay.Present(new SubliminalOverlayWindow(text)); }
        };
        SubliminalWhisperShow.After = (ms, then) => timers.Add((ms, then));
        SubliminalWhisperShow.Roll = () => 0.9;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => () => { };
        CoreSubliminal.BambiFreezeProvider = SubliminalWhisperShow.Freeze;
        _settings.Current.SubAudioEnabled = true;
        _settings.Current.SubAudioMuted = false;
        void Fire(int ms)
        {
            var index = timers.FindIndex(t => t.Ms == ms);
            Assert.True(index >= 0, $"missing {ms} ms display delay");
            var next = timers[index]; timers.RemoveAt(index); next.Then();
        }
        try
        {
            global::ConditioningControlPanel.Avalonia.App.NoteFeatureUsed("subliminal");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(DateTime.MinValue, field.GetValue(shell));
            if (outcome == "phrase") SubliminalWhisperShow.Phrase("Freeze");
            else CoreSubliminal.TriggerBambiFreeze();
            Assert.Equal(DateTime.MinValue, field.GetValue(shell));
            Fire(50); // whisper lead, then haptic anticipation
            Assert.Equal(DateTime.MinValue, field.GetValue(shell));
            if (outcome == "cancel-delay") SubliminalWhisperShow.StopAll();
            Fire(250); // posts the card to the UI queue
            Assert.Equal(DateTime.MinValue, field.GetValue(shell));
            if (outcome == "cancel-queued") SubliminalWhisperShow.StopAll();
            Dispatcher.UIThread.RunJobs();
            bool shown = outcome is "freeze" or "phrase";
            Assert.Equal(shown, (DateTime)field.GetValue(shell)! != DateTime.MinValue);
            if (shown)
            {
                Assert.Equal(new[] { "Freeze" }, drawn);
                if (outcome == "phrase")
                {
                    Assert.Empty(timers); // WPF SubliminalService.cs:289: plain Freeze is not Bambi Freeze.
                    return Task.CompletedTask;
                }
                field.SetValue(shell, DateTime.MinValue); // advance past the independent 4 s reaction throttle
                Fire(Assert.Single(timers).Ms); // 4-8 s Reset delay
                Fire(50); Fire(250);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(new[] { "Freeze", "Reset" }, drawn);
                Assert.NotEqual(DateTime.MinValue, field.GetValue(shell));
            }
            else Assert.Empty(drawn);
        }
        finally
        {
            SubliminalWhisperShow.StopAll(); SubliminalOverlay.CloseAll();
            (SubliminalWhisperShow.DrawNow, SubliminalWhisperShow.After, SubliminalWhisperShow.Roll,
                CoreAudio.PlayStoppableProvider, CoreSubliminal.BambiFreezeProvider) = saved;
            shell.Close(); Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    private static void DisplayFlash(MainShellWindow shell)
    {
        var present = FlashOverlay.Present;
        FlashOverlay.Present = (_, _, _) => true;
        try
        {
            Assert.True(FlashOverlay.Show(new RenderTargetBitmap(new PixelSize(1, 1)), new PixelRect(0, 0, 1, 1),
                "profile-test.png", shell.Screens.All[0], 1, TimeSpan.Zero, TimeSpan.FromSeconds(30), false, false, 0));
        }
        finally { FlashOverlay.Present = present; }
    }

    private static void DisplaySubliminal() => SubliminalOverlay.Present(new SubliminalOverlayWindow("Freeze"));

    /// <summary>WPF tier grow: BackEase EaseOut Amplitude 0.45 (overshoot ~1.11), not Avalonia's amplitude-1 BackEaseOut (~1.37).</summary>
    [Fact]
    public Task TierGrowUsesWpfBackEaseAmplitude() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var ease = new MainShellWindow.ProfileTierGrowEase();
        Assert.Equal(0, ease.Ease(0), 6);
        Assert.Equal(1, ease.Ease(1), 6);
        double peak = Enumerable.Range(0, 1001).Max(i => ease.Ease(i / 1000.0));
        double wpf = Enumerable.Range(0, 1001).Max(i => { double x = 1 - i / 1000.0; return 1 - (x * x * x - x * 0.45 * Math.Sin(Math.PI * x)); });
        Assert.Equal(wpf, peak, 6);
        Assert.True(peak > 1.05 && peak < 1.2, $"overshoot {peak}");
        return Task.CompletedTask;
    });

    [Fact]
    public Task FailedProfileBrowserOpenCopiesTheLinkAndShowsTheWpfPrompt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var shell = Shell();
        try
        {
            MainShellWindow.ProfileLinkAllowed = _ => true;
            MainShellWindow.OpenProfileLink = _ => false;
            shell.OpenPublicProfilePage();
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal(Loc.Get("title_open_link_in_browser"), dialog.Title);
            Assert.Equal(Loc.GetF("msg_browser_no_default_for", "open your public profile")
                + Loc.GetF("msg_browser_link_copied", MainShellWindow.ProfileSharingUrl),
                dialog.FindControl<TextBlock>("TxtMessage")!.Text);
            Assert.Equal(MainShellWindow.ProfileSharingUrl, await shell.Clipboard!.TryGetTextAsync());
            dialog.Close();
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    public Task SandboxRefusedProfileLinkNeverTouchesTheClipboard() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var shell = Shell();
        try
        {
            await shell.Clipboard!.SetTextAsync("untouched");
            bool opened = false;
            MainShellWindow.ProfileLinkAllowed = _ => false;
            MainShellWindow.OpenProfileLink = _ => { opened = true; return false; };
            shell.OpenPublicProfilePage();
            Dispatcher.UIThread.RunJobs();
            Assert.False(opened);
            Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal("untouched", await shell.Clipboard.TryGetTextAsync());
        }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
