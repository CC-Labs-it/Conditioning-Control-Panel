using System;
namespace ConditioningControlPanel.Services.Autonomy;

/// <summary>Takeover comments are unprompted and must not repeat the last line.</summary>
public static class TakeoverComment
{
    public const string Prompt = "Tease the user in one short line, out of nowhere. They are busy in the app and did nothing to ask for it. Be playful.";
    public static bool IsRepeat(string? last, string? next) =>
        !string.IsNullOrWhiteSpace(last) && !string.IsNullOrWhiteSpace(next)
        && string.Equals(last.Trim(), next.Trim(), StringComparison.OrdinalIgnoreCase);
}
