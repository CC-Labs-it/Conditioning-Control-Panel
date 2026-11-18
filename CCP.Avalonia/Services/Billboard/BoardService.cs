using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Billboard.Board;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.BillboardServices
{
    // WPF BoardService.Wpf.cs:16-62 and BoardPng.cs:13-33.
    public static class BoardService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly Lazy<BoardCacheService> Cache = new(() => new BoardCacheService(
            Path.Combine(CorePaths.UserData, "board"), Fetch, a => Dispatcher.UIThread.Post(a)));
        public static BoardCacheService Shared => Cache.Value;

        private static async Task<byte[]?> Fetch(int version, CancellationToken ct)
        {
            try
            {
                using var response = await Http.GetAsync($"https://codebambi-proxy.vercel.app/config/board/{version}.png", HttpCompletionOption.ResponseHeadersRead, ct);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > BoardCacheService.MaxPngBytes) return null;
                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(chunk, ct)) > 0)
                {
                    if (buffer.Length + count > BoardCacheService.MaxPngBytes) return null;
                    buffer.Write(chunk, 0, count);
                }
                return buffer.ToArray();
            }
            catch (Exception) { return null; }
        }
    }

    public static class BoardPng
    {
        public static void Install() => BoardPicture.PngDecoder ??= Decode;
        public static (int[] Pixels, int Width, int Height)? Decode(byte[] png)
        {
            using var stream = new SKMemoryStream(png);
            using var codec = SKCodec.Create(stream);
            if (codec == null || !BoardPicture.FitsStrip(codec.Info.Width, codec.Info.Height)) return null;
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap(info);
            if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success) return null;
            var pixels = new int[info.Width * info.Height];
            System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
            return (pixels, info.Width, info.Height);
        }
    }
}
