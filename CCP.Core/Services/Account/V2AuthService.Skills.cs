using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services;

public partial class V2AuthService
{
    // WPF ProfileSyncService.cs:3976-4126. Server authority, genuine auth recovery and one
    // balance sync retry; failed requests never spend locally.
    public async Task<(bool Success, string? Error, long? LifetimeSpent)> PurchaseSkillAsync(
        string skillId, Func<Task<bool>> syncBeforeRetry)
    {
        var s = _settings();
        if (s == null || !SkillTreeRules.CanPurchaseSkill(s, skillId))
            return (false, Loc.Get("skill_err_cannot_purchase"), null);
        if (string.IsNullOrEmpty(s.UnifiedId)) return (false, Loc.Get("skill_err_login_required"), null);
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                string body = new JObject { ["unified_id"] = s.UnifiedId, ["skill_id"] = skillId, ["skill_points"] = s.SkillPoints }.ToString();
                HttpRequestMessage Request()
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, $"{SERVER_URL}/v2/user/purchase-skill");
                    AddAuthHeader(request);
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    return request;
                }
                using var request = Request();
                var response = await _http.SendAsync(request);
                var json = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    if (await TryHandleMergedAsync(response, json)) return (false, Loc.Get("account_merged_retry_hint"), null);
                    if (await AuthRecovery.HandleUnauthorizedAsync(response) && !string.IsNullOrEmpty(_settings()?.AuthToken))
                    {
                        using var retry = Request();
                        response.Dispose();
                        response = await _http.SendAsync(retry);
                        json = await response.Content.ReadAsStringAsync();
                    }
                }
                using (response)
                {
                    // A sign-out/account switch while awaiting the server cannot mutate another wallet.
                    if (!ReferenceEquals(s, _settings()) || s.UnifiedId != (string?)JObject.Parse(body)["unified_id"])
                        return (false, Loc.Get("skill_err_login_required"), null);
                    if (!response.IsSuccessStatusCode) return (false, ServerError(json, response.StatusCode), null);
                    var result = JObject.Parse(json);
                    int? points = (int?)result["skill_points"];
                    if ((bool?)result["success"] != true)
                    {
                        var skill = SkillDefinition.All.First(x => x.Id == skillId);
                        if (points.HasValue)
                        {
                            if (SparklePoints.AfterBalanceRefusal(s.SkillPoints, points.Value, skill.Cost, attempt > 0)
                                == SparklePoints.RefusalStep.SyncAndRetry)
                            {
                                if (await syncBeforeRetry()) continue;
                                return (false, (string?)result["error"], null);
                            }
                            var adopted = SparklePoints.AdoptAfterRefusal(s.SkillPoints, points.Value, skill.Cost);
                            if (adopted.HasValue) { s.SkillPoints = adopted.Value; CoreSettings.Save(); }
                        }
                        return (false, (string?)result["error"], null);
                    }
                    if (points.HasValue) s.SkillPoints = points.Value;
                    if (result["unlocked_skills"] is JArray skills)
                        s.UnlockedSkills = s.UnlockedSkills.Union(skills.Values<string>().OfType<string>()).ToList();
                    CoreSettings.Save();
                    return (true, null, (long?)result["lifetime_points_spent"]);
                }
            }
        }
        catch (Exception) { return (false, Loc.Get("login_device_lost"), null); }
        return (false, Loc.Get("skill_err_cannot_purchase"), null);
    }
}
