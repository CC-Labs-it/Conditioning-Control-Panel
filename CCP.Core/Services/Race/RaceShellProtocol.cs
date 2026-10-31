using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Services.Race;
/// <summary>UTF-8 JSON objects, one per line, no stdout diagnostics. Page messages are unwrapped.
/// Shell emits shell-loaded after navigation, shell-focus {active}, shell-escape on non-repeat
/// keydown. Host sends focus or existing Glassroad frames. EOF in either direction ends the race.
/// The rt-race-shell lane must implement this contract; it is absent from the base checkout.</summary>
public static class RaceShellProtocol
{
    public const string Loaded = "shell-loaded", Focus = "shell-focus", Escape = "shell-escape";
    public const int MaxLineLength = 64 * 1024;
    public static JObject Parse(string line)
    {
        if (line.Length > MaxLineLength) throw new System.FormatException("Race frame too large");
        return JObject.Parse(line);
    }
}
