using System;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Vault;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    // WPF FunnelPageMessage.cs:23-96 and FunnelBeacon.cs:19-92: the page names a plan, never a URL.
    private static readonly Regex FunnelField = new("^[a-z0-9_]{1,32}$", RegexOptions.CultureInvariant);
    private static readonly HttpClient FunnelHttp = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static readonly object FunnelLock = new();
    internal static DateTime FunnelLastOpenUtc = DateTime.MinValue;
    internal static Action<string> FunnelOpen = url => ExternalOpener.Open(url);
    internal static Action<string, string> FunnelSend = (url, body) => _ = Task.Run(async () =>
    {
        try
        {
            using var content = new StringContent(body, Encoding.UTF8, "text/plain");
            using var response = await FunnelHttp.PostAsync(url, content).ConfigureAwait(false);
        }
        catch (Exception ex) { Log.Debug("[Funnel] beacon failed: {E}", ex.Message); }
    });

    private static string FunnelString(JObject message, string key) =>
        message[key]?.Type == JTokenType.String ? (string?)message[key] ?? "" : "";

    private static string CleanFunnelField(string value)
    {
        var field = value.Trim().ToLowerInvariant().Replace('-', '_');
        return FunnelField.IsMatch(field) ? field : "";
    }

    private void HandleBreakoutFunnel(JObject message)
    {
        try
        {
            var evt = FunnelString(message, "event");
            if (evt is not ("gate_shown" or "cta_clicked" or "checkout_started" or "ref")) return;
            var a = CleanFunnelField(FunnelString(message, "a"));
            var b = CleanFunnelField(FunnelString(message, "b"));
            string? url = null;
            if (evt == "cta_clicked" && PlanCatalog.Current.ForKey(FunnelString(message, "plan")) is { } plan)
            {
                if (b is not ("web" or "substar")) b = "patreon";
                url = b switch { "web" => PlanCatalog.WebUrlFor(plan, "panel"), "substar" => plan.SubstarUrl, _ => plan.PatreonUrl };
            }

            // WPF FunnelBeacon: allowlisted, anonymous, and silent in offline mode.
            if (!CoreSettings.Current.OfflineMode && !string.IsNullOrWhiteSpace(BackRoomApi.BaseUrl))
            {
                var body = evt + "|panel";
                if (a.Length > 0 || b.Length > 0) body += "|" + a;
                if (b.Length > 0) body += "|" + b;
                try { FunnelSend(BackRoomApi.BaseUrl.TrimEnd('/') + "/v2/public/funnel", body); }
                catch (Exception ex) { Log.Debug("[Funnel] beacon failed: {E}", ex.Message); }
            }
            // WPF BackRoomBridge.cs:245-249 counts after exit but never opens a checkout.
            if (url == null || _closing || _shellExiting || _breakoutMinimised || WindowState == global::Avalonia.Controls.WindowState.Minimized || !TakeFunnelOpenSlot(DateTime.UtcNow)) return;
            AccountSeed.NoteCheckoutOpened(url);
            FunnelOpen(url);
        }
        catch (Exception ex) { Log.Debug("[Funnel] page message failed: {E}", ex.Message); }
    }

    internal static bool TakeFunnelOpenSlot(DateTime now)
    {
        lock (FunnelLock)
        {
            if (now >= FunnelLastOpenUtc && now - FunnelLastOpenUtc < TimeSpan.FromSeconds(3)) return false;
            FunnelLastOpenUtc = now;
            return true;
        }
    }
}
