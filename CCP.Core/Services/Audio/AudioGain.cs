using System;

namespace ConditioningControlPanel.Services.Audio;

/// <summary>WPF f601b7917: feature levels sit beneath the master dial.</summary>
public static class AudioGain
{
    public static float MindWipe(int master, double own)
        => double.IsNaN(own) ? 0f : Math.Clamp(master, 0, 100) / 100f * (float)Math.Clamp(own, 0, 1);

    public static float Sound(int own, int master)
    {
        var level = Math.Clamp(own, 0, 100) / 100f * (Math.Clamp(master, 0, 100) / 100f);
        return level <= 0 ? 0f : Math.Max(0.05f, (float)Math.Pow(level, 1.5));
    }
}
