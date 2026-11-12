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
    private static readonly FieldInfo[] PanicState = Array.ConvertAll(
        new[] { "_quietUntilMs", "_engineHeld", "_panics" },
        name => typeof(PanicAftermath).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!);
    private static object?[] ReadPanicState() => Array.ConvertAll(PanicState, field => field.GetValue(null));
    private static void RestorePanicState(object?[] values)
    {
        for (int i = 0; i < PanicState.Length; i++) PanicState[i].SetValue(null, values[i]);
    }

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
        object? oldHeld = field.GetValue(EmiLineEngine.Instance);
        int plays = 0;
        object? oldUntil = HoldUntil.GetValue(EmiLineEngine.Instance);
        try
        {
            field.SetValue(EmiLineEngine.Instance, null);
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
            field.SetValue(EmiLineEngine.Instance, oldHeld);
            HoldUntil.SetValue(EmiLineEngine.Instance, oldUntil);
            CoreSettings.Current.MasterVolume = volume;
            CoreAudio.PlayStoppableProvider = play;
            CoreAudio.IsOutputSuppressedProvider = breaker;
        }
    }

    [Fact]
    public void SilenceGateTestRestoresAnExistingHold()
    {
        var engine = EmiLineEngine.Instance;
        var held = typeof(EmiLineEngine).GetField("_heldBy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previous = held.GetValue(engine);
        try
        {
            held.SetValue(engine, "existing-hold");
            EachSilenceGatePreventsPlayback("volume");
            Assert.Equal("existing-hold", held.GetValue(engine));
        }
        finally { held.SetValue(engine, previous); }
    }

    [Theory]
    [InlineData("palette")]
    [InlineData("card")]
    [InlineData("tail")]
    public async Task PanicTestsRestorePriorAftermath(string test)
    {
        var before = ReadPanicState();
        var held = typeof(EmiLineEngine).GetField("_heldBy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previousHeld = held.GetValue(EmiLineEngine.Instance);
        object?[] seeded = { Environment.TickCount64 + 60000, 1, 42L };
        try
        {
            held.SetValue(EmiLineEngine.Instance, null);
            RestorePanicState(seeded);
            if (test == "tail") await PanicHoldOutlivesStartForEveryRung();
            else await PanicEarlyRungStopsThePlayingLine(test == "card");
            Assert.Equal(seeded, ReadPanicState());
        }
        finally
        {
            RestorePanicState(before);
            held.SetValue(EmiLineEngine.Instance, previousHeld);
        }
    }

    // WPF MainWindow.xaml.cs:1620 and EmiDeskWindow.Bubble.cs:545-559: the hold cuts
    // a sounding line before the lock-card and palette rungs can return.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task PanicEarlyRungStopsThePlayingLine(bool lockCard) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var engine = EmiLineEngine.Instance;
        var service = EmiDeskService.Instance;
        var windowField = typeof(EmiDeskService).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var held = typeof(EmiLineEngine).GetField("_heldBy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var previousWindow = windowField.GetValue(service);
        var previousPanic = ReadPanicState();
        var previousHeld = held.GetValue(engine);
        var previousUntil = HoldUntil.GetValue(engine);
        var previousPlay = CoreAudio.PlayStoppableProvider;
        var previousBreaker = CoreAudio.IsOutputSuppressedProvider;
        var (enabled, key, volume) = (settings.PanicKeyEnabled, settings.PanicKey, settings.MasterVolume);
        int lines = 0, stops = 0;
        var shell = new global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        var desk = new EmiDeskWindow();
        try
        {
            held.SetValue(engine, null);
            HoldUntil.SetValue(engine, DateTime.MinValue);
            settings.PanicKeyEnabled = true;
            settings.PanicKey = "Escape";
            settings.MasterVolume = 50;
            CoreAudio.IsOutputSuppressedProvider = () => false;
            CoreAudio.PlayStoppableProvider = (_, _, tag, _, _) =>
            {
                if (tag == "emi-vox") { lines++; return () => stops++; }
                return () => { };
            };
            windowField.SetValue(service, desk);
            shell.Show();
            desk.Show();
            desk.Say("This line is playing.");
            desk.AdvanceTourSpeech(); desk.AdvanceTourSpeech(); desk.AdvanceTourSpeech();
            Assert.Equal(1, lines);
            Assert.Equal(0, stops);
            if (lockCard)
            {
                global::ConditioningControlPanel.Avalonia.Views.Windows.LockCardWindow.ShowOnAllMonitors("good girl", 1, strictMode: true);
                Assert.True(global::ConditioningControlPanel.Avalonia.Views.Windows.LockCardWindow.IsAnyOpen());
            }
            else
            {
                global::ConditioningControlPanel.Avalonia.Views.Windows.SettingsPaletteWindow.Toggle(shell);
                var instance = typeof(global::ConditioningControlPanel.Avalonia.Views.Windows.SettingsPaletteWindow)
                    .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
                Assert.NotNull(instance.GetValue(null));
            }
            shell.HandlePanicKeyPress(new DateTime(2026, 1, 1));
            Assert.Equal(1, stops);
            Assert.Null(desk.TourSpeechText);
            if (lockCard) Assert.False(global::ConditioningControlPanel.Avalonia.Views.Windows.LockCardWindow.IsAnyOpen());
            else Assert.False(global::ConditioningControlPanel.Avalonia.Views.Windows.SettingsPaletteWindow.TryConsumeEscape());
        }
        finally
        {
            global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.StopLockCards();
            global::ConditioningControlPanel.Avalonia.Views.Windows.SettingsPaletteWindow.CloseIfOpen();
            windowField.SetValue(service, previousWindow);
            desk.ShutDown();
            shell.Close();
            held.SetValue(engine, previousHeld);
            HoldUntil.SetValue(engine, previousUntil);
            RestorePanicState(previousPanic);
            (settings.PanicKeyEnabled, settings.PanicKey, settings.MasterVolume) = (enabled, key, volume);
            CoreAudio.PlayStoppableProvider = previousPlay;
            CoreAudio.IsOutputSuppressedProvider = previousBreaker;
        }
        return Task.CompletedTask;
    });

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
        var previousPanic = ReadPanicState();
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
            RestorePanicState(previousPanic);
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
