using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Startup.Conversion
{
    /// <summary>A pass redeem reply. <see cref="Tier"/> is what the pass gives now (1 Basic, 2 Prime)
    /// and <see cref="UntilUtc"/> when that ends. <see cref="Reason"/>: unknown_code,
    /// already_redeemed, expired, too_fast, bad_code (refused before sending) or offline.</summary>
    public sealed record CryptoPassRedeem(bool Ok, int Tier, DateTime? UntilUtc, string? Reason);

    /// <summary>
    /// THE PASS CODE WIRE. A pass is bought once on the website (crypto) and comes as a code like
    /// <c>CCP-PRIME-3M-7K4Q-X9WD-M2HT-ABCD</c> (a handed-out code may count days: <c>-14D-</c>); redeeming it prepays Basic or Prime on this account.
    /// Same base url and token as the post code wire. Nothing is retried; every failure reads as
    /// offline. The tier itself is never taken from this reply: after a redeem the app asks the
    /// server for the account's tier the usual way.
    /// </summary>
    public static class CryptoPassApi
    {
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
        private static readonly HttpClient Http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };

        private static readonly Regex Shape = new(
            "^CCP-(BASIC|PRIME)-[1-9][0-9]{0,2}[MD](-[2-9A-HJ-NP-Z]{4}){4}$", RegexOptions.CultureInvariant);

        /// <summary>The code as the server stores it (upper case, single dashes), or null when the
        /// text is not shaped like a pass code. Spaces typed in place of dashes are accepted.</summary>
        public static string? NormalizeCode(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length > 80) return null;
            var s = Regex.Replace(text.Trim().ToUpperInvariant(), "[\\s_-]+", "-");
            return Shape.IsMatch(s) ? s : null;
        }

        public static async Task<CryptoPassRedeem> RedeemAsync(string? code, CancellationToken ct = default)
        {
            var normal = NormalizeCode(code);
            if (normal == null) return new CryptoPassRedeem(false, 0, null, "bad_code");
            (string UnifiedId, string Token)? id;
            try { id = BackRoomApi.AppIdentity(); }
            catch (Exception ex) { Serilog.Log.Debug("[Pass] no identity: {E}", ex.GetType().Name); id = null; }
            if (id == null) return new CryptoPassRedeem(false, 0, null, "offline");

            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(Timeout);
            try
            {
                var body = new JObject { ["code"] = normal, ["unified_id"] = id.Value.UnifiedId };
                using var req = new HttpRequestMessage(HttpMethod.Post, $"{BackRoomApi.BaseUrl}/v2/crypto/redeem")
                {
                    Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json"),
                };
                req.Headers.Add("X-Auth-Token", id.Value.Token);
                using var res = await Http.SendAsync(req, budget.Token).ConfigureAwait(false);
                return ParseRedeem((int)res.StatusCode, await res.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug("[Pass] redeem failed: {E}", ex.GetType().Name);
                return new CryptoPassRedeem(false, 0, null, "offline");
            }
        }

        private static string? Str(JToken? t) => t?.Type == JTokenType.String ? (string?)t : null;

        public static CryptoPassRedeem ParseRedeem(int status, string? body)
        {
            JObject? o = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    // Date coercion off: the dates stay strings until ParseUtc reads them.
                    o = JsonConvert.DeserializeObject<JToken>(body, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None }) as JObject;
                }
                catch (JsonException) { o = null; }
            }

            if (status == 200 && o?.Value<bool?>("ok") == true && o["pass"] is JObject pass)
            {
                var tier = pass["tier"]?.Type == JTokenType.Integer ? (int)pass["tier"]! : 0;
                var until = ProfileExtras.ParseUtc(Str(tier >= 2 ? pass["prime_until"] : pass["basic_until"]))
                    ?? ProfileExtras.ParseUtc(Str(pass["ends_at"]));
                if (tier is 1 or 2 && until != null) return new CryptoPassRedeem(true, tier, until, null);
                return new CryptoPassRedeem(false, 0, null, "offline");
            }

            var word = Str(o?["error"]);
            if (word is "unknown_code" or "already_redeemed" or "too_fast" or "expired") return new CryptoPassRedeem(false, 0, null, word);
            var reason = status switch { 400 => "unknown_code", 409 => "already_redeemed", 410 => "expired", 429 => "too_fast", _ => "offline" };
            return new CryptoPassRedeem(false, 0, null, reason);
        }
    }
}
