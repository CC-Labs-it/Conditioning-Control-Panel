using System.Text;

namespace ConditioningControlPanel.Emblems;

/// <summary>
/// Drops a leading emoji (and the spaces after it) from a row label, so "🐙Hydra" draws as "Hydra"
/// when the row carries an emblem. The loc files keep their emoji; only the drawing loses it.
/// A whole emoji sequence goes: variation selectors, skin tones, ZWJ joins, flags, keycaps and tags.
/// CJK, Cyrillic, accented Latin, digits and plain symbols stay.
/// </summary>
public static class LeadingEmoji
{
    public static string Strip(string? label)
    {
        if (string.IsNullOrEmpty(label)) return string.Empty;
        int i = 0;
        // Leading spaces before the emoji are not part of the label either.
        while (i < label.Length && IsSpace(label[i])) i++;
        if (!TryReadEmoji(label, ref i)) return label;

        while (i < label.Length && IsSpace(label[i])) i++;
        if (i >= label.Length) return label; // an emoji-only label keeps its emoji: nothing else to show
        return label.Substring(i);
    }

    /// <summary>Reads one emoji sequence starting at <paramref name="i"/>; false (i unchanged) when there is none.</summary>
    private static bool TryReadEmoji(string s, ref int i)
    {
        int start = i;
        if (!Rune.TryGetRuneAt(s, i, out var first)) return false;

        // Keycap: digit, # or * followed by (FE0F) 20E3. A bare digit is text.
        if (first.Value is >= '0' and <= '9' or '#' or '*')
        {
            int j = i + 1;
            if (j < s.Length && s[j] == '️') j++;
            if (j < s.Length && s[j] == '⃣') { i = j + 1; return true; }
            return false;
        }

        if (IsRegionalIndicator(first.Value))
        {
            i += first.Utf16SequenceLength;
            if (Rune.TryGetRuneAt(s, i, out var second) && IsRegionalIndicator(second.Value)) i += second.Utf16SequenceLength;
            return true;
        }

        if (!IsEmojiBase(first.Value)) return false;
        i += first.Utf16SequenceLength;

        while (i < s.Length && Rune.TryGetRuneAt(s, i, out var r))
        {
            int v = r.Value;
            if (v is 0xFE0F or 0xFE0E or 0x20E3 || v is >= 0x1F3FB and <= 0x1F3FF || v is >= 0xE0020 and <= 0xE007F)
            {
                i += r.Utf16SequenceLength;
                continue;
            }
            if (v == 0x200D && Rune.TryGetRuneAt(s, i + 1, out var next) && IsEmojiBase(next.Value))
            {
                i += 1 + next.Utf16SequenceLength;
                continue;
            }
            break;
        }
        return i > start;
    }

    private static bool IsRegionalIndicator(int v) => v is >= 0x1F1E6 and <= 0x1F1FF;

    /// <summary>Code points that start an emoji in our labels (pictographs, dingbats, misc symbols).</summary>
    private static bool IsEmojiBase(int v) =>
        v is >= 0x1F000 and <= 0x1FAFF      // pictographs, emoticons, transport, symbols ext.
          or >= 0x2600 and <= 0x27BF        // misc symbols + dingbats (⚡ ✨ ❤ ✅)
          or >= 0x2300 and <= 0x23FF        // misc technical (⌛ ⏰ ⏱)
          or >= 0x2B00 and <= 0x2BFF        // arrows and stars (⭐ ⬆)
          or >= 0x2190 and <= 0x21FF        // arrows (↔ ↕ used as emoji)
          or 0x203C or 0x2049 or 0x2122 or 0x2139 or 0x24C2
          or 0x3030 or 0x303D or 0x3297 or 0x3299
          or 0x00A9 or 0x00AE;

    private static bool IsSpace(char c) => c == ' ' || c == ' ' || c == ' ' || c == ' ';
}
