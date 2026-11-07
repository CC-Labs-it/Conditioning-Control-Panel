using System;
using System.Runtime.CompilerServices;
using Microsoft.Web.WebView2.Core;

namespace ConditioningControlPanel.Services.Audio
{
    /// <summary>
    /// ccp-bugs #1540 / #1549: "random audio stings that sound like PMV snippets", and the recent-sounds
    /// ring named only whispers, pops and chimes, so the sound left through a player that never calls
    /// <see cref="EngineOffAudioLog.Note"/>. A WebView2 page playing a clip's audio track is the widest
    /// such door (games, Scrolller pictures, the browser video player, embeds).
    ///
    /// <para>Every WebView2 host already calls <c>ScrolllerWebGate.Attach</c>, so this hooks there:
    /// when a page starts playing audio, the ring gets one entry, player <c>web:&lt;host&gt;</c> and the
    /// page's file name, and a bug report names it. It records only; it never mutes or changes what
    /// plays.</para>
    /// </summary>
    public static class WebAudioWatch
    {
        private static readonly ConditionalWeakTable<CoreWebView2, object> Hooked = new();

        /// <summary>Safe to call twice on one core; never throws.</summary>
        public static void Attach(CoreWebView2? core)
        {
            if (core == null) return;
            try
            {
                if (Hooked.TryGetValue(core, out _)) return;
                Hooked.Add(core, new object());
                core.IsDocumentPlayingAudioChanged += (s, _) =>
                {
                    try
                    {
                        if (s is not CoreWebView2 c || !c.IsDocumentPlayingAudio) return;
                        var (player, file) = Describe(c.Source, c.IsMuted);
                        EngineOffAudioLog.Note(player, file);
                    }
                    catch { }
                };
            }
            catch { }
        }

        /// <summary>Pure: the ring's player and file for a page URL. The host names the surface
        /// (ccp.race3d, ccp.backroom, ...); the last path segment names the page.</summary>
        public static (string Player, string File) Describe(string? source, bool muted)
        {
            string host = "page", file = "";
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            {
                host = string.IsNullOrEmpty(uri.Host) ? uri.Scheme : uri.Host;
                var path = uri.AbsolutePath.TrimEnd('/');
                var slash = path.LastIndexOf('/');
                file = slash >= 0 ? path.Substring(slash + 1) : path;
            }
            if (string.IsNullOrEmpty(file)) file = "(page)";
            return ("web:" + host + (muted ? ":muted" : ""), file);
        }
    }
}
