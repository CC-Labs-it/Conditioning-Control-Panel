using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Services.Video;

/// <summary>Options for remote LibVLC clips. Local files need no HTTP identity.</summary>
internal static class RemoteVideoOptions
{
    internal static string[] For(string path) => VideoSelectionRules.IsRemoteMediaPath(path)
        ? new[] { ":http-reconnect", $":http-user-agent={ScrolllerSource.HonestUserAgent}" }
        : System.Array.Empty<string>();
}
