using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Chaos;

internal static class HostedPageMotion
{
    internal static string Argument(MotionLevel level) => level == MotionLevel.Off
        ? "--force-prefers-reduced-motion" : "--force-prefers-no-reduced-motion";

    internal static string Merge(string? arguments, MotionLevel level)
    {
        var switches = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var token in (arguments ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = token.IndexOf('=');
            var name = eq < 0 ? token : token[..eq];
            if (name is "--force-prefers-reduced-motion" or "--force-prefers-no-reduced-motion") continue;
            if (!switches.ContainsKey(name)) order.Add(name);
            if ((name is "--enable-features" or "--disable-features") && eq >= 0 && switches.TryGetValue(name, out var prior))
            {
                var values = (prior[(prior.IndexOf('=') + 1)..] + "," + token[(eq + 1)..]).Split(',', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal);
                switches[name] = name + "=" + string.Join(',', values);
            }
            else switches[name] = token;
        }
        order.Add(Argument(level));
        switches[Argument(level)] = Argument(level);
        return string.Join(' ', order.Select(name => switches[name]));
    }
}
