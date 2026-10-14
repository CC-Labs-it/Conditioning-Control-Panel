using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>What a completed pat on her body means once the poke ladder has looked at it.</summary>
public enum EmiPokeStep
{
    /// <summary>An ordinary pat: whatever the pet path was going to do anyway.</summary>
    Pat,

    /// <summary>The fourth poke inside the window: the same flick, wearing <c>-_-</c>.</summary>
    Annoyed,

    /// <summary>The fifth: the rage beat, wordless, and then a truce.</summary>
    Rage,

    /// <summary>The third: the pet streak. A chime and glee, the campus's three-pets beat.</summary>
    Glee
}

public sealed class EmiPokeLadder
{
    private int _count;
    private DateTime _lastAt = DateTime.MinValue;
    private DateTime _truceUntil = DateTime.MinValue;

    /// <summary>How many pokes are in the current run.</summary>
    public int Count => _count;

    /// <summary>True while the post-rage truce is on: the ladder refuses to climb.</summary>
    public bool InTruce(DateTime nowUtc) => nowUtc < _truceUntil;

    /// <summary>Book one poke and say what it earned.</summary>
    public EmiPokeStep Note(DateTime nowUtc)
    {
        if (nowUtc < _truceUntil)
        {
            // A truce is a truce: pats still land, they simply stop climbing.
            _count = 0;
            _lastAt = nowUtc;
            return EmiPokeStep.Pat;
        }

        if (_lastAt == DateTime.MinValue || (nowUtc - _lastAt).TotalMilliseconds > EmiPatFeedback.PokeWindowMs) _count = 0;
        _lastAt = nowUtc;
        _count++;

        if (_count >= EmiPatFeedback.PokeRageAt)
        {
            _count = 0;
            _truceUntil = nowUtc.AddMilliseconds(EmiPatFeedback.PokeTruceMs);
            return EmiPokeStep.Rage;
        }
        return _count == EmiPatFeedback.PokeGleeAt ? EmiPokeStep.Glee
             : _count == EmiPatFeedback.PokeAnnoyAt ? EmiPokeStep.Annoyed
             : EmiPokeStep.Pat;
    }

    /// <summary>Forget the run (she left, or she was dismissed). The truce is NOT forgotten.</summary>
    public void Reset()
    {
        _count = 0;
        _lastAt = DateTime.MinValue;
    }
}


public readonly record struct EmiPatFrame(string Text, int Ms);

/// <summary>WPF React/Alive/chains.js authored pat faces and timing, shared by both heads.</summary>
public static class EmiPatFeedback
{
    public const int PokeWindowMs = 4000, PokeGleeAt = 3, PokeAnnoyAt = 4, PokeRageAt = 5;
    public const int PokeTruceMs = 60000, PokeRageHoldMs = 1500;
    public const string PokeRageFace = ">:(", PokeAnnoyFace = "-_-";
    public static IReadOnlyList<EmiPatFrame> Pet { get; } = Array.AsReadOnly(new[] { new EmiPatFrame("^_^", 900) });
    public static IReadOnlyList<EmiPatFrame> Wink { get; } = Array.AsReadOnly(new[] { new EmiPatFrame("^_^", 420), new EmiPatFrame("^_~", 260), new EmiPatFrame("^_^", 600) });
    public static IReadOnlyList<EmiPatFrame> Flick { get; } = Array.AsReadOnly(new[] { new EmiPatFrame("^_~", 320), new EmiPatFrame("0_0", 180) });
    public static IReadOnlyList<EmiPatFrame> Annoyed { get; } = Array.AsReadOnly(new[] { new EmiPatFrame(PokeAnnoyFace, 700), new EmiPatFrame("0_0", 180) });
    public static IReadOnlyList<EmiPatFrame> Rage { get; } = Array.AsReadOnly(new[] { new EmiPatFrame(">.<", 200), new EmiPatFrame(">_<", 200), new EmiPatFrame(">.<", 200), new EmiPatFrame(PokeRageFace, PokeRageHoldMs) });
    public static IReadOnlyList<EmiPatFrame> Glee { get; } = Array.AsReadOnly(new[] { new EmiPatFrame("^_^", 300), new EmiPatFrame("(≧◡≦)", 1400) });
    public static string? FaceAt(IReadOnlyList<EmiPatFrame> frames, double elapsedMs)
    {
        double until = 0;
        foreach (var frame in frames)
        {
            until += frame.Ms;
            if (elapsedMs < until) return frame.Text;
        }
        return null;
    }
}
