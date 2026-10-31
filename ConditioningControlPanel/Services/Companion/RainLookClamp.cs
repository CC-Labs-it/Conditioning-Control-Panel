using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// The one look clamp (RAIN-V2-PLAN 2.4: Apply -> host clamp -> bake; also the wardrobe window's clamp on
    /// open and on every save, through <see cref="RainWardrobeCatalog.Clamp"/>). It takes a stored
    /// <c>RainLook</c> (<see cref="RainLookCodec"/> contract) and returns its canonical form with every piece the
    /// account cannot wear taken off: an unknown, locked or unowned single-slot piece leaves its slot, which the
    /// page and the codec read as Rain's authored default for that slot; unknown slots go too; accessories keep
    /// only the wearable ones, in order, without repeats (an empty list stays empty = none). Colours are free.
    /// PURE given the access.
    /// </summary>
    public static class RainLookClamp
    {
        public static string Clamp(string? stored, RainItemAccess access)
        {
            if (access == null) throw new ArgumentNullException(nameof(access));
            return RainLookCodec.Normalize(stored, (slot, id) => !access.IsWearable(slot, id));
        }

        /// <summary>The pieces <see cref="Clamp"/> takes off, as <c>slot:id</c> in look order (the first one is the Apply blocker).</summary>
        public static IReadOnlyList<string> Blockers(string? stored, RainItemAccess access)
        {
            if (access == null) throw new ArgumentNullException(nameof(access));
            var list = new List<string>();
            RainLookCodec.Normalize(stored, (slot, id) =>
            {
                if (access.IsWearable(slot, id)) return false;
                var key = RainItem.KeyOf(slot, id);
                if (!list.Contains(key)) list.Add(key);
                return true;
            });
            return list;
        }
    }
}
