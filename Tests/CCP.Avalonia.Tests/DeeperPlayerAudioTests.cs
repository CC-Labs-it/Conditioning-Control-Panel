using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

public sealed class DeeperPlayerAudioTests
{
    private sealed class Audio : IDeeperLocalAudio
    {
        public double DurationSeconds { get; set; } = 95;
        public double PositionSeconds { get; set; }
        public int Volume { get; set; }
        public bool Playing, Disposed;
        public event Action? Ended;
        public bool StartSucceeds = true;
        public void Play() { if (!StartSucceeds) throw new InvalidOperationException("Audio transport could not start"); Playing = true; }
        public event Action? Failed;
        public void Error() => Failed?.Invoke();
        public void Pause() => Playing = false;
        public void Dispose() { Disposed = true; Playing = false; }
        public void End() { PositionSeconds = 0; Playing = false; Ended?.Invoke(); }
    }

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private static void Invoke(object obj, string name, params object?[] args) =>
        obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(obj, args);
    private static void Click(EnhancementPlayerWindow window, string name) =>
        window.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task AudioUnloadsFailedOrPendingVideo(bool navigated) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var open = DeeperLocalAudio.Open;
        var audio = new Audio();
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(audio);
        var win = new EnhancementPlayerWindow(null, null);
        try
        {
            win.Show();
            var web = win.FindControl<ConditioningControlPanel.Avalonia.Views.Controls.WebHost>("VideoBrowser")!;
            var pending = new TaskCompletionSource<string?>();
            web.ScriptInvoker = _ => navigated ? Task.FromException<string?>(new InvalidOperationException()) : pending.Task;
            win.OpenLocalMediaFile(Path.Combine(Path.GetTempPath(), "pending.mp4"));
            if (navigated) Assert.Null(await web.InvokeScriptAsync("throw new Error()"));
            else Invoke(win, "PollVideoTimeAsync");
            typeof(EnhancementPlayerWindow).GetField("_videoNavigated", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(win, navigated);
            var previousPage = web.Source;
            win.OpenLocalMediaFile("audio.mp3");
            Assert.NotEqual(previousPage, web.Source);
            Assert.Equal(new Uri("about:blank"), web.Source);
            Assert.True(web.NavigationRequests > 0);
            Assert.False(web.AllowNavigation!(new Uri("https://hypnotube.com/video/late")));
            Assert.True(web.AllowNavigation(new Uri("about:blank")));
            Assert.True(audio.Playing);
            Click(win, "BtnPlayPause");
            pending.SetResult("40|500|1");
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(IconKind.Play, win.FindControl<ConditioningControlPanel.Avalonia.Controls.IconGlyph>("TxtPlayPauseGlyph")!.Kind);
        }
        finally { win.Close(); DeeperLocalAudio.Open = open; }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AudioStartRejectionOrLaterErrorResetsAndDisposes(bool laterError) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var open = DeeperLocalAudio.Open;
        var audio = new Audio { StartSucceeds = laterError };
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(audio);
        var win = new EnhancementPlayerWindow(null, null);
        try
        {
            win.Show();
            win.OpenLocalMediaFile("error.mp3");
            if (laterError) { Assert.True(audio.Playing); audio.Error(); }
            Dispatcher.UIThread.RunJobs();
            Assert.True(audio.Disposed);
            Assert.False(audio.Playing);
            Assert.Equal(Loc.Get("deeper_player_status_audio_failed"), win.FindControl<TextBlock>("TxtStatus")!.Text);
            // WPF keeps the previous total when playback fails (player.xaml.cs:648-652).
            Assert.Equal(laterError ? "1:35" : "0:00", win.FindControl<TextBlock>("TxtTotal")!.Text);
            Assert.Equal(IconKind.Play, win.FindControl<ConditioningControlPanel.Avalonia.Controls.IconGlyph>("TxtPlayPauseGlyph")!.Kind);
            var replacement = new Audio();
            DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(replacement);
            win.OpenLocalMediaFile("replacement.mp3");
            audio.Error();
            Dispatcher.UIThread.RunJobs();
            Assert.True(replacement.Playing);
            Click(win, "BtnPlayPause");
            replacement.StartSucceeds = false;
            Click(win, "BtnPlayPause");
            Assert.True(replacement.Disposed);
        }
        finally { win.Close(); DeeperLocalAudio.Open = open; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LocalPickerPathAutoplaysPausesSeeksStopsReplaysAndPanics() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var dir = Directory.CreateTempSubdirectory("ccp-deeper-player-audio-").FullName;
        var path = Path.Combine(dir, "clip.mp3");
        File.WriteAllBytes(path, new byte[16]);
        var open = DeeperLocalAudio.Open;
        var volume = CoreSettings.Current.DeeperPlayerVolume;
        CoreSettings.Current.DeeperPlayerVolume = 37;
        var first = new Audio();
        var replay = new Audio();
        var endedReplay = new Audio();
        var count = 0;
        DeeperLocalAudio.Open = p => Task.FromResult<IDeeperLocalAudio?>(p == path ? (++count == 1 ? first : count == 2 ? replay : endedReplay) : null);
        var win = new EnhancementPlayerWindow(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*", Regions = { new Region { End = 200 } } }, "test");
        try
        {
            win.Show();
            win.OpenLocalMediaFile(path); // real picker/association entry, not a test-only state assignment
            Dispatcher.UIThread.RunJobs();
            Assert.True(first.Playing);
            Assert.Equal(Loc.Get("deeper_player_pill_live"), win.FindControl<TextBlock>("StatusPillText")!.Text);
            Assert.Equal(37, first.Volume);
            Assert.Equal("1:35", win.FindControl<TextBlock>("TxtTotal")!.Text);
            Click(win, "BtnPlayPause");
            Assert.False(first.Playing);
            Click(win, "BtnPlayPause");
            Assert.True(first.Playing);
            first.PositionSeconds = 30;
            first.DurationSeconds = 100;
            Invoke(win, "UiTimer_Tick", null, EventArgs.Empty);
            Assert.Equal("0:30", win.FindControl<TextBlock>("TxtCurrent")!.Text);
            Assert.Equal("1:40", win.FindControl<TextBlock>("TxtTotal")!.Text);
            win.UpdateLayout();
            var waveform = win.FindControl<Canvas>("WaveformCanvas")!;
            var halfway = waveform.TranslatePoint(new Point(waveform.Bounds.Width * 0.5, 20), win)!.Value;
            win.MouseDown(halfway, global::Avalonia.Input.MouseButton.Left);
            win.MouseUp(halfway, global::Avalonia.Input.MouseButton.Left);
            Assert.Equal(50, first.PositionSeconds, 3);
            var point = waveform.TranslatePoint(new Point(waveform.Bounds.Width * 0.25, 20), win)!.Value;
            win.MouseDown(point, global::Avalonia.Input.MouseButton.Left);
            win.MouseUp(point, global::Avalonia.Input.MouseButton.Left);
            Assert.Equal(25, first.PositionSeconds, 3);
            var miniWidth = win.FindControl<Canvas>("MiniTimelineCanvas")!.Bounds.Width;
            Assert.True(miniWidth > 0);
            Invoke(win, "SeekToMiniPosition", miniWidth * 0.375);
            Assert.Equal(75, first.PositionSeconds, 3);
            Invoke(win, "SeekToMiniPosition", miniWidth * 0.75);
            Assert.Equal(100, first.PositionSeconds, 3);
            win.FindControl<Slider>("SliderVolume")!.Value = 23;
            Assert.Equal(23, first.Volume);
            Assert.Equal(23, CoreSettings.Current.DeeperPlayerVolume);
            Click(win, "BtnStop");
            Assert.True(first.Disposed);
            Assert.Equal("0:00", win.FindControl<TextBlock>("TxtCurrent")!.Text);
            Click(win, "BtnPlayPause");
            Dispatcher.UIThread.RunJobs();
            Assert.True(replay.Playing);
            Assert.Equal(2, count); // sticky path opens a fresh transport after Stop
            replay.PositionSeconds = 41;
            PanicSurfaces.All.Single(s => s.Id == "deeper-player").Stop(null);
            Assert.False(replay.Disposed);
            Assert.False(replay.Playing);
            Assert.Equal(41, replay.PositionSeconds);
            Click(win, "BtnPlayPause");
            Assert.True(replay.Playing);
            Click(win, "BtnStop");
            Assert.True(replay.Disposed);
            Click(win, "BtnPlayPause");
            Dispatcher.UIThread.RunJobs();
            Assert.True(endedReplay.Playing);
            endedReplay.End();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(IconKind.Play, win.FindControl<ConditioningControlPanel.Avalonia.Controls.IconGlyph>("TxtPlayPauseGlyph")!.Kind);
            Assert.Equal("0:00", win.FindControl<TextBlock>("TxtCurrent")!.Text);
            Assert.Equal(Loc.Get("deeper_player_status_ended"), win.FindControl<TextBlock>("TxtStatus")!.Text);
        }
        finally
        {
            win.Close();
            DeeperLocalAudio.Open = open;
            CoreSettings.Current.DeeperPlayerVolume = volume;
            Directory.Delete(dir, true);
        }
        Assert.True(replay.Disposed);
        Assert.True(endedReplay.Disposed);
        return Task.CompletedTask;
    });

    [Fact]
    public Task AReplacementOwnsTheClipAndAnUnavailableBackendNeverClaimsPlayback() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var open = DeeperLocalAudio.Open;
        var first = new TaskCompletionSource<IDeeperLocalAudio?>();
        var second = new TaskCompletionSource<IDeeperLocalAudio?>();
        DeeperLocalAudio.Open = p => p == "first.mp3" ? first.Task : p == "second.mp3" ? second.Task : Task.FromResult<IDeeperLocalAudio?>(null);
        var win = new EnhancementPlayerWindow(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*" }, "test");
        win.Show();
        try
        {
            win.OpenLocalMediaFile("first.mp3");
            win.OpenLocalMediaFile("second.mp3");
            var current = new Audio();
            second.SetResult(current);
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.True(current.Playing);
            Assert.Equal(Loc.Get("deeper_player_pill_live"), win.FindControl<TextBlock>("StatusPillText")!.Text);
            var stale = new Audio();
            first.SetResult(stale);
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.True(stale.Disposed);
            Assert.False(stale.Playing);
            Assert.True(current.Playing);
            win.OpenLocalMediaFile("unavailable.mp3");
            Dispatcher.UIThread.RunJobs();
            Invoke(win, "UiTimer_Tick", null, EventArgs.Empty); // WPF refreshes the pill on its 100 ms tick.
            Assert.True(current.Disposed);
            // WPF leaves the previous total in place if the replacement cannot open.
            Assert.Equal("1:35", win.FindControl<TextBlock>("TxtTotal")!.Text);
            Assert.Equal(IconKind.Play, win.FindControl<ConditioningControlPanel.Avalonia.Controls.IconGlyph>("TxtPlayPauseGlyph")!.Kind);
            Assert.Equal(Loc.Get("deeper_player_pill_loaded"), win.FindControl<TextBlock>("StatusPillText")!.Text);
            var videoSwap = new Audio();
            DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(videoSwap);
            win.OpenLocalMediaFile("swap.mp3");
            Assert.True(videoSwap.Playing);
            win.OpenLocalMediaFile(Path.Combine(Path.GetTempPath(), "swap.mp4"));
            Assert.True(videoSwap.Disposed);
            Assert.False(videoSwap.Playing);
        }
        finally { win.Close(); DeeperLocalAudio.Open = open; }
    });

    [Theory]
    [InlineData("stop")]
    [InlineData("panic")]
    [InlineData("close")]
    public Task AParseCompletingAfterStopPanicOrCloseNeverStartsSound(string action) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var open = DeeperLocalAudio.Open;
        var pending = new TaskCompletionSource<IDeeperLocalAudio?>();
        DeeperLocalAudio.Open = _ => pending.Task;
        var win = new EnhancementPlayerWindow(null, null);
        win.Show();
        try
        {
            win.OpenLocalMediaFile("pending.mp3");
            if (action == "stop") Click(win, "BtnStop");
            else if (action == "panic") PanicSurfaces.All.Single(s => s.Id == "deeper-player").Stop(null);
            else win.Close();
            var late = new Audio();
            pending.SetResult(late);
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.True(late.Disposed);
            Assert.False(late.Playing);
        }
        finally { win.Close(); DeeperLocalAudio.Open = open; }
    });
}
