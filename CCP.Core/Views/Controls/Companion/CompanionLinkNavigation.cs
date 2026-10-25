using System;

namespace ConditioningControlPanel.Views.Controls.Companion;

/// <summary>The watch chip only navigates HTTP(S); only HTTPS may leave the embedded browser.</summary>
public static class CompanionLinkNavigation
{
    public static bool TryBrowserUri(string? url, out Uri? uri)
    {
        uri = null;
        return Uri.TryCreate(url, UriKind.Absolute, out uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    public static bool CanOpenExternally(Uri uri) => uri.Scheme == Uri.UriSchemeHttps;
}
