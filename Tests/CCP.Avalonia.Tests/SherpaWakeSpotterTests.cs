using System;
using System.IO;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SherpaWakeSpotterTests
{
    private sealed class Handle : IDisposable
    {
        public bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public void MissingModelDoesNotInitialize()
    {
        int calls = 0;
        using var spotter = new SherpaWakeSpotter(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), (_, _, _) => { calls++; return new Handle(); });
        Assert.False(spotter.IsAvailable(true, .15, 2));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void FingerprintChangesRebuildAndFailureIsMemoizedUntilReset()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try
        {
            foreach (var name in new[] { "encoder.onnx", "decoder.onnx", "joiner.onnx", "tokens.txt", "keywords.txt" })
                File.WriteAllText(Path.Combine(root, name), "");
            int calls = 0;
            var first = new Handle();
            using var spotter = new SherpaWakeSpotter(root, (_, _, _) =>
            {
                calls++;
                if (calls > 1) throw new InvalidOperationException();
                return first;
            });
            Assert.False(spotter.IsAvailable(false, .15, 2));
            Assert.True(spotter.IsAvailable(true, .15, 2));
            Assert.True(spotter.IsAvailable(true, .15, 2));
            Assert.Equal(1, calls);
            Assert.False(spotter.IsAvailable(true, .20, 2));
            Assert.True(first.Disposed);
            Assert.False(spotter.IsAvailable(true, .20, 2));
            Assert.Equal(2, calls);
            spotter.ResetInitState();
            Assert.False(spotter.IsAvailable(true, .20, 2));
            Assert.Equal(3, calls);
        }
        finally { Directory.Delete(root, true); }
    }
}

