namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// Afterglow: every subliminal that flashes leaves a ghost of its words, and the ghosts line up
/// along the bottom of the screen. When the line is full it lights up as one mantra, holds, sinks
/// and clears, and the fog writes it. Pure timing: the head places and draws the words.
/// </summary>
public sealed class AfterglowLine
{
    /// <summary>Ghosts in a full line.</summary>
    public const int Capacity = 4;
    /// <summary>The ghost rises where the word flashed, as the flash itself leaves.</summary>
    public const double RiseSeconds = 0.5;
    /// <summary>Then travels to its place in the line.</summary>
    public const double TravelSeconds = 0.75;
    /// <summary>How visible a resting ghost is.</summary>
    public const double GhostAlpha = 0.42;
    /// <summary>The finished line: brightens, holds, sinks. The rise is never under 300 ms (gentle visuals).</summary>
    public const double FinaleRise = 0.5, FinaleHold = 1.2, FinaleSink = 1.1;
    /// <summary>A line nobody adds to for this long fades away instead of waiting forever.</summary>
    public const double StaleSeconds = 180;
    /// <summary>Longest text one ghost keeps.</summary>
    public const int MaxChars = 28;

    private readonly List<(string Text, double Age)> _ghosts = new(Capacity);
    private double _finale = -1, _idle;

    public int Count => _ghosts.Count;
    public string TextAt(int i) => _ghosts[i].Text;
    public bool InFinale => _finale >= 0;
    /// <summary>True while anything is still moving (the head repaints only then).</summary>
    public bool Animating
    {
        get
        {
            if (InFinale) return true;
            foreach (var g in _ghosts) if (g.Age < RiseSeconds + TravelSeconds) return true;
            return _idle > StaleSeconds - 2 && _ghosts.Count > 0;
        }
    }

    /// <summary>A subliminal was shown. False when the line cannot take it (full, in its finale, or empty text).</summary>
    public bool Add(string? text)
    {
        if (InFinale || _ghosts.Count >= Capacity) return false;
        text = (text ?? "").Trim();
        if (text.Length == 0) return false;
        if (text.Length > MaxChars) text = text[..MaxChars].TrimEnd();
        _ghosts.Add((text, 0));
        _idle = 0;
        return true;
    }

    /// <summary>One frame. Returns the finished line on the frame its finale begins, else null.</summary>
    public string? Step(double dt)
    {
        if (dt <= 0 || _ghosts.Count == 0) return null;
        for (int i = 0; i < _ghosts.Count; i++) _ghosts[i] = (_ghosts[i].Text, _ghosts[i].Age + dt);
        if (InFinale)
        {
            _finale += dt;
            if (_finale >= FinaleRise + FinaleHold + FinaleSink) Clear();
            return null;
        }
        _idle += dt;
        if (_idle >= StaleSeconds) { Clear(); return null; }
        if (_ghosts.Count == Capacity && _ghosts[^1].Age >= RiseSeconds + TravelSeconds)
        {
            _finale = 0;
            var parts = new string[_ghosts.Count];
            for (int i = 0; i < parts.Length; i++) parts[i] = _ghosts[i].Text;
            return string.Join(" ", parts);
        }
        return null;
    }

    /// <summary>0 where the word flashed, 1 in its place in the line.</summary>
    public double Travel(int i) => Whirlpool.Ease((_ghosts[i].Age - RiseSeconds) / TravelSeconds);

    /// <summary>How visible ghost <paramref name="i"/> is right now, 0..1.</summary>
    public double Alpha(int i)
    {
        double age = _ghosts[i].Age;
        if (InFinale) return (GhostAlpha + (1 - GhostAlpha) * FinaleGlowIn) * (1 - FinaleSinkOut);
        if (age < RiseSeconds) return 0.9 * age / RiseSeconds;
        double a = 0.9 + (GhostAlpha - 0.9) * Travel(i);
        if (_idle > StaleSeconds - 2) a *= Math.Clamp((StaleSeconds - _idle) / 2, 0, 1);
        return a;
    }

    /// <summary>0..1 as the finished line brightens.</summary>
    public double FinaleGlowIn => !InFinale ? 0 : Math.Clamp(_finale / FinaleRise, 0, 1);
    /// <summary>0..1 as the finished line sinks away.</summary>
    public double FinaleSinkOut => !InFinale ? 0 : Whirlpool.Ease((_finale - FinaleRise - FinaleHold) / FinaleSink);

    public void Clear()
    {
        _ghosts.Clear();
        _finale = -1;
        _idle = 0;
    }
}
