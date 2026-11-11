using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

public class EmiTourVoiceTests
{
    // PanicKeyTests leaves EMI's five-minute panic tail behind; each test clears and restores it.
    private static readonly FieldInfo HoldUntil = typeof(EmiLineEngine).GetField("_holdUntilUtc", BindingFlags.Instance | BindingFlags.NonPublic)!;

    [Fact]
    public void TickIsDeterministicAndUsesTheWpfWave()
    {
        var first = EmiVox.WriteWav(EmiVox.RenderTick());
        Assert.Equal(first, EmiVox.WriteWav(EmiVox.RenderTick()));
        Assert.Equal(4470, first.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(first, 0, 4));
        Assert.Contains(first[44..], b => b != 0);
    }

    [Fact]
    public void HeadOutputPlaysAtMasterGainAndStopsTheLine()
    {
        int volume = CoreSettings.Current.MasterVolume;
        var play = CoreAudio.PlayStoppableProvider;
        var breaker = CoreAudio.IsOutputSuppressedProvider;
        int stops = 0;
        var played = new System.Collections.Generic.List<(float Gain, string Tag)>();
        object? oldUntil = HoldUntil.GetValue(EmiLineEngine.Instance);
        try
        {
            HoldUntil.SetValue(EmiLineEngine.Instance, DateTime.MinValue);
            CoreSettings.Current.MasterVolume = 50;
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreAudio.PlayStoppableProvider = (_, v, tag, _, _) => { played.Add((v, tag)); return () => stops++; };
            using var vox = new EmiVox(new EmiVoxOutput());
            Assert.True(vox.Tick());
            Assert.True(vox.Speak("hi again", "idle"));
            Assert.Equal(new[] { (.5f, "emi-vox-tick"), (.5f, "emi-vox") }, played);
            vox.Stop();
            Assert.Equal(1, stops);
        }
        finally
        {
            HoldUntil.SetValue(EmiLineEngine.Instance, oldUntil);
            CoreSettings.Current.MasterVolume = volume;
            CoreAudio.PlayStoppableProvider = play;
            CoreAudio.IsOutputSuppressedProvider = breaker;
        }
    }

