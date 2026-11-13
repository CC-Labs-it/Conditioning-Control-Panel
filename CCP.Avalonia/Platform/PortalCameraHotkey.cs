using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Input;
using Serilog;
using Tmds.DBus.Protocol;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>One owned GlobalShortcuts connection for the camera chord.</summary>
internal sealed class PortalCameraHotkey : IDisposable
{
    private const string Iface = "org.freedesktop.portal.GlobalShortcuts";
    private const string Desktop = "/org/freedesktop/portal/desktop";
    private const string Id = "camera";
    private readonly object _gate = new();
    private readonly Action _pressed;
    private readonly PanicKeyHold _hold = new();
    private DBusConnection? _bus;
    private IDisposable? _down, _up, _closed;
    private string? _session;
    private bool _disposed, _bound;
    internal string Trigger { get; private set; } = "";
    internal bool IsBound { get { lock (_gate) return _bound && !_disposed; } }

    internal PortalCameraHotkey(Action pressed) => _pressed = pressed;
    internal static Func<DBusConnection> OpenConnection = () => new DBusConnection(DBusAddress.Session!);
    internal void SetSessionForTests(string session)
    {
        lock (_gate) { _session = session; _bound = true; }
    }

    internal static string PreferredTrigger(ChordMods mods, string key)
    {
        var name = X11PanicKey.XKeyName(key);
        if (name.Length == 0) return "";
        return ((mods & ChordMods.Ctrl) != 0 ? "CTRL+" : "")
             + ((mods & ChordMods.Alt) != 0 ? "ALT+" : "")
             + ((mods & ChordMods.Shift) != 0 ? "SHIFT+" : "")
             + ((mods & ChordMods.Win) != 0 ? "LOGO+" : "") + name;
    }

    internal async Task BindAsync(ChordMods mods, string key, string description)
    {
        // This also initializes SandboxNet from the user-data override before opening a bus.
        if (!OperatingSystem.IsLinux() || SandboxMarker.Active) return;
        try
        {
            var bus = OpenConnection();
            lock (_gate) { if (_disposed) { bus.Dispose(); return; } _bus = bus; }
            await bus.ConnectAsync();
            await PortalPanicShortcut.RegisterAsync(bus);
            var (code, result) = await PortalPanicShortcut.RequestAsync(bus, "CreateSession", "a{sv}",
                (ref MessageWriter w, string token) => w.WriteDictionary(new Dictionary<string, VariantValue>
                {
                    ["handle_token"] = token, ["session_handle_token"] = "ccp_camera_" + Guid.NewGuid().ToString("N")
                }));
            if (code != 0 || !result.TryGetValue("session_handle", out var handle)) { Dispose(); return; }
            var session = handle.Type == VariantValueType.ObjectPath ? handle.GetObjectPathAsString() : handle.GetString();
            lock (_gate) { if (_disposed) return; _session = session; }

            var down = await bus.AddMatchAsync(new MatchRule { Type = MessageType.Signal, Interface = Iface, Member = "Activated", Path = Desktop },
                (Message m, object? _) => { var r = m.GetBodyReader(); return (r.ReadObjectPathAsString(), r.ReadString()); },
                (Exception? ex, (string Session, string Id) a, object? _, object? _) =>
                { if (ex != null) Dispose(); else Route(a.Session, a.Id, true); }, null, null, false, ObserverFlags.None);
            lock (_gate) { if (_disposed) { down.Dispose(); return; } _down = down; }
            var up = await bus.AddMatchAsync(new MatchRule { Type = MessageType.Signal, Interface = Iface, Member = "Deactivated", Path = Desktop },
                (Message m, object? _) => { var r = m.GetBodyReader(); return (r.ReadObjectPathAsString(), r.ReadString()); },
                (Exception? ex, (string Session, string Id) a, object? _, object? _) =>
                { if (ex != null) Dispose(); else Route(a.Session, a.Id, false); }, null, null, false, ObserverFlags.None);
            lock (_gate) { if (_disposed) { up.Dispose(); return; } _up = up; }
            var closed = await bus.AddMatchAsync(new MatchRule { Type = MessageType.Signal, Interface = "org.freedesktop.portal.Session", Member = "Closed", Path = session },
                (Message m, object? _) => true,
                (Exception? ex, bool _, object? __, object? ___) => Dispose(), null, null, false, ObserverFlags.None);
            lock (_gate) { if (_disposed) { closed.Dispose(); return; } _closed = closed; }
            (code, result) = await PortalPanicShortcut.RequestAsync(bus, "BindShortcuts", "oa(sa{sv})sa{sv}", (ref MessageWriter w, string token) =>
            {
                w.WriteObjectPath(session);
                var shortcuts = w.WriteArrayStart(DBusType.Struct);
                w.WriteStructureStart(); w.WriteString(Id);
                w.WriteDictionary(new Dictionary<string, VariantValue> { ["description"] = description, ["preferred_trigger"] = PreferredTrigger(mods, key) });
                w.WriteArrayEnd(shortcuts); w.WriteString("");
                w.WriteDictionary(new Dictionary<string, VariantValue> { ["handle_token"] = token });
            });
            if (code != 0) { Dispose(); return; }
            lock (_gate)
            {
                if (_disposed) return;
                Trigger = PortalPanicShortcut.TriggerOf(result, Id);
                _bound = true;
            }
        }
        catch (Exception ex) { Log.Warning(ex, "Camera shortcut portal unavailable"); Dispose(); }
    }

    internal void Route(string session, string id, bool down, DateTime? now = null)
    {
        lock (_gate)
        {
            if (_disposed || !_bound || session != _session || id != Id) return;
            if (!down) { _hold.Up(); return; }
            if (!_hold.Down(now ?? DateTime.UtcNow)) _pressed();
        }
    }

    public void Dispose()
    {
        DBusConnection? bus;
        IDisposable? down, up, closed;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _bound = false; _hold.Up();
            bus = _bus; _bus = null;
            down = _down; up = _up; closed = _closed;
            _down = _up = _closed = null;
        }
        down?.Dispose(); up?.Dispose(); closed?.Dispose();
        // Closing the owned connection also closes its portal session, including pending requests.
        bus?.Dispose();
    }
}
