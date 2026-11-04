using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Avalonia.Views.Games;
using Xunit;
namespace CCP.Avalonia.Tests;
[Collection(RunsAloneCollection.Name)]
public sealed class DeeperEditorTestFireTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private static void Click(Button b) { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }
    [Fact]
    public void HapticFeedbackExistsInAllTenLanguages()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourceFile())!, "../../CCP.Core/Localization/Languages"));
        var files = Directory.GetFiles(root, "*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
            foreach (var suffix in new[] { "no_device", "row_off", "premium" })
                Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("deeper_editor_haptic_test_" + suffix).GetString()), file);
        }
    }
    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
    private static void WriteStereo(string path)
    {
        using var writer = new BinaryWriter(File.Create(path));
        const int rate = 48000, frames = rate * 12, bytes = frames * 4;
        writer.Write(0x46464952); writer.Write(36 + bytes); writer.Write(0x45564157);
        writer.Write(0x20746D66); writer.Write(16); writer.Write((short)1); writer.Write((short)2);
        writer.Write(rate); writer.Write(rate * 4); writer.Write((short)4); writer.Write((short)16);
        writer.Write(0x61746164); writer.Write(bytes);
        for (int i = 0; i < frames; i++) { writer.Write((short)(i < frames / 2 ? 24576 : 0)); writer.Write((short)(i < frames / 2 ? -24576 : 0)); }
    }
    [Fact]
    public void HeadDecoderPreservesOppositePhaseStereo()
    {
        var old = DeeperWaveform.Transcode;
        try
        {
            DeeperWaveform.Transcode = (_, dest) => WriteStereo(dest);
            var sout = RaceTrackPlayer.SoutFor("fixture.wav", preserveFormat: true);
            Assert.DoesNotContain("channels=", sout);
            Assert.DoesNotContain("samplerate=", sout);
            var result = DeeperWaveform.Decode("fixture");
            Assert.Equal(12, result.DurationSeconds);
            Assert.Equal(72, result.Peaks.Length);
            Assert.Equal(0.75f, result.Peaks[0]);
            Assert.All(result.Peaks.Take(36), peak => Assert.Equal(0.75f, peak));
            Assert.All(result.Peaks.Skip(36), peak => Assert.Equal(0, peak));
        }
        finally { DeeperWaveform.Transcode = old; }
    }
    [Fact]
    public Task HelpOpensOneTourAndEditorCloseAbandonsIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        ConditioningControlPanel.Avalonia.Tours.TutorialHead.Seed();
        ConditioningControlPanel.Avalonia.Tours.TutorialHead.ResetForTests();
        var editor = new DeeperEditorWindow(new Enhancement(), null);
        try
        {
            editor.Show(); Dispatcher.UIThread.RunJobs();
            Click(editor.BtnEditorHelp);
            Assert.True(CoreTutorial.IsActive);
            Assert.Equal("de_intro", CoreTutorial.CurrentStep!.Id);
            var root = editor.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TextPanel");
            Click(editor.BtnEditorHelp);
            Assert.Same(root, editor.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TextPanel"));
            editor.ForceClose();
            Assert.False(CoreTutorial.IsActive);
        }
        finally { editor.ForceClose(); ConditioningControlPanel.Avalonia.Tours.TutorialHead.ResetForTests(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task HapticTestWithoutADeviceSaysSoInTheStrip() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = CoreHaptics.Service;
        var editor = new DeeperEditorWindow();   // the sample project carries one haptic event
        try
        {
            CoreHaptics.Service = null;
            editor.Show();
            Dispatcher.UIThread.RunJobs();
            var track = editor.GetType().GetField("_enhancement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(editor) is Enhancement e ? e.HapticTracks[0] : throw new InvalidOperationException();
            editor.SelectHaptic(track, track.Events[0]);
            Click(editor.BtnTestHaptic);
            Assert.True(editor.TxtValidationSummary.IsVisible);
            Assert.False(editor.TxtValidationClean.IsVisible);
            Assert.Equal(Loc.Get("deeper_editor_haptic_test_no_device"), editor.TxtValidationSummary.Text);
        }
        finally
        {
            editor.Close();
            CoreHaptics.Service = old;
        }
        return Task.CompletedTask;
    });
    [Theory]
    [InlineData(false, true, "deeper_editor_haptic_test_row_off")]
    [InlineData(true, false, "deeper_editor_haptic_test_premium")]
    [InlineData(true, true, null)]
    public Task HapticTestHonoursRoutingAndPremium(bool routing, bool premium, string? message) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var settings = CoreSettings.Current.Haptics;
        settings.EnsureV2Migrated();
        settings.Enabled = false; // a manual preview waives the master toggle, as on WPF
        settings.V2.Provider("mock").Enabled = true;
        settings.V2.Provider("buttplug").Enabled = false;
        settings.V2.Provider("lovense").Enabled = false;
        settings.V2.Rule(ConditioningControlPanel.Services.Haptics.Core.HapticLayer.Pattern).Enabled = routing;
        CoreEntitlement.HasPremiumProvider = () => premium;
        var toasts = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var oldToast = ConditioningControlPanel.Services.Haptics.Core.MockProviderV2.Toast;
        ConditioningControlPanel.Services.Haptics.Core.MockProviderV2.Toast = toasts.Enqueue;
        using var haptics = new ConditioningControlPanel.Services.HapticService(settings);
        CoreHaptics.Service = haptics;
        var editor = new DeeperEditorWindow();
        try
        {
            Assert.True(await haptics.ConnectAsync());
            editor.Show(); Dispatcher.UIThread.RunJobs();
            var enhancement = (Enhancement)editor.GetType().GetField("_enhancement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(editor)!;
            var track = enhancement.HapticTracks[0];
            editor.SelectHaptic(track, track.Events[0]);
            Click(editor.BtnTestHaptic);
            if (message != null) Assert.Equal(Loc.Get(message), editor.TxtValidationSummary.Text);
            else
            {
                var deadline = System.Diagnostics.Stopwatch.StartNew();
                while (!toasts.Any(t => t.Contains('%')) && deadline.Elapsed < TimeSpan.FromSeconds(5))
                    await Task.Delay(20);
                Assert.Contains(toasts, t => t.Contains('%'));
            }
        }
        finally
        {
            editor.ForceClose(); haptics.PanicStop(); CoreHaptics.Service = null;
            ConditioningControlPanel.Services.Haptics.Core.MockProviderV2.Toast = oldToast;
        }
    });
    [Fact]
    public Task LocalAudioDrawsItsWaveform() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-deeper-wave-").FullName;
        var path = Path.Combine(dir, "clip.mp3");
        File.WriteAllBytes(path, new byte[16]);
        var oldOpen = DeeperLocalAudio.Open;
        var oldDecode = DeeperWaveform.Transcode;
        var oldWaveform = ConditioningControlPanel.Services.Deeper.AudioWaveformCache.Decoder;
        ConditioningControlPanel.Services.Deeper.AudioWaveformCache.Decoder = DeeperWaveform.Decode;
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(null);
        DeeperWaveform.Transcode = (_, dest) => WriteStereo(dest);
        var editor = new DeeperEditorWindow(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = path }, null);
        try
        {
            editor.Show();
            Dispatcher.UIThread.RunJobs();
            await editor.WaveformLoad.WaitAsync(TimeSpan.FromSeconds(10));
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(editor.WaveformPath.Data);
            Assert.Equal("0:12", editor.TxtTotalTime.Text);
        }
        finally
        {
            editor.Close();
            DeeperLocalAudio.Open = oldOpen;
            DeeperWaveform.Transcode = oldDecode;
            ConditioningControlPanel.Services.Deeper.AudioWaveformCache.Decoder = oldWaveform;
            Directory.Delete(dir, true);
        }
    });
    private sealed class DurationAudio : IDeeperLocalAudio
    {
        public double DurationSeconds => 29;
        public double PositionSeconds { get; set; }
        public event Action? Ended { add { } remove { } }
        public void Play() { }
        public void Pause() { }
        public void Dispose() { }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task DecodedDurationWinsRegardlessOfCompletionOrder(bool waveformFirst) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-duration-race-").FullName;
        var path = Path.Combine(dir, "clip.mp3");
        File.WriteAllBytes(path, new byte[16]);
        var oldOpen = DeeperLocalAudio.Open;
        var oldDecoder = ConditioningControlPanel.Services.Deeper.AudioWaveformCache.Decoder;
        var audio = new TaskCompletionSource<IDeeperLocalAudio?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseDecode = new System.Threading.ManualResetEventSlim();
        var decodeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DeeperLocalAudio.Open = _ => audio.Task;
        ConditioningControlPanel.Services.Deeper.AudioWaveformCache.Decoder = _ =>
        {
            decodeStarted.TrySetResult();
            if (!releaseDecode.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
            return new() { DurationSeconds = 12, Peaks = new float[] { 0.5f } };
        };
        var editor = new DeeperEditorWindow(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = path }, null);
        try
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var preview = (Task)typeof(DeeperEditorWindow).GetMethod("InitializePreviewAsync", flags)!.Invoke(editor, null)!;
            await decodeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (waveformFirst)
            {
                releaseDecode.Set();
                await editor.WaveformLoad.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(editor.TxtTotalTime.Text == "0:12", $"seconds={typeof(DeeperEditorWindow).GetField("_totalSeconds", flags)!.GetValue(editor)} decoded={typeof(DeeperEditorWindow).GetField("_hasWaveformDuration", flags)!.GetValue(editor)} generation={typeof(DeeperEditorWindow).GetField("_previewGeneration", flags)!.GetValue(editor)} disposed={typeof(DeeperEditorWindow).GetField("_playbackDisposed", flags)!.GetValue(editor)}");
                audio.SetResult(new DurationAudio());
                await preview.WaitAsync(TimeSpan.FromSeconds(10));
            }
            else
            {
                audio.SetResult(new DurationAudio());
                await preview.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal("0:29", editor.TxtTotalTime.Text);
                releaseDecode.Set();
                await editor.WaveformLoad.WaitAsync(TimeSpan.FromSeconds(10));
            }
            Assert.Equal("0:12", editor.TxtTotalTime.Text);
            typeof(DeeperEditorWindow).GetMethod("PlayheadTimer_Tick", flags)!.Invoke(editor, new object?[] { null, EventArgs.Empty });
            Assert.Equal("0:12", editor.TxtTotalTime.Text);
        }
        finally
        {
            releaseDecode.Set();
            audio.TrySetResult(null);
            editor.ForceClose();
            DeeperLocalAudio.Open = oldOpen;
            ConditioningControlPanel.Services.Deeper.AudioWaveformCache.Decoder = oldDecoder;
            Directory.Delete(dir, true);
        }
    });

    [Fact]
    public Task ReloadIgnoresOldDecodeAndFallsBackToPlayback() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-duration-reload-").FullName;
        var path = Path.Combine(dir, "clip.mp3");
        File.WriteAllBytes(path, new byte[16]);
        var oldOpen = DeeperLocalAudio.Open;
        var oldDecoder = ConditioningControlPanel.Services.Deeper.AudioWaveformCache.Decoder;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new System.Threading.ManualResetEventSlim();
        var calls = 0;
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(new DurationAudio());
        ConditioningControlPanel.Services.Deeper.AudioWaveformCache.Decoder = _ =>
        {
            if (System.Threading.Interlocked.Increment(ref calls) == 1)
            {
                started.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException();
                return new() { DurationSeconds = 12, Peaks = new float[] { 0.5f } };
            }
            throw new InvalidOperationException("decode failed");
        };
        var editor = new DeeperEditorWindow(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = path }, null);
        try
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var initialize = typeof(DeeperEditorWindow).GetMethod("InitializePreviewAsync", flags)!;
            await ((Task)initialize.Invoke(editor, null)!).WaitAsync(TimeSpan.FromSeconds(10));
            var oldLoad = editor.WaveformLoad;
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await ((Task)initialize.Invoke(editor, null)!).WaitAsync(TimeSpan.FromSeconds(10));
            await editor.WaveformLoad.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("0:29", editor.TxtTotalTime.Text);
            release.Set();
            await oldLoad.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("0:29", editor.TxtTotalTime.Text);
        }
        finally
        {
            release.Set();
            editor.ForceClose();
            DeeperLocalAudio.Open = oldOpen;
            ConditioningControlPanel.Services.Deeper.AudioWaveformCache.Decoder = oldDecoder;
            Directory.Delete(dir, true);
        }
    });

}
