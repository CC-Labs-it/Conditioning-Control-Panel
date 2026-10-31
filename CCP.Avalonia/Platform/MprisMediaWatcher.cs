using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Tmds.DBus.Protocol;

namespace ConditioningControlPanel.Avalonia.Platform;

// Linux counterpart of WPF Services/Awareness/SmtcMediaWatcher.cs:34-114.
// Optional, memory only, and inert until Start. Observer/consent ownership is part B.
internal sealed class MprisMediaWatcher : IMediaWatcher
{
    private const string Player = "org.mpris.MediaPlayer2.Player";
    private const string Path = "/org/mpris/MediaPlayer2";
    private readonly object _gate = new();
    private readonly MediaSessionWatcher _watcher;
    private DBusConnection? _bus;
    private bool _disposed;

    internal MprisMediaWatcher()
    {
        _watcher = new MediaSessionWatcher(InitializeAsync, ReadAsync);
    }

    public MediaSample? Current => _watcher.Current;
    public bool IsAvailable => _watcher.IsAvailable;
    // Observer tick/candidate/wipe hooks, WPF AwarenessObserver.cs:743-747,835-838,944-970.
    internal MediaInfo? TrackMedia() => _watcher.TrackMedia();
    internal bool TakeMediaChange() => _watcher.TakeMediaChange();
    internal void ResetMediaState() => _watcher.ResetMediaState();
    public void Start() => _watcher.Start();
    public void Stop() => _watcher.Stop();
    internal Task StartCompletion => _watcher.StartCompletion;

    public void Dispose()
    {
        _watcher.Dispose();
        lock (_gate)
        {
            _disposed = true;
            _bus?.Dispose();
            _bus = null;
        }
    }

    private async Task<bool> InitializeAsync()
    {
        // Same sandbox boundary as OsNotifications: tests never inspect a user's desktop.
        if (!OperatingSystem.IsLinux() || SandboxNet.Active || string.IsNullOrEmpty(DBusAddress.Session)) return false;
        lock (_gate) { if (_disposed) return false; if (_bus != null) return true; }
        var bus = new DBusConnection(DBusAddress.Session);
        try
        {
            await bus.ConnectAsync().ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed) { bus.Dispose(); return false; }
                _bus = bus;
            }
            return true;
        }
        catch { bus.Dispose(); return false; }
    }

    private async Task<MediaSample?> ReadAsync()
    {
        DBusConnection? bus;
        lock (_gate) bus = _bus;
        if (bus == null) return null;
        var names = await bus.CallMethodAsync(ListNames(bus),
            (Message m, object? _) => m.GetBodyReader().ReadArrayOfString(), null).ConfigureAwait(false);
        return await ReadPlayersAsync(names, async name =>
            await bus.CallMethodAsync(GetAll(bus, name),
                (Message m, object? _) => m.GetBodyReader().ReadDictionaryOfStringToVariantValue(), null).ConfigureAwait(false)).ConfigureAwait(false);
    }

    internal static async Task<MediaSample?> ReadPlayersAsync(IEnumerable<string> names,
        Func<string, Task<Dictionary<string, VariantValue>>> read)
    {
        MediaSample? fallback = null;
        // MPRIS has no global current session. Prefer a playing player, then stable bus-name order.
        foreach (var name in names.Where(n => n.StartsWith("org.mpris.MediaPlayer2.", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
        {
            try
            {
                var props = await read(name).ConfigureAwait(false);
                var sample = FromProperties(props);
                if (sample?.IsPlaying == true) return sample;
                fallback ??= sample;
            }
            catch { } // a player can vanish between ListNames and GetAll
        }
        return fallback;
    }

    private static MessageBuffer ListNames(DBusConnection bus)
    {
        var w = bus.GetMessageWriter();
        w.WriteMethodCallHeader(destination: "org.freedesktop.DBus", path: "/org/freedesktop/DBus",
            @interface: "org.freedesktop.DBus", member: "ListNames");
        return w.CreateMessage();
    }

    private static MessageBuffer GetAll(DBusConnection bus, string name)
    {
        var w = bus.GetMessageWriter();
        w.WriteMethodCallHeader(destination: name, path: Path, @interface: "org.freedesktop.DBus.Properties",
            member: "GetAll", signature: "s");
        w.WriteString(Player);
        return w.CreateMessage();
    }

    internal static MediaSample? FromProperties(Dictionary<string, VariantValue> props)
    {
        try
        {
            if (!props.TryGetValue("Metadata", out var metadata)) return null;
            metadata = Unwrap(metadata);
            var values = new Dictionary<string, VariantValue>();
            for (int i = 0; i < metadata.Count; i++)
            {
                var entry = metadata.GetDictionaryEntry(i);
                values[entry.Key.GetString()] = Unwrap(entry.Value);
            }
            string? state = null;
            long position = 0;
            try { if (props.TryGetValue("PlaybackStatus", out var v)) state = Unwrap(v).GetString(); } catch { }
            try { if (props.TryGetValue("Position", out var v)) position = Unwrap(v).GetInt64(); } catch { }
            return FromMetadata(values, state, position);
        }
        catch { return null; } // malformed metadata is an absent optional signal, never a user error
    }

    // WPF SmtcMediaWatcher.cs:96-114: blank title drops the session, absent state/position defaults.
    internal static MediaSample? FromMetadata(Dictionary<string, VariantValue> metadata, string? state, long microseconds)
    {
        try
        {
            var title = metadata.TryGetValue("xesam:title", out var t) ? Unwrap(t).GetString() : null;
            string? artist = null;
            if (metadata.TryGetValue("xesam:artist", out var a))
            {
                a = Unwrap(a);
                if (a.Count > 0) artist = a.GetItem(0).GetString();
            }
            // MPRIS position is in microseconds; clamp untrusted values before converting to ticks.
            var ticks = Math.Clamp(microseconds, 0, TimeSpan.MaxValue.Ticks / 10) * 10;
            return MediaSample.FromSession(title, artist, state, TimeSpan.FromTicks(ticks));
        }
        catch { return null; }
    }

    private static VariantValue Unwrap(VariantValue v) => v.Type == VariantValueType.Variant ? v.GetVariantValue() : v;
}
