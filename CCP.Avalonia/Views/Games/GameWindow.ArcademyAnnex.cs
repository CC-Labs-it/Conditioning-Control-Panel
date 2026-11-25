// The annex registry link. WPF ArcademyHostService.cs:663-667, 5440-5520.
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    internal const string ArcademyAnnexUrl = "https://codebambi-proxy.vercel.app/v2/arcademy/annex/stats";
    private static readonly TimeSpan ArcademyAnnexTimeout = TimeSpan.FromSeconds(6);
    private static readonly HttpClient ArcademyAnnexHttp = new() { Timeout = ArcademyAnnexTimeout };
    /// <summary>Tests only: the request goes through here. Public aggregate: no token is ever attached.</summary>
    internal static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> ArcademyAnnexSend =
        (request, ct) => ArcademyAnnexHttp.SendAsync(request, ct);
    private CancellationTokenSource _arcademyNet = new();
    internal Task? ArcademyLastAnnex { get; private set; }

    // Exactly one {type:"annex-stats", body} per ask, body null on any failure. The window is the generation:
    // WPF's static _generation guards a relaunch, here a relaunch is a new GameWindow and the old one is _closing.
    private void ArcademyAnnexStats()
    {
        if (CoreSettings.Current.OfflineMode) { Post(new { type = "annex-stats", body = (JObject?)null }); return; }
        var ct = _arcademyNet.Token;
        ArcademyLastAnnex = Task.Run(async () =>
        {
            JObject? body = null;
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(ArcademyAnnexTimeout);
                using var request = new HttpRequestMessage(HttpMethod.Get, ArcademyAnnexUrl);
                using var response = await ArcademyAnnexSend(request, deadline.Token).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();   // a cancelled or late ask is a failure, whatever came back
                if (!response.IsSuccessStatusCode) Log.Debug("[Arcademy] annex-stats {Status}", (int)response.StatusCode);
                else
                {
                    var text = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
                    if (text.Length > 400_000) Log.Information("[Arcademy] annex-stats is {N} chars - ignored", text.Length);
                    else body = JObject.Parse(text);
                }
            }
            catch (Exception ex) { Log.Debug("[Arcademy] annex-stats failed: {E}", ex.Message); body = null; }
            Dispatcher.UIThread.Post(() => { if (!_closing) Post(new { type = "annex-stats", body }); });
        });
    }

    // Panic cancels the open requests (they still answer null); teardown cancels them and drops the answer.
    private void ArcademyCancelNet()
    {
        var old = _arcademyNet;
        _arcademyNet = new CancellationTokenSource();
        old.Cancel();
    }
}
