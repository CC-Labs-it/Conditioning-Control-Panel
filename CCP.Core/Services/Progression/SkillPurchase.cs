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
namespace ConditioningControlPanel.Services;
// WPF SkillTreeService.cs:80-141; ProfileSyncService.cs:3936-4124; SeasonRecapService.cs:122,200.
public sealed class SkillPurchase {
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
    public SkillPurchase(Func<AppSettings?>? settings = null, HttpMessageHandler? handler = null) {
        _settings = settings ?? (() => CoreSettings.HasProvider ? CoreSettings.Service?.Current : null);
        _http = V2AuthService.Configure(new HttpClient(handler ?? new ServerClockHandler()));
        if (handler != null) Unauthorized = _ => Task.FromResult(false);
    }
    public async Task<(bool Success, string? Error)> PurchaseSkillAsync(string skillId) {
        var settings = _settings();
        if (settings == null) return (false, Loc.Get("skill_err_settings_unavailable"));
        if (!SkillTreeRules.CanPurchaseSkill(settings, skillId)) return (false, Loc.Get("skill_err_cannot_purchase"));
        var skill = SkillDefinition.All.First(s => s.Id == skillId);
        if (string.IsNullOrEmpty(settings.UnifiedId)) return (false, Loc.Get("skill_err_login_required"));
        var (success, error) = await RequestAsync(settings, skill, afterSync: false).ConfigureAwait(true);
        if (!success) return (false, error);
        ApplySkillEffects(settings, skillId);
        try { SkillUnlocked?.Invoke(this, skillId); }
        catch (Exception ex) { Log.Debug("SkillUnlocked observer failed: {E}", ex.Message); }
        return (true, null);
    }
    internal void ApplySkillEffects(AppSettings settings, string skillId) {
        if (skillId == "good_girl_streak")
        {
            settings.StreakShieldsRemaining = 1;
            settings.LastStreakShieldResetDate = DateTime.UtcNow.Date;
        }
        if (skillId == "oopsie_insurance") { settings.StreakFixCharges++; Save(); }
    }
    internal static void ApplyPurchaseResult(AppSettings settings, int? serverSkillPoints, List<string>? serverSkills) {
        if (serverSkillPoints.HasValue) settings.SkillPoints = serverSkillPoints.Value;
        if (serverSkills == null) return;
        settings.UnlockedSkills = (settings.UnlockedSkills ?? new List<string>()).Union(serverSkills).ToList();
    }
    private async Task<(bool success, string? error)> RequestAsync(AppSettings settings, SkillDefinition skill, bool afterSync) {
        var unifiedId = settings.UnifiedId;
        if (string.IsNullOrEmpty(unifiedId)) return (false, Loc.Get("skill_err_login_required"));
        try
        {
            var requestBody = JsonConvert.SerializeObject(new { unified_id = unifiedId, skill_id = skill.Id, skill_points = settings.SkillPoints });
            async Task<HttpResponseMessage> SendOnce() {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/v2/user/purchase-skill");
                var token = settings.AuthToken; // Re-read after recovery.
                if (!string.IsNullOrEmpty(token)) request.Headers.Add("X-Auth-Token", token);
                request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
                return await _http.SendAsync(request).ConfigureAwait(true);
            }
            using var first = await SendOnce().ConfigureAwait(true);
            var response = first;
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
            if (!string.Equals(settings.UnifiedId, unifiedId, StringComparison.Ordinal)) return (false, Loc.Get("skill_err_login_required"));
            if (!response.IsSuccessStatusCode)
            {
                if (V2AuthService.MergedRecovery is { } merged && await merged(response, json).ConfigureAwait(true))
                    return (false, Loc.Get("account_merged_retry_hint"));
                if (await Unauthorized(response).ConfigureAwait(true) && !string.IsNullOrEmpty(settings.AuthToken)
                    && string.Equals(settings.UnifiedId, unifiedId, StringComparison.Ordinal))
                {
                    response = await SendOnce().ConfigureAwait(true);
                    json = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
                    if (!string.Equals(settings.UnifiedId, unifiedId, StringComparison.Ordinal)) return (false, Loc.Get("skill_err_login_required"));
                }
            }
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized) return (false, Loc.Get("catalogue_toast_auth_failed"));
                try { return (false, JsonConvert.DeserializeObject<PurchaseSkillResponse>(json)?.Error ?? Loc.GetF("skill_err_server", response.StatusCode)); }
                catch { return (false, Loc.GetF("skill_err_server", response.StatusCode)); }
            }
            var result = JsonConvert.DeserializeObject<PurchaseSkillResponse>(json);
            if (result == null) return (false, Loc.Get("skill_err_invalid_response"));
            if (!result.Success)
            {
                var step = SparklePoints.AfterBalanceRefusal(settings.SkillPoints, result.SkillPoints, skill.Cost, afterSync);
                if (step == SparklePoints.RefusalStep.SyncAndRetry)
                {
                    if (SyncBeforeRetry is { } sync && await sync().ConfigureAwait(true))
                    {
                        if (!string.Equals(settings.UnifiedId, unifiedId, StringComparison.Ordinal)) return (false, Loc.Get("skill_err_login_required"));
                        return await RequestAsync(settings, skill, afterSync: true).ConfigureAwait(true);
                    }
                    return (false, result.Error ?? Loc.Get("dialog_purchase_failed"));
                }
                var adopted = SparklePoints.AdoptAfterRefusal(settings.SkillPoints, result.SkillPoints, skill.Cost);
                if (adopted.HasValue) { settings.SkillPoints = adopted.Value; Save(); }
                return (false, result.Error ?? Loc.Get("dialog_purchase_failed"));
            }
            ApplyPurchaseResult(settings, result.SkillPoints, result.UnlockedSkills);
            PointsSpent?.Invoke(skill.Cost);
            if (string.IsNullOrEmpty(settings.SeasonStatsSeason)) settings.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(settings, DateTime.UtcNow);
            settings.SeasonPointsSpent += skill.Cost;
            if (result.LifetimePointsSpent.HasValue) LifetimeSpentReconciled?.Invoke(result.LifetimePointsSpent.Value);
            Save();
            return (true, null);
        }
        catch (Exception ex) { Log.Error(ex, "Skill purchase request failed"); return (false, Loc.Get("skill_err_connection")); }
    }
    private sealed class PurchaseSkillResponse {
        [JsonProperty("success")] public bool Success { get; set; }
        [JsonProperty("error")] public string? Error { get; set; }
        [JsonProperty("skill_points")] public int? SkillPoints { get; set; }
        [JsonProperty("unlocked_skills")] public List<string>? UnlockedSkills { get; set; }
        [JsonProperty("lifetime_points_spent")] public long? LifetimePointsSpent { get; set; }
    }
}
