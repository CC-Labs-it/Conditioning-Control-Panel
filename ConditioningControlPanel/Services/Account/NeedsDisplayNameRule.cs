using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services
{
    /// <summary>Why a typed name cannot be sent yet. Mirrors the server's own checks.</summary>
    public enum DisplayNameShape { Ok, TooShort, TooLong, BadChars }

    /// <summary>
    /// The display name is required (owner call 2026-10-20): a signed-in account with no name is
    /// asked for one after every sign-in and at the next launch, until it has one. Pure, so the
    /// sign-in paths, the startup ladder and the null-name fallbacks all read the same rule.
    /// </summary>
    public static class NeedsDisplayNameRule
    {
        public const int MinLength = 3;
        public const int MaxLength = 20;

        // Same allowlist as the server's DISPLAY_NAME_INVALID_CHARS (proxy/server.js).
        private static readonly Regex Invalid =
            new(@"[^a-zA-Z0-9À-ɏЀ-ӿ　-鿿 _.\-]", RegexOptions.CultureInvariant);

        /// <summary>
        /// Ask when signed in and the server says so, or (older servers send no flag) when the
        /// name we hold is empty or whitespace. Never ask an anonymous install.
        /// </summary>
        public static bool ShouldAsk(bool signedIn, string? displayName, bool? serverSaysNeeded)
            => signedIn && (serverSaysNeeded == true || string.IsNullOrWhiteSpace(displayName));

        /// <summary>The first candidate that is not blank, trimmed, or null when every one is.</summary>
        public static string? FirstName(params string?[] candidates)
            => candidates.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c))?.Trim();

        public static DisplayNameShape Check(string? name)
        {
            var t = (name ?? "").Trim();
            if (t.Length < MinLength) return DisplayNameShape.TooShort;
            if (t.Length > MaxLength) return DisplayNameShape.TooLong;
            return Invalid.IsMatch(t) ? DisplayNameShape.BadChars : DisplayNameShape.Ok;
        }

        /// <summary>
        /// A prefill from a provider username (Discord, Patreon): characters the server refuses
        /// are dropped, the rest is cut to the limit. Null when nothing usable is left, so the
        /// box starts empty rather than with a name the server would bounce.
        /// </summary>
        public static string? Suggest(params string?[] candidates)
        {
            foreach (var c in candidates)
            {
                if (string.IsNullOrWhiteSpace(c)) continue;
                var sb = new StringBuilder();
                foreach (var ch in c.Trim())
                    if (!Invalid.IsMatch(ch.ToString())) sb.Append(ch);
                var s = sb.ToString().Trim();
                if (s.Length > MaxLength) s = s.Substring(0, MaxLength).Trim();
                if (Check(s) == DisplayNameShape.Ok) return s;
            }
            return null;
        }
    }
}
