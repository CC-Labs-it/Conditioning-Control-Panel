using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Safety;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF Services/Media/FlashClipPlayer.cs:14-193. The lease is returned immediately;
// native Stop is kept off the UI thread and bounded separately from live players.
internal sealed class FlashClipPlayer : IDisposable
{
    internal const int MaxPlayers = 8, MaxSharedPlayers = 5, MaxTearingDown = 8;
    internal static readonly ClipSlotBudget Budget = new(MaxPlayers, MaxSharedPlayers);
    private static int _tearingDown;
    private static readonly SemaphoreSlim TeardownSlots = new(MaxTearingDown);
    internal static int TearingDown => Volatile.Read(ref _tearingDown);
    internal static bool CanStartFlash => TearingDown < MaxTearingDown && Budget.CanAcquireFor(true);
    internal static Func<string, int, double, Action<Bitmap>, IDisposable> Create =
        (path, width, speed, show) => new NativePlayer(path, width, speed, show);

    private readonly ClipSlotBudget.Lease _lease;
    private IDisposable? _native;
    private int _disposed;
    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    internal bool Evictable { get => _lease.Evictable; set => _lease.Evictable = value; }

    private FlashClipPlayer(ClipSlotBudget.Lease lease) => _lease = lease;

    internal static FlashClipPlayer? TryStart(string? path, int width, double speed, Action<Bitmap> show, Action evicted, out bool atCap)
    {
        atCap = false;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        if (TearingDown >= MaxTearingDown) { atCap = true; return null; }
        FlashClipPlayer? result = null;
        var lease = Budget.TryAcquire(() => { try { evicted(); } finally { result?.Dispose(); } }, out _, reserved: true);
        if (lease == null) { atCap = true; return null; }
        result = new FlashClipPlayer(lease);
        try
        {
            result._native = Create(path, width, speed, frame => { if (!result.IsDisposed) show(frame); });
            return result;
        }
        catch (Exception)
        {
            Interlocked.Exchange(ref result._disposed, 1);
            lease.Dispose();
            return null;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Interlocked.Increment(ref _tearingDown);
        _lease.Dispose();
        var native = _native;
        _ = Task.Run(async () =>
        {
            await TeardownSlots.WaitAsync();
            try { native?.Dispose(); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "Flash clip teardown failed"); }
            finally { TeardownSlots.Release(); Interlocked.Decrement(ref _tearingDown); }
        });
    }

    private sealed class NativePlayer : IDisposable
    {
        private readonly MediaPlayer _player;
        private readonly Media _media;
        private readonly VlcFrameSink _sink;
        internal NativePlayer(string path, int width, double speed, Action<Bitmap> show)
        {
            var vlc = LibVlcAudio.Shared ?? throw new InvalidOperationException("LibVLC unavailable");
            _player = new MediaPlayer(vlc) { EnableHardwareDecoding = false };
            _media = new Media(vlc, path, FromType.FromPath);
            _media.AddOption(ConditioningControlPanel.Services.LibVlcSilence.NoAudioOption);
            _media.AddOption(":input-repeat=65535");
            _sink = new VlcFrameSink(_player, () => _media, show,
                () => { if (_sink!.Bitmap is { } frame) show(frame); }, Math.Clamp(width, 2, 480));
            var rate = (float)Math.Clamp(speed * GentleVisuals.Speed, .25, 4);
            try
            {
                if (!_player.Play(_media)) throw new InvalidOperationException("Clip playback refused");
                _player.SetRate(rate);
            }
            catch { _player.Dispose(); _media.Dispose(); _sink.Free(); throw; }
        }
        public void Dispose()
        {
            // A failed Stop leaves the buffer alive: a native callback may still use it.
            _player.Stop();
            _player.Dispose();
            _media.Dispose();
            Dispatcher.UIThread.Post(_sink.Free);
        }
    }
}
