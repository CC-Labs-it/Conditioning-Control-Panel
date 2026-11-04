using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Server purchase transport for the enhancement tree. A caller must confirm first and apply
    /// the returned values only to the account that sent the request. WPF's profile sync also retries a balance refusal after sync; that is not here yet.
    /// </summary>
    public sealed class SkillPurchase
    {
        private const string ServerUrl = "https://codebambi-proxy.vercel.app";
        private static readonly Lazy<HttpClient> Shared = new(() => V2AuthService.Configure(new HttpClient(new ServerClockHandler())));
        private readonly HttpClient _http;
        internal Func<HttpResponseMessage, Task<bool>> Recover = AuthRecovery.HandleUnauthorizedAsync;
        private int _pending;

        public SkillPurchase(HttpMessageHandler? handler = null) =>
            _http = handler == null ? Shared.Value : V2AuthService.Configure(new HttpClient(handler));

        /// <summary>Success includes the authoritative response. Failures never alter settings.</summary>
        public async Task<(bool success, string? error, int? points, List<string>? skills, long? lifetimeSpent)> BuyAsync(AppSettings settings, string skillId)
        {
            if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0)
                return (false, null, null, null, null);
            try
            {
                if (!SkillTreeRules.CanPurchaseSkill(settings, skillId))
                    return (false, Loc.Get("skill_err_cannot_purchase"), null, null, null);
                var unifiedId = settings.UnifiedId;
                if (string.IsNullOrEmpty(unifiedId))
                    return (false, Loc.Get("skill_err_login_required"), null, null, null);
                async Task<(HttpResponseMessage response, string json)> SendAsync()
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, ServerUrl + "/v2/user/purchase-skill");
                    var token = settings.AuthToken;
                    if (!string.IsNullOrEmpty(token)) request.Headers.Add("X-Auth-Token", token);
                    request.Content = new StringContent(JsonConvert.SerializeObject(new
                    {
                        unified_id = unifiedId, skill_id = skillId, skill_points = settings.SkillPoints
                    }), Encoding.UTF8, "application/json");
                    var response = await _http.SendAsync(request).ConfigureAwait(false);
                    return (response, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                }
                var (response, json) = await SendAsync().ConfigureAwait(false);
                try
                {
                    if (!response.IsSuccessStatusCode && V2AuthService.MergedRecovery is { } merged && await merged(response, json).ConfigureAwait(false))
                        return (false, Loc.Get("account_merged_retry_hint"), null, null, null);
                    if (response.StatusCode == HttpStatusCode.Unauthorized && await Recover(response).ConfigureAwait(false)
                        && settings.UnifiedId == unifiedId && !string.IsNullOrEmpty(settings.AuthToken))
                    {
                        response.Dispose();
                        (response, json) = await SendAsync().ConfigureAwait(false);
                    }
                    Reply? reply = null;
                    try { reply = JsonConvert.DeserializeObject<Reply>(json); } catch (JsonException) { }
                    if (!response.IsSuccessStatusCode)
                        return (false, reply?.Error ?? Loc.GetF("skill_purchase_server_error", (int)response.StatusCode), null, null, null);
                    if (reply?.Success != true)
                        return (false, reply?.Error ?? Loc.Get("skill_purchase_failed"), null, null, null);
                    // A success without the purchased skill is not enough to update the local tree.
                    if (reply.Points == null || reply.Skills == null || !reply.Skills.Contains(skillId))
                        return (false, Loc.Get("skill_purchase_invalid_response"), null, null, null);
                    return (true, null, reply.Points, reply.Skills, reply.LifetimeSpent);
                }
                finally { response.Dispose(); }
            }
            catch (Exception)
            {
                return (false, Loc.Get("skill_purchase_connection_failed"), null, null, null);
            }
            finally { Volatile.Write(ref _pending, 0); }
        }

        private sealed class Reply
        {
            [JsonProperty("success")] public bool Success { get; set; }
            [JsonProperty("error")] public string? Error { get; set; }
            [JsonProperty("skill_points")] public int? Points { get; set; }
            [JsonProperty("unlocked_skills")] public List<string>? Skills { get; set; }
            [JsonProperty("lifetime_points_spent")] public long? LifetimeSpent { get; set; }
        }
    }
}
