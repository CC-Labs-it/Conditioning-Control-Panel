namespace ConditioningControlPanel.BubbleCore;

/// <summary>
/// Which clips a popped bubble may play, as paths under Resources/sounds (the form
/// <c>ModResourceResolver.ResolveAudioPath</c> takes, so a mod overrides a clip by its name).
/// Soap, null and any unknown id get today's three pops. Every other skin gets its own three
/// variants, rendered offline by <c>Scripts/render-bubble-pops.mjs</c> from the bubble lab's sounds.
/// The lucky chime is not here: it replaces the pop for every skin, as it always has.
/// </summary>
public static class BubbleSkinSounds
{
    /// <summary>Clips rendered per skin. Keep in step with VARIANTS in the render script.</summary>
    public const int Variants = 3;

    /// <summary>Today's pops, in today's order (the caller's random pick over them is unchanged).</summary>
    public static readonly IReadOnlyList<string> SoapPops = new[] { "bubbles/Pop.mp3", "bubbles/Pop2.mp3", "bubbles/Pop3.mp3" };

    /// <summary>Skins with their own pop sound: every skin id but soap. Keep in step with SKINS in the script.</summary>
    public static readonly IReadOnlyList<string> Skins = BubbleSkinIds.All.Where(id => id != BubbleSkinIds.Soap).ToArray();

    private static readonly Dictionary<string, string[]> Clips = Skins.ToDictionary(
        id => id, id => Enumerable.Range(1, Variants).Select(v => $"bubbles/{id}_{v}.wav").ToArray());

    /// <summary>The clips for a skin: its own three, or <see cref="SoapPops"/> for soap and anything unknown.</summary>
    public static IReadOnlyList<string> PopClips(string? skin) =>
        skin != null && Clips.TryGetValue(skin, out var own) ? own : SoapPops;

    /// <summary>True when <see cref="PopClips"/> gives this skin its own clips rather than today's pops.</summary>
    public static bool HasOwnPop(string? skin) => skin != null && Clips.ContainsKey(skin);
}
