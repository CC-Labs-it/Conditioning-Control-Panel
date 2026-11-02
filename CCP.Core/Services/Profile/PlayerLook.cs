using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// What another player's small avatar wears (contract 4, "others see it"): their preset or Prize Counter
    /// avatar and their Prize Counter frame. Rides a person object on the friends, request and leash wires as
    /// an optional <c>cosmetics: { avatar_id, frame_id }</c> (the profile payload's own field names). The
    /// server clamps shop ids to the wearer's grants; this side only drops ids this build has no art for.
    /// A server that predates the field sends nothing and every avatar draws as before.
    /// </summary>
    public sealed record PlayerLook(string? AvatarId, string? FrameId)
    {
        public static readonly PlayerLook None = new(null, null);

        public bool IsEmpty => AvatarId == null && FrameId == null;

        /// <summary>Read <paramref name="cosmetics"/> against the ids this build knows. Pure, never throws.</summary>
        public static PlayerLook From(JToken? cosmetics, ISet<string> knownAvatarIds, ISet<string>? knownFrameIds)
        {
            try
            {
                if (cosmetics is not JObject o) return None;
                var avatar = Id(o["avatar_id"]);
                var frame = Id(o["frame_id"]);
                if (avatar != null && !knownAvatarIds.Contains(avatar)) avatar = null;
                if (frame != null && (knownFrameIds == null || !knownFrameIds.Contains(frame))) frame = null;
                return avatar == null && frame == null ? None : new PlayerLook(avatar, frame);
            }
            catch { return None; }
        }

        /// <summary>The ids this head can draw: avatars (preset plus shop) and shop frames. The WPF head seeds it
        /// from <c>CosmeticsCatalog.AllAvatarIds</c> and <c>ShopCosmetics.FrameIds</c>; unseeded means the Core
        /// preset avatars and no frames.</summary>
        public static volatile Func<(ISet<string> Avatars, ISet<string>? Frames)>? KnownIdsProvider;

        /// <summary>The app's read: preset plus shop avatars, shop frames.</summary>
        public static PlayerLook From(JToken? cosmetics)
        {
            try
            {
                var known = KnownIdsProvider?.Invoke() ?? (CosmeticsPool.AvatarIds, null);
                return From(cosmetics, known.Avatars, known.Frames);
            }
            catch { return None; }
        }

        private static string? Id(JToken? t)
        {
            if (t is not JValue { Type: JTokenType.String } v) return null;
            var s = ((string?)v)?.Trim();
            return string.IsNullOrEmpty(s) || s.Length > 64 ? null : s;
        }
    }
}
