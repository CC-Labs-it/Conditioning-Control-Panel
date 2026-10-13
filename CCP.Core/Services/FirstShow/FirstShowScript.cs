using System;

namespace ConditioningControlPanel.Services.FirstShow;

// The show's fixed script, lifted out of the WPF stage window (FirstShowDesktopWindow.cs Beats / Cues,
// FirstShowDesktopWindow.Guide.cs WelcomeSteps, FirstShowAudio.cs Cue) so both heads read one table.
internal static class FirstShowScript
{
    /// <summary>Seconds into the show at which each line lands (WPF Beats).</summary>
    internal static readonly double[] Beats = { 0, 2, 7, 11, 15, 19, 24, 28, 32 };

    /// <summary>The cue played with each beat (WPF Cues).</summary>
    internal static readonly string[] Cues = { "entrance", "flash", "pink", "drain", "words", "bubbles", "mix", "gather", "reveal" };

    /// <summary>The show ends and EMI asks for a verdict.</summary>
    internal const double Length = 35;

    /// <summary>Pictures stop spawning and everything gathers.</summary>
    internal const double GatherAt = 28;

    /// <summary>The logo reveal.</summary>
    internal const double RevealAt = 32;

    // The in-between timings WPF wrote as literals (FirstShowDesktopWindow.cs Frame, FirstShowEffects.cs
    // Tick/Spawn); named here so the Avalonia stage reads them instead of restating them.
    internal static double FlashAt => Beats[1]; internal static double PinkAt => Beats[2]; internal static double DrainAt => Beats[3];
    internal static double WordsAt => Beats[4]; internal static double BubblesAt => Beats[5]; internal const double DrainDeepAt = 13;
    internal const double FastFlashUntil = 4.5, FastFlashEvery = .39, FlashEvery = .71, BubbleEvery = .71;
    internal const double SubliminalEvery = 3.1, SubliminalUntil = 27;
    internal const double StepAsideAt = 31.6, StepAsideSeconds = .7, RevealSeconds = .85;
    /// <summary>EMI greets once she has arrived and the stage has been up this long.</summary>
    internal const double GreetAfter = .9;

    /// <summary>The text key of a beat's line (beat 8 is the ta-da).</summary>
    internal static string LineKey(int beat) => beat == 8 ? "first_show_tada" : "first_show_beat" + beat;

    /// <summary>WPF FirstShowAudio.Cue: a recorded sound under Resources/sounds per cue name.</summary>
    internal static string CueAsset(string name) => name switch
    {
        "gather" => "chaos/sink.mp3",
        "reveal" => "chaos/reveal_chime.mp3",
        "pop" => "bubbles/Pop3.mp3",
        "pink" => "chaos/ripple_cast.mp3",
        "drain" => "chaos/time_slow_in.mp3",
        "flash" => "chaos/chip_pop.mp3",
        _ => "chaos/ui_equip.mp3"
    };

    /// <summary>WPF: cues play at 32% of master.</summary>
    internal const double CueScale = .32;

    /// <summary>A guide step names a real UI target. Hover steps wait for the pointer.</summary>
    internal sealed record GuideStep(string Line, string Tab, string? Target, bool Hover = false);

    internal static readonly GuideStep[] WelcomeSteps =
    {
        new("guideHelp", "settings", "feature-help", true),
        new("guideMedia", "assets", "RemoteSourceChips"),
        new("guideFolder", "assets", "BtnOpenAssetsFolder"),
        new("guideSelection", "assets", "AssetTreeView")
    };

    /// <summary>How long a hover has to rest on the target to count.</summary>
    internal const double HoverSeconds = .65;

    /// <summary>A target that never shows up stops being waited for.</summary>
    internal const double TargetGiveUpSeconds = 3;

    /// <summary>The key as the bubble names it.</summary>
    internal static string PanicLabel(string? panicKey) =>
        string.IsNullOrWhiteSpace(panicKey) || panicKey == "Escape" ? "Esc" : panicKey!;

    /// <summary>EMI's face for a frame (WPF Frame: serious, the slow wink, the blink).</summary>
    internal static string Face(bool serious, double motion, double now)
    {
        var face = serious ? "o_o" : "^_^";
        if (!serious && motion > 0 && now % 12.7 > 10.9 && now % 12.7 < 11.18) face = "^_~";
        if (motion > 0 && now % 5.3 < .12) face = "-_-";
        return face;
    }

    /// <summary>EMI's pose for a frame.</summary>
    internal static string Pose(bool playing, bool ending, double time, double motion) =>
        playing && time >= 24 && time < 25 && motion > 0 ? "smug"
        : (playing && time >= GatherAt || ending) ? "celebration" : "idle";
}
