using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Services.Race;
/// <summary>UTF-8 JSON objects, one per line, no stdout diagnostics. Page messages are unwrapped.
/// The shell runs under XWayland (--ozone-platform=x11) so every configured panic key reaches X11.
/// A non-x11 backend is refused unless the configured panic key is Escape.
/// Shell emits shell-loaded {displayBackend:"x11"|"wayland"} from the actual window backend after navigation, shell-focus {active}, shell-escape on non-repeat
/// keydown with a positive, increasing sequence per process (repeat deliveries keep the same sequence) and the
/// displayBackend of the window that took the key, shell-escape-up on its keyup (the leash hold-to-cut).
/// One panic source per race session, never both (LinuxRaceHost.X11Source): the X11 listener when it listens and the shell runs under X11, else the shell. Host sends launch {url} as the first stdin frame, then focus or existing Glassroad frames. EOF in either direction ends the race.
/// The rt-race-shell lane must implement this contract; it is absent from the base checkout.</summary>
public static class RaceShellProtocol
{
    public const string Loaded = "shell-loaded", Focus = "shell-focus", Escape = "shell-escape", EscapeUp = "shell-escape-up";
    /// <summary>A new press of the shell's Escape (repeat deliveries keep their sequence), dropped when X11 is the panic source.</summary>
    public static bool AcceptEscape(ref long last, long sequence, bool x11Source)
        { if (sequence <= last) return false; last = sequence; return !x11Source; }
    public const int MaxLineLength = 64 * 1024;
    public static JObject Parse(string line)
    {
        if (line.Length > MaxLineLength) throw new System.FormatException("Race frame too large");
        return JObject.Parse(line);
    }
}
