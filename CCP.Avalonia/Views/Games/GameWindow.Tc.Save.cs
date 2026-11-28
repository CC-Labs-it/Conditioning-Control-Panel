// PORTED from ConditioningControlPanel/Services/Tc/TcHostService.Save.cs (main b523af076d, e7cf5a7ccc): the T&C save
// carry. One save string per account on the server (CCP-Server proxy/tc-save.js): save-offer -> PUT, save-ask -> GET.
// The host builds the path and body (Core TcSaveRules), signs as the player with the desktop door (unified_id +
// X-Auth-Token) and answers with save-result or save-incoming. Offline mode answers offline before any request is
// built. Nothing is written to disk, the Sparkle wallet is never touched, and the page applies an incoming save only
// after the player accepts its card. Transport and base url are the Pbp seams: a sandbox with no loopback has no base
// url and every call answers offline.
using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.PieceByPiece;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        /// <summary>Test seam: the last save call; done once its answer is queued on the UI thread.</summary>
        internal Task TcSavePending { get; private set; } = Task.CompletedTask;

        private static bool TcOfflineMode()
        {
            try { return CoreSettings.Current.OfflineMode; } catch { return false; }
        }

        /// <summary>WPF SaveSlotOpen: <c>caps.saveSlot</c> needs a signed-in account and Offline mode off.</summary>
        private static bool TcSaveSlotOpen => TcSaveRules.SlotOpen(CoreAccount.UnifiedUserId, PbpAuthToken(), TcOfflineMode());

        /// <summary>WPF OnSaveOffer: the save (already within the gate's caps) goes out as a PUT.</summary>
        private void OnTcSaveOffer(TcPageFrame f)
        {
            var uid = CoreAccount.UnifiedUserId;
            var token = PbpAuthToken();
            if (TcSaveRules.RefuseBeforeSend(uid, token, TcOfflineMode()) is { } refused) { PostTcSave(refused); return; }
            TcSavePending = SendTcSaveAsync(HttpMethod.Put, TcSaveRules.Path, TcSaveRules.PutBody(uid!, f.Save ?? ""), token, TcSaveRules.MapPut);
        }

        /// <summary>WPF OnSaveAsk: a GET for this account's slot.</summary>
        private void OnTcSaveAsk()
        {
            var uid = CoreAccount.UnifiedUserId;
            var token = PbpAuthToken();
            if (TcSaveRules.RefuseBeforeSend(uid, token, TcOfflineMode()) is { } refused) { PostTcSave(refused); return; }
            TcSavePending = SendTcSaveAsync(HttpMethod.Get, TcSaveRules.GetPath(uid!), null, token, TcSaveRules.MapGet);
        }

        private async Task SendTcSaveAsync(HttpMethod method, string path, string? body, string token, Func<int, string?, JObject> map)
        {
            int status = 0;
            string? reply = null;
            var baseUrl = PbpServerBase();
            var http = PbpHttpClient();
            try
            {
                if (!string.IsNullOrEmpty(baseUrl))
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));   // WPF SaveHttp timeout
                    using var request = new HttpRequestMessage(method, new Uri(new Uri(baseUrl), path));
                    if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    request.Headers.Add(PbpNetRelay.TokenHeader, token);
                    request.Headers.Add(PbpNetRelay.VersionHeader, CoreReleaseContent.AppVersion);
                    using var response = await http.SendAsync(request, cts.Token).ConfigureAwait(false);
                    status = (int)response.StatusCode;
                    reply = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    MergedAccountRecovery.TryHandle(status, reply);
                }
            }
            catch (Exception ex)
            {
                status = 0;
                Log.Debug("Tc: save {Method} failed: {E}", method.Method, ex.Message);
            }
            var frame = map(status, reply);
            Log.Information("Tc: save {Method} -> {Status} {Type} {Reason}", method.Method, status,
                (string?)frame["type"], (string?)frame["reason"] ?? "");
            PostTcSave(frame);
        }

        /// <summary>WPF PostSave: on the UI thread, dropped once the window is closing (WPF's _host is gone).</summary>
        private void PostTcSave(JObject frame) => Dispatcher.UIThread.Post(() =>
        {
            if (_tcOpen && !_closing) Post(frame);
        });
    }
}
