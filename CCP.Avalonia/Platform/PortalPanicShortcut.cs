using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using ConditioningControlPanel.Services.Leash;
using Tmds.DBus.Protocol;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// The panic key for NATIVE Wayland windows: org.freedesktop.portal.GlobalShortcuts (CreateSession,
/// BindShortcuts, Activated/Deactivated), raw over Tmds.DBus.Protocol like the tray probe. X11 focus is
/// <see cref="X11PanicKey"/>'s job; this covers the rest.
///
/// <para><b>Unlike the X11 listener this CONSUMES the key desktop-wide</b> - it is a compositor
/// grab, and the compositor (KDE: kglobalaccel) may show the user a confirmation dialog and lets
/// them rebind or refuse it. WHEN to bind is therefore the caller's policy
/// (docs/avalonia-decisions.html, panic key row); this class only binds and unbinds.</para>
///
/// <para><b>One shortcut id per key (ccp-bugs #1752).</b> GNOME and KDE remember the trigger a shortcut id
/// was first given and treat <c>preferred_trigger</c> as a hint for a NEW id only, so re-binding the one id
/// "panic" after a rebind kept the old Escape there. Each key therefore binds its own id
/// (<see cref="IdFor"/>), <see cref="Route"/> answers only the id of the open session, and the caller checks
/// the trigger the desktop reports with <see cref="MatchTrigger"/>.</para>
/// </summary>
internal static class PortalPanicShortcut
{
    internal const string Portal = "org.freedesktop.portal.Desktop", DesktopPath = "/org/freedesktop/portal/desktop";
    /// <summary>The id before #1752, kept as the test seam's default; real binds use <see cref="IdFor"/>.</summary>
    internal const string Iface = "org.freedesktop.portal.GlobalShortcuts", Id = "panic";

    /// <summary>The shortcut id for one stored key name: "panic-" and the name in lower case letters and digits.
    /// A new key is a new id, so the desktop takes its <c>preferred_trigger</c> instead of an old one.</summary>
    internal static string IdFor(string? key)
    {
        var b = new System.Text.StringBuilder("panic-");
        foreach (var c in key ?? "")
            if (c < 128 && char.IsLetterOrDigit(c)) b.Append(char.ToLowerInvariant(c));
        return b.Length == 6 ? "panic-none" : b.ToString();
    }

    // The id the open (or test) session listens for.
    private static volatile string _boundId = Id;
    internal static string BoundId => _boundId;

    private static string? _session;
    private static IDisposable? _activated, _deactivated;
    private static Action? _onReleased;
    private static volatile bool _held, _reportsRelease;
    private static DateTime _lastDown;
    internal static bool IsHeld => _held;
    /// <summary>This compositor has sent a Deactivated for our shortcut, so a hold can trust its release.</summary>
    internal static bool ReportsRelease => _reportsRelease;

    /// <summary>Test seam: a bound session without D-Bus.</summary>
    internal static void SetSessionForTests(string? session, Action? onReleased, string id = Id)
    {
        (_session, _onReleased, _held, _reportsRelease) = (session, onReleased, false, false);
        _boundId = id;
    }
    internal static Func<Action, Action, Task<string?>>? BindForTests;
    private static DBusConnection? _bus;

    internal static bool IsBound => !string.IsNullOrEmpty(_session);

