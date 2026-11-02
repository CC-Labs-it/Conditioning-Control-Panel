using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using XamlAnimatedGif;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The two emote layers (ImgAvatarAnimated / ImgAvatarAnimatedB) play a clip through one of two
    /// lanes: XamlAnimatedGif for a .gif (avatar0, Circe, user mods) or <see cref="EmoteClipPlayer"/>
    /// (SkiaSharp) for an animated .webp (Rain, 7.2). The crossfade, watchdog and pause code in
    /// AvatarTubeWindow.CirceEmotes.cs talk to a layer only through these members, so both lanes keep
    /// the same play-once, completion and "new frame ready" rules. A folder may hold either format:
    /// for clip <c>x</c> the layer plays <c>x.webp</c> when it ships, else <c>x.gif</c>. A baked look
    /// pack (lane R8) maps <c>x</c> through its manifest to <c>x.strip.png</c>, played by the same
    /// SkiaSharp player from the strip.
    /// </summary>
    public partial class AvatarTubeWindow
    {
        private EmoteClipPlayer? _webpLayerA, _webpLayerB;
        private readonly Dictionary<string, bool> _clipFileExists = new(StringComparer.OrdinalIgnoreCase);

        internal static bool IsWebpClip(Uri? uri)
            => uri != null && uri.AbsolutePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

        /// <summary>The URI of clip <paramref name="clip"/> as <paramref name="ext"/> in an emote folder: a bare
        /// embedded folder name (pack://Resources/) or an absolute directory (a mod's own set, a baked pack).</summary>
        internal static Uri EmoteClipUri(string folder, string clip, string ext)
            => System.IO.Path.IsPathRooted(folder)
                ? new Uri(System.IO.Path.Combine(folder, clip + ext), UriKind.Absolute)
                : new Uri($"pack://application:,,,/Resources/{folder}/{clip}{ext}", UriKind.Absolute);

        /// <summary>True when the clip file ships (cached per path; folders never change while engaged).</summary>
        private bool EmoteClipFileExists(Uri uri)
        {
            var key = uri.ToString();
            if (_clipFileExists.TryGetValue(key, out var hit)) return hit;
            bool exists;
            try
            {
                if (uri.IsFile) exists = System.IO.File.Exists(uri.LocalPath);
                else
                {
                    var sri = Application.GetResourceStream(uri);
                    exists = sri != null;
                    sri?.Stream.Dispose();
                }
            }
            catch { exists = false; }
            _clipFileExists[key] = exists;
            return exists;
        }

        // The manifest of the baked look pack being played (lane R8); packs are look-hashed and never
        // change once committed, so one read per folder.
        private string? _packManifestFolder;
        private Services.Companion.RainPackManifest? _packManifest;

        private Services.Companion.RainPackManifest? PackManifestFor(string folder)
        {
            if (!Services.Companion.RainPackPlayback.IsPackFolder(folder)) return null;
            string key;
            try { key = System.IO.Path.GetFullPath(folder).TrimEnd(System.IO.Path.DirectorySeparatorChar); }
            catch { return null; }
            if (!string.Equals(_packManifestFolder, key, StringComparison.OrdinalIgnoreCase))
            {
                _packManifest = Services.Companion.RainPackPlayback.ManifestFor(key);
                _packManifestFolder = key;
            }
            return _packManifest;
        }

        /// <summary>The clip to play. A baked pack: the manifest's strip. Anything else: the .webp when the
        /// folder ships one, else the .gif.</summary>
        private Uri ResolveEmoteClipUri(string folder, string clip)
        {
            var strip = Services.Companion.RainPackPlayback.ClipPath(folder, PackManifestFor(folder), clip);
            if (strip != null) return new Uri(strip, UriKind.Absolute);
            var webp = EmoteClipUri(folder, clip, ".webp");
            return EmoteClipFileExists(webp) ? webp : EmoteClipUri(folder, clip, ".gif");
        }

        private EmoteClipPlayer? WebpLayerIfAny(Image img)
            => ReferenceEquals(img, ImgAvatarAnimated) ? _webpLayerA
             : ReferenceEquals(img, ImgAvatarAnimatedB) ? _webpLayerB : null;

        private EmoteClipPlayer WebpLayerFor(Image img)
        {
            var existing = WebpLayerIfAny(img);
            if (existing != null) return existing;
            var p = new EmoteClipPlayer(img);
            p.Completed += _ => OnCirceLayerCompleted(img);
            p.Failed += (_, error) => OnCirceLayerError(img, "webp", error);
            if (ReferenceEquals(img, ImgAvatarAnimated)) _webpLayerA = p; else _webpLayerB = p;
            return p;
        }

        /// <summary>Load <paramref name="uri"/> on a layer to play ONCE. Always a fresh load, even when the
        /// layer already holds the same source (an identical XamlAnimatedGif URI would be a silent no-op).</summary>
        private void LayerLoadOnce(Image img, Uri uri)
        {
            if (Services.Companion.RainPackPlayback.IsStripFile(uri))
            {
                if (AnimationBehavior.GetSourceUri(img) != null) AnimationBehavior.SetSourceUri(img, null);
                var folder = System.IO.Path.GetDirectoryName(uri.LocalPath) ?? "";
                var c = Services.Companion.RainPackPlayback.ClipForFile(PackManifestFor(folder), uri.LocalPath);
                // No manifest entry: frames 0 makes the player report Failed (the usual error path).
                _ = WebpLayerFor(img).LoadStrip(uri, c?.Frames ?? 0, c?.Fps ?? 0);
                return;
            }
            if (IsWebpClip(uri))
            {
                if (AnimationBehavior.GetSourceUri(img) != null) AnimationBehavior.SetSourceUri(img, null);
                _ = WebpLayerFor(img).Load(uri);
                return;
            }
            WebpLayerIfAny(img)?.Stop();
            if (AnimationBehavior.GetSourceUri(img) != null) AnimationBehavior.SetSourceUri(img, null);
            AnimationBehavior.SetRepeatBehavior(img, new RepeatBehavior(1));
            AnimationBehavior.SetAutoStart(img, true);
            AnimationBehavior.SetSourceUri(img, uri);
        }

        /// <summary>Stop and free whatever the layer plays (both lanes).</summary>
        private void LayerStop(Image img)
        {
            try { WebpLayerIfAny(img)?.Stop(); } catch { /* ignore */ }
            try { AnimationBehavior.SetSourceUri(img, null); } catch { /* ignore */ }
        }

        /// <summary>The source the layer was last given, either lane.</summary>
        private Uri? LayerSource(Image img)
            => WebpLayerIfAny(img)?.Source ?? AnimationBehavior.GetSourceUri(img);

        /// <summary>Non-null once the layer's CURRENT clip has a renderable frame; a new object per load, so
        /// a crossfade can tell a fresh clip from the one the layer held before.</summary>
        private object? LayerFrameToken(Image img)
        {
            var w = WebpLayerIfAny(img);
            if (w?.Source != null) return w.ReadyToken;
            return AnimationBehavior.GetAnimator(img);
        }

        /// <summary>The layer's clip has finished its single pass (false while loading or with nothing loaded).</summary>
        private bool LayerIsComplete(Image img)
        {
            var w = WebpLayerIfAny(img);
            if (w?.Source != null) return w.IsReady && w.IsComplete;
            return AnimationBehavior.GetAnimator(img)?.IsComplete == true;
        }

        private void LayerPause(Image img)
        {
            try { WebpLayerIfAny(img)?.Pause(); } catch { }
            try { AnimationBehavior.GetAnimator(img)?.Pause(); } catch { }
        }

        private void LayerPlay(Image img)
        {
            try { WebpLayerIfAny(img)?.Play(); } catch { }
            try { AnimationBehavior.GetAnimator(img)?.Play(); } catch { }
        }
    }
}
