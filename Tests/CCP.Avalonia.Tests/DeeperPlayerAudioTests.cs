using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Deeper;
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
        public void Play() => Playing = true;
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
        var win = new EnhancementPlayerWindow(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*" }, "test");
        try
        {
            win.Show();
            win.OpenLocalMediaFile(path); // real picker/association entry, not a test-only state assignment
            Dispatcher.UIThread.RunJobs();
            Assert.True(first.Playing);
            Assert.True(MediaDurationCache.TryGetCached(path, out var cached));
            Assert.Equal(95, cached);
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
            Assert.True(MediaDurationCache.TryGetCached(path, out cached));
            Assert.Equal(95, cached); // WPF publishes only the first positive duration per load.
            Assert.True(win.FindControl<TextBlock>("TxtHint")!.IsVisible);
            Assert.Equal(Loc.Get("deeper_player_no_enh_for_media"), win.FindControl<TextBlock>("TxtHint")!.Text);
            if (Environment.GetEnvironmentVariable("CCP_DEEPER_PLAYER_RENDER") is { Length: > 0 } render)
            {
                win.UpdateLayout();
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)win.Bounds.Width, (int)win.Bounds.Height));
                bitmap.Render(win);
                bitmap.Save(render);
            }
            Invoke(win, "SeekAudio", 50d);
            Assert.Equal(50, first.PositionSeconds);
            win.UpdateLayout();
            var miniWidth = win.FindControl<Canvas>("MiniTimelineCanvas")!.Bounds.Width;
            Assert.True(miniWidth > 0);
            Invoke(win, "SeekToMiniPosition", miniWidth * 0.75);
            Assert.Equal(75, first.PositionSeconds, 3);
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
            PanicSurfaces.All.Single(s => s.Id == "deeper-player-audio").Stop(null);
            Assert.True(replay.Disposed);
            Assert.False(replay.Playing);
            Click(win, "BtnPlayPause");
            Dispatcher.UIThread.RunJobs();
            Assert.True(endedReplay.Playing);
            endedReplay.End();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(IconKind.Play, win.FindControl<ConditioningControlPanel.Avalonia.Controls.IconGlyph>("TxtPlayPauseGlyph")!.Kind);
            Assert.Equal("0:00", win.FindControl<TextBlock>("TxtCurrent")!.Text);
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
            Assert.True(current.Disposed);
            Assert.Equal("0:00", win.FindControl<TextBlock>("TxtTotal")!.Text);
            Assert.Equal(IconKind.Play, win.FindControl<ConditioningControlPanel.Avalonia.Controls.IconGlyph>("TxtPlayPauseGlyph")!.Kind);
            Assert.Equal(Loc.Get("deeper_player_pill_loaded"), win.FindControl<TextBlock>("StatusPillText")!.Text);
        }
        finally { win.Close(); DeeperLocalAudio.Open = open; }
    });

    [Fact]
    public Task AParseCompletingAfterPanicOrCloseNeverStartsSound() => AvaloniaTestDispatcher.RunAsync(async () =>
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
            PanicSurfaces.All.Single(s => s.Id == "deeper-player-audio").Stop(null);
            var late = new Audio();
            pending.SetResult(late);
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.True(late.Disposed);
            Assert.False(late.Playing);
            pending = new TaskCompletionSource<IDeeperLocalAudio?>();
            win.OpenLocalMediaFile("closing.mp3");
            win.Close();
            var closed = new Audio();
            pending.SetResult(closed);
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
            Assert.True(closed.Disposed);
            Assert.False(closed.Playing);
        }
        finally { win.Close(); DeeperLocalAudio.Open = open; }
    });
}
