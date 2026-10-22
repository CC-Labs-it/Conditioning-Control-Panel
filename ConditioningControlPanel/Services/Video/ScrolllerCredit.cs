using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services.Fyp;

namespace ConditioningControlPanel.Services.Video
{
    /// <summary>
    /// The "via Scrolller" credit on mandatory videos (owner, 2026-10-16: Scrolller asked for
    /// attribution; we credit them on every clip of theirs we play). A small corner badge while the
    /// clip plays, a short end card after it, and a link to the subreddit on scrolller.com.
    ///
    /// The remote video pool only keeps the clip URL, so this remembers which URL came from which
    /// post at fetch time (<see cref="Remember"/>) and the players look it up by URL
    /// (<see cref="For"/>). Bounded; old entries fall out first.
    /// </summary>
    internal static class ScrolllerCredit
    {
        /// <summary>End card length after a credited clip ends on its own.</summary>
        public const int OutroMs = 2500;

        private const int Cap = 256;
        private static readonly object Gate = new();
        private static readonly Dictionary<string, Credit> ByUrl = new(StringComparer.Ordinal);
        private static readonly Queue<string> Order = new();
        private static readonly Regex SubName = new("^[A-Za-z0-9_]{2,32}$", RegexOptions.CultureInvariant);

        internal sealed record Credit(string Sub, string Link);

        /// <summary>Credit for an entry id shaped "scrolller/&lt;sub&gt;/&lt;postId&gt;", or null.</summary>
        internal static Credit? FromId(string? id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var parts = id.Split('/');
            if (parts.Length != 3 || !string.Equals(parts[0], "scrolller", StringComparison.Ordinal)) return null;
            var link = LinkFor(parts[1]);
            return link == null ? null : new Credit(parts[1], link);
        }

        /// <summary>https://scrolller.com/r/&lt;sub&gt;, or null for anything that is not a plain
        /// subreddit name (the link reaches a command line in BrowserLauncher's fallbacks).</summary>
        internal static string? LinkFor(string? sub)
            => sub != null && SubName.IsMatch(sub) ? "https://scrolller.com/r/" + sub : null;

        /// <summary>The brand mark every native surface shows (brief contract: never a loc key).</summary>
        public const string Mark = "via Scrolller";

        /// <summary>The scrolller.com home page, for the calm credit lines (pickers, settings).</summary>
        public const string HomeLink = "https://scrolller.com";

        /// <summary>RemoteMediaCache's temp-file prefix (kept in step with it by a test). Scrolller
        /// is the only feed source, so a remote temp copy is always one of theirs.</summary>
        internal const string RemoteTempPrefix = "ccp_temp_remote_";

        /// <summary>An absolute http(s) url on scrolller.com or any of its subdomains
        /// (api, images, v, meta...). Never a look-alike such as notscrolller.com.</summary>
        internal static bool IsScrolllerUrl(string? url)
        {
            if (string.IsNullOrEmpty(url)) return false;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return false;
            var host = uri.Host;
            return string.Equals(host, "scrolller.com", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".scrolller.com", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Is the thing a surface is about to show Scrolller's? A scrolller url, an entry
        /// id shaped "scrolller/&lt;sub&gt;/&lt;postId&gt;", or a RemoteMediaCache temp copy. A local file,
        /// a pack temp file and anything else: false.</summary>
        internal static bool IsScrolllerMedia(string? pathOrUrl)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl)) return false;
            if (IsScrolllerUrl(pathOrUrl)) return true;
            if (FromId(pathOrUrl) != null) return true;
            if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                return System.IO.Path.GetFileName(pathOrUrl)
                    .StartsWith(RemoteTempPrefix, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
        }

        /// <summary>Open https://scrolller.com from a calm surface (a picker, Media History).
        /// Refused under Strict Lock, Lockdown or a Chaos or DtRH run. Never throws.</summary>
        public static void OpenHome()
        {
            try
            {
                if (!CanOpenLink(App.Settings?.Current?.StrictLockEnabled == true,
                        App.Lockdown?.IsActive == true, VideoService.LockedChaosRun())) return;
                ConditioningControlPanel.Helpers.BrowserLauncher.TryOpenUrl(HomeLink);
            }
            catch (Exception ex) { App.Logger?.Debug("ScrolllerCredit: home open failed: {E}", ex.Message); }
        }

        /// <summary>The link may open only outside every lock: a browser coming up over Strict Lock,
        /// Lockdown or a Chaos run would be a way out of them.</summary>
        internal static bool CanOpenLink(bool strict, bool lockdown, bool chaos) => !strict && !lockdown && !chaos;

        public static void Remember(FypAssetManifest.Entry entry)
        {
            var credit = FromId(entry.Id);
            if (credit == null || string.IsNullOrEmpty(entry.Url)) return;
            lock (Gate)
            {
                if (!ByUrl.ContainsKey(entry.Url))
                {
                    Order.Enqueue(entry.Url);
                    while (Order.Count > Cap) ByUrl.Remove(Order.Dequeue());
                }
                ByUrl[entry.Url] = credit;
            }
        }

        public static Credit? For(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            lock (Gate) return ByUrl.TryGetValue(url, out var c) ? c : null;
        }

        /// <summary>The <c>credit</c> field of the player page's <c>load</c> message, or null for a
        /// clip that is not Scrolller's. <paramref name="clickable"/> false = the badge shows but
        /// takes no press.</summary>
        public static object? PagePayload(string? url, bool clickable)
        {
            var c = For(url);
            if (c == null) return null;
            return new { sub = "r/" + c.Sub, site = "scrolller.com/r/" + c.Sub, clickable, outroMs = OutroMs };
        }

        /// <summary>Open the credit link in the user's browser. Never throws.</summary>
        public static void Open(string? url)
        {
            var link = For(url)?.Link;
            if (link == null) return;
            try
            {
                App.Logger?.Information("ScrolllerCredit: opening {Link}", link);
                ConditioningControlPanel.Helpers.BrowserLauncher.TryOpenUrl(link);
            }
            catch (Exception ex) { App.Logger?.Debug("ScrolllerCredit: open failed: {E}", ex.Message); }
        }
    }
}
