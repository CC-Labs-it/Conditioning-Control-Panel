using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.LogicalTree;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

// Inline transcripts exercise the real command matcher, guards and shell routes, without a mic.
public sealed class SheListeningActionTests
{
    [Fact]
    public void ExplicitMantraRequestIsInTheGrammarButDefersToTheListenFunnel() => Run(shell =>
    {
        Assert.Contains(shell.VoiceCmds.Available, i => i.Name == "mantra" && i.IsMantra);
        Assert.Contains("give me a mantra", shell.VoiceCmds.Grammar());
        Assert.False(shell.VoiceCmds.TryHandleInlineCommand("hey bambi give me a mantra", new[] { "hey bambi" }));
    });

    [Fact]
    public void WipeInvokesTheInstalledHostAndDisappearsWhenItIsRemoved() => Run(shell =>
    {
        var old = CoreMindWipe.TriggerOnceProvider;
        try
        {
            var calls = 0;
            CoreMindWipe.TriggerOnceProvider = () => calls++;
            Speak(shell, "wipe my mind");
            Assert.Equal(1, calls);
            CoreMindWipe.TriggerOnceProvider = null;
            Assert.DoesNotContain(shell.VoiceCmds.Available, i => i.Name == "wipe_once");
        }
        finally { CoreMindWipe.TriggerOnceProvider = old; }
    });

    [Fact]
    public void QuizUsesTheNormalInteractionPolicyWithoutStartingTheEngine() => Run(shell =>
    {
        var old = CoreEngine.PopQuiz;
        var host = new QuizHost();
        using var quiz = new PopQuizScheduler(host);
        CoreEngine.PopQuiz = quiz;
        try
        {
            Speak(shell, "quiz me");
            Assert.Equal(1, host.Opens);
            Assert.False(host.Test);
            Assert.False(quiz.IsRunning);
            host.IsQuizOpen = true;
            Speak(shell, "quiz me");
            Assert.Equal(1, host.Opens);
        }
        finally { CoreEngine.PopQuiz = old; }
    });

    [Fact]
    public void CountForcesAStandaloneGameWithTheFeatureOff() => Run(shell =>
    {
        var old = CoreEngine.BubbleCount;
        var enabled = CoreSettings.Current.BubbleCountEnabled;
        var count = new BubbleCountScheduler(new CountHost(), new FrozenClock());   // P08: the lead-in timer never fires
        CoreEngine.BubbleCount = count;
        CoreSettings.Current.BubbleCountEnabled = false;
        try
        {
            Speak(shell, "count the bubbles");
            Assert.True(count.IsBusy);
            Assert.False(count.IsRunning);
        }
        finally { count.ForceCleanup(); CoreEngine.BubbleCount = old; CoreSettings.Current.BubbleCountEnabled = enabled; }
    });

    [Theory]
    [InlineData(48, 48)]
    [InlineData(0, 70)]
    public void RepeatedMuteRemembersVolumeAndUnmuteRestoresVoiceAndWhispers(int start, int restored) => Run(shell =>
    {
        var s = CoreSettings.Current;
        var saved = (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted);
        try
        {
            s.MasterVolume = start;
            Speak(shell, "mute");
            Speak(shell, "mute");
            Assert.Equal(0, s.MasterVolume);
            Assert.False(s.SubAudioEnabled);
            Assert.True(s.AvatarMuted);
            Speak(shell, "unmute");
            Assert.Equal(restored, s.MasterVolume);
            Assert.True(s.SubAudioEnabled);
            Assert.False(s.AvatarMuted);
        }
        finally { (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted) = saved; AudioSettingsBinder.RaiseChanged(); }
    });

