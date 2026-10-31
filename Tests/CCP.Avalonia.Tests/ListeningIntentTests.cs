using System;
using System.Linq;
using System.IO;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ListeningIntentTests
{
    [Fact]
    public void ExistingFeatureHostsAreReachableAndMantraIsInTheGrammar() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = (CoreMindWipe.TriggerOnceProvider, CoreEngine.PopQuiz, CoreEngine.BubbleCount);
        var quiz = new QuizHost();
        var count = new CountHost();
        CoreEngine.PopQuiz = new PopQuizScheduler(quiz);
        CoreEngine.BubbleCount = new BubbleCountScheduler(count);
        var shell = new MainShellWindow();
        try
        {
            var calls = 0;
            CoreMindWipe.TriggerOnceProvider = () => calls++;
            var host = shell.VoiceCmds.Host;
            Assert.NotNull(host.ActionFor("wipe_once"));
            host.ActionFor("wipe_once")!();
            Assert.Equal(1, calls);
            host.ActionFor("quiz_once")!();
            Assert.Equal(1, quiz.Opened);
            Assert.False(quiz.Test); // WPF uses ShowPopQuiz(), not its test path.
            host.ActionFor("count_once")!();
            Assert.True(CoreEngine.BubbleCount.IsBusy); // forceTest reaches the lead-in with engine stopped.
            CoreEngine.BubbleCount.ForceCleanup();
            Assert.Contains("give me a mantra", shell.VoiceCmds.Grammar());
            Assert.Contains(shell.VoiceCmds.Available, i => i.Name == "mantra");
        }
        finally
        {
            shell.Close(); CoreEngine.BubbleCount?.ForceCleanup(); CoreEngine.PopQuiz?.Dispose();
            (CoreMindWipe.TriggerOnceProvider, CoreEngine.PopQuiz, CoreEngine.BubbleCount) = old;
        }
    });

    [Fact]
    public void VoiceVolumeClampsAndMuteRestoresTheLastAudibleVolume() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted);
        var shell = new MainShellWindow();
        try
        {
            var host = shell.VoiceCmds.Host;
            void Run(string name) { var action = host.ActionFor(name); Assert.NotNull(action); action(); }
            Run("unmute"); Assert.Equal(70, s.MasterVolume);
            s.MasterVolume = 95;
            Run("louder"); Assert.Equal(100, s.MasterVolume);
            Run("quieter"); Assert.Equal(85, s.MasterVolume);
            Run("mute"); Run("mute");
            Assert.Equal(0, s.MasterVolume); Assert.False(s.SubAudioEnabled); Assert.True(s.AvatarMuted);
            Run("unmute");
            Assert.Equal(85, s.MasterVolume); Assert.True(s.SubAudioEnabled); Assert.False(s.AvatarMuted);
            s.MasterVolume = 5; Run("quieter"); Assert.Equal(0, s.MasterVolume);
        }
        finally { shell.Close(); (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted) = old; }
    });
    [Fact]
    public void OverlayCommandsHoldWithoutChangingSettingsAndPanicClearsTheHolds() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.SpiralEnabled, s.SpiralOpacity, s.PinkFilterEnabled, s.PinkFilterOpacity);
        var shell = new MainShellWindow();
        try
        {
            s.SpiralEnabled = s.PinkFilterEnabled = false;
            s.SpiralOpacity = 27; s.PinkFilterOpacity = 12;
            var host = shell.VoiceCmds.Host;
            host.ActionFor("spiral_on")!(); host.ActionFor("pink_on")!();
            Assert.Equal(0.027, SpiralOverlay.PaintedOpacity, 6);   // WPF ApplySpiralOpacityDirect -> SpiralPaint curve (OverlayService.cs:1388)
            Assert.Equal(0.4, PinkFilterOverlay.VoiceOpacity);
            SpiralOverlay.Refresh(shell); PinkFilterOverlay.Refresh(shell);
            Assert.Equal(0.27, SpiralOverlay.VoiceOpacity);
            Assert.Equal(0.4, PinkFilterOverlay.VoiceOpacity);
            host.ActionFor("spiral_off")!(); host.ActionFor("pink_off")!();
            Assert.Null(SpiralOverlay.VoiceOpacity); Assert.Null(PinkFilterOverlay.VoiceOpacity);
            s.PinkFilterOpacity = 48;
            host.ActionFor("pink_on")!(); host.ActionFor("spiral_on")!();
            Assert.Equal(0.48, PinkFilterOverlay.VoiceOpacity);
            PanicSurfaces.StopAll("test", shell);
            Assert.Null(SpiralOverlay.VoiceOpacity); Assert.Null(PinkFilterOverlay.VoiceOpacity);
            Assert.False(s.SpiralEnabled); Assert.False(s.PinkFilterEnabled);
            Assert.Equal(27, s.SpiralOpacity); Assert.Equal(48, s.PinkFilterOpacity);
            host.ActionFor("pink_on")!(); host.ActionFor("spiral_on")!();
            shell.OnEngineStopped();
            Assert.Null(SpiralOverlay.VoiceOpacity); Assert.Null(PinkFilterOverlay.VoiceOpacity);
        }
        finally
        {
            SpiralOverlay.Stop(); PinkFilterOverlay.Stop(); shell.Close();
            (s.SpiralEnabled, s.SpiralOpacity, s.PinkFilterEnabled, s.PinkFilterOpacity) = old;
        }
    });

    [Fact]
    public void MuteCutsTheCurrentClipAndUpdatesTheAttachedVolumeSlider() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.AvatarEnabled, s.AvatarMuted, s.SubAudioEnabled, s.MasterVolume, s.CompanionVoiceLinesMuted);
        var oldAudio = CoreAudio.PlayStoppableProvider;
        var clip = Path.GetTempFileName();
        var shell = new MainShellWindow();
        try
        {
            s.AvatarMuted = false; s.SubAudioEnabled = true; s.MasterVolume = 62; s.CompanionVoiceLinesMuted = false;
            var stopped = 0;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, ended) => () => { stopped++; ended?.Invoke(); };
            shell.Show(); shell.SetAvatarEnabled(true);
            shell.Tube!.GigglePriority("A voiced line", playSound: true, aiGenerated: false,
                phraseAudioPath: clip, barkVoice: true);
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("companion"); Dispatcher.UIThread.RunJobs();
            var hero = global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(shell)
                .OfType<global::ConditioningControlPanel.Avalonia.Views.Controls.Companion.CompanionHeroCard>().Single().ViewModel!;
            Assert.False(hero.IsMuted);
            var beforeMute = stopped;
            shell.VoiceCmds.Host.ActionFor("mute")!();
            Dispatcher.UIThread.RunJobs();
            Assert.True(hero.IsMuted);   // the hero's mute chip re-reads AvatarMuted
            Assert.Equal(beforeMute + 1, stopped);
            Assert.True(shell.Tube.IsMuted);
            shell.ShowTab("appsettings"); Dispatcher.UIThread.RunJobs();
            var slider = shell.GetVisualDescendants().OfType<global::Avalonia.Controls.Slider>()
                .First(c => c.Name == "SliderMaster");
            Assert.Equal(0, slider.Value);
            shell.VoiceCmds.Host.ActionFor("unmute")!();
            Assert.Equal(62, slider.Value);
        }
        finally
        {
            shell.Close(); CoreAudio.PlayStoppableProvider = oldAudio; File.Delete(clip);
            (s.AvatarEnabled, s.AvatarMuted, s.SubAudioEnabled, s.MasterVolume, s.CompanionVoiceLinesMuted) = old;
        }
    });

    // WPF Session/SessionEngine.cs:799,814: subsequent ramp ticks overwrite a voice hold's
    // opacity through SetSustainedOverlayOpacity, without releasing its visibility ownership.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SessionRampTakesOpacityBackAfterAVoiceCommand(bool pink) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var shell = new MainShellWindow();
        var oldRunner = global::ConditioningControlPanel.Avalonia.App.Sessions;
        var runner = global::ConditioningControlPanel.Avalonia.App.Sessions = new SessionRunner(new SessionLogService());
        runner.Ticked += shell.OnSessionTick;
        try
        {
            var session = new ConditioningControlPanel.Models.Session { Id = "voice-ramp", Name = "Voice ramp", DurationMinutes = 10 };
            var settings = session.Settings;
            settings.RampCurve = ConditioningControlPanel.Models.RampCurve.Linear;
            settings.SpiralEnabled = settings.PinkFilterEnabled = true;
            settings.SpiralStartMinute = settings.PinkFilterStartMinute = 0;
            settings.SpiralOpacity = 20; settings.SpiralOpacityEnd = 80;
            settings.PinkFilterStartOpacity = 10; settings.PinkFilterEndOpacity = 90;
            runner.Start(session);
            runner.Tick(TimeSpan.FromMinutes(2));
            shell.VoiceCmds.Host.ActionFor(pink ? "pink_on" : "spiral_on")!();
            double Painted() => pink ? PinkFilterOverlay.PaintedOpacity : SpiralOverlay.PaintedOpacity;
            Assert.Equal(pink ? 0.4 : 0.02, Painted(), 6); // the command takes effect immediately
            runner.Tick(TimeSpan.FromMinutes(5));
            Assert.Equal(pink ? 0.5 : 0.05, Painted(), 6);
            runner.Tick(TimeSpan.FromMinutes(6));
            Assert.Equal(pink ? 0.58 : 0.116, Painted(), 6);
            Assert.NotNull(pink ? PinkFilterOverlay.VoiceOpacity : SpiralOverlay.VoiceOpacity);
            Assert.Equal(20, CoreSettings.Current.SpiralOpacity);
            Assert.Equal(10, CoreSettings.Current.PinkFilterOpacity);
        }
        finally
        {
            runner.Ticked -= shell.OnSessionTick;
            runner.Stop(); CoreEngine.Stop();
            SpiralOverlay.Stop(); PinkFilterOverlay.Stop();
            global::ConditioningControlPanel.Avalonia.App.Sessions = oldRunner;
            shell.Close();
        }
    });

    // WPF SessionEngine.cs:530 -> OverlayService.Stop:471, StopPinkFilter:1519, StopSpiral:2151.
    [Fact]
    public void PauseButtonClearsVoiceHoldsAndResumeDoesNotResurrectThem() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var shell = new MainShellWindow();
        var runner = global::ConditioningControlPanel.Avalonia.App.Sessions = new SessionRunner(new SessionLogService());
        try
        {
            CoreSettings.Current.SkipPauseXpWarning = true;
            runner.Start(new ConditioningControlPanel.Models.Session { Id = "voice-pause", Name = "Voice pause", DurationMinutes = 10 });
            shell.OnSessionTick();
            shell.VoiceCmds.Host.ActionFor("spiral_on")!(); shell.VoiceCmds.Host.ActionFor("pink_on")!();
            Assert.NotNull(SpiralOverlay.VoiceOpacity); Assert.NotNull(PinkFilterOverlay.VoiceOpacity);
            var pause = shell.Named<global::Avalonia.Controls.Button>("BtnPauseSession")!;
            pause.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
            Assert.True(runner.IsPaused);
            Assert.Null(SpiralOverlay.VoiceOpacity); Assert.Null(PinkFilterOverlay.VoiceOpacity);
            Assert.False(SpiralOverlay.IsShowing); Assert.False(PinkFilterOverlay.IsShowing);
            pause.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
            Assert.False(runner.IsPaused);
            Assert.Null(SpiralOverlay.VoiceOpacity); Assert.Null(PinkFilterOverlay.VoiceOpacity);
            Assert.False(SpiralOverlay.IsShowing); Assert.False(PinkFilterOverlay.IsShowing);
        }
        finally
        {
            runner.Stop(); CoreEngine.Stop();
            SpiralOverlay.Stop(); PinkFilterOverlay.Stop();
            global::ConditioningControlPanel.Avalonia.App.Sessions = null;
            shell.Close();
        }
    });

    // WPF AutonomyService.cs:574,1604; RemoteControlService.cs:1045,1189.
    [Theory]
    [InlineData("pulse-expiry", false, true)]
    [InlineData("pulse-cancel", false, true)]
    [InlineData("remote-end", false, true)]
    [InlineData("remote-leave", false, true)]
    [InlineData("pulse-expiry", true, true)]
    [InlineData("pulse-cancel", true, true)]
    [InlineData("remote-end", true, true)]
    [InlineData("remote-leave", true, true)]
    [InlineData("remote-leave", false, false)]
    public void OtherOverlayStopsDropIdleHoldsButKeepARunningEngineOrOptedOutDisconnect(string path, bool running, bool stopOnLeave)
        => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var shell = new MainShellWindow();
        var timers = new System.Collections.Generic.List<Action>();
        shell.PulseTimer = (action, _) => timers.Add(action);
        try
        {
            if (running) CoreEngine.Start(); else CoreEngine.Stop();
            CoreSettings.Current.StopEffectsOnRemoteDisconnect = stopOnLeave;
            if (path.StartsWith("pulse"))
            {
                CoreSettings.Current.AutonomyConsentGiven = true;
                CoreEntitlement.HasPremiumProvider = () => true;
                Assert.True(shell.SetAutonomyEnabled(true));
                Dispatcher.UIThread.RunJobs();
                shell.PerformAutonomy(AutonomyActionType.PinkFilterPulse);
            }
            shell.VoiceCmds.Host.ActionFor("spiral_on")!(); shell.VoiceCmds.Host.ActionFor("pink_on")!();
            Assert.NotNull(SpiralOverlay.VoiceOpacity); Assert.NotNull(PinkFilterOverlay.VoiceOpacity);
            switch (path)
            {
                case "pulse-expiry": Assert.Single(timers)(); break;
                case "pulse-cancel": shell.CancelAutonomyPulses(); break;
                case "remote-end": shell.OnRemoteSessionEnded(); break;
                case "remote-leave": shell.OnRemoteControllerChanged(); break;
            }
            var stopped = !running && (path != "remote-leave" || stopOnLeave);
            Assert.Equal(stopped, SpiralOverlay.VoiceOpacity == null);
            Assert.Equal(stopped, PinkFilterOverlay.VoiceOpacity == null);
        }
        finally
        {
            shell.CancelAutonomyPulses(); CoreEngine.Stop();
            SpiralOverlay.Stop(); PinkFilterOverlay.Stop();
            shell.Close();
        }
    });

    private sealed class QuizHost : IPopQuizHost
    {
        public int Opened; public bool Test;
        public bool IsQuizOpen => false;
        public bool IsLockCardOpen => false;
        public bool IsInteractionBusy => false;
        public bool Defer(Action replay) => false;
        public void DropDeferred() { }
        public void Open(bool isTest) { Opened++; Test = isTest; }
        public void CloseAll() { }
    }
    private sealed class CountHost : IBubbleCountHost
    {
        public void Show(string path, int difficulty, bool strict, Action<bool> onComplete) { }
        public void ShowMessage(string text, int ms, Action then) { }
        public void CloseAll() { }
        public double LastVideoDurationSeconds => 0;
    }

}
