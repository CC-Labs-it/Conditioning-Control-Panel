using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ConditioningControlPanel.Services.Billboard.Board
{
    /// <summary>WPF plumbing for the shared Core board cache: HTTP, dispatcher and DEBUG demo.</summary>
    public sealed partial class BoardService
    {
        public const string ServerBase = "https://codebambi-proxy.vercel.app";
        public const int MaxPngBytes = BoardCacheService.MaxPngBytes;

        private static readonly Lazy<BoardCacheService> SharedLazy = new(() => new BoardCacheService(
            Path.Combine(App.UserDataPath, "board"),
#if DEBUG
            DemoPath != null ? DemoFetchAsync :
#endif
            HttpFetchAsync, PostToUi));

        /// <summary>The app's board. The marquee hook and <see cref="BoardProvider"/> both use it.</summary>
        public static BoardCacheService Shared => SharedLazy.Value;

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

        /// <summary>The WPF marquee hook keeps the DEBUG demo override in this head.</summary>
        public static void OnMarquee(string? marqueeJson)
        {
            try
            {
#if DEBUG
                if (DemoPost() is { } demo) { _ = Shared.Apply(demo); return; }
#endif
                Shared.OnMarquee(marqueeJson);
            }
            catch (Exception ex) { App.Logger?.Warning("Board: marquee hand-off failed: {Error}", ex.Message); }
        }

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
