using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Bureau;

/// <summary>
/// The pure half of the "Beta Inspection Bureau" host, shared by both heads: endpoints, the
/// bridge's reply shapes, target parsing, batch assembly, the lockers rule and the dropped-zip
/// match. Nothing here touches a window, a WebView or a setting; the head's host (WPF
/// <c>Services/Bureau/BureauHostService.cs</c>) does that and calls in.
/// </summary>
internal static class BureauHostRules
{
    public const string ProductName = "Beta Inspection Bureau";

    public const int Protocol = 1;
    public const string SiteHost = "cclabs.app";
    public const string StartUrl = "https://cclabs.app/bureau/index.html";
    public const string ServerBase = "https://codebambi-proxy.vercel.app";
    public const int ServerFetchCount = 24;   // over-fetch: not every target resolves locally
    public const int BatchDecodeCap = 6;      // frames per bridge message (keeps postMessage light)

    /// <summary>Discord channel with the pack catalogue — the "requisition office".</summary>
    public const string CatalogueUrl = DiscordLinks.PackCatalogue;

    /// <summary>Gold certification is only wired when this is set (operator machines).</summary>
    public const string AdminTokenVariable = "CCP_BUREAU_ADMIN_TOKEN";

    /// <summary>Watchdog cadence, the silence that triggers recovery, and the exit grace.</summary>
    public static readonly TimeSpan HeartbeatCheckInterval = TimeSpan.FromSeconds(5);
    public const double HeartbeatSilenceSeconds = 20;
    public static readonly TimeSpan ExitGrace = TimeSpan.FromMilliseconds(1200);

    public static string? AdminToken => Environment.GetEnvironmentVariable(AdminTokenVariable);

    public static bool HasAuth(string? unifiedId, string? authToken) =>
        !string.IsNullOrEmpty(unifiedId) && !string.IsNullOrEmpty(authToken);

    /// <summary>A server target is "&lt;64-hex sha256&gt;:&lt;frame&gt;"; anything else is skipped.</summary>
    public static bool TryParseTarget(string? target, out string hash, out int frame)
    {
        hash = "";
        frame = 0;
        if (string.IsNullOrEmpty(target)) return false;
        var sep = target.IndexOf(':');
        if (sep != 64) return false;
        if (!int.TryParse(target[(sep + 1)..], out frame)) return false;
        hash = target[..sep];
        return true;
    }

    /// <summary>
    /// Turn <c>/v2/bureau/next</c> targets into at most <see cref="BatchDecodeCap"/> page items.
    /// With a pack filter only evidence owned by that installed pack is served; a target that is not
    /// in this user's packs (or the decoder diverged) is skipped. Certify-mode proposals ride along.
    /// </summary>
    public static List<JObject> BuildBatch(JArray targets, string? packFilter, JObject? proposals,
        Func<string, int, (string DataUri, int W, int H)?> decode)
    {
        var items = new List<JObject>();
        foreach (var t in targets)
        {
            if (items.Count >= BatchDecodeCap) break;
            var target = (string?)t;
            if (!TryParseTarget(target, out var hash, out var frame)) continue;

            // Locker scoping: only serve cases whose evidence lives in the loaded pack.
            if (!string.IsNullOrEmpty(packFilter))
            {
                if (!BureauIndexService.TryResolve(hash, out var owner, out _)) continue;
                if (!string.Equals(owner, packFilter, StringComparison.Ordinal)) continue;
            }

            var decoded = decode(hash, frame);
            if (decoded == null) continue;
            var item = new JObject
            {
                ["target"] = target,
                ["src"] = decoded.Value.DataUri,
                ["dims"] = new JArray(decoded.Value.W, decoded.Value.H),
            };
            var boxes = proposals?[target!];
            if (boxes != null) item["boxes"] = boxes;
            items.Add(item);
        }
        return items;
    }

    public static JObject NextRequest(string? unifiedId, bool certifying)
    {
        var req = new JObject { ["unified_id"] = unifiedId, ["count"] = ServerFetchCount };
        if (certifying) req["certify"] = true;
        return req;
    }

    public static JObject SubmitRequest(string? unifiedId, JObject page)
    {
        var req = new JObject
        {
            ["unified_id"] = unifiedId,
            ["target"] = (string?)page["target"] ?? "",
            ["dims"] = page["dims"],
            ["boxes"] = page["boxes"] ?? new JArray(),
        };
        if (page["amend"]?.Value<bool>() == true) req["amend"] = true;
        return req;
    }

    /// <summary>The page's <c>submit-result</c>: the server body on 200, else code + error.</summary>
    public static JObject SubmitReply(string target, int status, JObject? body)
    {
        var reply = status == 200 && body != null ? body : new JObject();
        reply["type"] = "submit-result";
        reply["target"] = target;
        reply["ok"] = status == 200;
        if (status != 200)
        {
            reply["code"] = status;
            reply["error"] = body?["error"]?.ToString() ?? $"server error {status}";
        }
        return reply;
    }

    public static JObject GoldRequest(JObject page) => new()
    {
        ["target"] = (string?)page["target"] ?? "",
        ["boxes"] = page["boxes"] ?? new JArray(),
        ["remove"] = page["remove"] ?? false,
    };

    /// <summary>One evidence locker, or null when the pack has no casework: a video-only pack
    /// that is not installed, or an installed pack with nothing indexed.</summary>
    public static JObject? Locker(string id, string name, int imageCount, bool installed, int indexed)
    {
        if (!installed && imageCount <= 0) return null;
        if (installed && indexed == 0) return null;
        return new JObject
        {
            ["id"] = id,
            ["name"] = name,
            ["installed"] = installed,
            ["images"] = installed ? indexed : imageCount,
        };
    }

    /// <summary>Distribution zips are named after the pack ("Bambi Core Collection.zip"), so a
    /// dropped zip matches on letters/digits of its stem vs the pack's Name or Id.</summary>
    public static bool ZipMatchesPack(string zipPath, string name, string id)
    {
        var stem = Norm(System.IO.Path.GetFileNameWithoutExtension(zipPath));
        return Norm(name) == stem || Norm(id) == stem;
    }

    private static string Norm(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>A server request: X-Auth-Token when signed in, x-admin-token only for admin routes.</summary>
    public static HttpRequestMessage BuildRequest(HttpMethod method, string path, JObject? body,
        string? authToken, string? adminToken, bool admin)
    {
        var req = new HttpRequestMessage(method, ServerBase + path);
        if (!string.IsNullOrEmpty(authToken)) req.Headers.TryAddWithoutValidation("X-Auth-Token", authToken);
        if (admin && !string.IsNullOrEmpty(adminToken)) req.Headers.TryAddWithoutValidation("x-admin-token", adminToken);
        if (body != null) req.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
        return req;
    }
}
