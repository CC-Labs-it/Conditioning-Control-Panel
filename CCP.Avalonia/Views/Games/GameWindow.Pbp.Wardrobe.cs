using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.PieceByPiece;
using ConditioningControlPanel.Services.PieceByPiece.Wardrobe;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    // WPF PieceByPieceHostService.Wardrobe.cs:45-74, 118-170. The counter door is excluded by B3.
    private readonly CancellationTokenSource _pbpEquipLifetime = new();
    private readonly SemaphoreSlim _pbpEquipWrites = new(1, 1);
    internal Task PbpEquipPending { get; private set; } = Task.CompletedTask;

    private static string[] PbpChessGrants() => PbpLookRules.Catalog.Values.SelectMany(v => v)
        .Select(PbpLookRules.GrantOf).Where(PrizeOwnership.IsGranted).ToArray();

    private void OnPbpGrantsChanged() => Dispatcher.UIThread.Post(() =>
    {
        if (!_closing && _pbpOpen && IsReady) PostPbpWardrobe();
    });

    private void PostPbpWardrobe()
    {
        if (_closing || !_pbpOpen) return;
        var grants = PbpChessGrants();
        Post(PbpLookRules.OwnedFrame(grants));
        Post(PbpLookRules.LookFrame(PbpLookRules.Clamp(PbpLookRules.Read(CoreSettings.Current.ChessLook).Look, grants)));
    }

    private void PickPbpLook(JObject message)
    {
        var s = CoreSettings.Current;
        var look = PbpLookRules.Clamp(PbpLookRules.Normal(message["look"]), PbpChessGrants());
        if (PbpLookRules.Read(s.ChessLook).Look == look && !string.IsNullOrEmpty(s.ChessLook)) return;
        s.ChessLook = PbpLookRules.Write(look, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        try { CoreSettings.Service?.Save(); } catch (Exception ex) { Diag.Swallowed(ex); }
        PbpEquipPending = PutPbpEquip(look);
    }

    private HttpRequestMessage PbpEquipRequest(HttpMethod method, string url, string uid, string token, ChessLook look)
    {
        var request = new HttpRequestMessage(method, url + "/v2/equip" +
            (method == HttpMethod.Get ? "?unified_id=" + Uri.EscapeDataString(uid) : ""));
        request.Headers.Add(PbpNetRelay.TokenHeader, token);
        request.Headers.Add(PbpNetRelay.VersionHeader, CoreReleaseContent.AppVersion);
        if (method == HttpMethod.Put)
            request.Content = new StringContent(PbpLookRules.EquipBody(uid, look).ToString(Formatting.None), Encoding.UTF8, "application/json");
        return request;
    }

    private async Task PullPbpEquip()
    {
        var uid = CoreAccount.UnifiedUserId;
        var token = PbpAuthToken();
        var url = PbpServerBase()?.TrimEnd('/');
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(url)) return;
        var ct = _pbpEquipLifetime.Token;
        try
        {
            using var request = PbpEquipRequest(HttpMethod.Get, url, uid, token, ChessLook.Empty);
            using var response = await PbpHttpClient().SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return;
            var (server, at) = PbpLookRules.FromEquip(JObject.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)));
            ChessLook? push = null;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested || _closing || CoreAccount.UnifiedUserId != uid) return;
                var s = CoreSettings.Current;
                var (local, localAt) = PbpLookRules.Read(s.ChessLook);
                var (look, _, shouldPush) = PbpLookRules.Reconcile(local, localAt, server, at);
                if (server != null && at > localAt)
                {
                    s.ChessLook = PbpLookRules.Write(look, at);
                    try { CoreSettings.Service?.Save(); } catch (Exception ex) { Diag.Swallowed(ex); }
                    PostPbpWardrobe();
                }
                else if (shouldPush) push = PbpLookRules.Clamp(look, PbpChessGrants());
            });
            if (push is {} value) await PutPbpEquip(value).ConfigureAwait(false);
        }
        catch (Exception ex) { Log.Debug("PieceByPiece: equip pull kept local look: {E}", ex.Message); }
    }

    private async Task PutPbpEquip(ChessLook look)
    {
        var uid = CoreAccount.UnifiedUserId;
        var token = PbpAuthToken();
        var url = PbpServerBase()?.TrimEnd('/');
        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(url)) return;
        var ct = _pbpEquipLifetime.Token;
        try
        {
            await _pbpEquipWrites.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (CoreAccount.UnifiedUserId != uid) return;
                using var request = PbpEquipRequest(HttpMethod.Put, url, uid, token, look);
                using var response = await PbpHttpClient().SendAsync(request, ct).ConfigureAwait(false);
            }
            finally { _pbpEquipWrites.Release(); }
        }
        catch (Exception ex) { Log.Debug("PieceByPiece: equip push kept local look: {E}", ex.Message); }
    }
}
