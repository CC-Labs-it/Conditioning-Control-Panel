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
            time.Advance(TimeSpan.FromSeconds(8.105));
            BeatLoop.StepAll();
            Assert.Equal(-3, tilt.Angle, 3);
            time.Advance(TimeSpan.FromSeconds(0.595));
            BeatLoop.StepAll();
            Assert.Equal(-12, tilt.Angle, 3);
            _settings.Current.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
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

    [Fact]
    public Task FailedProfileBrowserOpenCopiesTheLinkAndShowsTheWpfPrompt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var shell = Shell();
        try
        {
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
}
