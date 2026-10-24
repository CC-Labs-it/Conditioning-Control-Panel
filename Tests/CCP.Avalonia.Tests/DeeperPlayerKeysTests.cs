using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF EnhancementPlayerWindow keyboard shortcuts, hold-Esc stop and waveform peaks.</summary>
public sealed class DeeperPlayerKeysTests
{
    private sealed class Audio : IDeeperLocalAudio
    {
        public double DurationSeconds { get; set; } = 100;
        public double PositionSeconds { get; set; }
        public int Volume { get; set; }
        public bool Playing, Disposed;
        public event Action? Ended { add { } remove { } }
        public void Play() => Playing = true;
        public void Pause() => Playing = false;
        public void Dispose() { Disposed = true; Playing = false; }
    }

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static void Press(Window w, Key key, bool down = true, KeyModifiers mods = KeyModifiers.None) =>
        w.RaiseEvent(new KeyEventArgs { RoutedEvent = down ? InputElement.KeyDownEvent : InputElement.KeyUpEvent, Key = key, KeyModifiers = mods });

    private static void Tick(EnhancementPlayerWindow w) =>
        typeof(EnhancementPlayerWindow).GetMethod("EscHoldTimer_Tick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(w, new object?[] { null, EventArgs.Empty });

    [Fact]
    public Task KeysDriveTransportVolumeMuteAndHoldEscStopsWhileATapOnlyClosesAnIdlePlayer() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var open = DeeperLocalAudio.Open;
        var decoder = AudioWaveformCache.Decoder;
        var settings = CoreSettings.Current;
        var (volume, strict) = (settings.DeeperPlayerVolume, settings.StrictLockEnabled);
        settings.DeeperPlayerVolume = 37;
        settings.StrictLockEnabled = false;
        var audio = new Audio();
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(audio);
        AudioWaveformCache.Decoder = _ => throw new InvalidDataException("no decode in this test");
        var win = new EnhancementPlayerWindow(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*" }, "test");
        var closed = false;
        win.Closed += (_, _) => closed = true;
        try
        {
            win.Show();
            win.OpenLocalMediaFile("clip.mp3");
            Dispatcher.UIThread.RunJobs();
            Assert.True(audio.Playing);

            Press(win, Key.Space);
            Assert.False(audio.Playing);
            Press(win, Key.Space); // still down: a repeat never toggles back
            Assert.False(audio.Playing);
            Press(win, Key.Space, down: false);
            Press(win, Key.Space);
            Assert.True(audio.Playing);
            Press(win, Key.Space, down: false);

            audio.PositionSeconds = 10;
            Press(win, Key.Right);
            Assert.Equal(15, audio.PositionSeconds);
            Press(win, Key.Right, mods: KeyModifiers.Shift);
            Assert.Equal(45, audio.PositionSeconds);
            audio.PositionSeconds = 2;
            Press(win, Key.Left);
            Assert.Equal(0, audio.PositionSeconds);

            var slider = win.FindControl<Slider>("SliderVolume")!;
            Press(win, Key.Up);
            Assert.Equal(42, audio.Volume);
            Press(win, Key.Down);
            Press(win, Key.Down, down: false);
            Assert.Equal(37, audio.Volume);
            Press(win, Key.M);
            Press(win, Key.M, down: false);
            Assert.Equal(0, audio.Volume);
            Assert.Equal(37, settings.DeeperPlayerVolume); // a muted 0 is never persisted
            Press(win, Key.M);
            Press(win, Key.M, down: false);
            Assert.Equal(37, slider.Value);

            // A tap over a live session never closes it.
            Press(win, Key.Escape);
            Press(win, Key.Escape, down: false);
            Assert.False(closed);
            Assert.True(audio.Playing);

            // Holding Esc for 3 s stops playback; the release after it is consumed.
            var now = new DateTime(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
            win.UtcNow = () => now;
            var hint = win.FindControl<Border>("HoldEscHint")!;
            Press(win, Key.Escape);
            Assert.True(hint.IsVisible);
            for (var s = 1; s <= 3; s++)
            {
                now = now.AddSeconds(1.05);
                Press(win, Key.Escape); // key repeat keeps the hold alive
                Tick(win);
            }
            Assert.True(audio.Disposed);
            Assert.False(hint.IsVisible);
            Assert.Equal(Loc.Get("deeper_player_status_escape_stopped"), win.FindControl<TextBlock>("TxtStatus")!.Text);
            Press(win, Key.Escape, down: false);
            Assert.False(closed);

            // Strict lock: even an idle player ignores the tap.
            settings.StrictLockEnabled = true;
            Press(win, Key.Escape);
            Press(win, Key.Escape, down: false);
            Assert.False(closed);
            settings.StrictLockEnabled = false;
            Press(win, Key.Escape);
            Press(win, Key.Escape, down: false);
            Assert.True(closed);
            return Task.CompletedTask;
        }
        finally
        {
            if (!closed) win.Close();
            DeeperLocalAudio.Open = open;
            AudioWaveformCache.Decoder = decoder;
            settings.DeeperPlayerVolume = volume;
            settings.StrictLockEnabled = strict;
        }
    });

    [Fact]
    public Task APickedClipDrawsItsDecodedPeaks() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var open = DeeperLocalAudio.Open;
        var decoder = AudioWaveformCache.Decoder;
        var dir = Directory.CreateTempSubdirectory("ccp-deeper-player-peaks-").FullName;
        var path = System.IO.Path.Combine(dir, "clip.mp3");
        File.WriteAllBytes(path, new byte[16]);
        DeeperLocalAudio.Open = _ => Task.FromResult<IDeeperLocalAudio?>(new Audio());
        string? decoded = null;
        AudioWaveformCache.Decoder = p =>
        {
            decoded = p;
            return new AudioWaveformResult { Peaks = new[] { 0.2f, 0.9f, 0.4f, 1f }, DurationSeconds = 100 };
        };
        var win = new EnhancementPlayerWindow(new Enhancement { MediaType = MediaTypes.Audio, MediaSource = "*" }, "test");
        try
        {
            win.Show();
            win.OpenLocalMediaFile(path);
            var wave = win.FindControl<global::Avalonia.Controls.Shapes.Path>("WaveformPath")!;
            // Time-bounded, not a yield count: the decode and the disk-cache write run on the pool,
            // which a loaded Windows runner did not finish inside 2000 yields.
            var until = DateTime.UtcNow.AddSeconds(15);
            while (wave.Data == null && DateTime.UtcNow < until)
            {
                await Task.Yield();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(path, decoded);
            Assert.NotNull(wave.Data);
        }
        finally
        {
            win.Close();
            DeeperLocalAudio.Open = open;
            AudioWaveformCache.Decoder = decoder;
            try { Directory.Delete(dir, true); } catch { }
        }
    });
}