    /// <summary>Opens a portal session and binds <paramref name="key"/> (a stored Key name) as the
    /// "panic" shortcut. <paramref name="onActivated"/> runs on a D-Bus thread. Returns the trigger
    /// the compositor actually assigned (the user may have changed it; "" if it did not say), or
    /// null when the portal is absent, the user refused, or anything failed - never throws.</summary>
    internal static async Task<string?> BindAsync(string key, string description, Action onActivated, Action onDeactivated)
    {
        if (BindForTests != null) return await BindForTests(onActivated, onDeactivated);
        if (!OperatingSystem.IsLinux() || _session != null) return null;
        var id = _boundId = IdFor(key);
        _onReleased = onDeactivated;
        _reportsRelease = false;
        _session = "";   // claimed: a second caller while this one waits gets null, not a second session
        try
        {
            // Own connection, not DBusConnection.Session: the shared one autoconnects, and UniqueName
            // (needed for the request path) throws on autoconnect connections. Dropping it also
            // makes the portal close the session, so no grab can outlive this connection.
            var bus = _bus = new DBusConnection(DBusAddress.Session!);
            await bus.ConnectAsync();
            await RegisterAsync(bus);

            var (code, results) = await RequestAsync(bus, "CreateSession", "a{sv}", (ref MessageWriter w, string token) =>
                w.WriteDictionary(new Dictionary<string, VariantValue>
                {
                    ["handle_token"] = token,
                    ["session_handle_token"] = "ccp_panic",
                }));
            if (code != 0 || !results.TryGetValue("session_handle", out var handle)) { await UnbindAsync(); return Fail($"CreateSession answered {code}"); }
            // The spec types session_handle as a string holding an object path; read either.
            var session = handle.Type == VariantValueType.ObjectPath ? handle.GetObjectPathAsString() : handle.GetString();
            _session = session;   // from here on every failure path closes it

            _activated = await bus.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Interface = Iface, Member = "Activated", Path = DesktopPath },
                (Message m, object? _) => { var r = m.GetBodyReader(); return (r.ReadObjectPathAsString(), r.ReadString()); },
                (Exception? ex, (string Session, string Id) a, object? _, object? _) =>
                {
                    if (ex == null) Route(a.Session, a.Id, true, onActivated, onDeactivated);
                    else Release();
                },
                null, null, false, ObserverFlags.None);

