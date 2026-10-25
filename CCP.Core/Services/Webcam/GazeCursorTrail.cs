using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services;

/// <summary>Bounded, frame-free WPF gaze cursor history, in monitor-local DIPs.</summary>
public sealed class GazeCursorTrail
{
    public const float LifeSeconds = .55f;
    public readonly record struct Node(float X, float Y, float Age);
    private readonly List<Node> _nodes = new(64);
    public int Count => _nodes.Count;
    public Node this[int index] => _nodes[index];
    public void Clear() => _nodes.Clear();
    public bool Tick(double elapsedSeconds, (double X, double Y)? position)
    {
        float dt = (float)Math.Clamp(elapsedSeconds, 0, .1);
        bool changed = dt > 0 && _nodes.Count > 0;
        for (int i = _nodes.Count - 1; i >= 0; i--)
        {
            var n = _nodes[i] with { Age = _nodes[i].Age + dt };
            if (n.Age >= LifeSeconds) _nodes.RemoveAt(i); else _nodes[i] = n;
        }
        if (position is not { } p) return changed;
        if (_nodes.Count > 0)
        {
            var last = _nodes[^1];
            double dx = p.X - last.X, dy = p.Y - last.Y;
            if (dx * dx + dy * dy < 9) return changed;
        }
        if (_nodes.Count >= 64) _nodes.RemoveAt(0);
        _nodes.Add(new Node((float)p.X, (float)p.Y, 0));
        return true;
    }
}
