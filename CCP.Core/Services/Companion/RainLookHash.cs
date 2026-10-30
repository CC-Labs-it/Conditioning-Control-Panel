using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Companion.Rain;

/// <summary>
/// Names a baked Rain look (RAIN-V2-PLAN 2.3): SHA-256 over the normalized look JSON, the pack
/// layout revision and the clip-set version, as 64 lowercase hex characters. Normalized = object
/// keys sorted (ordinal) at every depth and no whitespace, so the same look written with its keys
/// in another order names the same pack. Array order is kept: it is the look's own data.
/// </summary>
internal static class RainLookHash
{
    /// <summary>Bump when the clip set the baker renders changes (new actions in the wardrobe GLB):
    /// every old pack then misses and the look is baked again. 2 = the 17-action GLB
    /// (release asset rain-assets-v1, clip set rain-v2-17-20261030).</summary>
    public const int ClipSetVersion = 2;

    /// <summary>The pack layout revision (manifest.json "rev"); RainPackStore writes and checks it.</summary>
    public const int PackRev = 1;

    public static string Compute(string? lookJson) =>
        Compute(lookJson, PackRev, ClipSetVersion);

    public static string Compute(string? lookJson, int packRev, int clipSetVersion)
    {
        var payload = $"rain-pack|rev={packRev}|clips={clipSetVersion}|{Normalize(lookJson)}";
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>The look as compact JSON with sorted keys. Blank or unreadable input = "{}".</summary>
    public static string Normalize(string? lookJson)
    {
        if (string.IsNullOrWhiteSpace(lookJson)) return "{}";
        JToken token;
        try
        {
            // Strings stay strings: a date-looking value must not round-trip through DateTime.
            using var reader = new JsonTextReader(new System.IO.StringReader(lookJson)) { DateParseHandling = DateParseHandling.None };
            token = JToken.ReadFrom(reader);
        }
        catch (JsonException) { return "{}"; }
        return Sort(token).ToString(Formatting.None);
    }

    /// <summary>True for a string shaped like a hash this class makes (safe as a folder name).</summary>
    public static bool IsWellFormed(string? hash) =>
        hash != null && hash.Length == 64 && hash.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

    private static JToken Sort(JToken token)
    {
        switch (token)
        {
            case JObject obj:
                var sorted = new JObject();
                foreach (var p in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                    sorted.Add(p.Name, Sort(p.Value));
                return sorted;
            case JArray arr:
                return new JArray(arr.Select(Sort));
            default:
                return token.DeepClone();
        }
    }
}
