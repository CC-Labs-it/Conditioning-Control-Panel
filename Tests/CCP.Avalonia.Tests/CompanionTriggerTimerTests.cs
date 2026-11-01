using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionTriggerTimerTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        PanicAftermath.EndQuiet();
    }

    [Fact]
    public Task OpeningHistoryDuringTriggerLeadKeepsHistoryAndSuppressesWhisper() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        s.AvatarEnabled = true; s.AvatarMuted = false; s.TriggerModeEnabled = true;
        s.SubAudioEnabled = true; s.SubAudioMuted = false; s.CustomTriggers = new List<string> { "Reset" };
        var whispers = 0;
        var haptics = 0;
        CoreAudio.PlayStoppableProvider = (_, _, tag, _, _) => { if (tag == "whisper") whispers++; return () => { }; };
        var parent = new Window();
        var tube = new AvatarTubeWindow(parent) { TriggerHaptic = _ => haptics++ };
        try
        {
            parent.Show(); tube.Show();
            tube.OnTriggerTick();
            Assert.Equal(1, haptics);
            Assert.True(tube.IsSpeaking);
            tube.FindControl<MenuItem>("MenuItemShowChatHistory")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            var history = tube.FindControl<Grid>("ChatHistoryView")!;
            var bubble = tube.FindControl<Border>("SpeechBubble")!;
            var scroller = tube.FindControl<ScrollViewer>("SpeechScroller")!;
            Assert.True(history.IsVisible);
            tube.FinishTriggerLead(); // a callback already queued when the history opened
            Assert.Equal((true, true, false, 0), (history.IsVisible, bubble.IsVisible, scroller.IsVisible, whispers));
        }
        finally { tube.Close(); parent.Close(); PanicAftermath.EndQuiet(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EnabledAtConstructionFiresTheFirstTick() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var s = CoreSettings.Current;
        s.AvatarEnabled = false; s.TriggerModeEnabled = true; s.TriggerIntervalSeconds = 60;
        s.CustomTriggers = new List<string> { "first" };
        var tube = new AvatarTubeWindow(null);
        var pulse = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        tube.TriggerHaptic = text => pulse.TrySetResult(text);
        try
        {
            tube.Show();
            var winner = await Task.WhenAny(pulse.Task, Task.Delay(TimeSpan.FromSeconds(8)));
            Assert.Same(pulse.Task, winner);
            Assert.Equal("first", await pulse.Task);
        }
        finally { tube.Close(); PanicAftermath.EndQuiet(); }
    });

    [Fact]
    public Task ControlsAndQuickMenuRestartTheLiveTimer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        s.AvatarEnabled = true; s.TriggerModeEnabled = true; s.TriggerIntervalSeconds = 60;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("companion");
            var room = CollapsedRoom.Show(shell);
            shell.ShowTab("companionai"); // the real v2 page adopts the live Triggers cell
            Dispatcher.UIThread.RunJobs();
            var tube = shell.Tube!;
            Assert.NotNull(tube);
            Assert.Equal(TimeSpan.FromSeconds(60), tube.TriggerInterval);
            Assert.Equal(TimeSpan.FromSeconds(2), tube.FirstTriggerInterval);
            var cell = room.WorkshopParts!.Triggers;
            cell.SyncFromSettings();
            cell.FindControl<Slider>("SliderTriggerIntervalCompanion")!.Value = 90;
            Assert.Equal(90, s.TriggerIntervalSeconds);
            Assert.Equal(TimeSpan.FromSeconds(90), tube.TriggerInterval);
            cell.FindControl<CheckBox>("ChkTriggerModeCompanion")!.IsChecked = false;
            Assert.Null(tube.TriggerInterval);
            Assert.Null(tube.FirstTriggerInterval);
            tube.OnMenuTriggerMode();
            Assert.True(s.TriggerModeEnabled);
            Assert.True(cell.FindControl<CheckBox>("ChkTriggerModeCompanion")!.IsChecked);
            Assert.Equal(TimeSpan.FromSeconds(90), tube.TriggerInterval);
            tube.OnMenuTriggerMode();
            Assert.False(cell.FindControl<CheckBox>("ChkTriggerModeCompanion")!.IsChecked);
            Assert.Null(tube.TriggerInterval);
        }
        finally { shell.Close(); PanicAftermath.EndQuiet(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TriggerBubbleTypesSlowlyAndPlaysTheLinkedWhisper() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        s.AvatarEnabled = true; s.AvatarMuted = false; s.TriggerModeEnabled = true;
        s.SubAudioEnabled = true; s.SubAudioMuted = false; s.CustomTriggers = new List<string> { "Reset" };
        var plays = 0;
        var stops = 0;
        CoreAudio.PlayStoppableProvider = (_, _, tag, _, _) => { if (tag == "whisper") plays++; return () => stops++; };
        var parent = new Window();
        var tube = new AvatarTubeWindow(parent);
        var clock = new Clock();
        tube.SpeechClock = clock;
        tube.TriggerHaptic = _ => { };
        try
        {
            parent.Show(); tube.Show();
            tube.OnTriggerTick();
            tube.FinishTriggerLead();
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            Assert.Equal(1, plays);
            Assert.Equal("", text.Text);
            Assert.Equal(TimeSpan.FromSeconds(5.0 + 5 * 0.05 + 5 * 0.075), tube.TriggerBubbleInterval);
            tube.AdvanceTriggerTypewriter();
            Assert.Equal("R", text.Text);
            for (int i = 1; i < 5; i++) tube.AdvanceTriggerTypewriter();
            Assert.Equal("Reset", text.Text);
            tube.EndSpeechBubble();
            clock.Now += TimeSpan.FromSeconds(3);
            tube.OnTriggerTick(); tube.FinishTriggerLead();
            tube.AdvanceTriggerTypewriter();
            tube.ShowListeningBubble("listening");
            tube.AdvanceTriggerTypewriter();
            Assert.Equal("listening", text.Text);
            tube.PanicSilence();
            Assert.Equal(2, stops); // the second whisper replaces the first, panic cuts the second
        }
        finally { tube.Close(); parent.Close(); PanicAftermath.EndQuiet(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TriggerMarkdownBecomesClickableOnlyAfterTypingAndUsesTheSharedGuard() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        s.AvatarEnabled = true; s.AvatarMuted = false; s.TriggerModeEnabled = true; s.SubAudioEnabled = false;
        s.CustomTriggers = new List<string> { "Open [clip](https://example.com/video) now" };
        var oldNavigate = CompanionLinkLauncher.Navigate;
        var oldRelay = RemoteControlTabView.Relay;
        using var relay = new RemoteRelay(() => null, () => null, "test", (_, _) => null, _ => { });
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        _ = RemoteControlTabView.Relay.Value;
        var urls = new List<string>();
        CompanionLinkLauncher.Navigate = url => { urls.Add(url); return true; };
        var parent = new Window();
        var tube = new AvatarTubeWindow(parent) { TriggerHaptic = _ => { } };
        try
        {
            parent.Show(); tube.Show(); tube.OnTriggerTick(); tube.FinishTriggerLead();
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            tube.AdvanceTriggerTypewriter();
            Assert.Equal("O", text.Text);
            for (int i = 0; i < 20; i++) tube.AdvanceTriggerTypewriter();
            var link = Assert.IsType<Button>(Assert.Single(text.Inlines!.OfType<InlineUIContainer>()).Child);
            Assert.Equal("clip", Assert.IsType<TextBlock>(link.Content).Text);
            Assert.Equal(new[] { "Open ", " now" }, text.Inlines.OfType<Run>().Select(x => x.Text));
            Dispatcher.UIThread.RunJobs();
            tube.UpdateLayout();
            Assert.True(link.Bounds.Width > 0 && link.Bounds.Height > 0, $"Link bounds: {link.Bounds}");
            var point = link.TranslatePoint(new Point(link.Bounds.Width / 2, link.Bounds.Height / 2), tube)!.Value;
            Assert.True(point.X >= 0 && point.Y >= 0 && point.X < tube.Bounds.Width && point.Y < tube.Bounds.Height,
                $"Link point {point}, tube bounds {tube.Bounds}");
            tube.MouseDown(point, MouseButton.Left); tube.MouseUp(point, MouseButton.Left);
            Assert.Equal(new[] { "https://example.com/video" }, urls);
            typeof(RemoteRelay).GetProperty(nameof(RemoteRelay.ControllerConnected))!.SetValue(relay, true);
            tube.MouseDown(point, MouseButton.Left); tube.MouseUp(point, MouseButton.Left);
            Assert.Single(urls);
        }
        finally
        {
            tube.Close(); parent.Close(); PanicAftermath.EndQuiet();
            CompanionLinkLauncher.Navigate = oldNavigate; RemoteControlTabView.Relay = oldRelay;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task InvalidMarkdownTargetsStayPlainAndPreemptedTypingCannotRestoreLinks() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        s.AvatarEnabled = true; s.AvatarMuted = false; s.TriggerModeEnabled = true; s.SubAudioEnabled = false;
        s.CustomTriggers = new List<string> { "See [file](file:///tmp/file)" };
        var parent = new Window();
        var tube = new AvatarTubeWindow(parent) { TriggerHaptic = _ => { } };
        try
        {
            parent.Show(); tube.Show(); tube.OnTriggerTick(); tube.FinishTriggerLead();
            for (int i = 0; i < 40; i++) tube.AdvanceTriggerTypewriter();
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            Assert.Empty(text.Inlines!.OfType<InlineUIContainer>());
            Assert.Equal("See file", string.Concat(text.Inlines.OfType<Run>().Select(x => x.Text)));
            tube.EndSpeechBubble();
            s.CustomTriggers = new List<string> { "[clip](https://example.com)" };
            tube.SpeechClock = new Clock { Now = DateTimeOffset.UtcNow.AddHours(1) };
            tube.OnTriggerTick(); tube.FinishTriggerLead(); tube.AdvanceTriggerTypewriter();
            Assert.Equal("c", text.Text);
            tube.ShowListeningBubble("listening");
            for (int i = 0; i < 10; i++) tube.AdvanceTriggerTypewriter();
            Assert.Equal("listening", text.Text);
            Assert.Empty(text.Inlines!.OfType<InlineUIContainer>());
        }
        finally { tube.Close(); parent.Close(); PanicAftermath.EndQuiet(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TriggerTicksHonorSpeechMuteAndPanic() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        s.AvatarEnabled = true; s.AvatarMuted = false; s.TriggerModeEnabled = true;
        s.TriggerIntervalSeconds = 60; s.CustomTriggers = new List<string> { "test phrase" };
        s.SubAudioEnabled = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var tube = shell.Tube!;
            var clock = new Clock();
            tube.SpeechClock = clock;
            var pulses = new List<string>();
            tube.TriggerHaptic = pulses.Add;
            var bubble = tube.FindControl<Border>("SpeechBubble")!;

            tube.OnTriggerTick();
            Assert.Equal(new[] { "test phrase" }, pulses);
            Assert.False(bubble.IsVisible); // the WPF 0.6 second lead-in
            tube.OnTriggerTick();           // speech latch prevents overlap
            Assert.Single(pulses);
            tube.FinishTriggerLead();
            Assert.True(bubble.IsVisible);
            tube.EndSpeechBubble();
            clock.Now += TimeSpan.FromSeconds(3);

            s.AvatarMuted = true;
            tube.OnTriggerTick();
            Assert.Equal(2, pulses.Count); // haptic only while muted
            Assert.False(bubble.IsVisible);
            clock.Now += TimeSpan.FromSeconds(3);
            s.AvatarMuted = false;
            PanicAftermath.ArmQuiet();
            tube.OnTriggerTick();
            Assert.Equal(2, pulses.Count);
            PanicAftermath.EndQuiet();
            tube.OnTriggerTick();
            Assert.Equal(3, pulses.Count);
            PanicAftermath.ArmQuiet(); // panic during the lead-in suppresses the bubble
            tube.FinishTriggerLead();
            Assert.False(bubble.IsVisible);
            PanicAftermath.EndQuiet();
            clock.Now += TimeSpan.FromSeconds(3);
            tube.OnTriggerTick();
            tube.Giggle("queued");
            s.TriggerModeEnabled = false;
            tube.RestartTriggerTimer();
            Assert.Equal("queued", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
        }
        finally { shell.Close(); PanicAftermath.EndQuiet(); }
        return Task.CompletedTask;
    });
}
