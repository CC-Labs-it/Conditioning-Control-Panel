using System;

namespace ConditioningControlPanel.Services.Possession;

/// <summary>The warden's head-free rules (moved out of the WPF Warden): the verbs it barks, its
/// cooldowns and beats, where a knock shoves and a stare stands, the note left in the empty tube and
/// the stare line used when there is no bark service. The head owns the tube and the glides.</summary>
public static class WardenRules
{
    // PossessionWarden bark verbs (ctx verb). Every shipped pack must carry a line for each.
    public const string Knock = "knock";
    public const string Stare = "stare";
    public const string Leave = "leave";
    public const string Return = "return";
    public static readonly string[] Verbs = { Knock, Stare, Leave, Return };

    // The tube is a shared, user-visible actor: the bark system, the bubble egg and the chat window all
    // move it. These cooldowns keep the haunt from feeling like the companion is having a seizure.
    public static readonly TimeSpan AppearanceCooldown = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan StareCooldown = TimeSpan.FromSeconds(60);

    /// <summary>Hard ceiling on any single verb. A glide that never returns (tube thread wedged, handle
    /// died mid-flight) must not hold the director's effect slot for the rest of the lockdown.</summary>
    public static readonly TimeSpan VerbTimeout = TimeSpan.FromSeconds(15);

    /// <summary>The knock's beat: something arrives, considers the card, and only THEN does the card go.
    /// Without it the fall reads as a glitch instead of an act.</summary>
    public const int KnockBeatMs = 500;
    /// <summary>The line has to finish and the silence after it is what makes it a stare.</summary>
    public const int StareLingerMs = 2500;
    /// <summary>A knock stays put so the tube is standing there while the card falls; home follows.</summary>
    public const int GoHomeDelayMs = 2000;

    /// <summary>A verb may show the tube once the last appearance is <see cref="AppearanceCooldown"/> old.</summary>
    public static bool CanAppear(DateTime now, DateTime lastAppearance) => now - lastAppearance >= AppearanceCooldown;

    /// <summary>A stare refuses inside <see cref="StareCooldown"/> of the previous one.</summary>
    public static bool CanStare(DateTime now, DateTime lastStare) => now - lastStare >= StareCooldown;

    /// <summary>The knock's shove: 12 px toward the target. Small on purpose - the card falling is the
    /// payoff, the tube only has to look like the cause.</summary>
    public static double KnockShove(double targetCentreX, double tubeCentreX) => targetCentreX > tubeCentreX ? 12 : -12;

    /// <summary>Top-left that centres a tube of <paramref name="tubeW"/> x <paramref name="tubeH"/> on the window.</summary>
    public static (double X, double Y) StareSpot(double winX, double winY, double winW, double winH, double tubeW, double tubeH)
        => (winX + winW / 2.0 - tubeW / 2.0, winY + winH / 2.0 - tubeH / 2.0);

    // The note in the empty tube (wave 2, item C4). Loc keys, not bark lines: it is TEXT ON SCREEN with
    // no audio and no per-mod substitution - the bark packs own what she SAYS, the language files own
    // what the room WRITES.
    public static readonly string[] NoteKeys =
    {
        "possession_tube_note_1",
        "possession_tube_note_2",
        "possession_tube_note_3",
    };

    /// <summary>English fallbacks, for the case where the key is missing from a language file.</summary>
    public static readonly string[] NoteFallbacks =
    {
        "back soon. you are not.",
        "stepped out. you did not.",
        "gone a moment. you have longer.",
    };

    /// <summary>The note text for variant <paramref name="index"/> given what the language lookup
    /// returned. A lookup hands back the KEY when a file is missing it; a card reading
    /// "possession_tube_note_2" would be the single least in-character thing in the feature.</summary>
    public static string NoteText(int index, string? localized)
        => string.IsNullOrWhiteSpace(localized) || string.Equals(localized, NoteKeys[index], StringComparison.Ordinal)
            ? NoteFallbacks[index]
            : localized;

    /// <summary>The stare's line when there is no bark service at all (the packs own the words otherwise).</summary>
    public static string FallbackStareLine(string? reason) => reason switch
    {
        EscapeKinds.Close => "still trying the door? i can see you doing it~",
        EscapeKinds.Minimize => "you can hide the window. you cannot hide from me~",
        EscapeKinds.Stop => "no. we are not stopping. look at me~",
        EscapeKinds.SystemKey => "that key does nothing. i do~",
        _ => "i am watching you. i have been the whole time~",
    };
}
