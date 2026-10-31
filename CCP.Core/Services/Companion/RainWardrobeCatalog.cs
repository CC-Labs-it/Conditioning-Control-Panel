using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.Companion.Rain
{
    public static class RainWardrobeCatalog
    {
        public sealed record Item(string ShopId, string Slot, string WardrobeId)
        {
            public string Grant => "cos." + ShopId;
            public string LockKey => Slot + ":" + WardrobeId;
        }

        public static IReadOnlyList<Item> Items => RainItemManifest.Shipped.Items
            .Where(i => i.Access == RainItemAccessKind.Shop && i.Grant!.StartsWith("cos.", StringComparison.Ordinal))
            .Select(i => new Item(i.Grant!.Substring(4), i.Slot, i.PageId)).ToList();

        private static RainItemAccess Access(IEnumerable<string>? grants, IRainProgressSource progress)
            => new(RainItemManifest.Shipped, new RainGrantSet(grants), progress);

        public static IReadOnlyList<string> LockedKeys(IEnumerable<string>? grants, IRainProgressSource progress)
        {
            var access = Access(grants, progress);
            return access.Manifest.Items.Where(i => !access.StateOf(i).Wearable).Select(i => i.Id).ToList();
        }

        public static bool IsLocked(string slot, string id, IEnumerable<string>? grants, IRainProgressSource progress)
        {
            var state = Access(grants, progress).StateOf(slot, id);
            return state.Item != null && !state.Wearable;
        }

        public static string Clamp(string? stored, IEnumerable<string>? grants, IRainProgressSource progress)
            => RainLookClamp.Clamp(stored, Access(grants, progress));
    }

}
