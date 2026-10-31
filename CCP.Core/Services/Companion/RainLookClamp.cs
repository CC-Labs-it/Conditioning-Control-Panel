using System; using System.Collections.Generic;
namespace ConditioningControlPanel.Services.Companion.Rain;
public static class RainLookClamp
{
    public static string Clamp(string? stored, RainItemAccess access)
    {
        if (access == null) throw new ArgumentNullException(nameof(access));
        return RainLookCodec.Normalize(stored, (slot, id) => !access.IsWearable(slot, id));
    }
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
