using ConditioningControlPanel.Looks;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Services.Looks;

/// <summary>
/// The word style worn right now, for the surfaces that show words (subliminal cards, the Steam
/// fog). Read through <see cref="WornLooks"/> only, once, and again after <see cref="WornLooks.Changed"/>
/// names the word slot (any thread), so a frame never calls the look source.
/// </summary>
public static class WornWordStyle
{
    private static volatile int _style = -1;

    /// <summary>Reads the worn slot. Tests swap it (the shared WornLooks.Source stays untouched).</summary>
    internal static Func<string, string?> Read = WornLooks.Get;

    static WornWordStyle()
    {
        WornLooks.Changed += slot => { if (slot == WornLooks.Word) _style = -1; };
    }

    /// <summary>The worn style; Classic outside the shop preview.</summary>
    public static WordStyle Current
    {
        get
        {
            int s = _style;
            if (s < 0) _style = s = (int)WordStyles.Of(Read(WornLooks.Word));
            return (WordStyle)s;
        }
    }

    /// <summary>How much a styled word may move: Still under Gentle visuals or motion Off (the word
    /// keeps its face and only fades), half under Reduced.</summary>
    public static WordMotion Motion
    {
        get
        {
            if (Safety.GentleVisuals.On) return WordMotion.Still;
            return MotionOf(ConditioningControlPanel.Services.Super.SuperStepAside.LevelProvider());
        }
    }

    public static WordMotion MotionOf(Models.MotionLevel level) => level switch
    {
        Models.MotionLevel.Off => WordMotion.Still,
        Models.MotionLevel.Reduced => WordMotion.Reduced,
        _ => WordMotion.Full,
    };
}
