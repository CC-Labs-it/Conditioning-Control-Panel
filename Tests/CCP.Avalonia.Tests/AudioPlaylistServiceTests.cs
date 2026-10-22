using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.AudioPlayer;
using ConditioningControlPanel.Avalonia.AudioPlayer;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using App = ConditioningControlPanel.Avalonia.App;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AudioPlaylistServiceTests
{
    private static void Setup()
    {
        if (Application.Current == null) global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    [Fact]
    public void OwnPlayerSkipsMissingFilesPausesSeeksScalesAndStopsWithoutDroppingTracks() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var settings = new AppSettings { MasterVolume = 50 };
        var device = new SilentPlayer();
        using var service = new AudioPlaylistService(device, settings);
        var tracks = new List<string?>(); service.TrackChanged += tracks.Add;
        service.Add(new[] { "missing", "a.mp3", "b.mp3" });
        service.Play();
        Assert.Equal(new[] { "missing", "a.mp3" }, device.Attempts);
        Assert.Equal("a.mp3", service.CurrentPath);
        Assert.True(service.IsPlaying);
        Assert.Equal(40, device.Volume);
        service.Volume = 60; Assert.Equal(30, device.Volume);
        settings.MasterVolume = 25; Assert.Equal(15, device.Volume);
        service.Pause(); Assert.True(service.IsPaused);
        service.Play(); Assert.True(service.IsPlaying);
        service.Seek(999); Assert.Equal(10, service.PositionSeconds);
        service.Previous(); Assert.Equal(0, service.PositionSeconds); // after three seconds, restart
        service.Loop = AudioPlaylistLoop.One; device.Finish();
        Assert.Equal("a.mp3", device.CurrentPath);
        service.Next(); Assert.Equal("b.mp3", device.CurrentPath); // a manual next escapes loop-one
        service.Stop();
        Assert.False(service.IsPlaying); Assert.Equal(0, service.PositionSeconds);
        Assert.Equal(new[] { "missing", "a.mp3", "b.mp3" }, settings.AudioPlaylistPaths);
        Assert.Equal(60, settings.AudioPlaylistVolume);
        Assert.Equal(new[] { "a.mp3", "b.mp3" }, tracks);
    });
    [Fact]
    public void RemovingPlayingTrackMovesOnAndNaturalEndRewindsWithoutStartingAgain() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup(); var device = new SilentPlayer();
        using var service = new AudioPlaylistService(device, new AppSettings());
        service.Add(new[] { "a.mp3", "b.mp3", "c.mp3" }); service.PlayAt(1);
        service.RemoveAt(1); Assert.Equal("c.mp3", device.CurrentPath);
        device.Finish(); Assert.False(service.IsPlaying); Assert.Equal("a.mp3", service.CurrentPath);
        service.Clear(); Assert.Empty(service.Tracks); Assert.Null(service.CurrentPath);
    });
    [Fact]
    public void DeeperCardPlayGestureAndPanicReachTheSameIndependentPlayer() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup(); var device = new SilentPlayer(); var settings = new AppSettings();
        using var service = new AudioPlaylistService(device, settings);
        service.Add(new[] { "a.mp3" });
        var card = new AudioPlaylistCard(); card.Bind(service);
        card.FindControl<Button>("PlayButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(device.IsPlaying);
        var previous = App.AudioPlaylist; App.AudioPlaylist = service;
        try
        {
            MainShellWindow.StopEngine(); Assert.False(device.IsPlaying);
            service.Play(); PanicSurfaces.StopAll("playlist test"); Assert.False(device.IsPlaying);
        }
        finally { App.AudioPlaylist = previous; card.Bind(null); }
        Assert.Single(service.Tracks);
        Assert.Single(settings.AudioPlaylistPaths);
    });
    [Fact]
    public void WatchdogMutesWhileUiIsBusyThenStopsWithoutWritingSettings() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup(); var device = new SilentPlayer();
        var settings = new AppSettings { AudioPlaylistPaths = new List<string> { "a.mp3" }, AudioPlaylistVolume = 55 };
        using var service = new AudioPlaylistService(device, settings);
        service.Play();
        Task.Run(service.StopEmergency).GetAwaiter().GetResult();
        Assert.Equal(0, device.Volume); Assert.True(device.IsPlaying);
        Dispatcher.UIThread.RunJobs(); Assert.False(device.IsPlaying);
        Assert.Equal(55, settings.AudioPlaylistVolume); Assert.Single(settings.AudioPlaylistPaths);
    });
    private sealed class SilentPlayer : IPlaylistAudioPlayer
    {
        public List<string> Attempts { get; } = new();
        public string? CurrentPath { get; private set; }
        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }
        public long CurrentTimeMs { get; private set; }
        public long DurationMs => 10000;
        public int Volume { get; set; }
        public bool Play(string path) { Attempts.Add(path); if (path == "missing") return false; CurrentPath = path; IsPlaying = true; IsPaused = false; CurrentTimeMs = 0; return true; }
        public void Resume() { IsPlaying = true; IsPaused = false; }
        public void Pause() { IsPlaying = false; IsPaused = true; }
        public void Stop() { IsPlaying = IsPaused = false; CurrentPath = null; CurrentTimeMs = 0; }
        public void Seek(double seconds) { CurrentTimeMs = (long)(seconds * 1000); PlaybackTimeMsChanged?.Invoke(CurrentTimeMs); }
        public void Finish() { IsPlaying = false; Ended?.Invoke(); }
        public void Dispose() => Stop();
        public event Action<long>? PlaybackTimeMsChanged;
        public event Action? Ended;
        public event Action? StoppedUnexpectedly { add { } remove { } }
    }
}