[Collection(RunsAloneCollection.Name)]
public sealed class SherpaWakeCaptureTests
{
    private sealed class Handle : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }
    private sealed class Mic : ConditioningControlPanel.Services.Speech.IMicSource
    {
        internal Action<byte[], int>? Feed;
        internal int Stops;
        public bool HasDevice => true;
        public System.Collections.Generic.IReadOnlyList<ConditioningControlPanel.SpeechInputDevice> ListDevices() => Array.Empty<ConditioningControlPanel.SpeechInputDevice>();
        public IDisposable Start(Action<byte[], int> feed) { Feed = feed; return new Stop(this); }
        private sealed class Stop(Mic mic) : IDisposable { public void Dispose() { mic.Feed = null; mic.Stops++; } }
    }
    private static string Model()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        foreach (var name in new[] { "encoder.onnx", "decoder.onnx", "joiner.onnx", "tokens.txt", "keywords.txt" }) File.WriteAllText(Path.Combine(root, name), "");
        return root;
    }
    private static async System.Threading.Tasks.Task Until(Func<bool> predicate)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!predicate())
        {
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(DateTime.UtcNow < end, "timed out");
            await System.Threading.Tasks.Task.Delay(20);
        }
    }

    [Fact]
    public System.Threading.Tasks.Task WakeLoopUsesSpotterAndKeywordHitHasNoTranscript() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var root = Model();
        var mic = new Mic();
        var engineHandle = new Handle();
        using var kws = new SherpaWakeSpotter(root, (_, _, _) => engineHandle, _ => new Handle(), (_, _, samples) => samples.Length > 0);
        using var speech = new ConditioningControlPanel.Services.Speech.SpeechEngine(mic, Array.Empty<string>());
        using var cancel = new System.Threading.CancellationTokenSource();
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        var hit = false;
        string? transcript = "unset";
        shell.WakeSpeechOverride = speech;
        shell.WakeSpeechReadyOverride = () => true;
        shell.WakeSpotterOverride = kws;
        shell.WakeMicOverride = mic;
        shell.WakeVoskOverride = (_, _) => throw new InvalidOperationException("Vosk should not run");
        shell.WakeHeardOverride = text => { transcript = text; hit = true; cancel.Cancel(); };
        var noise = ConditioningControlPanel.CoreSettings.Current.SpeechNoiseSuppression;
        var words = ConditioningControlPanel.CoreSettings.Current.SpeechWakeWords;
        ConditioningControlPanel.CoreSettings.Current.SpeechWakeWords = "hey bambi";
        ConditioningControlPanel.CoreSettings.Current.SpeechNoiseSuppression = false;
        try
        {
            var loop = shell.WakeLoopAsync(cancel.Token);
            await Until(() => mic.Feed != null && kws.IsListening);
            kws.ResetInitState();
            Assert.False(engineHandle.Disposed);
            shell.CancelVoicePrompt();
            await Until(() => mic.Stops >= 1 && mic.Feed != null && kws.IsListening);
            mic.Feed!(new byte[] { 1, 0 }, 2);
            await Until(() => hit);
            await loop;
            Assert.Null(transcript);
            Assert.False(kws.IsListening);
            Assert.Equal(2, mic.Stops);
        }
        finally { cancel.Cancel(); shell.RequestExit(); ConditioningControlPanel.CoreSettings.Current.SpeechNoiseSuppression = noise; ConditioningControlPanel.CoreSettings.Current.SpeechWakeWords = words; Directory.Delete(root, true); }
    });

    [Fact]
    public System.Threading.Tasks.Task MissingModelUsesVoskFallback() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var mic = new Mic();
        using var kws = new SherpaWakeSpotter(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        using var speech = new ConditioningControlPanel.Services.Speech.SpeechEngine(mic, Array.Empty<string>());
        using var cancel = new System.Threading.CancellationTokenSource();
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        var called = false;
        var words = ConditioningControlPanel.CoreSettings.Current.SpeechWakeWords;
        ConditioningControlPanel.CoreSettings.Current.SpeechWakeWords = "hey bambi";
        shell.WakeSpeechOverride = speech;
        shell.WakeSpeechReadyOverride = () => true;
        shell.WakeSpotterOverride = kws;
        shell.WakeMicOverride = mic;
        shell.WakeVoskOverride = (_, _) => { called = true; cancel.Cancel(); return System.Threading.Tasks.Task.FromResult<string?>(null); };
        try { await shell.WakeLoopAsync(cancel.Token); Assert.True(called); Assert.Null(mic.Feed); }
        finally { cancel.Cancel(); shell.RequestExit(); ConditioningControlPanel.CoreSettings.Current.SpeechWakeWords = words; }
    });

    [Fact]
    public async System.Threading.Tasks.Task CancellationClosesCaptureAndDropsListening()
    {
        var root = Model();
        var mic = new Mic();
        using var kws = new SherpaWakeSpotter(root, (_, _, _) => new Handle(), _ => new Handle(), (_, _, _) => false);
        using var cancel = new System.Threading.CancellationTokenSource();
        try
        {
            var wait = kws.WaitForWakeAsync(mic, .15, 2, cancel.Token);
            Assert.True(kws.IsListening);
            cancel.Cancel();
            Assert.False(await wait);
            Assert.False(kws.IsListening);
            Assert.Null(mic.Feed);
            Assert.Equal(1, mic.Stops);
        }
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async System.Threading.Tasks.Task SileroIsPreferredAndMissingModelFallsBackToEnergy(bool silero)
    {
        var root = Model(); var mic = new Mic(); var vad = new Handle(); int updates = 0, decoded = 0;
        using var kws = new SherpaWakeSpotter(root, (_, _, _) => new Handle(), _ => new Handle(),
            (_, _, _) => { decoded++; return false; }, _ => silero ? (samples => { updates++; return true; }, vad) : null);
        using var cancel = new System.Threading.CancellationTokenSource();
        try
        {
            var wait = kws.WaitForWakeAsync(mic, .15, 2, cancel.Token, true);
            mic.Feed!(new byte[1600], 1600);
            Assert.Equal(silero ? 1 : 0, updates);
            Assert.Equal(silero ? 1 : 0, decoded);
            cancel.Cancel(); Assert.False(await wait);
            Assert.Equal(silero, vad.Disposed);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed record Th(float T) : IDisposable { public void Dispose() { } }

    [Fact]
    public async System.Threading.Tasks.Task CalibrationPicksStrictestThresholdThatCatchesSaysAndClosesMic()
    {
        var root = Model(); var mic = new Mic();
        using var kws = new SherpaWakeSpotter(root, (_, t, _) => new Th(t), _ => new Handle(),
            (spk, _, samples) => ((Th)spk).T <= .2001f && System.Linq.Enumerable.Any(samples, x => x > .3f));
        void Chunks(int n, byte hi) { for (int i = 0; i < n; i++) { var b = new byte[1600]; for (int j = 1; j < b.Length; j += 2) b[j] = hi; mic.Feed!(b, b.Length); } }
        try
        {
            var run = kws.CalibrateAsync(mic, 2, false, null, default);
            Chunks(8, 0); // room tone seeds the floor
            for (int say = 0; say < 5; say++) { Chunks(10, 0x40); Chunks(12, 0); }
            var (threshold, message) = await run;
            Assert.Equal(.20, threshold!.Value, 3);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("she_wake_cal_done", 5, 5, .20), message);
            Assert.Equal(1, mic.Stops);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void StatusAndTooltipFollowModelStateAndLanguage() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.Run(() =>
    {
        var root = Model(); var mic = new Mic();
        using var active = new SherpaWakeSpotter(root, (_, _, _) => new Handle());
        using var failed = new SherpaWakeSpotter(root, (_, _, _) => throw new InvalidOperationException());
        using var missing = new SherpaWakeSpotter(Path.Combine(root, "missing"));
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var language = loc.CurrentLanguage;
        shell.WakeMicOverride = mic;
        try
        {
            shell.Show();
            var tab = shell.SheListeningPage!;
            var row = (global::Avalonia.Controls.Control)tab.TxtSL_WakeEngineStatus.Parent!.Parent!;
            foreach (var (wake, key) in new[] { (missing, "she_wake_model_missing"), (failed, "she_wake_model_failed"), (active, "she_wake_model_active") })
            {
                shell.WakeSpotterOverride = wake;
                shell.RefreshWakeEngineStatus();
                foreach (var (code, _, _) in ConditioningControlPanel.Localization.LocalizationManager.AvailableLanguages)
                {
                    loc.SetLanguage(code); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                    var expected = ConditioningControlPanel.Localization.Loc.Get(key);
                    Assert.NotEqual(key, expected);
                    Assert.Equal(expected, tab.TxtSL_WakeEngineStatus.Text);
                    Assert.Equal(expected, global::Avalonia.Controls.ToolTip.GetTip(row));
                    Assert.Equal(key == "she_wake_model_active" ? global::Avalonia.Media.Color.FromRgb(0x90, 0xEE, 0x90) : global::Avalonia.Media.Color.FromRgb(0x88, 0x88, 0x88),
                        ((global::Avalonia.Media.ISolidColorBrush)tab.TxtSL_WakeEngineStatus.Foreground!).Color);
                }
            }
        }
        finally { shell.RequestExit(); loc.SetLanguage(language); Directory.Delete(root, true); }
    });

    private static void Feed(Mic mic, int n, short v)
    {
        for (int i = 0; i < n; i++)
        {
            var b = new byte[1600];
            for (int j = 0; j < b.Length; j += 2) (b[j], b[j + 1]) = ((byte)v, (byte)(v >> 8));
            mic.Feed?.Invoke(b, b.Length);
        }
    }
    private static SherpaWakeSpotter LoudSaysFireAtPointTwenty(string root) => new(root, (_, t, _) => new Th(t), _ => new Handle(),
        (spk, _, samples) => ((Th)spk).T <= .2001f && System.Linq.Enumerable.Any(samples, x => x > .3f));

    [Fact]
    public async System.Threading.Tasks.Task CancelDuringSweepDiscardsTheResult()
    {
        var root = Model(); var mic = new Mic();
        using var cts = new System.Threading.CancellationTokenSource();
        int decodes = 0;
        using var kws = new SherpaWakeSpotter(root, (_, t, _) => new Th(t), _ => new Handle(), (spk, _, samples) =>
        {
            if (++decodes == 1) cts.Cancel(); // Stop/panic lands while the sweep decodes
            return ((Th)spk).T <= .2001f && System.Linq.Enumerable.Any(samples, x => x > .3f);
        });
        try
        {
            var run = kws.CalibrateAsync(mic, 2, false, null, cts.Token);
            Feed(mic, 8, 0);
            for (int say = 0; say < 5; say++) { Feed(mic, 10, 0x4000); Feed(mic, 12, 0); }
            var (threshold, message) = await run;
            Assert.Null(threshold);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("she_wake_cal_cancelled"), message);
            Assert.True(decodes < 4); // the sweep stopped instead of decoding every candidate
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async System.Threading.Tasks.Task CalibrationFloorFollowsRisingRoomNoise()
    {
        var root = Model(); var mic = new Mic();
        using var kws = LoudSaysFireAtPointTwenty(root);
        try
        {
            var run = kws.CalibrateAsync(mic, 2, false, null, default);
            Feed(mic, 8, 200); // a quiet room seeds the floor
            for (double v = 200; v < 1200; v *= 1.01) Feed(mic, 1, (short)v); // a fan spins up past the seeded onset gate
            for (int say = 0; say < 5; say++) { Feed(mic, 10, 0x4000); Feed(mic, 12, 1200); }
            var (threshold, message) = await run;
            Assert.Equal(.20, threshold!.Value, 3);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("she_wake_cal_done", 5, 5, .20), message);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async System.Threading.Tasks.Task CalibrationCatchingUnderSeventyPercentReportsPartialFit()
    {
        var root = Model(); var mic = new Mic();
        using var kws = LoudSaysFireAtPointTwenty(root);
        try
        {
            var run = kws.CalibrateAsync(mic, 2, false, null, default);
            Feed(mic, 8, 0);
            foreach (var loud in new[] { true, true, true, false, false }) { Feed(mic, 10, (short)(loud ? 0x4000 : 0x2000)); Feed(mic, 12, 0); }
            var (threshold, message) = await run;
            Assert.Equal(.20, threshold!.Value, 3);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("she_wake_cal_partial", 3, 5, .20), message);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public System.Threading.Tasks.Task PanicDuringCalibrationClosesMicAtOnce() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var root = Model(); var mic = new Mic();
        using var kws = new SherpaWakeSpotter(root, (_, _, _) => new Handle(), _ => new Handle(), (_, _, _) => false);
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow { WakeSpotterOverride = kws, WakeMicOverride = mic };
        var s = ConditioningControlPanel.CoreSettings.Current;
        var consent = s.MicConsentGiven;
        s.MicConsentGiven = true;
        try
        {
            shell.Show();
            shell.SL_Calibrate_Click();
            await Until(() => mic.Feed != null);
            Assert.True(kws.IsListening); // the mic pill reads this
            System.Linq.Enumerable.Single(ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All, x => x.Id == "wake-calibration").Stop(shell);
            var end = DateTime.UtcNow.AddMilliseconds(250);
            while (mic.Feed != null && DateTime.UtcNow < end) await System.Threading.Tasks.Task.Delay(5);
            Assert.Null(mic.Feed);
            await Until(() => !shell.CalibratingWake);
            Assert.False(kws.IsListening);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("she_wake_cal_cancelled"), shell.SheListeningPage!.TxtSL_WakeEngineStatus.Text);
        }
        finally { s.MicConsentGiven = consent; shell.RequestExit(); Directory.Delete(root, true); }
    });

    [Fact]
    public System.Threading.Tasks.Task MicBorrowerSeesCalibrationTakesMicAtOnceAndOwnsReArm() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var root = Model(); var mic = new Mic();
        using var kws = new SherpaWakeSpotter(root, (_, _, _) => new Handle(), _ => new Handle(), (_, _, _) => false);
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow { WakeSpotterOverride = kws, WakeMicOverride = mic };
        var s = ConditioningControlPanel.CoreSettings.Current;
        var (consent, ptt) = (s.MicConsentGiven, s.SpeechPushToTalkEnabled);
        var (premium, speech) = (ConditioningControlPanel.CoreEntitlement.HasPremiumProvider, ConditioningControlPanel.CoreSpeech.IsAvailableProvider);
        s.MicConsentGiven = true;
        try
        {
            shell.Show();
            shell.SL_Calibrate_Click();
            await Until(() => mic.Feed != null);
            // Push-to-talk enabled mid-calibration: it would re-arm when calibration ends on its own.
            (s.SpeechPushToTalkEnabled, ConditioningControlPanel.CoreEntitlement.HasPremiumProvider, ConditioningControlPanel.CoreSpeech.IsAvailableProvider) = (true, () => true, () => true);
            Assert.True(shell.UserDrivenVoiceArmed); // lock card, intake and Deeper speak stand it down
            shell.StopVoiceInput(); // the borrower's hand-off
            Assert.Null(mic.Feed); // freed before the borrower opens its own capture
            await Until(() => !shell.CalibratingWake);
            Assert.False(shell.PushToTalkArmed); // the borrower still holds the mic
            shell.RefreshVoiceInputModes(); // the borrower releases it
            Assert.True(shell.PushToTalkArmed);
        }
        finally
        {
            (s.MicConsentGiven, s.SpeechPushToTalkEnabled) = (consent, ptt);
            (ConditioningControlPanel.CoreEntitlement.HasPremiumProvider, ConditioningControlPanel.CoreSpeech.IsAvailableProvider) = (premium, speech);
            shell.StopVoiceInput(); shell.RequestExit(); Directory.Delete(root, true);
        }
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public System.Threading.Tasks.Task BorrowerReturningBeforeCalibrationUnwindsReArmsUnlessStopped(bool stopAfter) => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var root = Model(); var mic = new Mic();
        using var kws = new SherpaWakeSpotter(root, (_, _, _) => new Handle(), _ => new Handle(), (_, _, _) => false);
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow { WakeSpotterOverride = kws, WakeMicOverride = mic };
        var s = ConditioningControlPanel.CoreSettings.Current;
        var (consent, ptt) = (s.MicConsentGiven, s.SpeechPushToTalkEnabled);
        var (premium, speech) = (ConditioningControlPanel.CoreEntitlement.HasPremiumProvider, ConditioningControlPanel.CoreSpeech.IsAvailableProvider);
        s.MicConsentGiven = true;
        try
        {
            shell.Show();
            shell.SL_Calibrate_Click();
            await Until(() => mic.Feed != null);
            (s.SpeechPushToTalkEnabled, ConditioningControlPanel.CoreEntitlement.HasPremiumProvider, ConditioningControlPanel.CoreSpeech.IsAvailableProvider) = (true, () => true, () => true);
            shell.StopVoiceInput(); // the borrower takes the mic...
            shell.RefreshVoiceInputModes(); // ...and hands it back while calibration is still unwinding
            Assert.True(shell.CalibratingWake);
            if (stopAfter) shell.StopVoiceInput(); // an explicit stop after the return wins
            await Until(() => !shell.CalibratingWake);
            Assert.Equal(!stopAfter, shell.PushToTalkArmed);
        }
        finally
        {
            (s.MicConsentGiven, s.SpeechPushToTalkEnabled) = (consent, ptt);
            (ConditioningControlPanel.CoreEntitlement.HasPremiumProvider, ConditioningControlPanel.CoreSpeech.IsAvailableProvider) = (premium, speech);
            shell.StopVoiceInput(); shell.RequestExit(); Directory.Delete(root, true);
        }
    });
}