            _deactivated = await bus.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Interface = Iface, Member = "Deactivated", Path = DesktopPath },
                (Message m, object? _) => { var r = m.GetBodyReader(); return (r.ReadObjectPathAsString(), r.ReadString()); },
                (Exception? ex, (string Session, string Id) a, object? _, object? _) =>
                {
                    if (ex == null) Route(a.Session, a.Id, false, onActivated, onDeactivated);
                    else Release();
                },
                null, null, false, ObserverFlags.None);

            (code, results) = await RequestAsync(bus, "BindShortcuts", "oa(sa{sv})sa{sv}", (ref MessageWriter w, string token) =>
            {
                w.WriteObjectPath(session);
                var shortcuts = w.WriteArrayStart(DBusType.Struct);
                w.WriteStructureStart();
                w.WriteString(id);
                w.WriteDictionary(new Dictionary<string, VariantValue>
                {
                    ["description"] = description,
                    ["preferred_trigger"] = X11PanicKey.XKeyName(key),
                });
                w.WriteArrayEnd(shortcuts);
                w.WriteString("");   // parent_window: none
                w.WriteDictionary(new Dictionary<string, VariantValue> { ["handle_token"] = token });
            });
            if (code != 0) { await UnbindAsync(); return Fail($"BindShortcuts answered {code} (refused or cancelled)"); }
            var trigger = TriggerOf(results, id);
            Log.Information("Panic key: bound '{Key}' as '{Id}' through the GlobalShortcuts portal, trigger '{Trigger}'", key, id, trigger);
            return trigger;
        }
        catch (Exception ex)
        {
            await UnbindAsync();
            return Fail(ex is TimeoutException ? "no answer in 30 s (an unanswered dialog counts as refused)" : ex.Message);
        }

        static string? Fail(string why)
        {
            Log.Warning("Panic key: GlobalShortcuts portal unavailable ({Why}); native Wayland windows rely on the tray", why);
            return null;
        }
    }

    // Ignore other sessions/shortcuts. An Activated within LeashHoldToCut.RepeatGap of the last one while
    // held is a repeat (WPF MainWindow.Leash.cs:310-357); after the gap it is a new press even if a
    // Deactivated was lost, so the panic key never goes dead.
    internal static bool Route(string session, string id, bool pressed, Action down, Action up, DateTime? now = null)
    {
        // Only the open session's own id: an id bound for an earlier key is never a panic (#1752).
        if (string.IsNullOrEmpty(_session) || session != _session || id != _boundId) return false;
        var t = now ?? DateTime.UtcNow;
        if (pressed)
        {
            var repeat = _held && t - _lastDown <= LeashHoldToCut.RepeatGap && t >= _lastDown;
            _lastDown = t;
            if (repeat) return true;
            _held = true;
            down();
        }
        else
        {
            _reportsRelease = true;
            if (_held) { _held = false; up(); }
        }
        return true;
    }

    private static void Release()
    {
        var held = _held;
        _held = false;
        if (held) _onReleased?.Invoke();
    }

    /// <summary>`--portal-check`: CreateSession, expect Response 0 with a session_handle, then
    /// Session.Close - the round trip BindAsync depends on, without binding anything.</summary>
    internal static async Task<int> CheckAsync()
    {
        using var bus = new DBusConnection(DBusAddress.Session!);
        await bus.ConnectAsync();
        Console.WriteLine($"connected as {bus.UniqueName}");
        Console.WriteLine($"Registry.Register {(await RegisterAsync(bus) ? "ok" : "refused or absent")}");
        var (code, results) = await RequestAsync(bus, "CreateSession", "a{sv}", (ref MessageWriter w, string token) =>
            w.WriteDictionary(new Dictionary<string, VariantValue>
            {
                ["handle_token"] = token,
                ["session_handle_token"] = "ccp_check",
            }));
        var ok = code == 0 && results.TryGetValue("session_handle", out var handle);
        Console.WriteLine($"CreateSession response {code}, session_handle {(ok ? results["session_handle"].ToString() : "(none)")}");
        if (!ok) return 1;
        var h = results["session_handle"];
        var session = h.Type == VariantValueType.ObjectPath ? h.GetObjectPathAsString() : h.GetString();
        await bus.CallMethodAsync(CallHeader(bus, session, "org.freedesktop.portal.Session", "Close", null));
        Console.WriteLine("Session.Close ok");
        return 0;
    }

    /// <summary>org.freedesktop.host.portal.Registry.Register: tells the portal this (unsandboxed)
    /// connection is <see cref="Program.AppId"/>, so KDE files the panic shortcut under the app rather
    /// than the launching terminal. Must precede every other portal call on the connection. False when
    /// refused (Flatpak: the portal already knows the id) or absent (xdg-desktop-portal before 1.19).</summary>
    internal static async Task<bool> RegisterAsync(DBusConnection bus)
    {
        try
        {
            await bus.CallMethodAsync(CallHeader(bus, DesktopPath, "org.freedesktop.host.portal.Registry", "Register", "sa{sv}",
                (ref MessageWriter w) => { w.WriteString(Program.AppId); w.WriteDictionary(new Dictionary<string, VariantValue>()); }));
            return true;
        }
        catch (Exception ex)
        {
            Log.Information("Portal: Registry.Register not accepted ({Why}); shortcuts may be filed under the launcher", ex.Message);
            return false;
        }
    }

    /// <summary>trigger_description of our shortcut in a BindShortcuts result (a(sa{sv})).</summary>
    internal static string TriggerOf(Dictionary<string, VariantValue> results, string id = Id)
    {
        if (!results.TryGetValue("shortcuts", out var list)) return "";
        for (var i = 0; i < list.Count; i++)
        {
            var shortcut = list.GetItem(i);
            if (shortcut.GetItem(0).GetString() != id) continue;
            var props = shortcut.GetItem(1);
            for (var j = 0; j < props.Count; j++)
            {
                var e = props.GetDictionaryEntry(j);
                if (e.Key.GetString() != "trigger_description") continue;
                var v = e.Value.Type == VariantValueType.Variant ? e.Value.GetVariantValue() : e.Value;
                return v.GetString();
            }
        }
        return "";
    }

    internal enum TriggerMatch { Unknown, Chosen, Other }

    /// <summary>Whether the trigger_description the desktop reported is the chosen <paramref name="key"/>
    /// (#1752). <see cref="TriggerMatch.Other"/> only when it names a key we recognise that is not the chosen
    /// one, or the chosen key under a modifier (the panic key is one bare key); a label we cannot read
    /// ("", a glyph, another language) is <see cref="TriggerMatch.Unknown"/>, so a wording we do not know never
    /// costs the user a working grab. Several triggers ("Esc, Num Lock") are Other when any one is.</summary>
    internal static TriggerMatch MatchTrigger(string? key, string? trigger)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(trigger)) return TriggerMatch.Unknown;
        var chosen = new HashSet<string>(StringComparer.Ordinal) { Canon(key) };
        var x = X11PanicKey.XKeyName(key);
        if (x.Length > 0) chosen.Add(Canon(x));
        chosen.Remove("");
        var result = TriggerMatch.Unknown;
        foreach (var part in trigger.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var one = MatchOne(part, chosen);
            if (one == TriggerMatch.Other) return TriggerMatch.Other;
            if (one == TriggerMatch.Chosen) result = TriggerMatch.Chosen;
        }
        return result;
    }

    private static TriggerMatch MatchOne(string trigger, HashSet<string> chosen)
    {
        var bare = trigger;
        bool modified = false;
        // GTK accelerator syntax: "<Control>Escape".
        if (bare.StartsWith('<') && bare.LastIndexOf('>') is var gt and > 1 && gt < bare.Length - 1)
        {
            modified = true;
            bare = bare[(gt + 1)..];
        }
        // "Ctrl+Esc", "Shift+Num Lock": only when every part before the key is a modifier we know.
        var cut = bare.LastIndexOf('+', bare.Length - 2 < 0 ? 0 : bare.Length - 2);
        if (cut > 0)
        {
            var mods = bare[..cut].Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (mods.Length > 0 && Array.TrueForAll(mods, m => Modifiers.Contains(Norm(m))))
            {
                modified = true;
                bare = bare[(cut + 1)..];
            }
        }
        var c = Canon(bare);
        if (c.Length == 0) return TriggerMatch.Unknown;
        if (!modified && chosen.Contains(c)) return TriggerMatch.Chosen;
        return modified || Known(c) ? TriggerMatch.Other : TriggerMatch.Unknown;
    }

    private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal)
    {
        "ctrl", "control", "primary", "alt", "altgr", "shift", "super", "meta", "win", "logo", "hyper", "cmd",
        "mod1", "mod2", "mod3", "mod4", "mod5",
    };

    // Spellings of one key (stored Key names, X keysym names, KDE and GNOME labels) to one form.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["esc"] = "escape", ["return"] = "enter", ["back"] = "backspace", ["del"] = "delete", ["ins"] = "insert",
        ["prior"] = "pageup", ["pgup"] = "pageup", ["next"] = "pagedown", ["pgdn"] = "pagedown", ["pgdown"] = "pagedown",
        ["scroll"] = "scrolllock", ["capital"] = "capslock", ["snapshot"] = "print", ["printscreen"] = "print",
        ["prtsc"] = "print", ["prtscr"] = "print", ["sysrq"] = "print", ["apps"] = "menu", ["cancel"] = "break",
        ["spacebar"] = "space",
    };

    private static readonly HashSet<string> Named = new(StringComparer.Ordinal)
    {
        "escape", "enter", "backspace", "delete", "insert", "pageup", "pagedown", "scrolllock", "capslock", "numlock",
        "print", "menu", "break", "pause", "space", "tab", "home", "end", "up", "down", "left", "right",
    };

    private static string Norm(string s)
    {
        var b = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s.Trim())
            if (ch != ' ' && ch != '_' && ch != '-') b.Append(char.ToLowerInvariant(ch));
        return b.ToString();
    }

    private static string Canon(string s)
    {
        var n = Norm(s);
        if (Aliases.TryGetValue(n, out var a)) return a;
        if (n.Length == 2 && n[0] == 'd' && char.IsAsciiDigit(n[1])) return n[1..];          // D1 -> 1
        if (n.Length == 7 && n.StartsWith("numpad", StringComparison.Ordinal)) return "kp" + n[6];   // NumPad0 -> kp0
        return n;
    }

    private static bool Known(string c) =>
        Named.Contains(c)
        || (c.Length == 1 && char.IsAsciiLetterOrDigit(c[0]))
        || (c.Length == 3 && c.StartsWith("kp", StringComparison.Ordinal) && char.IsAsciiDigit(c[2]))
        || (c.Length is 2 or 3 && c[0] == 'f' && int.TryParse(c[1..], out var f) && f is >= 1 and <= 35)
        || (char.IsAsciiLetter(c[0]) && Enum.TryParse<global::Avalonia.Input.Key>(c, true, out _));

    /// <summary>Closes the portal session, which releases the compositor grab.</summary>
    internal static async Task UnbindAsync()
    {
        var session = _session;
        _session = null;
        if (session == "") session = null;
        _activated?.Dispose();
        _activated = null;
        _deactivated?.Dispose();
        _deactivated = null;
        Release();
        _onReleased = null;
        var bus = _bus;
        _bus = null;
        if (bus == null) return;
        try
        {
            if (session != null)
                await bus.CallMethodAsync(CallHeader(bus, session, "org.freedesktop.portal.Session", "Close", null));
        }
        catch (Exception ex) { Log.Warning(ex, "Panic key: closing the portal session failed"); }
        bus.Dispose();
    }

    /// <summary>One portal Request round trip: subscribe to the Response on the predicted request
    /// path BEFORE calling (the portal may answer before the call returns), then call and await.</summary>
    internal static async Task<(uint Code, Dictionary<string, VariantValue> Results)> RequestAsync(
        DBusConnection bus, string member, string signature, BodyWriter writeBody, CancellationToken cancellation = default,
        string iface = Iface, bool untilAnswered = false)
    {
        var token = "ccp" + Guid.NewGuid().ToString("N");
        var sender = bus.UniqueName!.TrimStart(':').Replace('.', '_');
        var request = $"{DesktopPath}/request/{sender}/{token}";
        var answer = new TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var sub = await bus.AddMatchAsync(
            new MatchRule { Type = MessageType.Signal, Interface = "org.freedesktop.portal.Request", Member = "Response",
                            Path = request },
            (Message m, object? _) => { var r = m.GetBodyReader(); return (r.ReadUInt32(), r.ReadDictionaryOfStringToVariantValue()); },
            (Exception? ex, (uint, Dictionary<string, VariantValue>) v, object? _, object? _) =>
            {
                if (ex != null) answer.TrySetException(ex); else answer.TrySetResult(v);
            },
            // An unbounded wait must still end when the connection goes away.
            null, null, false, untilAnswered ? ObserverFlags.EmitOnConnectionDispose : ObserverFlags.None);

        await bus.CallMethodAsync(CallHeader(bus, DesktopPath, iface, member, signature, (ref MessageWriter w) => writeBody(ref w, token)));
        if (untilAnswered)
            return await AwaitOrCloseAsync(answer.Task, cancellation,
                () => bus.CallMethodAsync(CallHeader(bus, request, "org.freedesktop.portal.Request", "Close", null)));
        // KDE may show the user a dialog for BindShortcuts; effects wait at most this long for it.
        return await answer.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellation);
    }

    /// <summary>Waits for a Response with no timeout of its own. On cancellation it calls Request.Close
    /// ("Closes the portal request to which this object refers and ends all related user interaction
    /// (dialogs, etc). A Response signal will not be emitted in this case." -
    /// https://github.com/flatpak/xdg-desktop-portal/blob/main/data/org.freedesktop.portal.Request.xml)
    /// and still returns a Response that was already in flight, so a result (e.g. a screenshot file) is never dropped.
    /// If Close fails it keeps waiting for the Response (or the connection's end).</summary>
    internal static async Task<T> AwaitOrCloseAsync<T>(Task<T> answer, CancellationToken cancellation, Func<Task> close)
    {
        try { return await answer.WaitAsync(cancellation).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            if (answer.IsCompletedSuccessfully) return answer.Result;
            try
            {
                await close().ConfigureAwait(false);
                await Task.WhenAny(answer, Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false); // a Response sent before Close
            }
            catch (Exception ex)
            {
                // The request may still complete: keep observing so its result (a screenshot file) is still cleaned up.
                Log.Debug("Portal: Request.Close failed ({Type})", ex.GetType().Name);
                try { await answer.ConfigureAwait(false); } catch { }
            }
            if (answer.IsCompletedSuccessfully) return answer.Result;
            throw;
        }
    }

    // MessageWriter is a STRUCT: handing it to an Action<MessageWriter> writes the body into a copy,
    // and the original then serialises a header whose body length/offset do not match what was
    // written. dbus-broker answers "invalid body" by disconnecting ("Connection closed by peer").
    // By-ref delegates keep one writer.
    internal delegate void BodyWriter(ref MessageWriter w, string token);
    internal delegate void BodyWriterNoToken(ref MessageWriter w);

    internal static MessageBuffer CallHeader(DBusConnection bus, string path, string iface, string member, string? signature,
        BodyWriterNoToken? body = null)
    {
        var w = bus.GetMessageWriter();
        w.WriteMethodCallHeader(destination: Portal, path: path, @interface: iface, member: member, signature: signature);
        body?.Invoke(ref w);
        return w.CreateMessage();
    }
}
