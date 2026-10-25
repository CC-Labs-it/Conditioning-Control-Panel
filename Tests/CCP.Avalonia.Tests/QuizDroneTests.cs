using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class QuizDroneTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    public Task DroneUsesFirstExistingCueAndPerceptualGainAndPanicStopsIt(bool enabled, bool neutralExists, bool legacyExists)
        => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetTempPath(), "quiz-drone-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var legacy = Path.Combine(root, "00 Bimbo Drone.mp3");
        var neutral = Path.Combine(root, "quiz_drone.mp3");
        if (legacyExists) File.WriteAllBytes(legacy, new byte[] { 0 });
        if (neutralExists) File.WriteAllBytes(neutral, new byte[] { 0 });
        var (play, resolve, master) = (QuizWindow.PlayDrone, QuizWindow.ResolveDrone, CoreSettings.Current.MasterVolume);
        var voice = new Voice();
        var calls = new List<(string Path, double Volume, bool Loop)>();
        QuizWindow.PlayDrone = (p, v, l, _) => { calls.Add((p, v, l)); return voice; };
        QuizWindow.ResolveDrone = n => Path.Combine(root, n);
        CoreSettings.Current.MasterVolume = 60;
        var host = new Window { Content = new GradedIntakeTabView() };
        host.Show();
        var tab = (GradedIntakeTabView)host.Content!;
        QuizWindow? window = null;
        using var sub = Window.WindowOpenedEvent.AddClassHandler(typeof(QuizWindow), (s, _) => window = (QuizWindow?)s);
        try
        {
            // The user path: Graded Intake's drone checkbox, then Start Quiz (WPF MainWindow.Lab.cs:135).
            tab.ChkQuizDrone.IsChecked = enabled;
            CoreAi.IsAvailableProvider = () => true;
            tab.BtnStartQuiz_Click(null, new RoutedEventArgs());
            Assert.NotNull(window);
            Dispatcher.UIThread.RunJobs();   // Loaded is posted, not raised inside Show()
            if (enabled && (neutralExists || legacyExists))
            {
                Assert.Single(calls);        // Loaded fired and started exactly one loop
                Invoke(window!, "Window_Loaded");
                Assert.Single(calls);        // a repeated Loaded cannot start a second loop
                Assert.Equal(neutralExists ? neutral : legacy, calls[0].Path);
                Assert.True(calls[0].Loop);
                Assert.Equal(Math.Pow(0.6f * 0.35f, 1.5), calls[0].Volume, 6);
                Invoke(window!, "EnterSurrenderMode");
                Assert.Equal(0, voice.Volume);
                Invoke(window!, "ExitSurrenderMode");
                Assert.Equal(calls[0].Volume, voice.Volume, 6);
            }
            else Assert.Empty(calls);
            var quiz = PanicSurfaces.All.Single(s => s.Id == "quiz");
            Assert.Null(quiz.OwnsTheScreen);   // WPF: a closing step, never a game (GameSurfaces.cs:56-64)
            quiz.Stop(null);
            Assert.Equal(enabled && (neutralExists || legacyExists) ? 1 : 0, voice.Disposals);
            Assert.False(window!.IsVisible);
        }
        finally
        {
            CoreAi.IsAvailableProvider = null;
            window?.Close();
            host.Close();
            (QuizWindow.PlayDrone, QuizWindow.ResolveDrone, CoreSettings.Current.MasterVolume) = (play, resolve, master);
            Directory.Delete(root, true);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public void EndpointFailureRetainsDefaultAndDevicesAlreadyRead()
    {
        var fallback = new LibVlcAudio.OutputDevice("", "System default");
        var device = new LibVlcAudio.OutputDevice("endpoint", "Speakers");
        IEnumerable<LibVlcAudio.OutputDevice> Read()
        {
            yield return device;
            throw new InvalidOperationException("endpoint removed");
        }
        var list = new List<LibVlcAudio.OutputDevice> { fallback };
        Assert.Same(list, WindowsAudioDevices.Append(list, Read));
        Assert.Equal(new[] { fallback, device }, list);
    }

    private static void Invoke(QuizWindow window, string name) => typeof(QuizWindow)
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);

    private sealed class Voice : MindWipePlayer.IVoice
    {
        public double Volume { get; set; }
        public int Disposals;
        public void Dispose() => Disposals++;
    }
}
