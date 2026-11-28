using System;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF ChaosWebViewHost.cs:1519 disables the default menu for ccp.game.
internal static class LocalPagePolicy
{
    internal static bool IsLocalPage(Uri? uri, WebAssetServer server) =>
        uri is { IsAbsoluteUri: true } && uri.Scheme == Uri.UriSchemeHttp
        && string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
        && uri.Port == server.Port;

    internal static bool AllowsMicrophone(string? uri, WebAssetServer server) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u) && IsLocalPage(u, server);

    internal static bool SuppressContextMenu(Uri? uri, WebAssetServer server, bool homeBrowser) =>
        !homeBrowser && IsLocalPage(uri, server);
}
