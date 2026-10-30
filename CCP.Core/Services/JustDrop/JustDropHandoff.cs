using System;
using Serilog;

namespace ConditioningControlPanel.Services.JustDrop
{
    /// <summary>
    /// The head-free half of WPF Services/JustDrop/JustDropHostService.cs: where the Just Drop window
    /// opens and which request carries the app's credential. Both heads read it; each owns its window
    /// (WPF JustDropHostService over ChaosWebViewHost, Avalonia Views/Games/GameWindow.JustDrop.cs).
    ///
    /// <para><b>The sign-in handoff</b> (WPF JustDropHostService.cs:160-201). The window's FIRST
    /// navigation is <see cref="HandoffPath"/>, which takes the ccp-server token as a HEADER (never the
    /// url, so it stays out of history and the Referer), mints the site session and redirects to
    /// <c>next</c>. <see cref="AuthHeaderFor(Uri?, string?)"/> answers for that one path on that one
    /// origin and for nothing else the page or anything it embeds asks for.</para>
    /// </summary>
    internal static class JustDropHandoff
    {
        /// <summary>The dashboard app. Bare cclabs.app is the marketing site and serves none of this.</summary>
        public const string SiteHost = "app.cclabs.app";
        public const string SiteOrigin = "https://" + SiteHost;

        /// <summary>The shop: browse, order, wallet.</summary>
        public const string ShopPath = "/dashboard/express";

        /// <summary>The bare player: no wallet and no payout, so a replay is free by construction.</summary>
        public const string PlayerPath = "/express/play";

        public const string HandoffPath = "/api/auth/desktop-session";
        public const string AuthHeaderName = "X-CCP-Auth-Token";

        /// <summary>WPF LaunchReplay (:100): one delivered order in the bare player.</summary>
        public static string ReplayPath(string orderCode) =>
            $"{PlayerPath}?order={Uri.EscapeDataString(orderCode.Trim())}";

        /// <summary>WPF BuildStartUrl (:166): the handoff carrying <c>next</c> with an account in hand,
        /// the destination itself without one (the site asks who is calling, as it should).</summary>
        public static string BuildStartUrl(string nextPath, string? unifiedId, string? authToken)
        {
            if (string.IsNullOrEmpty(unifiedId) || string.IsNullOrEmpty(authToken))
            {
                Log.Information("JustDropHost: no account state; opening signed-out");
                return SiteOrigin + nextPath;
            }
            return $"{SiteOrigin}{HandoffPath}" +
                   $"?unified_id={Uri.EscapeDataString(unifiedId)}" +
                   $"&next={Uri.EscapeDataString(nextPath)}";
        }

        /// <summary>WPF AttachAuthHeader (:184): the token for the handoff path on the site's https
        /// origin, null for every other url (and when signed out). Never logged.</summary>
        public static string? AuthHeaderFor(Uri? request, string? authToken)
        {
            if (string.IsNullOrEmpty(authToken) || !IsSite(request)) return null;
            if (!string.IsNullOrEmpty(request!.UserInfo)) return null;
            return string.Equals(request.AbsolutePath, HandoffPath, StringComparison.Ordinal) ? authToken : null;
        }

        /// <summary>WPF IsRefusedHandoff (JustDropHostService.cs:223, ccp-bugs #1499): a navigation that
        /// completes ON the handoff url is a refusal (a stale token's 401 shows raw JSON), because a
        /// success never completes there, it 303s to <c>next</c>. WPF also reads the HTTP status;
        /// Avalonia.Controls.WebView reports none, so landing there is the whole test.</summary>
        public static bool IsRefusedHandoff(Uri? completed) =>
            IsSite(completed) && string.IsNullOrEmpty(completed!.UserInfo)
            && string.Equals(completed.AbsolutePath.TrimEnd('/'), HandoffPath, StringComparison.Ordinal);

        /// <summary>WPF PrimaryHost: the window stays on the site's own https origin.</summary>
        public static bool IsSite(Uri? url) =>
            url is { IsAbsoluteUri: true } && url.Scheme == Uri.UriSchemeHttps && url.IsDefaultPort
            && string.Equals(url.Host, SiteHost, StringComparison.OrdinalIgnoreCase);

        /// <summary>The ccp-server token (empty signed out) and its account; never logged.</summary>
        public static string AuthToken()
        {
            try { return CoreSettings.Current.AuthToken ?? string.Empty; } catch { return string.Empty; }
        }

        public static string UnifiedId()
        {
            try { return CoreAccount.UnifiedUserId ?? string.Empty; } catch { return string.Empty; }
        }
    }
}