    // WPF MainWindow.Patreon.cs:2269 calls ApplyMuteState, which overwrites the effective
    // mute in AvatarTubeWindow.ChatInput.cs:1353-1362 even after minimize muted the window.
    [Fact]
    public void VoiceUnmuteClearsTemporaryWindowMute() => Run(shell =>
    {
        var s = CoreSettings.Current;
        var saved = (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted, s.AvatarEnabled);
        try
        {
            s.AvatarEnabled = true;
            s.AvatarMuted = false;
            shell.Show();
            shell.ShowAvatarTube();
            Dispatcher.UIThread.RunJobs();
            var tube = Assert.IsType<AvatarTubeWindow>(shell.Tube);
            tube.SetWindowMute(true);
            Assert.True(tube.IsMuted);
            Speak(shell, "unmute");
            Assert.False(s.AvatarMuted);
            Assert.False(tube.IsMuted);
            Assert.Equal("menu_mute_avatar_off", (tube.FindControl<MenuItem>("MenuItemMute")!.Header as IconLabel)?.Key);
        }
        finally { (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted, s.AvatarEnabled) = saved; AudioSettingsBinder.RaiseChanged(); }
    });

    [Fact]
    public void MuteCutsTheClipAndRefreshesQuickMenuAndHeroMirror() => Run(shell =>
    {
        var s = CoreSettings.Current;
        var saved = (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted, s.AvatarEnabled);
        try
        {
            s.AvatarMuted = false;
            s.AvatarEnabled = true;
            shell.Show();
            shell.ShowTab("companion");
            shell.ShowAvatarTube();
            Dispatcher.UIThread.RunJobs();
            var tube = shell.Tube!;
            var hero = shell.GetLogicalDescendants().OfType<CompanionHeroCard>().Single().ViewModel!;
            Assert.False(hero.IsMuted);
            var cuts = 0;
            typeof(AvatarTubeWindow).GetField("_stopSpoken", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(tube, (Action)(() => cuts++));
            shell.ApplyVoiceMute(true);   // the route itself; a spoken ack would cut the clip on its own
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, cuts);
            Assert.True(hero.IsMuted);
            Assert.Equal("menu_mute_avatar_on", (tube.FindControl<MenuItem>("MenuItemMute")!.Header as IconLabel)?.Key);
        }
        finally { (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted, s.AvatarEnabled) = saved; AudioSettingsBinder.RaiseChanged(); }
    });

    [Theory]
    [InlineData("louder", 40, 55)]
    [InlineData("quieter", 40, 25)]
    [InlineData("louder", 95, 100)]
    [InlineData("quieter", 5, 0)]
    public void VolumeStepsByFifteenClampsAndRepaintsMirrors(string phrase, int start, int expected) => Run(shell =>
    {
        var saved = CoreSettings.Current.MasterVolume;
        var section = new AudioSettingsSection();
        var parent = new Window { Content = section };
        parent.Show();
        try
        {
            CoreSettings.Current.MasterVolume = start;
            AudioSettingsBinder.RaiseChanged();
            Speak(shell, phrase);
            Assert.Equal(expected, CoreSettings.Current.MasterVolume);
            Assert.Equal(expected, section.FindControl<Slider>("SliderMaster")!.Value);
            Assert.Equal($"{expected}%", section.FindControl<TextBlock>("TxtMaster")!.Text);
        }
        finally { parent.Close(); CoreSettings.Current.MasterVolume = saved; AudioSettingsBinder.RaiseChanged(); }
    });

    private static void Speak(MainShellWindow shell, string phrase)
    {
        Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi " + phrase, new[] { "hey bambi" }));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Run(Action<MainShellWindow> test) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var shell = new MainShellWindow();
        try { test(shell); }
        finally { shell.RequestExit(); }
    });

    private sealed class QuizHost : IPopQuizHost
    {
        public bool IsQuizOpen { get; set; }
        public bool IsLockCardOpen => false;
        public bool IsInteractionBusy => false;
        public int Opens;
        public bool Test;
        public bool Defer(Action replay) => false;
        public void DropDeferred() { }
        public void Open(bool isTest) { Opens++; Test = isTest; }
        public void CloseAll() { }
    }

    private sealed class FrozenClock : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period) => new Idle();
        private sealed class Idle : ITimer
        {
            public bool Change(TimeSpan due, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }
    }

    private sealed class CountHost : IBubbleCountHost
    {
        public double LastVideoDurationSeconds => 0;
        public void Show(string path, int difficulty, bool strict, Action<bool> onComplete) => throw new InvalidOperationException("No real playback in this test");
        public void ShowMessage(string text, int ms, Action then) { }
        public void CloseAll() { }
    }
}
