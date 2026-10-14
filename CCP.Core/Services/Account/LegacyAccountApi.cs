using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The legacy V1 unified-account endpoints (pre v5.5), moved from WPF AccountService's
    /// "V1 Authentication" region: /auth/lookup, /auth/register, /auth/link-provider, /auth/claim, each
    /// authorised by the PROVIDER's OAuth token. Only the pending-registration path still calls them.
    /// Same headers as WPF's static client (V2AuthService.Configure: X-Client-Version + User-Agent).
    /// </summary>
    public sealed class LegacyAccountApi
    {
        private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";
        private static readonly Lazy<HttpClient> Shared = new(() => V2AuthService.Configure(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }));
        private readonly HttpClient _http;

        /// <param name="handler">Test seam; null uses the shared production client.</param>
        public LegacyAccountApi(HttpMessageHandler? handler = null) =>
            _http = handler == null ? Shared.Value : V2AuthService.Configure(new HttpClient(handler));

        public sealed record LookupResult(bool Exists, string? UnifiedId, string? DisplayName, bool HasDisplayName,
                                          bool CanAutoLink, string? AutoLinkUnifiedId, string? AutoLinkDisplayName);
        public sealed record RegisterResult(bool Success, string? UnifiedId, string? DisplayName, string? Error, bool CanClaim);
        public sealed record LinkResult(bool Success, string? UnifiedId, string? Error);

        private async Task<(HttpResponseMessage Response, string Json)> PostAsync(string path, object body, string accessToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{ProxyBaseUrl}{path}")
            {
                Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            var response = await _http.SendAsync(request).ConfigureAwait(false);
            return (response, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        }

        /// <summary>Is this provider account linked to a unified user? Null on any failure (WPF LookupAccountAsync).</summary>
        public async Task<LookupResult?> LookupAsync(string provider, string accessToken)
        {
            try
            {
                var (response, json) = await PostAsync("/auth/lookup", new { provider }, accessToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    Log.Warning("AccountService: Lookup failed - {Status} (body {Bytes} bytes)", (int)response.StatusCode, json.Length);
                    return null;
                }
                var data = JObject.Parse(json);
                return new LookupResult((bool?)data["exists"] ?? false, data["unified_id"]?.Value<string>(),
                    data["display_name"]?.Value<string>(), (bool?)data["has_display_name"] ?? false, (bool?)data["can_auto_link"] ?? false,
                    data["auto_link_unified_id"]?.Value<string>(), data["auto_link_display_name"]?.Value<string>());
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AccountService: LookupAccountAsync failed");
                return null;
            }
        }

        /// <summary>Register a new unified user with a display name (WPF RegisterUserAsync).</summary>
        public async Task<RegisterResult> RegisterAsync(string provider, string accessToken, string displayName)
        {
            try
            {
                var (response, json) = await PostAsync("/auth/register", new { display_name = displayName, provider }, accessToken).ConfigureAwait(false);
                var data = JObject.Parse(json);
                if (response.IsSuccessStatusCode && (bool?)data["success"] == true)
                    return new RegisterResult(true, data["unified_id"]?.Value<string>(), data["display_name"]?.Value<string>(), null, false);
                return new RegisterResult(false, null, null, data["error"]?.Value<string>() ?? "Registration failed", (bool?)data["can_claim"] ?? false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AccountService: RegisterUserAsync failed");
                return new RegisterResult(false, null, null, ex.Message, false);
            }
        }

        /// <summary>Link a provider to an existing unified user (WPF LinkProviderAsync).</summary>
        public Task<LinkResult> LinkAsync(string provider, string accessToken, string unifiedId) =>
            LinkOrClaimAsync("/auth/link-provider", new { unified_id = unifiedId, provider }, accessToken, "Linking failed", "LinkProviderAsync");

        /// <summary>Claim an existing display name, merging accounts (WPF ClaimAccountAsync).</summary>
        public Task<LinkResult> ClaimAsync(string provider, string accessToken, string displayName) =>
            LinkOrClaimAsync("/auth/claim", new { display_name = displayName, provider }, accessToken, "Claim failed", "ClaimAccountAsync");

        private async Task<LinkResult> LinkOrClaimAsync(string path, object body, string accessToken, string fallback, string what)
        {
            try
            {
                var (response, json) = await PostAsync(path, body, accessToken).ConfigureAwait(false);
                var data = JObject.Parse(json);
                if (response.IsSuccessStatusCode && (bool?)data["success"] == true)
                    return new LinkResult(true, data["unified_id"]?.Value<string>(), null);
                return new LinkResult(false, null, data["error"]?.Value<string>() ?? fallback);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AccountService: {What} failed", what);
                return new LinkResult(false, null, ex.Message);
            }
        }
    }
}
