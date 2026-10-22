using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Race;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Row views-deeper-editor-window (wire-views-deeper-editor-window-b): the haptic Test
/// button, Preview into the shell's one player, and the audio waveform peaks.</summary>
public sealed class DeeperEditorTestFireTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static void Click(Button b) { b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs(); }

    /// <summary>WPF DeeperEditorWindow.xaml.cs:2755: no device -> the strip says so.</summary>
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
            // The sample project is clean or not: either way the strip shows the message.
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

    /// <summary>WPF DeeperEditorWindow.xaml.cs:4180: Preview loads the live project into the ONE
    /// shared player; a second Preview reuses it.</summary>
    [Fact]
    public Task PreviewOpensTheShellsOnePlayer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        MainShellWindow? w = null;
        try
        {
            w = new MainShellWindow();
            w.Show();
            Dispatcher.UIThread.RunJobs();
            w.OpenDeeperEditor(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "/tmp/none.mp3" }, null);
            Dispatcher.UIThread.RunJobs();
            var editor = w.OwnedWindows.OfType<DeeperEditorWindow>().Single();
            Assert.Empty(w.OwnedWindows.OfType<EnhancementPlayerWindow>());

            Click(editor.BtnPreview);
            Click(editor.BtnPreview);
            Assert.Single(w.OwnedWindows.OfType<EnhancementPlayerWindow>());
        }
        finally
        {
            foreach (var owned in w?.OwnedWindows.ToList() ?? new()) owned.Close();
            w?.Close();
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF DeeperEditorWindow.xaml.cs:1197: a local audio clip draws its peaks.</summary>
    [Fact]
    public Task LocalAudioDrawsItsWaveform() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-deeper-wave-").FullName;
        var path = Path.Combine(dir, "clip.mp3");
        File.WriteAllBytes(path, new byte[16]);
        var oldOpen = DeeperLocalAudio.Open;
        var oldDecode = TrackDecoder.DecodeProvider;
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(null);
        var samples = new float[TrackPcm.SampleRate * 20];
        samples[TrackPcm.SampleRate * 10] = 0.9f;
        TrackDecoder.DecodeProvider = (p, _, _) => p == path ? samples : throw new InvalidOperationException(p);
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
            TrackDecoder.DecodeProvider = oldDecode;
            Directory.Delete(dir, true);
        }
    });
}
