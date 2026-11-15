using System;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        private static readonly HttpClient AnnexHttp = new() { Timeout = TimeSpan.FromSeconds(6) };
        internal static Func<Task<string>> FetchAnnexStats = () =>
            AnnexHttp.GetStringAsync("https://codebambi-proxy.vercel.app/v2/arcademy/annex/stats");

        // WPF ArcademyHostService.cs:5453-5519: public, unauthenticated aggregate.
        private async void HandleAnnexStats()
        {
            JObject? body = null;
            try
            {
                if (!CoreSettings.Current.OfflineMode)
                {
                    var text = await FetchAnnexStats();
                    if (text.Length <= 400_000) body = JObject.Parse(text);
                }
            }
            catch (Exception) { }
            Dispatcher.UIThread.Post(() =>
            {
                if (!_closing && !_shellExiting) Post(new { type = "annex-stats", body });
            });
        }
    }
}
