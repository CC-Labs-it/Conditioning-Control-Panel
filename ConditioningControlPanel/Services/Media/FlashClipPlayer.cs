using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibVLCSharp.Shared;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace ConditioningControlPanel.Services;

// Silent clip frames use the same transparent WPF/Skia surfaces as GIFs.
internal sealed class FlashClipPlayer : IDisposable
{
    /// <summary>App-wide player slots, shared by flashes, bubbles and any other non-flash clip.
    /// Counted as live leases (ClipSlotBudget), so a missed or doubled release can never drift
    /// the count.</summary>
    internal const int MaxPlayers = 8;
    /// <summary>Bubbles and any other non-flash clip share this many; the rest stay free for flashes.</summary>
    internal const int MaxSharedPlayers = 5;
    internal static readonly ClipSlotBudget Budget = new(MaxPlayers, MaxSharedPlayers);
    /// <summary>A disposed player gives its slot back at once and is torn down on the pool. If
    /// LibVLC Stop stalls, those teardowns must not pile up without bound: above this many still
    /// in flight, new starts are refused (as at the cap) until they drain.</summary>
    internal const int MaxTearingDown = 8;
    private static int _tearingDown;
    private static long _lastRefusedLogMs;

    internal static int TearingDown => Volatile.Read(ref _tearingDown);

    /// <summary>True when a flash's TryStart right now would get a slot (free, or one marked evictable).</summary>
    internal static bool CanStartFlash => TearingDown < MaxTearingDown && Budget.CanAcquireFor(reserved: true);

    private readonly ClipSlotBudget.Lease _lease;
    private readonly object _gate = new();
    private readonly Action<BitmapSource> _show;
    private readonly int _width, _height;
    private readonly MediaPlayer _player;
    private readonly Media _media;
    private IntPtr _buffer;
    private byte[]? _latest;
    private long _lastFrame;
    private bool _queued;
    private volatile bool _disposed;
    private int _disposeOnce;

    /// <summary>This picture is leaving (a flash fading out): another clip may take the slot.</summary>
    internal bool Evictable { get => _lease.Evictable; set => _lease.Evictable = value; }

    internal bool IsDisposed => _disposed;

    internal static FlashClipPlayer? Start(string? path, int width, int height, Action<BitmapSource> show, double speed = 1)
        => TryStart(path, width, height, show, speed, out _);

    /// <param name="refusedAtCap">True when the only reason for null was a full budget: the
    /// caller may try again once <see cref="CanStartFlash"/> says a slot is free.</param>
    /// <param name="flash">A flash: may use the slots bubbles and the cascade cannot take.</param>
    internal static FlashClipPlayer? TryStart(string? path, int width, int height, Action<BitmapSource> show, double speed, out bool refusedAtCap, bool flash = false)
    {
        refusedAtCap = false;
        if (path == null || VideoService.SharedLibVLC is not { } vlc) return null;
        // A deleted temp file would "play" (Play returns true) and show nothing while holding a slot.
        if (!System.IO.File.Exists(path)) { VideoDiag.Log("CLIP", "clip file missing, poster stays"); return null; }
        if (TearingDown >= MaxTearingDown)
        {
            refusedAtCap = true;
            LogRefused($"refused, {TearingDown} player(s) still tearing down");
            return null;
        }
        FlashClipPlayer? created = null;
        var lease = Budget.TryAcquire(() => created?.Dispose(), out var evicted, reserved: flash);
        if (lease == null)
        {
            refusedAtCap = true;
            LogRefused($"refused at cap ({(flash ? "flash" : "shared")}), live={Budget.Live}/{MaxPlayers} shared={Budget.LiveShared}/{MaxSharedPlayers}");
            return null;
        }
        try
        {
            created = new FlashClipPlayer(vlc, path, width, height, show, speed, lease);
            VideoDiag.Log("CLIP", $"acquire live={Budget.Live}/{MaxPlayers}{(evicted != null ? " (evicted a leaving one)" : "")}");
            return created;
        }
        catch (Exception ex)
        {
            lease.Dispose();
            App.Logger?.Debug("Flash clip failed: {Error}", ex.Message);
            VideoDiag.Log("CLIP", $"start failed, live={Budget.Live}/{MaxPlayers}: {ex.Message}");
            return null;
        }
    }

