using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Tc;

// ============================ save carry ============================
//
// One save string per account on the server (CCP-Server proxy/tc-save.js): save-offer -> PUT,
// save-ask -> GET. The host builds the path and body (TcSaveRules), signs as the player with the
// desktop door (unified_id + X-Auth-Token), and answers the page with save-result or save-incoming.
// Nothing is written to disk, nothing here touches the Sparkle wallet, and the page applies an incoming save only
// after the player accepts its card.
internal static partial class TcHostService
{
    private const string ProxyBaseUrl = "https://codebambi-proxy.vercel.app";

    private static readonly HttpClient SaveHttp = BuildSaveHttp();

    private static HttpClient BuildSaveHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        try { c.DefaultRequestHeaders.UserAgent.ParseAdd($"ConditioningControlPanel/{UpdateService.AppVersion}"); }
        catch (Exception ex) { Diag.Swallowed(ex); }
        return c;
    }

    /// <summary><c>caps.saveSlot</c>: true only for a signed-in account (the slot is keyed on it).</summary>
    private static bool SaveSlotOpen => TcSaveRules.SignedIn(App.UnifiedUserId, SafeAuthToken());

    private static string SafeAuthToken()
    {
        try { return App.Settings?.Current?.AuthToken ?? string.Empty; }
        catch { return string.Empty; }
    }

    /// <summary><c>save-offer</c> (already within the gate's size caps) -&gt; PUT -&gt; <c>save-result</c>.</summary>
    private static void OnSaveOffer(TcPageFrame f)
    {
        var uid = App.UnifiedUserId;
        var token = SafeAuthToken();
        if (!TcSaveRules.SignedIn(uid, token)) { PostSave(TcSaveRules.Result(false, TcSaveRules.ReasonSignIn)); return; }
        var body = TcSaveRules.PutBody(uid!, f.Save ?? "");
        _ = SendSaveAsync(HttpMethod.Put, TcSaveRules.Path, body, token, TcSaveRules.MapPut);
    }

    /// <summary><c>save-ask</c> -&gt; GET -&gt; <c>save-incoming</c> or <c>save-result</c>.</summary>
    private static void OnSaveAsk()
    {
        var uid = App.UnifiedUserId;
        var token = SafeAuthToken();
        if (!TcSaveRules.SignedIn(uid, token)) { PostSave(TcSaveRules.Result(false, TcSaveRules.ReasonSignIn)); return; }
        _ = SendSaveAsync(HttpMethod.Get, TcSaveRules.GetPath(uid!), null, token, TcSaveRules.MapGet);
    }

    private static async Task SendSaveAsync(HttpMethod method, string path, string? body, string token, Func<int, string?, JObject> map)
    {
        int status = 0;
        string? reply = null;
        try
        {
            using var request = new HttpRequestMessage(method, new Uri(new Uri(ProxyBaseUrl), path));
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            request.Headers.Add(PieceByPiece.PbpNetRelay.TokenHeader, token);
            request.Headers.Add(PieceByPiece.PbpNetRelay.VersionHeader, UpdateService.AppVersion);
            using var response = await SaveHttp.SendAsync(request, CancellationToken.None).ConfigureAwait(false);
            status = (int)response.StatusCode;
            reply = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            MergedAccountRecovery.TryHandle(status, reply);
        }
        catch (Exception ex)
        {
            status = 0;
            App.Logger?.Debug("Tc: save {Method} failed: {E}", method.Method, ex.Message);
        }
        var frame = map(status, reply);
        App.Logger?.Information("Tc: save {Method} -> {Status} {Type} {Reason}", method.Method, status,
            (string?)frame["type"], (string?)frame["reason"] ?? "");
        PostSave(frame);
    }

    private static void PostSave(JObject frame) => RunOnUi(() =>
    {
        try { _host?.Post(frame); }
        catch (Exception ex) { App.Logger?.Debug("Tc: save post failed: {E}", ex.Message); }
    });
}
