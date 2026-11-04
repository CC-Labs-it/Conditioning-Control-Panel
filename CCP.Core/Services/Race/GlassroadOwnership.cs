using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Race;

/// <summary>Glassroad CONTRACT section 7: only kart grants leave the host.</summary>
public static class GlassroadOwnership
{
    public static JArray KartGrants(IEnumerable<string>? grants)
    {
        var list = new SortedSet<string>(StringComparer.Ordinal);
        if (grants != null)
            foreach (var g in grants)
                if (g is { Length: <= 64 } && g.StartsWith("cos.kart.", StringComparison.Ordinal)) list.Add(g);
        var rows = new JArray();
        foreach (var g in list) { if (rows.Count >= 64) break; rows.Add(g); }
        return rows;
    }

    public static JObject OwnedFrame(IEnumerable<string>? grants)
        => new() { ["type"] = "owned", ["grants"] = KartGrants(grants) };
}
