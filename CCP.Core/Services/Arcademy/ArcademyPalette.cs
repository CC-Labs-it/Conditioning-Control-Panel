using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Arcademy;

/// <summary>Arcademy init palette, with the active mod's known string tokens overlaid.</summary>
internal static class ArcademyPalette
{
    private static readonly Dictionary<string, string> Defaults = new(StringComparer.Ordinal)
    {
        ["ground"] = "#14142B",
        ["navy"] = "#1A1A2E",
        ["panel"] = "#252542",
        ["ink"] = "#F2EBDD",
        ["pink"] = "#FF69B4",
        ["lavender"] = "#B8A6E8",
        ["gold"] = "#F0C24B",
    };

    public static JObject Resolve(string? modJson)
    {
        var result = new JObject();
        foreach (var pair in Defaults) result[pair.Key] = pair.Value;
        if (string.IsNullOrWhiteSpace(modJson)) return result;
        try
        {
            foreach (var property in JObject.Parse(modJson).Properties())
            {
                if (!Defaults.ContainsKey(property.Name) || property.Value.Type != JTokenType.String) continue;
                var value = (string?)property.Value;
                if (string.IsNullOrWhiteSpace(value) || value.Length > 96) continue;
                result[property.Name] = value;
            }
        }
        catch (Newtonsoft.Json.JsonException) { }
        return result;
    }
}
