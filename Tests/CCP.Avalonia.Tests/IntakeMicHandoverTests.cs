using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Quiz;
using ConditioningControlPanel.Services.Speech;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class IntakeMicHandoverTests
{
    private sealed class NoMic : IMicSource
    {
        public bool HasDevice => true;
        public IReadOnlyList<SpeechInputDevice> ListDevices() => Array.Empty<SpeechInputDevice>();
        public IDisposable Start(Action<byte[], int> onPcm) => throw new InvalidOperationException("No real microphone in tests");
    }

    private sealed class TestSpeech(Func<bool> borrowed) : SpeechEngine(new NoMic(), Array.Empty<string>())
    {
        internal readonly TaskCompletionSource Listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<PhraseResult> Answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override bool IsAvailable => true;
        public override Task<PhraseResult> RecognizePhraseAsync(string target, RecognizeOptions? options = null, CancellationToken ct = default)
        {
            Assert.True(borrowed());
            Listening.TrySetResult();
            return Answer.Task.WaitAsync(ct);
        }
    }

    [Theory]
    [InlineData("answer")]
    [InlineData("stop")]
    [InlineData("close")]
    [InlineData("panic")]
    [InlineData("error")]
    public Task SpokenAnswerBorrowsAndReturnsOnEveryExit(string exit) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        IntakeTestApp.Ensure();
        var consent = CoreSettings.Current.MicConsentGiven;
        var hasMic = CoreSpeech.HasCaptureDeviceProvider;
        CoreSettings.Current.MicConsentGiven = true;
        CoreSpeech.HasCaptureDeviceProvider = () => true;
        var wakeListening = true;
        var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mic = new IntakeMicBorrow(() => wakeListening, () => wakeListening = false,
            () => { wakeListening = true; returned.TrySetResult(); });
        using var speech = new TestSpeech(() => mic.IsOwed && !wakeListening);
        var host = new IntakeHostWindow { Speech = () => speech, Mic = mic };
        try
        {
            host.Show();
            host.HandleMessage("{\"type\":\"speech-start\",\"id\":1,\"phrase\":\"test phrase\"}");
            Assert.False(wakeListening); // A start borrows before scheduling the listen loop.
            await speech.Listening.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(wakeListening);
            switch (exit)
            {
                case "answer": speech.Answer.TrySetResult(new PhraseResult { Matched = true }); break;
                case "stop": host.HandleMessage("{\"type\":\"speech-stop\",\"id\":1}"); break;
                case "close": host.Close(); break;
                case "panic": IntakeHostWindow.StopMicsForPanic(); break;
                case "error": speech.Answer.TrySetException(new InvalidOperationException("fake recognizer failure")); break;
            }
            await returned.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(wakeListening);
            Assert.Equal(0, mic.Holders);
        }
        finally
        {
            host.Close();
            CoreSettings.Current.MicConsentGiven = consent;
            CoreSpeech.HasCaptureDeviceProvider = hasMic;
        }
    });
}
