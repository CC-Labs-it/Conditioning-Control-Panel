using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Lobby;

public enum RemoteClaimStatus { Success, Conflict, Merged, Error }

/// <summary>The URL is transient and contains the PIN. Do not log or persist it.</summary>
public sealed class RemoteClaimResult
{
    public RemoteClaimStatus Status { get; }
    public string? SessionUrl { get; }
    internal RemoteClaimResult(RemoteClaimStatus status, string? url = null) { Status = status; SessionUrl = url; }
}

/// <summary>WPF AvailableSubjectsService.TryClaimAsync: the caller authenticates separately from the subject.</summary>
public sealed class RemoteDirectoryClaimClient
{
    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _http;
    private readonly Func<(string UnifiedId, string Token)?> _identity;
    private readonly string _baseUrl;

    public RemoteDirectoryClaimClient(HttpClient? http = null, Func<(string UnifiedId, string Token)?>? identity = null, string? baseUrl = null)
    {
        _http = http ?? Shared;
        _identity = identity ?? LobbyWire.DefaultIdentity;
        _baseUrl = baseUrl ?? LobbyWire.DefaultBaseUrl() ?? "";
    }

    public async Task<RemoteClaimResult> ClaimAsync(string subjectUnifiedId, CancellationToken ct = default)
    {
        try
        {
            var id = _identity();
            if (id == null || string.IsNullOrEmpty(id.Value.UnifiedId) || string.IsNullOrEmpty(id.Value.Token)
                || string.IsNullOrEmpty(subjectUnifiedId) || string.IsNullOrEmpty(_baseUrl))
                return new(RemoteClaimStatus.Error);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl.TrimEnd('/')}/v2/directory/claim");
            request.Headers.Add("X-Client-Version", CoreReleaseContent.AppVersion);
            request.Headers.UserAgent.ParseAdd($"ConditioningControlPanel/{CoreReleaseContent.AppVersion}");
            request.Headers.Add("X-Auth-Token", id.Value.Token);
            request.Headers.Add("X-Caller-Unified-Id", id.Value.UnifiedId);
            request.Content = new StringContent(JsonConvert.SerializeObject(new { unified_id = subjectUnifiedId }), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                var recovery = V2AuthService.MergedRecovery;
                if (recovery != null && await recovery(response, null).ConfigureAwait(false)) return new(RemoteClaimStatus.Merged);
                return new(RemoteClaimStatus.Conflict);
            }
            if (!response.IsSuccessStatusCode) return new(RemoteClaimStatus.Error);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var url = JObject.Parse(body)["session_url"]?.ToString();
            return string.IsNullOrEmpty(url) ? new(RemoteClaimStatus.Error) : new(RemoteClaimStatus.Success, url);
        }
        catch (Exception) { return new(RemoteClaimStatus.Error); }
    }
}
