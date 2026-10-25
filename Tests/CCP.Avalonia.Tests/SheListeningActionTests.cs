using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
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
        var count = new BubbleCountScheduler(new CountHost());
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

    private sealed class CountHost : IBubbleCountHost
    {
        public double LastVideoDurationSeconds => 0;
        public void Show(string path, int difficulty, bool strict, Action<bool> onComplete) => throw new InvalidOperationException("No real playback in this test");
        public void ShowMessage(string text, int ms, Action then) { }
        public void CloseAll() { }
    }
}