    /// <summary>At most one refusal line a second: a full cap is retried every heartbeat tick.</summary>
    private static void LogRefused(string message)
    {
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastRefusedLogMs) <= 1000) return;
        Interlocked.Exchange(ref _lastRefusedLogMs, now);
        VideoDiag.Log("CLIP", message);
    }

    private FlashClipPlayer(LibVLC vlc, string path, int width, int height, Action<BitmapSource> show, double speed, ClipSlotBudget.Lease lease)
    {
        _lease = lease;
        _show = show;
        double scale = Math.Min(1, 480.0 / Math.Max(1, Math.Max(width, height)));
        _width = Math.Max(2, (int)(width * scale));
        _height = Math.Max(2, (int)(height * scale));
        _buffer = Marshal.AllocHGlobal(_width * _height * 4);
        // Never set Mute here: LibVLC mutes the whole process's audio session (LibVlcSilence).
        _player = new MediaPlayer(vlc) { EnableHardwareDecoding = false };
        _media = new Media(vlc, path, FromType.FromPath);
        try
        {
            _media.AddOption(":no-audio");
            _media.AddOption(":input-repeat=65535");
            _player.SetVideoFormat("RV32", (uint)_width, (uint)_height, (uint)_width * 4);
            _player.SetVideoCallbacks(Lock, null, Display);
            if (!_player.Play(_media)) throw new InvalidOperationException("Clip playback refused");
            // Gentle visuals: clips play at the gentle speed too (Speed is 1 when off).
            _player.SetRate((float)Math.Clamp(speed * Safety.GentleVisuals.Speed, .25, 4));
        }
        catch
        {
            _player.Dispose(); _media.Dispose(); Marshal.FreeHGlobal(_buffer);
            throw;
        }
    }

    private IntPtr Lock(IntPtr opaque, IntPtr planes)
    {
        Marshal.WriteIntPtr(planes, _buffer);
        return IntPtr.Zero;
    }

    private void Display(IntPtr opaque, IntPtr picture)
    {
        if (_disposed || Environment.TickCount64 - _lastFrame < 66) return;
        _lastFrame = Environment.TickCount64;
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        lock (_gate)
        {
            if (_disposed) return;
            _latest ??= new byte[_width * _height * 4];
            Marshal.Copy(_buffer, _latest, 0, _latest.Length);
            if (_queued) return;
            _queued = true;
        }
        dispatcher.BeginInvoke(() =>
        {
            byte[]? pixels;
            lock (_gate) { pixels = _latest; _latest = null; _queued = false; }
            if (_disposed || pixels == null) return;
            var frame = BitmapSource.Create(_width, _height, 96, 96, PixelFormats.Bgr32, null, pixels, _width * 4);
            frame.Freeze();
            _show(frame);
        });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeOnce, 1) != 0) return;
        _disposed = true;
        // The slot goes back now, on every path: nothing reads this player's frames any more.
        // Only the native teardown below is deferred.
        if (_lease.Release())
            VideoDiag.Log("CLIP", $"release live={Budget.Live}/{MaxPlayers}");
        // Native callbacks retain their buffer until Stop and Dispose have completed.
        Interlocked.Increment(ref _tearingDown);
        _ = Task.Run(() =>
        {
            try
            {
                _player.Stop();
                _player.Dispose();
                _media.Dispose();
                Marshal.FreeHGlobal(_buffer);
            }
            catch (Exception ex)
            {
                // Keep the native buffer if shutdown cannot be confirmed: a callback may still write it.
                App.Logger?.Warning("Flash clip cleanup failed: {Error}", ex.Message);
            }
            finally
            {
                // A Stop that never returns stays counted, which is the point of the limit.
                Interlocked.Decrement(ref _tearingDown);
            }
        });
    }
}
