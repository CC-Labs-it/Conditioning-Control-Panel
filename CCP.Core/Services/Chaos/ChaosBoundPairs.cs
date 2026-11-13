using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>Bound resolution clock, matching WPF BubbleService.cs:1800-1872.</summary>
public sealed class ChaosBoundPairs
{
    private sealed class Pair(AmbientBubble a, AmbientBubble b)
    {
        public AmbientBubble? A = a, B = b;
        public DateTime? First;
    }

    private readonly Dictionary<int, Pair> _pairs = new();

    public void Add(int key, AmbientBubble a, AmbientBubble b) => _pairs.Add(key, new Pair(a, b));

    /// <summary>Returns the enraged survivor, if a half detonated.</summary>
    public AmbientBubble? Resolve(int key, AmbientBubble half, bool defused, DateTime now)
    {
        if (!_pairs.TryGetValue(key, out var pair)) return null;
        if (ReferenceEquals(pair.A, half)) pair.A = null;
        else if (ReferenceEquals(pair.B, half)) pair.B = null;
        else return null;
        var survivor = pair.A ?? pair.B;
        if (survivor == null) { _pairs.Remove(key); return null; }
        if (defused) { pair.First ??= now; return null; }
        _pairs.Remove(key);
        survivor.Enrage();
        return survivor;
    }

    /// <summary>Returns survivors whose second-half window elapsed. Frozen ticks shift the clock by 32 ms.</summary>
    public List<AmbientBubble> Step(DateTime now, bool frozen)
    {
        var enraged = new List<AmbientBubble>();
        var expired = new List<int>();
        foreach (var (key, pair) in _pairs)
        {
            if (pair.First is not { } first) continue;
            if (frozen) pair.First = first.AddMilliseconds(32);
            else if ((now - first).TotalMilliseconds > ChaosTuning.BOUND_WINDOW_MS)
            {
                var survivor = pair.A ?? pair.B;
                if (survivor != null) { survivor.Enrage(); enraged.Add(survivor); }
                expired.Add(key);
            }
        }
        foreach (var key in expired) _pairs.Remove(key);
        return enraged;
    }

    public void Clear() => _pairs.Clear();
}
