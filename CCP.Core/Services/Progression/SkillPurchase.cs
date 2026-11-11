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
// WPF SkillTreeService.cs:80; ProfileSyncService.cs:3936-4110; SeasonRecapService.cs:200.
public sealed class SkillPurchase
{
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
        var (success, error) = await RequestAsync(settings, skill, afterSync: false).ConfigureAwait(true);
        if (!success) return (false, error);
        ApplySkillEffects(settings, skillId);
        try { SkillUnlocked?.Invoke(this, skillId); }
        catch (Exception ex) { Log.Debug("SkillUnlocked observer failed: {E}", ex.Message); }
        return (true, null);
    }
    internal void ApplySkillEffects(AppSettings settings, string skillId)
    {
        if (skillId == "good_girl_streak")
            (settings.StreakShieldsRemaining, settings.LastStreakShieldResetDate) = (1, DateTime.UtcNow.Date);
        else if (skillId == "oopsie_insurance") { settings.StreakFixCharges++; Save(); }
    }
    private async Task<(bool Success, string? Error)> RequestAsync(AppSettings settings, SkillDefinition skill, bool afterSync)
    {
        var id = settings.UnifiedId;
        if (string.IsNullOrEmpty(id)) return (false, Loc.Get("skill_err_login_required"));
        try
        {
            var body = JsonConvert.SerializeObject(new { unified_id = id, skill_id = skill.Id, skill_points = settings.SkillPoints });
            async Task<HttpResponseMessage> SendOnce()
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://codebambi-proxy.vercel.app/v2/user/purchase-skill");
                if (!string.IsNullOrEmpty(settings.AuthToken)) request.Headers.Add("X-Auth-Token", settings.AuthToken);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                return await _http.SendAsync(request);
            }
            using var first = await SendOnce();
            var (response, json) = (first, await first.Content.ReadAsStringAsync());
            if (settings.UnifiedId != id) return (false, Loc.Get("skill_err_login_required"));
            if (!response.IsSuccessStatusCode && V2AuthService.MergedRecovery is { } merged && await merged(response, json)) return (false, Loc.Get("account_merged_retry_hint"));
            if (!response.IsSuccessStatusCode && await Unauthorized(response) && !string.IsNullOrEmpty(settings.AuthToken) && settings.UnifiedId == id) response = await SendOnce();
            using var recovered = ReferenceEquals(response, first) ? null : response;
            if (!ReferenceEquals(response, first)) json = await response.Content.ReadAsStringAsync();
            if (settings.UnifiedId != id) return (false, Loc.Get("skill_err_login_required"));
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized) return (false, Loc.GetF("skill_err_purchase_session_expired", Loc.Get("nav_door_settings")));
                var error = Loc.Get("dialog_purchase_failed") + $" ({(int)response.StatusCode})";
                try { error = JsonConvert.DeserializeObject<PurchaseSkillResponse>(json)?.Error ?? error; }
                catch (JsonException) { }
                return (false, error);
            }
            if (JsonConvert.DeserializeObject<PurchaseSkillResponse>(json) is not { } result) return (false, Loc.Get("skill_err_invalid_response"));
            if (!result.Success)
            {
                if (SparklePoints.AfterBalanceRefusal(settings.SkillPoints, result.SkillPoints, skill.Cost, afterSync) == SparklePoints.RefusalStep.SyncAndRetry)
                {
                    if (SyncBeforeRetry is { } sync && await sync())
                        return settings.UnifiedId == id ? await RequestAsync(settings, skill, afterSync: true) : (false, Loc.Get("skill_err_login_required"));
                    return (false, result.Error ?? Loc.Get("dialog_purchase_failed"));
                }
                if (SparklePoints.AdoptAfterRefusal(settings.SkillPoints, result.SkillPoints, skill.Cost) is { } adopted) { settings.SkillPoints = adopted; Save(); }
                return (false, result.Error ?? Loc.Get("dialog_purchase_failed"));
            }
            if (result.SkillPoints.HasValue) settings.SkillPoints = result.SkillPoints.Value;
            if (result.UnlockedSkills != null) settings.UnlockedSkills = (settings.UnlockedSkills ?? new List<string>()).Union(result.UnlockedSkills).ToList();
            PointsSpent?.Invoke(skill.Cost);
            if (skill.Cost > 0 && string.IsNullOrEmpty(settings.SeasonStatsSeason)) settings.SeasonStatsSeason = EnhancementAnalytics.CurrentSeasonKey(settings, DateTime.UtcNow);
            settings.SeasonPointsSpent += skill.Cost;
            if (result.LifetimePointsSpent.HasValue) LifetimeSpentReconciled?.Invoke(result.LifetimePointsSpent.Value);
            Save();
            return (true, null);
        }
        catch (Exception ex) { Log.Error(ex, "Skill purchase request failed"); return (false, Loc.Get("wizard_connect_failed")); }
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
