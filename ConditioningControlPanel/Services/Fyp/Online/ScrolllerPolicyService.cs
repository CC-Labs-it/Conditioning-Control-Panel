using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Fyp.Online;

/// <summary>
/// Reads the server's Scrolller switch (<c>GET /config/scrolller</c>, CCP-Server) at launch and
/// every 30 minutes and hands it to <see cref="ScrolllerPolicy.Apply"/>. Any failure (offline,
/// HTTP error, a body that does not parse) leaves the policy exactly as it was, so a cold start
/// with no answer keeps the shipped behaviour. <c>min_version</c> is read and kept here, not
/// acted on yet (owner, 2026-10-18).
/// </summary>
internal static class ScrolllerPolicyService
{
    /// <summary>The server's answer, already shaped. Null fields = "no cap" / "no minimum".</summary>
    internal sealed record Answer(bool Enabled, int? MaxClipShortSide, string? MinVersion,
        long? FreeMb = null, long? BasicMb = null, long? PrimeMb = null, long? FreeFirstDayMb = null);

    private static readonly TimeSpan Period = TimeSpan.FromMinutes(30);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static Timer? _timer;
    private static int _inFlight;
    private static volatile string? _minVersion;

    /// <summary>Lowest app version the server still wants talking to Scrolller, or null.
    /// Exposed for a later release; nothing obeys it yet.</summary>
    public static string? MinVersion => _minVersion;

    /// <summary>Starts the launch read and the 30-minute poll. Idempotent. Never throws.</summary>
    public static void Start()
    {
        try
        {
            if (_timer != null) return;
            _timer = new Timer(_ => _ = RefreshAsync(), null, TimeSpan.Zero, Period);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ScrolllerPolicy: poll did not start: {E}", ex.Message);
        }
    }

    /// <summary>One read. Single-flight; never throws.</summary>
    public static async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _inFlight, 1) != 0) return;
        try
        {
            // Offline mode promises no external connections at all.
            if (App.Settings?.Current?.OfflineMode == true) return;
            using var res = await Http.GetAsync($"{BackRoomApi.BaseUrl}/config/scrolller").ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
            {
                App.Logger?.Debug("ScrolllerPolicy: /config/scrolller returned {Status}; policy unchanged", (int)res.StatusCode);
                return;
            }
            var answer = Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (answer == null) return;
            bool was = ScrolllerPolicy.Enabled;
            int capWas = ScrolllerPolicy.MaxClipShortSide;
            Accept(answer);
            if (was != ScrolllerPolicy.Enabled || capWas != ScrolllerPolicy.MaxClipShortSide)
                App.Logger?.Information("ScrolllerPolicy: enabled={Enabled}, clip cap={Cap}",
                    ScrolllerPolicy.Enabled, ScrolllerPolicy.MaxClipShortSide);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("ScrolllerPolicy: read failed ({E}); policy unchanged", ex.Message);
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }

    /// <summary>Body -> answer, or null when the body is not a JSON object (policy unchanged).
    /// Only an explicit <c>enabled: false</c> switches Scrolller off; a cap or version of the
    /// wrong type reads as "none".</summary>
    internal static Answer? Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        JObject o;
        try { o = JObject.Parse(body); }
        catch { return null; }

        bool enabled = !(o["enabled"] is JValue { Type: JTokenType.Boolean } e && !(bool)e);
        int? cap = o["max_clip_short_side"] is JValue { Type: JTokenType.Integer } c
            && (long)c >= 0 && (long)c <= int.MaxValue ? (int)(long)c : null;
        string? ver = o["min_version"] is JValue { Type: JTokenType.String } v
            && !string.IsNullOrWhiteSpace((string?)v) ? ((string)v!).Trim() : null;
        var daily = o["daily_mb"] as JObject;
        return new Answer(enabled, cap, ver, Mb(daily?["free"]), Mb(daily?["basic"]), Mb(daily?["prime"]), Mb(daily?["free_first_day"]));
    }

    /// <summary>A whole, non-negative MB figure, else null (the default stays).</summary>
    private static long? Mb(JToken? t)
        => t is JValue { Type: JTokenType.Integer } v && (long)v >= 0 ? (long)v : null;

    /// <summary>Applies an answer: the switch and cap through <see cref="ScrolllerPolicy"/>
    /// (which clamps the cap), the minimum version here.</summary>
    internal static void Accept(Answer answer)
    {
        ScrolllerPolicy.Apply(answer.Enabled, answer.MaxClipShortSide);
        ScrolllerAllowance.ApplyLimits(answer.FreeMb, answer.BasicMb, answer.PrimeMb, answer.FreeFirstDayMb);
        _minVersion = answer.MinVersion;
    }

    /// <summary>True when the switch is off and <paramref name="url"/> is on scrolller.com or a
    /// subdomain: the native fetchers refuse it as if the network had failed.</summary>
    internal static bool Blocks(string? url)
        => !ScrolllerPolicy.Enabled && global::ConditioningControlPanel.Services.Video.ScrolllerCredit.IsScrolllerUrl(url);

    /// <summary>
    /// May <paramref name="url"/> be fetched FRESH from the network right now? Not a Scrolller
    /// url: always yes. A Scrolller url: only while the switch is on, today's share is not used up
    /// (<see cref="ScrolllerAllowance.Spent"/>) and the user is not away
    /// (<see cref="ScrolllerPolicy.Away"/>). A cached copy (<see cref="ScrolllerDiskCache"/>) needs
    /// only the switch on.
    /// </summary>
    internal static bool MayFetchFresh(string? url)
    {
        if (!global::ConditioningControlPanel.Services.Video.ScrolllerCredit.IsScrolllerUrl(url)) return true;
        return FreshRule(ScrolllerPolicy.Enabled, ScrolllerAllowance.Spent, ScrolllerPolicy.Away);
    }

    /// <summary>The rule behind <see cref="MayFetchFresh"/>, pure.</summary>
    internal static bool FreshRule(bool enabled, bool spent, bool away) => enabled && !spent && !away;
}
