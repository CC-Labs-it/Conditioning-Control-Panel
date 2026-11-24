// WPF ArcademyHostService.cs:4632-4663, 4912-4922.
using System;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        // Until the provider and its consent gate are ported, terminate every request without media.
        private void PostArcademyAssetsUnavailable(JObject message)
        {
            var reqId = (string?)message["reqId"] ?? "";
            Post(new { type = "assets", reqId, urls = Array.Empty<object>(), done = true });
        }
    }
}
