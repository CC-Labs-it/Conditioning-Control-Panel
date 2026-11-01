using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ConditioningControlPanel.Services.Billboard.Board
{
    /// <summary>
    /// Keeps the one live board post (Tonight Board, 2026-10-07). The marquee poll hands it every
    /// response (<see cref="OnMarquee"/>); a new version is fetched ONCE from
    /// <c>/config/board/{v}.png</c> (immutable on the server) and kept on disk under the user data
    /// folder, so a restart or a second poll costs no download. A cleared post drops the picture
    /// and its file; an expiring post raises <see cref="Changed"/> at its time so the deck lets go.
    /// </summary>
    public sealed partial class BoardService : BoardCacheService
    {
        public const string ServerBase = "https://codebambi-proxy.vercel.app";

        private static readonly Lazy<BoardService> SharedLazy = new(() => new BoardService(
            Path.Combine(App.UserDataPath, "board"),
#if DEBUG
            DemoPath != null ? DemoFetchAsync :
#endif
            HttpFetchAsync, PostToUi));

        /// <summary>The app's board. The marquee hook and <see cref="BoardProvider"/> both use it.</summary>
        public static BoardService Shared => SharedLazy.Value;

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        public BoardService(string cacheDir, Func<int, CancellationToken, Task<byte[]?>> fetch, Action<Action>? post = null)
            : base(cacheDir, fetch, post) { }

#if DEBUG
        public override void OnMarquee(string? marqueeJson)
        {
            try
            {
                if (DemoPost() is { } demo) { _ = Apply(demo); return; }
                base.OnMarquee(marqueeJson);
            }
            catch (Exception ex) { App.Logger?.Warning("Board: marquee hand-off failed: {Error}", ex.Message); }
        }
#endif

        // ---- the app's plumbing -------------------------------------------------------------

        private static async Task<byte[]?> HttpFetchAsync(int version, CancellationToken ct)
        {
            try
            {
                using var res = await Http.GetAsync($"{ServerBase}/config/board/{version}.png",
                    HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return null;
                if (res.Content.Headers.ContentLength is long len && len > MaxPngBytes) return null;
                var bytes = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                return bytes.Length <= MaxPngBytes ? bytes : null;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Board: fetching post {Version} failed: {Error}", version, ex.Message);
                return null;
            }
        }

        private static void PostToUi(Action a)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.HasShutdownStarted) { a(); return; }
            d.BeginInvoke(a);
        }
    }
}
