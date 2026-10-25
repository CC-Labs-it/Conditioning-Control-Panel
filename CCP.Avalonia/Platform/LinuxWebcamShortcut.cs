using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Webcam chords use the compositor portal on Wayland and root grabs on X11.
/// Owns pending portal consent too: a setting change or shell close releases the session.
/// The portal is only asked once the caller allows it (decisions.md 2026-10-25: webcam consent,
/// plus a saved calibration for quick recal); otherwise, and when it is refused, the X11 grab stands in.</summary>
internal sealed class LinuxWebcamShortcut : IDisposable
{
    private readonly Func<Task> _unbind;
    private volatile bool _disposed, _bound;
    private readonly Func<bool> _alive;
    private readonly Func<IDisposable?>? _fallback;
    private IDisposable? _fallbackGrab;
    internal bool IsBound => !_disposed && ((_bound && _alive()) || _fallbackGrab != null);
    internal string? Trigger { get; private set; }
    internal bool FellBack => _fallbackGrab != null;
    internal Task Ready { get; }

    internal LinuxWebcamShortcut(string trigger, string description, Action pressed,
        Func<string, string, Action, Task<string?>> bind, Func<Task> unbind, Func<bool>? alive = null,
        Func<IDisposable?>? fallback = null)
    {
        _unbind = unbind;
        _fallback = fallback;
        _alive = alive ?? (() => true);
        Ready = BindAsync();
        async Task BindAsync()
        {
            try
            {
                var assigned = await bind(trigger, description, () => { if (IsBound && !_disposed) pressed(); });
                if (_disposed) { await unbind(); return; }
                Trigger = assigned;
                _bound = assigned != null;
                if (!_bound) FallBack();
            }
            catch (Exception ex) { Log.Warning(ex, "Webcam portal shortcut unavailable"); await unbind(); FallBack(); }
        }
    }

    // Refused/absent portal: keep the base's X11 grab (reach while an XWayland client has focus).
    private void FallBack()
    {
        lock (this)
        {
            if (_disposed || _fallback == null) return;
            try { _fallbackGrab = _fallback(); }
            catch (Exception ex) { Log.Warning(ex, "Webcam X11 fallback grab failed"); }
        }
    }

    internal static IDisposable? Register(string id, ChordMods mods, string key, string description, Action pressed, bool portalAllowed)
    {
        if (!OperatingSystem.IsLinux()) return null;
        if (!UsesPortal(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"), Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), portalAllowed))
            return X11SummonChord.Register(id, mods, key, pressed);
        var portal = new PortalShortcut(id);
        return new LinuxWebcamShortcut(PreferredTrigger(mods, key), description, pressed, portal.BindAsync, portal.UnbindAsync, () => portal.IsBound,
            () => X11SummonChord.Register(id, mods, key, pressed));
    }

    internal static bool UsesPortal(string? display, string? sessionType, bool allowed = true)
        => allowed && (!string.IsNullOrEmpty(display) || string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase));

    // Portal preferred_trigger uses XKB modifier/key names, not Avalonia's display chord text.
    internal static string PreferredTrigger(ChordMods mods, string key)
        => ((mods & ChordMods.Ctrl) != 0 ? "CTRL+" : "") + ((mods & ChordMods.Alt) != 0 ? "ALT+" : "")
         + ((mods & ChordMods.Shift) != 0 ? "SHIFT+" : "") + ((mods & ChordMods.Win) != 0 ? "LOGO+" : "")
         + X11PanicKey.XKeyName(key);

    internal static bool Armed(IDisposable? registration)
        => registration is LinuxWebcamShortcut portal ? portal.IsBound : registration != null;
    internal static string Hint(IDisposable? registration, string fallback)
        => registration is LinuxWebcamShortcut portal ? (portal.FellBack ? fallback : portal.Trigger ?? "") : fallback;

    public void Dispose()
    {
        IDisposable? grab;
        lock (this)
        {
            if (_disposed) return;
            _disposed = true;
            _bound = false;
            grab = _fallbackGrab;
            _fallbackGrab = null;
        }
        grab?.Dispose();
        _ = CloseAsync();
    }
    private async Task CloseAsync()
    {
        try { await _unbind(); }
        catch (Exception ex) { Log.Warning(ex, "Webcam portal shortcut release failed"); }
    }
}
