// THE STUDENT ID PHOTO on disk. WPF Services/Arcademy/ArcademyAvatarCache.cs, JPEG through SkiaSharp
// in place of WPF's JpegBitmapEncoder. The CDN url carries the Discord snowflake: it is never logged
// and never reaches the page, only the data: URI does.
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal static class ArcademyAvatarCache
{
    private const string AvatarFile = "avatar.jpg", HashFile = "avatar.hash", LegacyAvatarFile = "avatar.png";
    public const int MaxDataUriChars = 48 * 1024;                                    // WPF :60
    private const string DataUriPrefix = "data:image/jpeg;base64,";
    private static readonly int MaxImageBytes = (MaxDataUriChars - DataUriPrefix.Length) / 4 * 3;
    private const string EncodeRecipe = "j85-48k-1";                                 // WPF :79, same sidecar on both heads
    private const int JpegQuality = 85, MaxFetchBytes = 512 * 1024;
    private static readonly int[] DecodeWidths = { 128, 96 };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private static readonly SemaphoreSlim FetchGate = new(1, 1);
    private static int _noTagWarned;

    /// <summary>Tests only: the linked account's avatar tag (WPF :131-138, g: guild before u: user) and its
    /// 128px CDN url; null when Discord is not linked.</summary>
    internal static Func<(string? Tag, string? Url)?> Account = () =>
        AccountSeed.Discord is { IsAuthenticated: true } d
            ? (!string.IsNullOrEmpty(d.GuildAvatar) ? "g:" + d.GuildAvatar : !string.IsNullOrEmpty(d.Avatar) ? "u:" + d.Avatar : null, d.GetAvatarUrl(128))
            : null;
    /// <summary>Tests only: the CDN GET (WPF :265-275), null on any refusal.</summary>
    internal static Func<string, Task<byte[]?>> Fetch = async url =>
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        if (!(resp.Content.Headers.ContentType?.MediaType ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
        if (resp.Content.Headers.ContentLength > MaxFetchBytes) return null;
        return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    };

    public static string Dir => Path.Combine(CorePaths.UserData, "arcademy");   // shared with the web profile: never deleted

    private static string? PathFor(string bare)
    {
        try { Directory.CreateDirectory(Dir); return Path.Combine(Dir, bare); }
        catch (Exception ex) { Log.Warning("ArcademyAvatarCache.PathFor({F}): {E}", bare, ex.Message); return null; }
    }

    private static string Sidecar(string tag, bool ok) => EncodeRecipe + (ok ? "|ok|" : "|no|") + tag;

    private static string? ReadTag()
    {
        try
        {
            var p = PathFor(HashFile);
            if (p == null || !File.Exists(p)) return null;
            var tag = File.ReadAllText(p).Trim();
            return tag.Length == 0 || tag.Length > 160 ? null : tag;
        }
        catch { return null; }
    }

    // WPF :189-209: no network and no hash check; a stale real picture beats a blank well.
    public static string? ReadDataUri()
    {
        try
        {
            var p = PathFor(AvatarFile);
            if (p == null || !File.Exists(p)) return null;
            var len = new FileInfo(p).Length;
            if (len == 0 || len > MaxImageBytes) return null;
            var bytes = File.ReadAllBytes(p);
            if (bytes.Length < 3 || bytes[0] != 0xFF || bytes[1] != 0xD8 || bytes[2] != 0xFF) return null;
            var uri = DataUriPrefix + Convert.ToBase64String(bytes);
            return uri.Length > MaxDataUriChars ? null : uri;
        }
        catch (Exception ex) { Log.Debug("ArcademyAvatarCache.ReadDataUri: {E}", ex.Message); return null; }
    }

    // WPF :225-323: true only when the bytes on disk changed. Never throws.
    public static async Task<bool> EnsureCachedAsync()
    {
        try
        {
            if (Account() is not { } acct) return false;
            if (acct.Tag is not { } tag)
            {
                if (Interlocked.Exchange(ref _noTagWarned, 1) == 0)
                    Log.Warning("ArcademyAvatarCache: no fetchable Discord avatar hash (default avatar?), student photo stays pending");
                return false;
            }
            var file = PathFor(AvatarFile);
            if (file == null || Current(file, tag)) return false;
            if (CoreSettings.Current.OfflineMode) { Log.Debug("ArcademyAvatarCache: refresh declined, offline mode"); return false; }
            if (string.IsNullOrEmpty(acct.Url)) return false;
            var url = acct.Url.Replace(".gif?", ".png?");   // the still of an animated avatar

            if (!await FetchGate.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false)) return false;
            try
            {
                if (Current(file, tag)) return false;   // the other caller just did this hash
                var raw = await Fetch(url).ConfigureAwait(false);
                if (raw == null || raw.Length == 0 || raw.Length > MaxFetchBytes) return false;
                var img = Encode(raw);
                if (img == null)
                {
                    Log.Warning("ArcademyAvatarCache: avatar would not fit {Cap} chars at any decode width, no student photo", MaxDataUriChars);
                    WriteSidecar(tag, false);
                    return false;
                }
                if (!WriteAtomic(file, img)) return false;
                WriteSidecar(tag, true);
                try { if (PathFor(LegacyAvatarFile) is { } legacy && File.Exists(legacy)) File.Delete(legacy); } catch { }
                Log.Information("ArcademyAvatarCache: student photo cached ({N} bytes)", img.Length);
                return true;
            }
            finally { FetchGate.Release(); }
        }
        catch (Exception ex) { Log.Debug("ArcademyAvatarCache.EnsureCachedAsync failed: {E}", ex.Message); return false; }
    }

    // Cached for this tag, or known not to fit (WPF's negative cache, :245-253).
    private static bool Current(string file, string tag)
    {
        var side = ReadTag();
        return (File.Exists(file) && side == Sidecar(tag, true)) || side == Sidecar(tag, false);
    }

    private static void WriteSidecar(string tag, bool ok)
    {
        if (PathFor(HashFile) is { } p) WriteAtomic(p, System.Text.Encoding.UTF8.GetBytes(Sidecar(tag, ok)));
    }

    // WPF :333-373: JPEG has no alpha; Discord serves an opaque square, so dropping it changes nothing in practice.
    private static byte[]? Encode(byte[] raw)
    {
        using var src = SKBitmap.Decode(raw);
        if (src == null || src.Width <= 0) return null;
        foreach (var width in DecodeWidths)
        {
            try
            {
                var info = new SKImageInfo(width, Math.Max(1, (int)Math.Round(src.Height * (double)width / src.Width)), SKColorType.Rgba8888, SKAlphaType.Opaque);
                using var scaled = src.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                using var data = scaled?.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
                var bytes = data?.ToArray();
                if (bytes is { Length: > 0 } && bytes.Length <= MaxImageBytes) return bytes;
            }
            catch (Exception ex) { Log.Debug("ArcademyAvatarCache.Encode({W}): {E}", width, ex.Message); }
        }
        return null;
    }

    private static bool WriteAtomic(string path, byte[] bytes)
    {
        var tmp = path + ".tmp";
        try { File.WriteAllBytes(tmp, bytes); File.Move(tmp, path, overwrite: true); return true; }
        catch (Exception ex)
        {
            Log.Warning("ArcademyAvatarCache.WriteAtomic: {E}", ex.Message);
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return false;
        }
    }
}
