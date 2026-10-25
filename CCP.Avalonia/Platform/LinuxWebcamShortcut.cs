using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Webcam chords use the compositor portal on Wayland and root grabs on X11.
/// Owns pending portal consent too: a setting change or shell close releases the session.</summary>
internal sealed class LinuxWebcamShortcut : IDisposable
{
    private readonly Func<Task> _unbind;
    private volatile bool _disposed, _bound;
    private readonly Func<bool> _alive;
    internal bool IsBound => _bound && !_disposed && _alive();
    internal string? Trigger { get; private set; }
    internal Task Ready { get; }

    internal LinuxWebcamShortcut(string trigger, string description, Action pressed,
        Func<string, string, Action, Task<string?>> bind, Func<Task> unbind, Func<bool>? alive = null)
    {
        _unbind = unbind;
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
            }
            catch (Exception ex) { Log.Warning(ex, "Webcam portal shortcut unavailable"); await unbind(); }
        }
    }

    internal static IDisposable? Register(string id, ChordMods mods, string key, string description, Action pressed)
    {
        if (!OperatingSystem.IsLinux()) return null;
        if (!UsesPortal(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"), Environment.GetEnvironmentVariable("XDG_SESSION_TYPE")))
            return X11SummonChord.Register(id, mods, key, pressed);
        var portal = new PortalShortcut(id);
        return new LinuxWebcamShortcut(PreferredTrigger(mods, key), description, pressed, portal.BindAsync, portal.UnbindAsync, () => portal.IsBound);
    }

    internal static bool UsesPortal(string? display, string? sessionType)
        => !string.IsNullOrEmpty(display) || string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase);

    // Portal preferred_trigger uses XKB modifier/key names, not Avalonia's display chord text.
    internal static string PreferredTrigger(ChordMods mods, string key)
        => ((mods & ChordMods.Ctrl) != 0 ? "CTRL+" : "") + ((mods & ChordMods.Alt) != 0 ? "ALT+" : "")
         + ((mods & ChordMods.Shift) != 0 ? "SHIFT+" : "") + ((mods & ChordMods.Win) != 0 ? "LOGO+" : "")
         + X11PanicKey.XKeyName(key);

    internal static bool Armed(IDisposable? registration)
        => registration is LinuxWebcamShortcut portal ? portal.IsBound : registration != null;
    internal static string Hint(IDisposable? registration, string fallback)
        => registration is LinuxWebcamShortcut portal ? portal.Trigger ?? "" : fallback;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _bound = false;
        _ = CloseAsync();
    }
    private async Task CloseAsync()
    {
        try { await _unbind(); }
        catch (Exception ex) { Log.Warning(ex, "Webcam portal shortcut release failed"); }
    }
}
