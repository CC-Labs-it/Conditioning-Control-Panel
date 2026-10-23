using System;
using System.Linq;
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
        var savedFlash = (CoreFlash.ShowProvider, CoreFlash.AmbientShowProvider, CoreFlash.IsBusyProvider,
            global::ConditioningControlPanel.Avalonia.Views.Overlays.FlashOverlay.Fire);
        try
        {
            // The startup seam App.axaml.cs uses; the burst itself is stubbed (no overlay windows in a test).
            global::ConditioningControlPanel.Avalonia.Views.Overlays.FlashOverlay.Fire = (_, _) => { };
            global::ConditioningControlPanel.Avalonia.Views.Overlays.FlashOverlay.SeedCore(() => shell,
                () => global::ConditioningControlPanel.Avalonia.App.NoteFeatureUsed(
                    ConditioningControlPanel.Services.Companion.Brain.MemorySignalWriter.FeatureFlash));
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

            CoreFlash.ShowProvider!();
            Run();
            if (animates) Assert.True(maxTilt > 1, $"a flash never tilted the bubble (max {maxTilt})");
            else Assert.Equal(0, maxTilt);
            Assert.Equal(1, minOpacity);

            maxTilt = 0;
            global::ConditioningControlPanel.Avalonia.App.NoteFeatureUsed(
                ConditioningControlPanel.Services.Companion.Brain.MemorySignalWriter.FeatureSubliminal);
            Run();
            if (animates) Assert.True(minOpacity < 0.95, $"a subliminal never dipped the bubble (min {minOpacity})");
            else Assert.Equal(1, minOpacity);
            Assert.Equal(0, maxTilt);
        }
        finally
        {
            (CoreFlash.ShowProvider, CoreFlash.AmbientShowProvider, CoreFlash.IsBusyProvider,
                global::ConditioningControlPanel.Avalonia.Views.Overlays.FlashOverlay.Fire) = savedFlash;
            shell.Close(); Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

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
