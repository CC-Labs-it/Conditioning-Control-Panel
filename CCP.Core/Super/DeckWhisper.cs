namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// The "words behind the card" look (shop slot <c>behind</c>, owner 2026-11-17). Worn, a Deck card has
/// one picture on its front and a spiral on its back with one of the player's own subliminal phrases
/// coming at the viewer over it; every flip to the back rolls a new spiral and a new phrase
/// (<see cref="DeckSpiralBack"/>). It replaced the first version, a small word sliding out from behind
/// the card, which read as nothing. The row ids are unchanged.
/// </summary>
public static class DeckWhisper
{
    public const string NoneRow = "fx.behind.none", WhisperRow = "fx.behind.whisper";

    /// <summary>Does the worn row give the card a spiral back? Anything else is today's: picture to picture.</summary>
    public static bool On(string? row) => row == WhisperRow;

    /// <summary>
    /// The next word, from the player's own pool (ticked words only, 1 to 16 letters, lower case),
    /// never the one this card just showed while there is another. Null when the pool is empty.
    /// </summary>
    public static string? Pick(IEnumerable<KeyValuePair<string, bool>>? pool, string? last, Random rng)
        => Pick(pool, last, rng, 16, lower: true);

    /// <summary>As above, phrases up to <paramref name="maxChars"/> long, in the player's own case when
    /// <paramref name="lower"/> is false (the spiral back's phrase: <see cref="DeckSpiralBack.MaxChars"/>).</summary>
    public static string? Pick(IEnumerable<KeyValuePair<string, bool>>? pool, string? last, Random rng, int maxChars, bool lower)
    {
        if (pool == null) return null;
        var words = new List<string>();
        foreach (var kv in pool)
        {
            if (!kv.Value || string.IsNullOrWhiteSpace(kv.Key)) continue;
            var w = string.Join(' ', kv.Key.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (lower) w = w.ToLowerInvariant();
            if (w.Length > 0 && w.Length <= maxChars && !words.Contains(w)) words.Add(w);
        }
        if (words.Count == 0) return null;
        if (words.Count > 1 && last != null) words.Remove(last);
        return words[rng.Next(words.Count)];
    }
}
