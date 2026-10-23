// ADAPTED from WPF FriendsLook.Avatar/PaintPhotoAsync at 4f75aed68cb06c56af5dc60472ca1ce0d2463000.
using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Services.BackRoom;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    internal static HttpClient PhotoHttp { get; set; } = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static async Task PaintPhotoAsync(Border disc, string url)
    {
        var full = url.StartsWith("/", StringComparison.Ordinal) ? BackRoomApi.ProductionBaseUrl + url : url;
        if (!full.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            // The existing art cache also holds decoded avatar photos. Both are UI-thread owned.
            if (!ArtCache.TryGetValue(full, out var bitmap))
            {
                var bytes = await PhotoHttp.GetByteArrayAsync(full);
                using var stream = new MemoryStream(bytes);
                bitmap = Bitmap.DecodeToWidth(stream, 96);
                ArtCache[full] = bitmap;
            }
            disc.Background = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            disc.Child = null;
        }
        catch { /* A missing picture keeps the coloured initials, as WPF does. */ }
    }
}
