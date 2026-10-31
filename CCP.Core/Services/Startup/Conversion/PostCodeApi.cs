using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Startup.Conversion
{
    /// <summary>The live post code, as <c>GET /v2/post-code/live</c> describes it (never the code).</summary>
    public sealed record PostCodeLive(bool Live, string? Feature, DateTime? EndsUtc, bool Redeemed);

    /// <summary>A redeem reply. <see cref="Reason"/>: unknown_code, already_redeemed, expired,
    /// bad_code (refused before sending) or offline.</summary>
    public sealed record PostCodeRedeem(bool Ok, string? Feature, DateTime? UntilUtc, string? Reason);

    /// <summary>
    /// THE POST CODE WIRE (brief C6): the weekly Patreon post carries a code that opens one Daily
    /// Gift feature for a while, once per account per code. Same base url and token as the
    /// invites wire. Nothing is retried; every failure reads as offline.
    /// </summary>
    public static class PostCodeApi
    {
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
        private static readonly HttpClient Http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        public static async Task<PostCodeLive?> LiveAsync(CancellationToken ct = default)
        {
            var id = Identity();
            if (id == null) return null;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(Timeout);
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get,
                    $"{BackRoomApi.BaseUrl}/v2/post-code/live?unified_id={Uri.EscapeDataString(id.Value.UnifiedId)}");
                req.Headers.Add("X-Auth-Token", id.Value.Token);
                using var res = await Http.SendAsync(req, budget.Token).ConfigureAwait(false);
                return ParseLive((int)res.StatusCode, await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug("[PostCode] live check failed: {E}", ex.GetType().Name);
                return null;
            }
        }

        public static async Task<PostCodeRedeem> RedeemAsync(string? code, CancellationToken ct = default)
        {
            var normal = NormalizeCode(code);
            if (normal == null) return new PostCodeRedeem(false, null, null, "bad_code");
            var id = Identity();
            if (id == null) return new PostCodeRedeem(false, null, null, "offline");
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(Timeout);
            try
            {
                var body = new JObject { ["code"] = normal, ["unified_id"] = id.Value.UnifiedId };
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{BackRoomApi.BaseUrl}/v2/post-code/redeem")
                {
                    Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"),
                };
                req.Headers.Add("X-Auth-Token", id.Value.Token);
                using var res = await Http.SendAsync(req, budget.Token).ConfigureAwait(false);
                return ParseRedeem((int)res.StatusCode, await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug("[PostCode] redeem failed: {E}", ex.GetType().Name);
                return new PostCodeRedeem(false, null, null, "offline");
            }
        }

        /// <summary>Trimmed, 1..64 visible characters with no spaces inside, or null.</summary>
        public static string? NormalizeCode(string? code)
        {
            var c = code?.Trim();
            if (string.IsNullOrEmpty(c) || c.Length > 64) return null;
            foreach (var ch in c) if (char.IsWhiteSpace(ch) || char.IsControl(ch)) return null;
            return c;
        }

        public static PostCodeLive? ParseLive(int status, string? body)
        {
            if (status != 200 || Read(body) is not JObject o) return null;
            var feature = Feature(o["feature"]);
            return new PostCodeLive(o.Value<bool?>("live") == true && feature != null, feature,
                ProfileExtras.ParseUtc(Str(o["ends_at"])), o.Value<bool?>("redeemed") == true);
        }

        public static PostCodeRedeem ParseRedeem(int status, string? body)
        {
            var o = Read(body);
            if (status == 200 && o?.Value<bool?>("ok") == true)
                return new PostCodeRedeem(true, Feature(o["feature"]), ProfileExtras.ParseUtc(Str(o["until"])), null);
            var word = Str(o?["reason"]) ?? Str(o?["error"]);
            if (word is "unknown_code" or "already_redeemed" or "expired") return new PostCodeRedeem(false, null, null, word);
            var reason = status switch { 400 => "unknown_code", 409 => "already_redeemed", 410 => "expired", _ => "offline" };
            return new PostCodeRedeem(false, null, null, reason);
        }

        private static string? Feature(JToken? t)
        {
            var f = Str(t)?.Trim().ToLowerInvariant();
            return f != null && Array.IndexOf(ProfileExtras.GrantKeys, f) >= 0 ? f : null;
        }

        private static string? Str(JToken? t) => t?.Type == JTokenType.String ? (string?)t : null;

        private static JObject? Read(string? body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                // Date coercion off: ends_at / until stay strings (see Descent.DescentReader.ParseWire).
                return JsonConvert.DeserializeObject<JToken>(body, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None }) as JObject;
            }
            catch (JsonException) { return null; }
        }

        private static (string UnifiedId, string Token)? Identity()
        {
            try { return BackRoomApi.AppIdentity(); }
            catch (Exception ex) { Serilog.Log.Debug("[PostCode] no identity: {E}", ex.GetType().Name); return null; }
        }
    }
}
