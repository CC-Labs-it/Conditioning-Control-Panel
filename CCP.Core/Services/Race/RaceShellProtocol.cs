using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Services.Race;
/// <summary>UTF-8 JSON objects, one per line, no stdout diagnostics. Page messages are unwrapped.
/// The shell runs under XWayland (--ozone-platform=x11) so every configured panic key reaches X11.
/// A non-x11 backend is refused unless the configured panic key is Escape.
/// Shell emits shell-loaded {displayBackend:"x11"|"wayland"} from the actual window backend after navigation, shell-focus {active}, shell-escape on non-repeat
/// keydown with a positive, increasing sequence per process (repeat deliveries keep the same sequence) and the
/// displayBackend of the window that took the key, shell-escape-up on its keyup (the leash hold-to-cut).
/// A press of unknown backend while X11 listens counts once across both sources (<see cref="Claim"/>). Host sends launch {url} as the first stdin frame, then focus or existing Glassroad frames. EOF in either direction ends the race.
/// The rt-race-shell lane must implement this contract; it is absent from the base checkout.</summary>
public static class RaceShellProtocol
{
    public const string Loaded = "shell-loaded", Focus = "shell-focus", Escape = "shell-escape", EscapeUp = "shell-escape-up";
    public static bool AcceptEscape(ref long last, long sequence, bool nativeListening, string? displayBackend, long nowMs = 0)
    {
        if (sequence <= last) return false; last = sequence;
        return !nativeListening || (displayBackend == null ? Claim(true, nowMs) : displayBackend != "x11");
    }
    private static readonly object Gate = new();
    private static (long At, bool Shell) _claim = (long.MinValue / 2, false);
    /// <summary>One physical press seen by the X11 listener and by a shell of unknown backend: the first source wins for 500 ms.</summary>
    public static bool Claim(bool shell, long nowMs)
        { lock (Gate) { if (shell != _claim.Shell && nowMs - _claim.At is >= 0 and < 500) return false; _claim = (nowMs, shell); return true; } }
    public const int MaxLineLength = 64 * 1024;
    public static JObject Parse(string line)
    {
        if (line.Length > MaxLineLength) throw new System.FormatException("Race frame too large");
        return JObject.Parse(line);
    }
}
