using System;
using System.Collections.Generic;
using System.Text;

namespace ConditioningControlPanel.Services.Banner
{
    /// <summary>
    /// The substitution table, lifted wholesale from
    /// <c>Services/Possession/Effects/GlyphRotEffect.cs</c> so the marquee's rot and the haunt's
    /// rot are the same alphabet.
    ///
    /// <para>INVARIANT, same as there: a rot is the same CHARACTER COUNT as the text it ate, and
    /// every substitute is a single base character (a combining mark rides on the one in front of
    /// it), so a healing line never re-flows and never shoves its neighbours around.</para>
    /// </summary>
    public static class MarqueeGlyphRot
    {
        private static readonly char[] Boxes = { '▯', '▮', '□', '▪', '◻', '▉', '▒' };

        private static readonly Dictionary<char, char[]> LookAlikes = new()
        {
            ['a'] = new[] { 'а', '@' },
            ['e'] = new[] { 'е', '3' },
            ['o'] = new[] { 'о', '0' },
            ['i'] = new[] { 'і', '1' },
            ['s'] = new[] { 'ѕ', '5' },
            ['c'] = new[] { 'с' },
            ['n'] = new[] { 'п' },
            ['t'] = new[] { 'т', '7' },
            ['r'] = new[] { 'г' },
            ['y'] = new[] { 'у' },
            ['h'] = new[] { 'һ' },
            ['m'] = new[] { 'м' },
        };

        private static readonly Random Rng = new();

        public static string Rot(string? text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(text!.Length);
            foreach (var c in text) sb.Append(Rot(c));
            return sb.ToString();
        }

        public static string Rot(char c)
        {
            if (c == ' ') return " ";
            var lower = char.ToLowerInvariant(c);
            if (LookAlikes.TryGetValue(lower, out var picks) && Rng.Next(100) < 55)
                return picks[Rng.Next(picks.Length)].ToString();
            return Boxes[Rng.Next(Boxes.Length)].ToString();
        }
    }
}
