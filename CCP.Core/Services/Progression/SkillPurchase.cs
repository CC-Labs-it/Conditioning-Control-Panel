using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Serilog;
namespace ConditioningControlPanel.Services
{
    // WPF SkillTreeService.cs:80; ProfileSyncService.cs:3936-4110; SeasonRecapService.cs:200.
    public sealed class SkillPurchase
    {
        private const string ServerUrl = "https://codebambi-proxy.vercel.app";
        public static volatile SkillPurchase? Current;
        private readonly HttpClient _http;
        private readonly Func<AppSettings?> _settings;
        public Func<Task<bool>>? SyncBeforeRetry { get; set; }
        public Action<int>? PointsSpent { get; set; }
        public Action<long>? LifetimeSpentReconciled { get; set; }
        public Action Save { get; set; } = () => { if (CoreSettings.HasProvider) CoreSettings.Save(); };
        public Func<HttpResponseMessage, Task<bool>> Unauthorized { get; set; } = AuthRecovery.HandleUnauthorizedAsync;
        public event EventHandler<string>? SkillUnlocked;
        public SkillPurchase(Func<AppSettings?>? settings = null, HttpMessageHandler? handler = null)
        {
            _settings = settings ?? (() => CoreSettings.HasProvider ? CoreSettings.Service?.Current : null);
            _http = V2AuthService.Configure(new HttpClient(handler ?? new ServerClockHandler()));
            if (handler != null) Unauthorized = _ => Task.FromResult(false);
        }
        public async Task<(bool Success, string? Error)> PurchaseSkillAsync(string skillId)
        {
            var settings = _settings();
            if (settings == null) return (false, Loc.Get("skill_err_settings_unavailable"));
            if (!SkillTreeRules.CanPurchaseSkill(settings, skillId)) return (false, Loc.Get("skill_err_cannot_purchase"));
            var skill = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
            if (skill == null) return (false, Loc.Get("skill_err_unknown"));
            if (string.IsNullOrEmpty(settings.UnifiedId)) return (false, Loc.Get("skill_err_login_required"));
            var (success, error) = await RequestAsync(settings, skillId, afterSync: false).ConfigureAwait(true);
            if (!success) return (false, error);
            ApplySkillEffects(settings, skillId);
            try { SkillUnlocked?.Invoke(this, skillId); }
            catch (Exception ex) { Log.Debug("SkillUnlocked observer failed: {E}", ex.Message); }
            return (true, null);
        }
        internal void ApplySkillEffects(AppSettings settings, string skillId)
        {
            switch (skillId)
            {
                case "good_girl_streak":
                    settings.StreakShieldsRemaining = 1;
                    settings.LastStreakShieldResetDate = DateTime.UtcNow.Date;
                    break;
                case "oopsie_insurance":
                    settings.StreakFixCharges++;
                    Save();
                    break;
            }
        }
        internal static void ApplyPurchaseResult(AppSettings settings, int? serverSkillPoints, List<string>? serverSkills)
        {
            if (serverSkillPoints.HasValue) settings.SkillPoints = serverSkillPoints.Value;
            if (serverSkills != null)
            {
                var merged = new HashSet<string>(settings.UnlockedSkills ?? new List<string>());
                foreach (var skill in serverSkills) merged.Add(skill);
                settings.UnlockedSkills = merged.ToList();
            }
        }
        private async Task<(bool success, string? error)> RequestAsync(AppSettings settings, string skillId, bool afterSync)
        {
            var unifiedId = settings.UnifiedId;
            if (string.IsNullOrEmpty(unifiedId))
                return (false, "Purchasing enhancements requires a cloud account. Please log in first.");
            try
            {
                var requestBody = JsonConvert.SerializeObject(new
                {
                    unified_id = unifiedId,
                    skill_id = skillId,
                    skill_points = settings.SkillPoints,
                });
                async Task<HttpResponseMessage> SendOnce()
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/v2/user/purchase-skill");
                    var token = settings.AuthToken;   // read per send: the retry carries the recovered token
                    if (!string.IsNullOrEmpty(token)) request.Headers.Add("X-Auth-Token", token);
                    request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                    return await _http.SendAsync(request).ConfigureAwait(true);
                }
                using var first = await SendOnce().ConfigureAwait(true);
                var response = first;
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
                if (!string.Equals(settings.UnifiedId, unifiedId, StringComparison.Ordinal))
                    return (false, Loc.Get("skill_err_login_required"));
                if (!response.IsSuccessStatusCode)
                {
                    if (V2AuthService.MergedRecovery is { } merged && await merged(response, json).ConfigureAwait(true))
                        return (false, Loc.Get("account_merged_retry_hint"));
                    if (await Unauthorized(response).ConfigureAwait(true) && !string.IsNullOrEmpty(settings.AuthToken)
                        && string.Equals(settings.UnifiedId, unifiedId, StringComparison.Ordinal))
                    {
                        response = await SendOnce().ConfigureAwait(true);
                        json = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
                        if (!string.Equals(settings.UnifiedId, unifiedId, StringComparison.Ordinal))
                            return (false, Loc.Get("skill_err_login_required"));
                    }
                }
                if (!response.IsSuccessStatusCode)
                {
                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        return (false, $"Your session has expired. Open ⚙️ {Loc.Get("nav_door_settings")} → Account and sign in again to purchase skills.");
                    }
                    string errorMsg;
                    try { errorMsg = JsonConvert.DeserializeObject<PurchaseSkillResponse>(json)?.Error ?? $"Server error: {response.StatusCode}"; }
                    catch { errorMsg = $"Server error: {response.StatusCode}"; }
                    return (false, errorMsg);
                }
                var result = JsonConvert.DeserializeObject<PurchaseSkillResponse>(json);
                if (result == null) return (false, "Invalid server response");
                if (!result.Success)
                {
                    var refused = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
                    if (refused != null)
                    {
                        var step = SparklePoints.AfterBalanceRefusal(settings.SkillPoints, result.SkillPoints, refused.Cost, afterSync);
                        if (step == SparklePoints.RefusalStep.SyncAndRetry)
                        {
                            if (SyncBeforeRetry is { } sync && await sync().ConfigureAwait(true))
                            {
                                if (!string.Equals(settings.UnifiedId, unifiedId, StringComparison.Ordinal))
                                    return (false, Loc.Get("skill_err_login_required"));
                                return await RequestAsync(settings, skillId, afterSync: true).ConfigureAwait(true);
                            }
                            return (false, result.Error ?? "Purchase failed");
                        }
                        var adopted = SparklePoints.AdoptAfterRefusal(settings.SkillPoints, result.SkillPoints, refused.Cost);
                        if (adopted.HasValue)
                        {
                            settings.SkillPoints = adopted.Value;
                            Save();
                        }
                    }
                    return (false, result.Error ?? "Purchase failed");
                }
                ApplyPurchaseResult(settings, result.SkillPoints, result.UnlockedSkills);
                var bought = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
                if (bought != null)
                {
                    PointsSpent?.Invoke(bought.Cost);
                    if (bought.Cost > 0 && string.IsNullOrEmpty(settings.SeasonStatsSeason))
                        settings.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(settings, DateTime.UtcNow);
                    settings.SeasonPointsSpent += bought.Cost;
                }
                if (result.LifetimePointsSpent.HasValue) LifetimeSpentReconciled?.Invoke(result.LifetimePointsSpent.Value);
                Save();
                return (true, null);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Skill purchase request failed");
                return (false, "Connection failed. Please check your internet connection.");
            }
        }
        private sealed class PurchaseSkillResponse
        {
            [JsonProperty("success")] public bool Success { get; set; }
            [JsonProperty("error")] public string? Error { get; set; }
            [JsonProperty("skill_points")] public int? SkillPoints { get; set; }
            [JsonProperty("unlocked_skills")] public List<string>? UnlockedSkills { get; set; }
            [JsonProperty("lifetime_points_spent")] public long? LifetimePointsSpent { get; set; }
        }
    }
}
