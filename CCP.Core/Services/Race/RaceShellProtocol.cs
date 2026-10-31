using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Services.Race;
/// <summary>UTF-8 JSON objects, one per line, no stdout diagnostics. Page messages are unwrapped.
/// Shell emits shell-loaded {displayBackend:"x11"|"wayland"} from the actual window backend after navigation, shell-focus {active}, shell-escape on non-repeat
/// keydown with a positive, increasing sequence per process (repeat deliveries keep the same sequence).
/// X11 owns Escape only for a confirmed x11 shell when listening; the shell is the fallback. Host sends focus or existing Glassroad frames. EOF in either direction ends the race.
/// The rt-race-shell lane must implement this contract; it is absent from the base checkout.</summary>
public static class RaceShellProtocol
{
    public const string Loaded = "shell-loaded", Focus = "shell-focus", Escape = "shell-escape";
    public static bool AcceptEscape(ref long last, long sequence, bool nativeListening, string? displayBackend)
    {
        if ((nativeListening && displayBackend == "x11") || sequence <= last) return false;
        last = sequence;
        return true;
    }
    public const int MaxLineLength = 64 * 1024;
    public static JObject Parse(string line)
    {
        if (line.Length > MaxLineLength) throw new System.FormatException("Race frame too large");
        return JObject.Parse(line);
    }
}
