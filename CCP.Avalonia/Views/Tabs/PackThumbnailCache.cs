using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

// WPF MainWindow.Assets.cs:842-951. Scope to the store so replacement packs/profiles
// cannot reuse another store's decrypted images. The weak key does not retain the store.
internal sealed class PackThumbnailCache
{
    private static readonly ConditionalWeakTable<ContentPackStore, PackThumbnailCache> Stores = new();
    internal static PackThumbnailCache For(ContentPackStore store) => Stores.GetValue(store, _ => new PackThumbnailCache());
    private readonly Dictionary<string, (Bitmap Image, long Bytes, long Access)> _entries = new();
    private long _bytes, _access;

    // Called inside the existing four-load semaphore. Failures are not cached,
    // so the next folder visit can retry. Decode outside the lock, as WPF does.
    internal Bitmap? GetOrLoad(string key, Func<Bitmap?> load)
    {
        lock (_entries)
        {
            if (_entries.TryGetValue(key, out var hit))
            {
                _entries[key] = (hit.Image, hit.Bytes, ++_access);
                return hit.Image;
            }
        }
        var image = load();
        if (image == null) return null;
        lock (_entries)
        {
            if (_entries.TryGetValue(key, out var raced))
            {
                image.Dispose(); // this duplicate has never been published to a view
                _entries[key] = (raced.Image, raced.Bytes, ++_access);
                return raced.Image;
            }
            var bytes = (long)image.PixelSize.Width * image.PixelSize.Height * 4;
            while (_entries.Count > 0 && (_entries.Count >= 50 || _bytes + bytes > 50L * 1024 * 1024))
            {
                var oldest = _entries.MinBy(pair => pair.Value.Access);
                _bytes -= oldest.Value.Bytes;
                _entries.Remove(oldest.Key);
                // Do not Dispose: a visible thumbnail may still own this immutable bitmap.
            }
            // ponytail: unlike WPF, do not retain an individual image larger than the byte cap.
            if (bytes <= 50L * 1024 * 1024)
            {
                _entries[key] = (image, bytes, ++_access);
                _bytes += bytes;
            }
            return image;
        }
    }
}
