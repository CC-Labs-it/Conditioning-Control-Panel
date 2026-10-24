using System;
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
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The tube's right-click menu (WPF AvatarTubeWindow.ChatInput.cs:867-1538): every item acts
/// through the shell, so what it starts panic stops; resize/dismiss, the input-panel close rule and the
/// close-time cleanup (no voice, no singleton hook left behind).</summary>
public sealed class TubeQuickMenuTests
{
    private static void Click(AvatarTubeWindow tube, string name) =>
        tube.FindControl<MenuItem>(name)!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static string? HeaderKey(AvatarTubeWindow tube, string name) =>
        (tube.FindControl<MenuItem>(name)!.Header as IconLabel)?.Key;

    private static Task WithShell(Action<MainShellWindow, AvatarTubeWindow> body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.AvatarEnabled, s.AvatarMuted, s.SubAudioMuted, s.TriggerModeEnabled, s.AutonomyModeEnabled,
            s.AutonomyConsentGiven, s.AvatarTubeDetached, s.AvatarTubeScale, s.FlashEnabled, s.PanicKeyEnabled, s.PanicKey, s.AutonomyResumeOnStartup);
        s.AvatarEnabled = true; s.AvatarTubeDetached = false; s.AvatarTubeScale = 1.0;
        s.AutonomyModeEnabled = s.AutonomyConsentGiven = false; s.AutonomyResumeOnStartup = false;
        s.PanicKeyEnabled = true; s.PanicKey = "F8";
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            Assert.NotNull(shell.Tube);
            body(shell, shell.Tube!);
        }
        finally
        {
            shell.Autonomy.Stop();
            LockdownService.Current?.Dispose();
            LockdownService.Current = null;
            CoreEntitlement.HasPremiumProvider = null;
            CoreEntitlement.IsFreeTodayProvider = null;
            CoreEngine.Stop();
            CoreEngine.StoppedHook = null;
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            (s.AvatarEnabled, s.AvatarMuted, s.SubAudioMuted, s.TriggerModeEnabled, s.AutonomyModeEnabled,
                s.AutonomyConsentGiven, s.AvatarTubeDetached, s.AvatarTubeScale, s.FlashEnabled, s.PanicKeyEnabled, s.PanicKey, s.AutonomyResumeOnStartup) = saved;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task CompanionStartResumesParkedSessionWithoutCountingAnotherRun() => WithShell((shell, tube) =>
    {
        var old = global::ConditioningControlPanel.Avalonia.App.Sessions;
        var runner = new SessionRunner(new SessionLogService());
        global::ConditioningControlPanel.Avalonia.App.Sessions = runner;
        try
        {
            runner.Start(new ConditioningControlPanel.Models.Session { Id = "tube_resume", DurationMinutes = 10 });
            MainShellWindow.StopEngine();
            var count = CoreSettings.Current.TotalSessions;
            Click(tube, "MenuItemEngine");
            Assert.True(CoreEngine.IsRunning);
            Assert.False(runner.IsPaused);
            Assert.Equal(count, CoreSettings.Current.TotalSessions);
        }
        finally
        {
            runner.Stop();
            global::ConditioningControlPanel.Avalonia.App.Sessions = old;
            CoreSession.IsSessionRunningProvider = old == null ? null : () => old.IsRunning;
        }
    });

    [Fact]
    public Task EngineItemStartsAndPanicStopsWhatItStarted() => WithShell((shell, tube) =>
    {
        CoreSettings.Current.FlashEnabled = true;
        Click(tube, "MenuItemEngine");
        Assert.True(CoreEngine.IsRunning);
        tube.UpdateQuickMenuState();
        Assert.Equal("menu_stop_engine", HeaderKey(tube, "MenuItemEngine"));

        // #479: under Lockdown the tube's Stop is refused and counted as the main Stop's escape.
        var ld = LockdownService.Current = new LockdownService();
        ld.Activate(TimeSpan.FromMinutes(30));
        var escapes = 0;
        ld.EscapeAttempted += _ => escapes++;
        tube.UpdateQuickMenuState();
        Assert.False(tube.FindControl<MenuItem>("MenuItemEngine")!.IsEnabled);
        tube.OnMenuEngine();
        Assert.True(CoreEngine.IsRunning);
        Assert.Equal(1, escapes);
        ld.Deactivate();

        // Safety: panic stops the engine the tube started.
        shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 0));
        Dispatcher.UIThread.RunJobs();
        Assert.False(CoreEngine.IsRunning);

        Click(tube, "MenuItemEngine");
        Assert.True(CoreEngine.IsRunning);
        Click(tube, "MenuItemEngine");
        Assert.False(CoreEngine.IsRunning);
    });

    [Fact]
    public Task TakeoverItemNeedsEntitlementHoldsUnderLockdownAndPanicStopsIt() => WithShell((shell, tube) =>
    {
        var s = CoreSettings.Current;
        CoreEntitlement.HasPremiumProvider = () => false;
        CoreEntitlement.IsFreeTodayProvider = _ => false;   // not the ? box's free day either
        tube.UpdateQuickMenuState();
        Assert.Equal("menu_takeover_locked_format", HeaderKey(tube, "MenuItemBambiTakeover"));
        tube.OnMenuTakeover();
        Assert.False(s.AutonomyModeEnabled);

        CoreEntitlement.HasPremiumProvider = () => true;
        Click(tube, "MenuItemBambiTakeover");
        Assert.True(s.AutonomyConsentGiven);   // choosing it from her menu is the consent
        Assert.True(s.AutonomyModeEnabled);
        Assert.True(shell.Autonomy.IsEnabled);

        var ld = LockdownService.Current = new LockdownService();
        ld.Activate(TimeSpan.FromMinutes(30));
        tube.OnMenuTakeover();   // #514
        Assert.True(shell.Autonomy.IsEnabled);
        ld.Deactivate();

        shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 0));
        Dispatcher.UIThread.RunJobs();
        Assert.False(shell.Autonomy.IsEnabled);

        tube.OnMenuTakeover();   // the saved switch was on: this click turns it off
        Assert.False(s.AutonomyModeEnabled);
    });

    [Fact]
    public Task TogglesFlipTheirSettingsAndHeaders() => WithShell((_, tube) =>
    {
        var s = CoreSettings.Current;
        (s.AvatarMuted, s.SubAudioMuted, s.TriggerModeEnabled) = (false, false, false);
        Click(tube, "MenuItemMute");
        Click(tube, "MenuItemMuteWhispers");
        Click(tube, "MenuItemTriggerMode");
        Assert.True(s.AvatarMuted && s.SubAudioMuted && s.TriggerModeEnabled);
        Assert.Equal("menu_mute_avatar_on", HeaderKey(tube, "MenuItemMute"));
        Assert.Equal("menu_mute_whispers_on", HeaderKey(tube, "MenuItemMuteWhispers"));
        Assert.Equal("menu_trigger_mode_on", HeaderKey(tube, "MenuItemTriggerMode"));
        Click(tube, "MenuItemMute");
        Assert.False(s.AvatarMuted);
        Assert.False(tube.FindControl<MenuItem>("MenuItemPauseBrowser")!.IsVisible);
    });

    [Fact]
    public Task FreeTubeShrinksGrowsSavesAndAttachResets() => WithShell((shell, tube) =>
    {
        Assert.False(tube.FindControl<MenuItem>("MenuItemShrink")!.IsVisible);
        var baseWidth = tube.Width;
        tube.Detach();
        Assert.True(tube.FindControl<MenuItem>("MenuItemShrink")!.IsVisible);
        Click(tube, "MenuItemShrink");
        Click(tube, "MenuItemShrink");
        Click(tube, "MenuItemShrink");   // already at the floor
        Assert.Equal(0.5, tube.CurrentScale);
        Assert.Equal(baseWidth * 0.5, tube.Width, 3);
        Assert.Equal(0.5, CoreSettings.Current.AvatarTubeScale);
        Assert.False(tube.FindControl<MenuItem>("MenuItemShrink")!.IsEnabled);
        Assert.Equal("menu_shrink_min", HeaderKey(tube, "MenuItemShrink"));
        for (var i = 0; i < 5; i++) Click(tube, "MenuItemGrow");
        Assert.Equal(1.5, tube.CurrentScale);
        Assert.Equal("menu_grow_max", HeaderKey(tube, "MenuItemGrow"));

        tube.Attach();   // WPF resets her to the base size, unsaved
        Assert.Equal(1.0, tube.CurrentScale);
        Assert.Equal(baseWidth, tube.Width, 3);
        Assert.Equal(1.5, CoreSettings.Current.AvatarTubeScale);
        Assert.False(tube.FindControl<MenuItem>("MenuItemGrow")!.IsVisible);
    });

    [Fact]
    public Task DismissSavesHidesAndReattaches() => WithShell((shell, tube) =>
    {
        tube.Detach();
        Click(tube, "MenuItemDismiss");
        Dispatcher.UIThread.RunJobs();
        Assert.False(CoreSettings.Current.AvatarEnabled);
        Assert.False(tube.IsDetached);
        Assert.False(tube.IsVisible);
    });

    [Fact]
    public Task PressOutsideTheInputPanelClosesIt() => WithShell((_, tube) =>
    {
        var panel = tube.FindControl<Border>("InputPanel")!;
        tube.OpenChatInput();
        Dispatcher.UIThread.RunJobs();
        Assert.True(panel.IsVisible);
        var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
        PointerPressedEventArgs Press(Visual source) => new(source, pointer, tube, default, 0,
            new PointerPointProperties(RawInputModifiers.RightMouseButton, PointerUpdateKind.RightButtonPressed), KeyModifiers.None);
        tube.FindControl<TextBox>("TxtUserInput")!.RaiseEvent(Press(tube.FindControl<TextBox>("TxtUserInput")!));
        Assert.True(panel.IsVisible);
        var avatar = tube.FindControl<Border>("AvatarBorder")!;
        avatar.RaiseEvent(Press(avatar));   // WPF ImgAvatar_MouseRightButtonDown
        Assert.False(panel.IsVisible);
    });

    [Fact]
    public Task ClosingTheTubeStopsHerVoice() => WithShell((shell, tube) =>
    {
        var s = CoreSettings.Current;
        var (volume, voMuted) = (s.MasterVolume, s.CompanionVoiceLinesMuted);
        var provider = CoreAudio.PlayStoppableProvider;
        var clip = Path.GetTempFileName();
        var live = 0;
        try
        {
            s.MasterVolume = 80; s.AvatarMuted = false; s.CompanionVoiceLinesMuted = false;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { live++; return () => live--; };
            int Hooks() => (typeof(ConditioningControlPanel.Services.Companion.Asks.CompanionAskService)
                .GetField("CardChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(ConditioningControlPanel.Services.Companion.Asks.CompanionAskService.Instance) as Delegate)?.GetInvocationList().Length ?? 0;
            var hooksBefore = Hooks();
            tube.ShowAskCard(new ConditioningControlPanel.Services.Companion.Asks.AskCard(
                ConditioningControlPanel.Services.Companion.Asks.AskKind.Feedback, "ok?",
                new[] { new ConditioningControlPanel.Services.Companion.Asks.AskChoice("y", "Yes",
                    ConditioningControlPanel.Services.Companion.Asks.AskTone.Yes,
                    ConditioningControlPanel.Services.Companion.Asks.AskAction.Loved) }, DateTime.UtcNow));
            Assert.Equal(hooksBefore + 1, Hooks());
            tube.GigglePriority("hi", phraseAudioPath: clip, barkVoice: true);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, live);
            tube.Close();   // WPF OnClosed (Windowing.cs:1517) StopVoiceLineAudio
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, live);
            Assert.Equal(hooksBefore, Hooks());   // the singleton no longer holds the closed tube
        }
        finally
        {
            CoreAudio.PlayStoppableProvider = provider;
            (s.MasterVolume, s.CompanionVoiceLinesMuted) = (volume, voMuted);
            File.Delete(clip);
        }
    });
}
