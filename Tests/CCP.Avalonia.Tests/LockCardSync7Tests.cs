using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class LockCardSync7Tests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public Task NonpositiveCountRequiresOneTypedRepeat(int repeats) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        try
        {
            LockCardWindow.ShowOnAllMonitors("obey", repeats, strictMode: false, isTest: true);
            Dispatcher.UIThread.RunJobs();
            var card = LockCardWindow.Primary!;
            Assert.Contains("0 / 1", card.FindControl<TextBlock>("TxtProgress")!.Text);
            card.FindControl<TextBox>("TxtInput")!.Focus();
            card.KeyTextInput("obey");
            Dispatcher.UIThread.RunJobs();
            Assert.True(card.IsCompleted);
            Assert.Equal(1, card.CompletedRepeats);
        }
        finally { LockCardWindow.ForceCloseAll(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("PROCESS. COMPLY. REPEAT.", "process comply repeat")]
    [InlineData("SYSTEM OVERLOAD — SHUTDOWN", "system overload shutdown")]
    public Task OptionalPunctuationSolvesTheRealCard(string phrase, string typed) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        try
        {
            LockCardWindow.ShowOnAllMonitors(phrase, 1, strictMode: false, isTest: true);
            Dispatcher.UIThread.RunJobs();
            var card = LockCardWindow.Primary!;
            card.FindControl<TextBox>("TxtInput")!.Focus();
            foreach (var c in typed)
            {
                card.KeyTextInput(c.ToString());
                Dispatcher.UIThread.RunJobs();
            }
            Assert.True(card.IsCompleted);
            Assert.Equal(0, card.TotalErrors);
        }
        finally { LockCardWindow.ForceCloseAll(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task VoiceHintsUseSentencePunctuation() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var saved = (CoreSpeech.IsAvailableProvider, CoreSpeech.HasCaptureDeviceProvider, CoreSpeech.ModelStatusProvider);
        try
        {
            // Pin every branch; the machine's real speech state must not pick the one checked.
            var hints = new List<string> { LockCardFeatureControl.VoiceHint(false) };
            (CoreSpeech.IsAvailableProvider, CoreSpeech.HasCaptureDeviceProvider) = (() => true, () => true);
            hints.Add(LockCardFeatureControl.VoiceHint(true));
            (CoreSpeech.IsAvailableProvider, CoreSpeech.HasCaptureDeviceProvider) = (() => false, () => false);
            hints.Add(LockCardFeatureControl.VoiceHint(true));
            CoreSpeech.HasCaptureDeviceProvider = () => true;
            CoreSpeech.ModelStatusProvider = () => CoreSpeechModelStatus.LoadFailed;
            hints.Add(LockCardFeatureControl.VoiceHint(true));
            CoreSpeech.ModelStatusProvider = () => CoreSpeechModelStatus.NotProbed;
            hints.Add(LockCardFeatureControl.VoiceHint(true));
            Assert.Equal(5, hints.Distinct().Count());
            Assert.All(hints, h => Assert.DoesNotContain("—", h));
        }
        finally { (CoreSpeech.IsAvailableProvider, CoreSpeech.HasCaptureDeviceProvider, CoreSpeech.ModelStatusProvider) = saved; }
        return Task.CompletedTask;
    });

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.RenderProof.EnsureSetUp();
    }
}
