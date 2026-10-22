using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>One named desktop moment and its optional context, emitted by either head.</summary>
public sealed record EmiMoment(string Id, object? Context);

public sealed record EmiSayFrame(string Face, int Ms, string? Bubble);

/// <summary>WPF chains.js makeSay's locked typing cadence and linear reading hold, shared by both heads.</summary>
public static class EmiSayFrames
{
    public static IReadOnlyList<EmiSayFrame> Create(string? line, string reactionFace = "^_^", int holdMs = 1800)
        => Array.AsReadOnly(new[]
        {
            new EmiSayFrame("0_0", 420, "."),
            new EmiSayFrame("0_0", 420, ".."),
            new EmiSayFrame("0_0", 520, "..."),
            new EmiSayFrame(string.IsNullOrEmpty(reactionFace) ? "^_^" : reactionFace, Math.Max(400, holdMs), line ?? string.Empty),
            new EmiSayFrame("0_0", 200, null)
        });

    public static int HoldMs(string? line) => Math.Max(4050, 1890 + 61 * (line?.Length ?? 0));
}
