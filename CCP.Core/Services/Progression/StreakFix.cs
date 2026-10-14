using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The Quests tab's "Fix day": spend one streak-fix charge on a missed day. The rules (missed day,
    /// wire date, the local apply after a server yes) are shared by both heads; <see cref="UseAsync"/> is
    /// the Avalonia head's transport for WPF <c>ProfileSyncService.UseOopsieInsuranceAsync</c>
    /// (<c>POST /v2/user/use-oopsie</c>). Charges live on the account: nothing is applied locally unless
    /// the server said yes.
    /// ponytail: no 401 auth recovery (WPF HandleUnauthorizedAsync), as the rest of the Avalonia head;
    /// WPF keeps its own send in ProfileSyncService for that reason.
    /// </summary>
    public sealed class StreakFix
    {
        private const string ServerUrl = "https://codebambi-proxy.vercel.app";
        private readonly HttpClient _http;
        private readonly Func<AppSettings?> _settings;

        /// <param name="settings">Default: the head's settings.</param>
        /// <param name="handler">Test seam; null is the real network.</param>
        public StreakFix(Func<AppSettings?>? settings = null, HttpMessageHandler? handler = null)
        {
            _settings = settings ?? (() => CoreSettings.HasProvider ? CoreSettings.Service?.Current : null);
            _http = V2AuthService.Configure(new HttpClient(handler ?? new ServerClockHandler()));
        }

        /// <summary>The wire date: invariant, so a Buddhist or Umm al-Qura system calendar never writes its
        /// own year onto the wire.</summary>
        public static string WireDate(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        /// <summary>Any day of this month before today without a completed daily.</summary>
        public static bool HasMissedDays(QuestProgress? progress, DateTime today)
        {
            var done = new HashSet<DateTime>(
                progress?.DailyQuestCompletionDates?.Select(d => d.Date) ?? Enumerable.Empty<DateTime>());
            return Enumerable.Range(1, today.Day - 1).Select(d => new DateTime(today.Year, today.Month, d)).Any(d => !done.Contains(d));
        }

        /// <summary><c>credits</c> is the balance the server holds after the spend (null from an older server).</summary>
        public async Task<(bool success, string? error, int? credits)> UseAsync(DateTime day)
        {
            var settings = _settings();
            var unifiedId = settings?.UnifiedId;
            if (settings == null || string.IsNullOrEmpty(unifiedId))
                return (false, "Oopsie Insurance requires a cloud account. Please log in first.", null);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{ServerUrl}/v2/user/use-oopsie");
                var token = settings.AuthToken;
                if (!string.IsNullOrEmpty(token)) request.Headers.Add("X-Auth-Token", token);
                request.Content = new StringContent(
                    JsonConvert.SerializeObject(new { unified_id = unifiedId, fix_date = WireDate(day) }), Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(request).ConfigureAwait(true);
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(true);
                if (!response.IsSuccessStatusCode)
                {
                    if (V2AuthService.MergedRecovery is { } merged && await merged(response, json).ConfigureAwait(true))
                        return (false, Loc.Get("account_merged_retry_hint"), null);
                    string? error = null;
                    try { error = JsonConvert.DeserializeObject<Reply>(json)?.Error; } catch (JsonException) { } // swallow: a non-JSON body reads as the status line
                    error ??= $"Server error: {response.StatusCode}";
                    Log.Warning("Oopsie insurance failed: {Error}", error);
                    return (false, error, null);
                }
                Reply? result = null;
                try { result = JsonConvert.DeserializeObject<Reply>(json); } catch (JsonException) { } // swallow: an older server may answer without a body
                Log.Information("Oopsie insurance used via server: {Credits} charge(s) left", result?.OopsieCredits);
                return (true, null, result?.OopsieCredits);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Oopsie insurance request failed");
                return (false, $"Connection failed: {ex.Message}", null);
            }
        }

        /// <summary>The local half after the server said yes: the server's balance (else one off ours), the day
        /// added once to the completion dates, the streak recounted. XP is deliberately not touched: the fix is
        /// free, and the server's echoed total can be behind this device.</summary>
        public static void ApplySpent(AppSettings settings, QuestService? quests, DateTime day, int? credits)
        {
            settings.StreakFixCharges = credits ?? Math.Max(0, settings.StreakFixCharges - 1);
            settings.SeasonalStreakRecoveryUsed = true;   // back-compat flag, no longer a gate
            if (quests?.Progress != null && !quests.Progress.DailyQuestCompletionDates.Any(d => d.Date == day.Date))
            {
                quests.Progress.DailyQuestCompletionDates.Add(day.Date);
                quests.Save();
            }
            quests?.RecalculateStreak();
        }

        private sealed class Reply
        {
            [JsonProperty("error")] public string? Error { get; set; }
            [JsonProperty("oopsie_credits")] public int? OopsieCredits { get; set; }
        }
    }
}
