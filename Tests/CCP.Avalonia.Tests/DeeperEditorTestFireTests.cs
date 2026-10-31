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
}