    // WPF EmiVox.cs:500-523: volume, breaker, a held moment and a timed hold (the panicPressed tail).
    [Theory]
    [InlineData("volume")]
    [InlineData("breaker")]
    [InlineData("hold")]
    [InlineData("panicTail")]
    public void EachSilenceGatePreventsPlayback(string gate)
    {
        int volume = CoreSettings.Current.MasterVolume;
        var play = CoreAudio.PlayStoppableProvider;
        var breaker = CoreAudio.IsOutputSuppressedProvider;
        var field = typeof(EmiLineEngine).GetField("_heldBy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int plays = 0;
        object? oldUntil = HoldUntil.GetValue(EmiLineEngine.Instance);
        try
        {
            HoldUntil.SetValue(EmiLineEngine.Instance, gate == "panicTail" ? DateTime.UtcNow.AddMinutes(5) : DateTime.MinValue);
            CoreSettings.Current.MasterVolume = gate == "volume" ? 0 : 50;
            CoreAudio.IsOutputSuppressedProvider = () => gate == "breaker";
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { plays++; return () => { }; };
            if (gate == "hold") field.SetValue(EmiLineEngine.Instance, "test");
            using var vox = new EmiVox(new EmiVoxOutput());
            Assert.False(vox.Tick());
            Assert.False(vox.Speak("hi again", "idle"));
            Assert.Equal(0, plays);
        }
        finally
        {
            field.SetValue(EmiLineEngine.Instance, null);
            HoldUntil.SetValue(EmiLineEngine.Instance, oldUntil);
            CoreSettings.Current.MasterVolume = volume;
            CoreAudio.PlayStoppableProvider = play;
            CoreAudio.IsOutputSuppressedProvider = breaker;
        }
    }

    /// <summary>WPF MainWindow.xaml.cs:1620: the panicPressed hold is armed before any rung, and START
    /// (PanicAftermath.EndQuiet) never ends it, so typing ticks stay silent for the tail.</summary>
    [Fact]
    public Task PanicHoldOutlivesStartForEveryRung() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var engine = EmiLineEngine.Instance;
        var until = HoldUntil;
        object? oldUntil = until.GetValue(engine);
        bool enabled = s.PanicKeyEnabled;
        string key = s.PanicKey;
        int volume = s.MasterVolume;
        var play = CoreAudio.PlayStoppableProvider;
        var breaker = CoreAudio.IsOutputSuppressedProvider;
        int plays = 0;
        var shell = new global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        try
        {
            s.PanicKeyEnabled = true;
            s.PanicKey = "F8";
            s.MasterVolume = 50;
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { plays++; return () => { }; };
            shell.Show();
            using var tick = new EmiVox(new EmiVoxOutput());
            foreach (bool card in new[] { false, true })
            {
                until.SetValue(engine, DateTime.MinValue);
                PanicAftermath.EndQuiet();
                Assert.True(tick.Tick());
                if (card) global::ConditioningControlPanel.Avalonia.Views.Windows.LockCardWindow.ShowOnAllMonitors("good girl", 1, strictMode: true);
                shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, card ? 10 : 0));
                shell.StartEngineFromUser();
                CoreEngine.Stop();
                Assert.False(PanicAftermath.TriggersSilenced());
                Assert.False(tick.Tick());
            }
            Assert.Equal(2, plays);
        }
        finally
        {
            CoreEngine.Stop();
            global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.StopLockCards();
            shell.Close();
            until.SetValue(engine, oldUntil);
            PanicAftermath.EndQuiet();
            s.PanicKeyEnabled = enabled;
            s.PanicKey = key;
            s.MasterVolume = volume;
            CoreAudio.PlayStoppableProvider = play;
            CoreAudio.IsOutputSuppressedProvider = breaker;
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF EmiDeskWindow.Bubble.cs:168-184: dots tick, the landed line is babbled in the
    /// face's mood, and a cleared bubble, a cancel, a dismiss or a teardown cuts the line.</summary>
    [Fact]
    public Task BubbleDotsTickLinesSpeakAndCancellationCutsPlayback() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        int volume = CoreSettings.Current.MasterVolume;
        var play = CoreAudio.PlayStoppableProvider;
        var breaker = CoreAudio.IsOutputSuppressedProvider;
        int stops = 0;
        var played = new System.Collections.Generic.List<(string Path, string Tag)>();
        var desk = new EmiDeskWindow();
        object? oldUntil = HoldUntil.GetValue(EmiLineEngine.Instance);
        void Land(EmiDeskWindow w) { w.AdvanceTourSpeech(); w.AdvanceTourSpeech(); w.AdvanceTourSpeech(); }
        try
        {
            HoldUntil.SetValue(EmiLineEngine.Instance, DateTime.MinValue);
            CoreSettings.Current.MasterVolume = 50;
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreAudio.PlayStoppableProvider = (path, _, tag, _, _) => { played.Add((path, tag)); return () => stops++; };
            desk.Show();
            desk.Say("hello", "<3");
            Assert.Equal(new[] { "emi-vox-tick" }, played.ConvertAll(p => p.Tag));
            desk.AdvanceTourSpeech(); desk.AdvanceTourSpeech();
            Assert.Equal(3, played.Count);
            desk.AdvanceTourSpeech();
            Assert.Equal("hello", desk.TourSpeechText);
            Assert.Equal("emi-vox", played[3].Tag);
            Assert.Equal(0, stops);
            desk.AdvanceTourSpeech();
            Assert.Equal(1, stops);
            desk.AdvanceTourSpeech();
            Assert.Equal(1, stops);
            Assert.Equal(4, played.Count);
            desk.Say("hello", "zz");
            Land(desk);
            Assert.Equal("emi-vox", played[7].Tag);
            Assert.NotEqual(played[3].Path, played[7].Path);
            desk.CancelChain();
            Assert.Equal(2, stops);
            desk.Say("dismiss");
            Land(desk);
            desk.RunDismiss();
            Assert.Equal(3, stops);
            desk.ShutDown();
            var other = new EmiDeskWindow();
            try
            {
                other.Show();
                other.Say("teardown");
                Land(other);
                other.ShutDown();
                Assert.Equal(4, stops);
            }
            finally { other.ShutDown(); }
        }
        finally
        {
            desk.ShutDown();
            CoreSettings.Current.MasterVolume = volume;
            CoreAudio.PlayStoppableProvider = play;
            CoreAudio.IsOutputSuppressedProvider = breaker;
            HoldUntil.SetValue(EmiLineEngine.Instance, oldUntil);
        }
        return Task.CompletedTask;
    });
}
