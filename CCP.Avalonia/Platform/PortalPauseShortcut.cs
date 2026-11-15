using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Input;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;
using Tmds.DBus.Protocol;
using static ConditioningControlPanel.Avalonia.Platform.PortalPanicShortcut;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF MainWindow.xaml.cs:975-989 on native Wayland. The portal owns this
/// connection and may refuse or reassign the requested chord.</summary>
internal sealed class PortalPauseShortcut : IDisposable
{
    private const string Id = "pause";
    private const string Path = "/org/freedesktop/portal/desktop";
    private readonly object _gate = new();
    private readonly Action _pressed, _closed;
    private readonly string _trigger;
    private readonly PanicKeyHold _hold = new();
    private DBusConnection? _bus;
    private IDisposable? _activated, _deactivated, _changed, _sessionClosed;
    private string? _session;
    private bool _ready, _disposed, _lost;
    internal static Func<DBusConnection> OpenConnection = () => new(DBusAddress.Session!);

    internal PortalPauseShortcut(string key, Action pressed, Action closed) => (_trigger, _pressed, _closed) = (X11PanicKey.XKeyName(key), pressed, closed);

    // A compositor may reassign a shortcut. Only the requested bare key is safe to route.
    internal bool IsSafeTrigger(string? trigger) => _trigger.Length != 0
        && string.Equals(trigger?.Trim(), _trigger, StringComparison.OrdinalIgnoreCase);

    internal static async Task<IDisposable?> BindAsync(string key, Action pressed, Action closed, CancellationToken cancellation)
    {
        if (SandboxMarker.Active || string.IsNullOrWhiteSpace(key) || X11PanicKey.XKeyName(key).Length == 0) return null;
        var binding = new PortalPauseShortcut(key, pressed, closed);
        try
        {
            var bus = binding._bus = OpenConnection();
            using var cancel = cancellation.Register(bus.Dispose);
            await bus.ConnectAsync().AsTask().WaitAsync(cancellation);
            await RegisterAsync(bus).WaitAsync(cancellation);
            var (code, results) = await RequestAsync(bus, "CreateSession", "a{sv}", (ref MessageWriter w, string token) =>
                w.WriteDictionary(new Dictionary<string, VariantValue>
                {
                    ["handle_token"] = token,
                    ["session_handle_token"] = "ccp_pause_" + Guid.NewGuid().ToString("N"),
                }), cancellation);
            if (code != 0 || !results.TryGetValue("session_handle", out var handle)) return null;
            var session = handle.Type == VariantValueType.ObjectPath ? handle.GetObjectPathAsString() : handle.GetString();
            binding._session = session;

            binding._activated = await bus.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Interface = Iface, Member = "Activated", Path = Path },
                (Message m, object? _) => { var r = m.GetBodyReader(); return (r.ReadObjectPathAsString(), r.ReadString()); },
                (Exception? ex, (string Session, string Id) a, object? _, object? _) =>
                { if (ex == null) binding.Route(a.Session, a.Id, true); else binding.Lost(); }, null, null, false, ObserverFlags.None);
            binding._deactivated = await bus.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Interface = Iface, Member = "Deactivated", Path = Path },
                (Message m, object? _) => { var r = m.GetBodyReader(); return (r.ReadObjectPathAsString(), r.ReadString()); },
                (Exception? ex, (string Session, string Id) a, object? _, object? _) =>
                { if (ex == null) binding.Route(a.Session, a.Id, false); else binding.Lost(); }, null, null, false, ObserverFlags.None);
            binding._sessionClosed = await bus.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Interface = "org.freedesktop.portal.Session", Member = "Closed", Path = session },
                (Message m, object? _) => true,
                (Exception? ex, bool _, object? a, object? b) => binding.Lost(), null, null, false, ObserverFlags.None);
            binding._changed = await bus.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Interface = Iface, Member = "ShortcutsChanged", Path = Path },
                (Message m, object? _) =>
                {
                    var r = m.GetBodyReader();
                    var changedSession = r.ReadObjectPathAsString();
                    var end = r.ReadArrayStart(DBusType.Struct);
                    string? trigger = null;
                    while (r.HasNext(end))
                    {
                        r.AlignStruct();
                        var id = r.ReadString();
                        var props = r.ReadDictionaryOfStringToVariantValue();
                        if (id == Id && props.TryGetValue("trigger_description", out var value))
                            trigger = (value.Type == VariantValueType.Variant ? value.GetVariantValue() : value).GetString();
                    }
                    return (changedSession, trigger);
                },
                (Exception? ex, (string Session, string? Trigger) a, object? _, object? _) =>
                { if (ex != null) binding.Lost(); else binding.ShortcutChanged(a.Session, a.Trigger); },
                null, null, false, ObserverFlags.None);

            (code, results) = await RequestAsync(bus, "BindShortcuts", "oa(sa{sv})sa{sv}", (ref MessageWriter w, string token) =>
            {
                w.WriteObjectPath(session);
                var shortcuts = w.WriteArrayStart(DBusType.Struct);
                w.WriteStructureStart();
                w.WriteString(Id);
                w.WriteDictionary(new Dictionary<string, VariantValue>
                {
                    ["description"] = Loc.Get("btn_devices_pause"),
                    ["preferred_trigger"] = binding._trigger,
                });
                w.WriteArrayEnd(shortcuts);
                w.WriteString("");
                w.WriteDictionary(new Dictionary<string, VariantValue> { ["handle_token"] = token });
            }, cancellation);
            if (code != 0 || !binding.IsSafeTrigger(TriggerOf(results, Id)))
            {
                Log.Warning("Pause key: Wayland shortcut refused or assigned an unverified chord");
                return null;
            }
            if (cancellation.IsCancellationRequested || !binding.Ready()) return null;
            return binding;
        }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested) Log.Warning("Pause key: Wayland shortcut unavailable ({Why})", ex.Message);
            return null;
        }
        finally
        {
            if (!binding.IsReady || cancellation.IsCancellationRequested) binding.Dispose();
        }
    }

    private bool IsReady { get { lock (_gate) return _ready && !_disposed && !_lost; } }
    internal bool Ready() { lock (_gate) { if (_disposed || _lost) return false; return _ready = true; } }

    internal void Route(string session, string id, bool down, DateTime? now = null)
    {
        lock (_gate)
        {
            if (_disposed || _lost || !_ready || session != _session || id != Id) return;
            if (!down) { _hold.Up(); return; }
            if (_hold.Down(now ?? DateTime.UtcNow)) return;
            _pressed();
        }
    }

    internal void ShortcutChanged(string session, string? trigger)
    {
        lock (_gate) { if (_disposed || _lost || session != _session || IsSafeTrigger(trigger)) return; _lost = true; }
        Dispose();
        _closed();
    }

    // Allows the same routing rules to be tested without a compositor or a bus.
    internal void SetSessionForTests(string session) => _session = session;

    internal void Lost()
    {
        lock (_gate) { if (_disposed || _lost) return; _lost = true; }
        Dispose();
        _closed();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _ready = false;
        }
        _activated?.Dispose(); _deactivated?.Dispose(); _changed?.Dispose(); _sessionClosed?.Dispose();
        // A session belongs to its D-Bus connection. Dropping it also closes a pending request.
        _bus?.Dispose();
    }
}
