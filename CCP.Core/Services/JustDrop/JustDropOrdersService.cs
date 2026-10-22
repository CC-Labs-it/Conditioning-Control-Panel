using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Serilog;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.JustDrop
{
    /// <summary>
    /// The account's Just Drop receipt drawer, read straight from ccp-server.
    ///
    /// <para><b>Why this is allowed, given the doctrine.</b> <see cref="JustDropService"/> says the
    /// desktop keeps no order list, and it still doesn't: nothing here is stored, nothing is merged
    /// with local state, and the answer is re-asked rather than remembered. What this reads is the
    /// SERVER's drawer, live, so the Takeaway shelf can show the user what they actually own instead
    /// of a desktop-side guess at it. The shop remains the only thing that can create, price or
    /// delete an order.</para>
    ///
    /// <para><b>The device door.</b> ccp-server's <c>/v2/justdrop/*</c> routes take either a
    /// Supabase JWT or an <c>X-Auth-Token</c> + <c>unified_id</c> pair. The second door exists
    /// because CCP Mobile has no Supabase session - and neither does the desktop, which holds the
    /// same ccp-server credential the phone does. Its own header says it plainly: "two browsers, the
    /// desktop WebView2 host and a phone all share one wallet by construction." So this needs no
    /// token exchange, no cookie and no login.</para>
    ///
    /// <para>Every failure returns an EMPTY list rather than throwing. A drawer we cannot read is a
    /// shelf with no cards on it, which is a state the shelf already renders; it is never a reason
    /// for the Session door to fail to open.</para>
    /// </summary>
    internal static class JustDropOrdersService
    {
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

        /// <summary>
        /// How long a fetched drawer is reused. The shelf refreshes on every visit to the Session
        /// door, and without this a user flicking between tabs would spend a request per visit for
        /// an answer that only changes when they buy or finish something. Both of those close the
        /// Just Drop window, which invalidates this explicitly - so the window is short and the
        /// staleness it allows is bounded by an event we already catch.
        /// </summary>
        private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(90);

        private static readonly SemaphoreSlim _gate = new(1, 1);
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };

        private static IReadOnlyList<Order> _cached = Array.Empty<Order>();
        private static string? _cachedAccount;
        internal static HttpClient? HttpOverride;
        private static DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

        /// <summary>One filed receipt, in the server's own shape (justdrop.js cleanOrder).</summary>
        internal sealed class Order
        {
            public string Id { get; init; } = "";
            public string Code { get; init; } = "";
            public string Name { get; init; } = "";
            /// <summary>S / M / L / XXL.</summary>
            public string SizeId { get; init; } = "";
            public int Items { get; init; }
            /// <summary>Pocket Money the order cost.</summary>
            public int Pm { get; init; }
            public DateTimeOffset At { get; init; }
            /// <summary>The SERVER's replay gate for Pocket Money: a paid order stays paid across a
            /// re-order. The desktop's XP equivalent is <see cref="CreditedOrders"/>; the two are
            /// deliberately separate ledgers for two separate currencies.</summary>
            public bool PaidOut { get; init; }

            public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Loc.Get("sd_takeaway_order_fallback") : Name;
            public string DateLabel => At.ToString("MMM d").ToUpperInvariant();
            public string Meta => Minutes > 0 ? Loc.GetF("sd_takeaway_meta", Minutes, DateLabel) : DateLabel;
            public string MinutesLabel => Minutes > 0 ? Loc.GetF("takeaway_row_min", Minutes) : "";
            public string AgeLabel(DateTimeOffset now)
            {
                int days = (int)Math.Floor((now - At).TotalDays);
                return days <= 0 ? Loc.Get("takeaway_today")
                    : Loc.GetF(days == 1 ? "takeaway_day_ago" : "takeaway_days_ago", days);
            }

            /// <summary>Session length in minutes, from the size. The order does not carry a
            /// duration - the size IS the duration, and the table lives in the web app's
            /// data.ts (S 5 / M 15 / L 30 / XXL 60). An unknown size reads as 0 rather than
            /// guessing, so a new size added web-side shows up as missing instead of as wrong.</summary>
            public int Minutes => SizeId switch
            {
                "S" => 5,
                "M" => 15,
                "L" => 30,
                "XXL" => 60,
                _ => 0,
            };
        }

        /// <summary>Drop the cache so the next call re-asks. Called when something has happened that
        /// changes the drawer - closing the Just Drop window, most of all.</summary>
        public static void Invalidate() => _cachedAt = DateTimeOffset.MinValue;

        /// <summary>
        /// The account's orders, newest first, at most 30 (the server's cap). Empty when signed out,
        /// when the server is unreachable, or when anything at all goes wrong.
        /// </summary>
        public static async Task<IReadOnlyList<Order>> FetchAsync(CancellationToken ct = default)
        {
            var token = SafeAuthToken();
            var unifiedId = SafeUnifiedId();
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(unifiedId)
                || (HttpOverride == null && !SandboxNet.Allows(new Uri(ProxyBaseUrl))))
            {
                // Signed out is not an error - it is a user with no drawer.
                return Array.Empty<Order>();
            }

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // Re-check inside the gate: a caller may have refreshed while we queued.
                if (_cachedAccount == unifiedId && _cachedAt > DateTimeOffset.UtcNow - CacheFor) return _cached;

                var url = $"{ProxyBaseUrl}/v2/justdrop/orders?unified_id={Uri.EscapeDataString(unifiedId)}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                // The device door, NOT Authorization: Bearer. resolveCaller() picks the JWT door
                // whenever a bearer is present, and ours is not a Supabase JWT - sending it as one
                // would be rejected rather than fall through to this door.
                request.Headers.Add("X-Auth-Token", token);
                request.Headers.Add("Accept", "application/json");

                using var response = await (HttpOverride ?? _http).SendAsync(request, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    await MergedAccountRecovery.TryHandleAsync(response).ConfigureAwait(false);   // contract D
                    Log.Debug("JustDrop orders: {Status}; shelf stays empty", (int)response.StatusCode);
                    return Array.Empty<Order>();
                }

                var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var parsed = Parse(body);
                _cachedAccount = unifiedId;
                _cached = parsed;
                _cachedAt = DateTimeOffset.UtcNow;
                Log.Information("JustDrop orders: {Count} in the drawer", parsed.Count);
                return parsed;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log.Debug("JustDrop orders: fetch failed ({Error})", ex.Message);
                return Array.Empty<Order>();
            }
            finally { _gate.Release(); }
        }

        internal static IReadOnlyList<Order> Parse(string body)
        {
            var list = new List<Order>();
            try
            {
                var rows = JObject.Parse(body)["orders"] as JArray;
                if (rows == null) return list;

                foreach (var row in rows)
                {
                    if (row is not JObject o) continue;
                    var code = (string?)o["code"];
                    if (string.IsNullOrWhiteSpace(code)) continue;   // a receipt with no code cannot be replayed

                    var atMs = o["at"]?.Value<long?>() ?? 0;
                    list.Add(new Order
                    {
                        Id = (string?)o["id"] ?? "",
                        Code = code!,
                        Name = (string?)o["name"] ?? "",
                        SizeId = (string?)o["sizeId"] ?? "",
                        Items = o["items"]?.Value<int?>() ?? 0,
                        Pm = o["pm"]?.Value<int?>() ?? 0,
                        At = atMs > 0
                            ? DateTimeOffset.FromUnixTimeMilliseconds(atMs).ToLocalTime()
                            : DateTimeOffset.Now,
                        PaidOut = o["paidOut"]?.Value<bool?>() == true,
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Debug("JustDrop orders: unparseable body ({Error})", ex.Message);
                return Array.Empty<Order>();
            }
            return list;
        }

        private static string SafeAuthToken()
        {
            try { return CoreSettings.Current.AuthToken ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string SafeUnifiedId()
        {
            try { return CoreAccount.UnifiedUserId ?? string.Empty; }
            catch { return string.Empty; }
        }
    }
}
