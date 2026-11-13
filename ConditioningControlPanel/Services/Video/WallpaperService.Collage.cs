using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Studio > Wallpaper: a collage composed from the user's own images (or one picture they picked),
    /// up while a session runs, or the whole time CCP is open with "Run with CCP". Panic always takes it
    /// down (App.KillAllAudio); a plain Stop leaves it only under "Run with CCP". Independent of the
    /// Takeover shuffle in the other half of this class, but it shares the write queue and the #692
    /// breadcrumb, so a crash still puts the user's own wallpaper back on the next launch.
    /// </summary>
    public partial class WallpaperService
    {
        private System.Windows.Threading.DispatcherTimer? _collageTimer;
        private bool _collageRunning;
        private string? _collageCurrentFile;
        private int _collageComposing;   // 0/1 via Interlocked: one compose at a time
        private string? _lastCollagePack; // avoid picking the same theme folder twice in a row
        private bool _ownImageRunning;    // Own image mode: the user's picture is up (used in place, never deleted)

        /// <summary>Stored as the "original" when the desktop is a solid colour: restoring clears the
        /// wallpaper rather than setting a file.</summary>
        internal const string SolidColorMarker = "solid-colour";

        /// <summary>The frozen copy of the user's wallpaper, kept only until the real one is back.</summary>
        internal const string FrozenOriginalName = "ccp_original_wallpaper.jpg";

        /// <summary>Raised (on any thread) when the picture "Save this wallpaper" would copy changes: a new
        /// collage, an own image going up, a stop.</summary>
        public event EventHandler? SavableWallpaperChanged;

        private void RaiseSavableWallpaperChanged()
        {
            try { SavableWallpaperChanged?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { Diag.Swallowed(ex, "savable wallpaper changed"); }
        }

        /// <summary>The picture on the desktop right now that "Save this wallpaper" can copy: the current
        /// Static collage (a temp file the next rotation deletes) or the Own image. Null while nothing of
        /// ours is up, and always in Dynamic mode (it never writes a file).</summary>
        public string? CurrentSavableWallpaper()
        {
            string? path;
            lock (_lock)
            {
                if (!_collageRunning) return null;
                path = _ownImageRunning ? App.Settings?.Current?.CollageOwnImagePath : _collageCurrentFile;
            }
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
        }

        /// <summary>Clamped recompose interval in seconds (10 s to 24 h). Shared with Dynamic mode.</summary>
        internal static int CollageInterval()
            => Math.Clamp(App.Settings?.Current?.CollageWallpaperIntervalSeconds ?? 60, 10, 86400);

        /// <summary>Whether the collage cycles on the timer (vs composing one and holding it). Shared with
        /// Dynamic mode.</summary>
        internal static bool RotateOn() => App.Settings?.Current?.CollageWallpaperRotate ?? true;

        // ---------------------------------------------------------------- what to restore

        private static string FrozenOriginalPath() => Path.Combine(App.GetMediaTempPath(), FrozenOriginalName);

        /// <summary>What to hand the write queue to put <paramref name="original"/> back: "" clears the
        /// wallpaper for a solid colour, then the user's own file while it exists, then the frozen copy.
        /// The own file comes first on purpose: pointing Windows at a copy in our temp folder would leave
        /// the desktop hanging off CCP's assets folder for good.</summary>
        internal static string RestoreTarget(string original, Func<string, bool> exists, string frozenCopy,
            string? transcodedCache = null)
        {
            if (original == SolidColorMarker) return "";
            // Windows' own cache is never handed back as itself: by now it holds OUR picture, and setting
            // the file Windows transcodes into from itself leaves a white desktop (ccp-bugs #1584).
            if (IsTranscodedCache(original, transcodedCache)) return frozenCopy;
            if (exists(original)) return original;
            return exists(frozenCopy) ? frozenCopy : original;
        }

        /// <summary>Can <paramref name="original"/> be put back at all? Never hand Windows an empty or
        /// missing path unless it is the solid-colour clear (#692), nor its own transcoded cache unless a
        /// frozen copy of it was taken (#1584).</summary>
        internal static bool IsRestorable(string? original, Func<string, bool> exists, string frozenCopy,
            string? transcodedCache = null)
        {
            if (original == SolidColorMarker) return true;
            if (string.IsNullOrWhiteSpace(original)) return false;
            if (IsTranscodedCache(original, transcodedCache)) return exists(frozenCopy);
            return exists(original) || exists(frozenCopy);
        }

        private static string RestoreTarget(string original)
            => RestoreTarget(original, File.Exists, FrozenOriginalPath(), TranscodedCachePath());

        private static bool IsRestorable(string? original)
            => IsRestorable(original, File.Exists, FrozenOriginalPath(), TranscodedCachePath());

        /// <summary>%AppData%\Microsoft\Windows\Themes\TranscodedWallpaper: the copy Windows makes of every
        /// wallpaper it sets. On a slideshow SPI_GETDESKWALLPAPER names this file, not a source.</summary>
        private static string? TranscodedCachePath()
        {
            try
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
            }
            catch { return null; }
        }

        /// <summary>Is <paramref name="path"/> Windows' transcoded cache? Pure. Every wallpaper write lands
        /// in that file, so it is never a source to put back.</summary>
        internal static bool IsTranscodedCache(string? path, string? transcodedCache)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(transcodedCache)) return false;
            try { return string.Equals(Path.GetFullPath(path), Path.GetFullPath(transcodedCache), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(path, transcodedCache, StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>The original to remember for a path Windows reports: the path itself, or, when it is
        /// Windows' own cache (a picture set from Photos, a browser or theme sync), a frozen copy of it, since
        /// our first write overwrites that file (#1584). Null when the copy could not be made.</summary>
        private static string? OriginalFromReported(string path)
        {
            if (!IsTranscodedCache(path, TranscodedCachePath())) return path;
            try
            {
                var frozen = FrozenOriginalPath();
                File.Copy(path, frozen, overwrite: true);
                App.Logger?.Information("[Wallpaper] Desktop reports Windows' own cache; a frozen copy is the original");
                return frozen;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("[Wallpaper] Could not freeze Windows' wallpaper cache: {Error}", ex.Message);
                return null;
            }
        }

        /// <summary>For Dynamic's stop repaint (<see cref="DynamicWallpaperService"/>): is this registry path
        /// Windows' own cache, which must never be re-set from itself?</summary>
        internal static bool IsTranscodedCachePath(string? path) => IsTranscodedCache(path, TranscodedCachePath());

        /// <summary>The real wallpaper is back: the frozen copy has done its job. Kept when Windows is
        /// pointing at it (the source was gone), since Windows re-reads that path on logon.</summary>
        private static void DropFrozenOriginal(string restoredTo)
        {
            try
            {
                var frozen = FrozenOriginalPath();
                if (!string.Equals(restoredTo, frozen, StringComparison.OrdinalIgnoreCase) && File.Exists(frozen))
                    File.Delete(frozen);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "drop frozen wallpaper"); }
        }

        /// <summary>HKCU\Control Panel\Desktop WallpaperStyle as a word: Center, Tile, Stretch, Fit, Fill,
        /// Span, or the raw value. For the log.</summary>
        internal static string ReadWallpaperStyle()
        {
            var raw = ReadWallpaperStyleRaw();
            return TryUnpackStyle(raw, out var style, out var tile) ? WallpaperStyleName(style, tile) : "unknown";
        }

        // ---------------------------------------------------------------- fit (#1544)

        /// <summary>The user's own WallpaperStyle + TileWallpaper, packed "style|tile", taken before CCP
        /// writes a fit of its own. Null while CCP writes none (Fit = Windows setting).</summary>
        private string? _originalStyle;

        /// <summary>The registry pair Windows reads at SPI_SETDESKWALLPAPER time for a fit. Null for
        /// <see cref="Models.WallpaperFit.Windows"/>: today's behaviour, the user's fit is never touched.</summary>
        internal static string? FitStyle(Models.WallpaperFit fit) => fit switch
        {
            Models.WallpaperFit.Fill => PackStyle("10", "0"),
            Models.WallpaperFit.Fit => PackStyle("6", "0"),
            Models.WallpaperFit.Stretch => PackStyle("2", "0"),
            Models.WallpaperFit.Span => PackStyle("22", "0"),
            Models.WallpaperFit.Center => PackStyle("0", "0"),
            _ => null,
        };

        internal static string PackStyle(string style, string tile) => style.Trim() + "|" + tile.Trim();

        internal static bool TryUnpackStyle(string? packed, out string style, out string tile)
        {
            style = tile = "";
            if (string.IsNullOrWhiteSpace(packed)) return false;
            int bar = packed.IndexOf('|');
            if (bar < 0) return false;
            style = packed[..bar];
            tile = packed[(bar + 1)..];
            return style.Length > 0;
        }

        /// <summary>The fit Studio > Wallpaper writes with its own pictures, or null to leave the user's.</summary>
        private static string? OurFitStyle() => FitStyle(App.Settings?.Current?.CollageWallpaperFit ?? Models.WallpaperFit.Windows);

        private static string? ReadWallpaperStyleRaw()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                var style = key?.GetValue("WallpaperStyle")?.ToString() ?? "";
                var tile = key?.GetValue("TileWallpaper")?.ToString() ?? "0";
                return style.Length == 0 ? NoStyleMarker : PackStyle(style, tile);
            }
            catch (Exception ex) { Diag.Swallowed(ex, "wallpaper style"); return null; }
        }

        /// <summary>Captured when the desktop has no WallpaperStyle value at all: the restore deletes ours, so
        /// the registry ends exactly as it was.</summary>
        internal const string NoStyleMarker = "none";

        /// <summary>Write the pair, or delete it for <see cref="NoStyleMarker"/>. Windows applies it at the next
        /// SPI_SETDESKWALLPAPER, so this runs on the write queue right before the set. REG_SZ, as Windows
        /// itself writes them.</summary>
        private static void WriteWallpaperStyle(string? packed)
        {
            try
            {
                if (packed == NoStyleMarker)
                {
                    using var desk = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
                    desk?.DeleteValue("WallpaperStyle", throwOnMissingValue: false);
                    desk?.DeleteValue("TileWallpaper", throwOnMissingValue: false);
                    return;
                }
                if (!TryUnpackStyle(packed, out var style, out var tile)) return;
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Control Panel\Desktop");
                key?.SetValue("WallpaperStyle", style, Microsoft.Win32.RegistryValueKind.String);
                key?.SetValue("TileWallpaper", tile, Microsoft.Win32.RegistryValueKind.String);
            }
            catch (Exception ex) { App.Logger?.Warning("[Wallpaper] Could not write the desktop fit: {Error}", ex.Message); }
        }

        /// <summary>Take the user's own fit once, before the first write that carries ours. Caller holds _lock.</summary>
        private void CaptureOriginalStyleIfNeeded()
        {
            if (_originalStyle != null || OurFitStyle() == null) return;
            _originalStyle = ReadWallpaperStyleRaw();
            if (_originalStyle == null)
            {
                App.Logger?.Warning("[Collage] Could not read the desktop fit - the chosen fit is not applied this time");
                return;
            }
            PersistOriginalStyle(_originalStyle);
            App.Logger?.Information("[Collage] Captured desktop fit {Style}; it comes back on stop", ReadWallpaperStyle());
        }

        /// <summary>The fit to write with our next picture: ours only once the user's own was taken. Caller holds _lock.</summary>
        private string? RunningFitStyle() => _originalStyle != null ? OurFitStyle() : null;

        internal static string WallpaperStyleName(string style, string tile) => style.Trim() switch
        {
            "0" => tile.Trim() == "1" ? "Tile" : "Center",
            "2" => "Stretch",
            "6" => "Fit",
            "10" => "Fill",
            "22" => "Span",
            "" => "unknown",
            var other => "style " + other,
        };

        /// <summary>BackgroundType: 0 picture, 1 solid colour, 2 slideshow, 3 Spotlight. -1 unknown.</summary>
        private static int ReadBackgroundType()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers");
                if (key?.GetValue("BackgroundType") is int bt) return bt;
            }
            catch (Exception ex) { Diag.Swallowed(ex, "wallpaper background type"); }
            return -1;
        }

        /// <summary>True when one wallpaper write would destroy what the desktop has: a slideshow,
        /// Spotlight, or a different picture per monitor. SPI can only put ONE file back, so Static and Own
        /// image stay off there; a skipped effect is far cheaper than a desktop the user can never get
        /// back (#692). Dynamic never writes, so it is fine on all of them.</summary>
        public static bool WriteWouldLoseDesktop() => WriteWouldLoseDesktop(out _);

        /// <summary>The same check, naming what would be lost for the log. Shared by Studio > Wallpaper and
        /// the Takeover wallpaper (<see cref="Activate"/>, #1584).</summary>
        internal static bool WriteWouldLoseDesktop(out string? why)
        {
            int backgroundType = ReadBackgroundType();
            int slideshow = ReadSlideshowStatus();
            why = DesktopLossReason(backgroundType, slideshow, MonitorWallpapers());
            if (why != null)
                App.Logger?.Information("[Wallpaper] Desktop check: {Why} (BackgroundType {Type}, slideshow status {Status})",
                    why, backgroundType, slideshow);
            return why != null;
        }

        internal static bool WouldLoseDesktop(int backgroundType, IReadOnlyList<string> monitorWallpapers)
            => DesktopLossReason(backgroundType, 0, monitorWallpapers) != null;

        /// <summary>DESKTOP_SLIDESHOW_STATE.DSS_SLIDESHOW: a slideshow is configured.</summary>
        internal const int SlideshowConfiguredFlag = 0x2;

        /// <summary>What one wallpaper write would destroy, or null. Pure. The slideshow is asked twice:
        /// BackgroundType is missing on some Windows 10 desktops, and IDesktopWallpaper.GetStatus is the
        /// answer Windows itself gives (ccp-bugs #1584: a Windows 10 slideshow ended on a white desktop).</summary>
        internal static string? DesktopLossReason(int backgroundType, int slideshowStatus, IReadOnlyList<string> monitorWallpapers)
        {
            if (backgroundType == 2 || (slideshowStatus > 0 && (slideshowStatus & SlideshowConfiguredFlag) != 0)) return "slideshow";
            if (backgroundType >= 3) return "Spotlight";
            for (int i = 1; i < monitorWallpapers.Count; i++)
                if (!string.Equals(monitorWallpapers[0], monitorWallpapers[i], StringComparison.OrdinalIgnoreCase))
                    return "a different picture per monitor";
            return null;
        }

        /// <summary>IDesktopWallpaper.GetStatus, or -1 when Windows will not say.</summary>
        private static int ReadSlideshowStatus()
        {
            try
            {
                var dw = (IDesktopWallpaper)new DesktopWallpaperClass();
                try { return dw.GetStatus(); }
                finally { Marshal.ReleaseComObject(dw); }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Wallpaper] IDesktopWallpaper.GetStatus failed: {Error}", ex.Message);
                return -1;
            }
        }

        private static List<string> MonitorWallpapers()
        {
            var paths = new List<string>();
            try
            {
                var dw = (IDesktopWallpaper)new DesktopWallpaperClass();
                try
                {
                    uint count = dw.GetMonitorDevicePathCount();
                    for (uint i = 0; i < count; i++)
                    {
                        try { paths.Add(dw.GetWallpaper(dw.GetMonitorDevicePathAt(i)) ?? ""); }
                        catch (COMException) { }   // a monitor that just went away
                    }
                }
                finally { Marshal.ReleaseComObject(dw); }
            }
            catch (Exception ex) { App.Logger?.Debug("[Collage] IDesktopWallpaper query failed: {Error}", ex.Message); }
            return paths;
        }

        /// <summary>The path to remember for the restore, with a frozen copy beside it in case the source
        /// goes away during the session. Null when nothing restorable exists.</summary>
        private static string? CaptureOriginalWallpaper()
        {
            // Solid colour first: there is no image, and probing the sources below would grab a stale one.
            if (ReadBackgroundType() == 1)
            {
                App.Logger?.Information("[Collage] Original desktop is a solid colour - will clear the wallpaper on stop");
                return SolidColorMarker;
            }

            var path = GetLiveWallpaperPath(out var source);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                App.Logger?.Warning("[Collage] Could not find a restorable current wallpaper - not taking over");
                return null;
            }

            // Windows' own transcoded cache has no stable path: our collage overwrites that very file. The
            // registry or IDesktopWallpaper can name it too, so the path decides, not the source (#1584).
            bool mustUseCopy = source == "transcoded-cache" || IsTranscodedCache(path, TranscodedCachePath());
            try
            {
                var frozen = FrozenOriginalPath();
                if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(frozen), StringComparison.OrdinalIgnoreCase))
                    File.Copy(path, frozen, overwrite: true);
                App.Logger?.Information("[Collage] Captured original wallpaper ({Source}): {Path}", source, path);
                return mustUseCopy ? frozen : path;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Collage] Could not snapshot original wallpaper: {Error}", ex.Message);
                return mustUseCopy ? null : path;
            }
        }

        /// <summary>The wallpaper on the desktop RIGHT NOW: IDesktopWallpaper, then the registry, then the
        /// path Windows records of what it shows, then its transcoded cache (last resort).</summary>
        private static string? GetLiveWallpaperPath(out string source)
        {
            source = "none";
            var monitors = MonitorWallpapers();
            if (monitors.Count > 0 && IsForeignImage(monitors[0])) { source = "IDesktopWallpaper"; return monitors[0]; }

            var reg = ReadCurrentWallpaper();
            if (IsForeignImage(reg)) { source = "registry"; return reg; }

            var cached = ReadTranscodedImageCachePath();
            if (cached != null) { source = "image-cache"; return cached; }

            try
            {
                var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
                if (File.Exists(cache)) { source = "transcoded-cache"; return cache; }
            }
            catch (Exception ex) { Diag.Swallowed(ex, "transcoded wallpaper probe"); }
            return null;
        }

        /// <summary>HKCU\Control Panel\Desktop\TranscodedImageCache: a blob whose tail is the UTF-16 path of
        /// the source Windows is showing. A real image file, so no desktop icons can get baked in.</summary>
        private static string? ReadTranscodedImageCachePath()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                if (key?.GetValue("TranscodedImageCache") is byte[] data && data.Length > 24)
                {
                    var s = System.Text.Encoding.Unicode.GetString(data, 24, data.Length - 24);
                    int z = s.IndexOf('\0');
                    if (z >= 0) s = s.Substring(0, z);
                    s = s.Trim();
                    if (IsForeignImage(s)) return s;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("[Collage] TranscodedImageCache read failed: {Error}", ex.Message); }
            return null;
        }

        /// <summary>A real image we did NOT create. Our own collage and frozen copies must never be captured
        /// back as "the original", or a stop-then-start loop keeps restoring our own stale output.</summary>
        private static bool IsForeignImage(string? p)
        {
            if (string.IsNullOrWhiteSpace(p) || !File.Exists(p)) return false;
            var name = Path.GetFileName(p);
            return !name.StartsWith("ccp_collage_", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("ccp_original_wallpaper", StringComparison.OrdinalIgnoreCase);
        }

        // IDesktopWallpaper: the vtable up to GetStatus, in order (shobjidl_core.h). Only reads are called.
        [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktopWallpaper
        {
            void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID,
                              [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
            [return: MarshalAs(UnmanagedType.LPWStr)]
            string GetMonitorDevicePathAt(uint monitorIndex);
            uint GetMonitorDevicePathCount();
            void GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID, out NativeRect displayRect);
            void SetBackgroundColor(uint color);
            uint GetBackgroundColor();
            void SetPosition(int position);
            int GetPosition();
            void SetSlideshow(IntPtr items);
            IntPtr GetSlideshow();
            void SetSlideshowOptions(int options, uint slideshowTick);
            void GetSlideshowOptions(out int options, out uint slideshowTick);
            void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string? monitorID, int direction);
            int GetStatus();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }

        [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")]
        private class DesktopWallpaperClass { }

        // ---------------------------------------------------------------- run / stop

        /// <summary>
        /// Begin the wallpaper in the selected mode. No-op unless it is switched on. Captures the real
        /// wallpaper first (the #692 path). UI thread; the compose itself runs off-thread.
        /// </summary>
        public void StartCollage()
        {
            var s = App.Settings?.Current;
            if (s == null || !s.CollageWallpaperEnabled) return;

            // Dynamic mode: the moving collage owns the desktop instead, and it never writes a wallpaper.
            if (DynamicWallpaperService.ShouldRun(s))
            {
                App.DynamicWallpaper?.Start();
                return;
            }

            // Own image: one picture, held. Moved or deleted since it was picked: leave the desktop alone.
            string? ownImage = null;
            if (s.CollageMode == Models.WallpaperMode.OwnImage)
            {
                ownImage = s.CollageOwnImagePath;
                if (string.IsNullOrWhiteSpace(ownImage) || !File.Exists(ownImage))
                {
                    App.Logger?.Warning("[Collage] Own image mode, but the picked image is missing - desktop left alone");
                    return;
                }
            }

            // A write still in flight (a restore after a crash, or a mode switch) means the desktop may not
            // show the user's own wallpaper yet: capturing now would save the collage as "the original"
            // (#692). Wait a bounded moment, the same 3 s as Dispose; still busy: skip this start.
            if (!_isActive && _setQueue.IsBusy && !_setQueue.Flush(TimeSpan.FromSeconds(3)))
            {
                App.Logger?.Warning("[Collage] A wallpaper write is still running - not taking over this time");
                return;
            }

            string? fit;
            lock (_lock)
            {
                if (_collageRunning) return;

                if (!_isActive)
                {
                    if (WriteWouldLoseDesktop(out var lost))
                    {
                        App.Logger?.Warning("[Collage] {Why} on the desktop: not writing over it", lost);
                        return;
                    }
                    var original = CaptureOriginalWallpaper();
                    if (original == null) return;
                    PersistOriginal(original);
                    _originalWallpaperPath = original;
                    _isActive = true;
                }
                // Before the first write that carries our fit; the restore puts it back (#1544).
                CaptureOriginalStyleIfNeeded();
                fit = RunningFitStyle();
                _collageRunning = true;
                _ownImageRunning = ownImage != null;
            }

            if (ownImage != null)
            {
                // Used where it is: never copied, and never held as _collageCurrentFile (Stop deletes that).
                SetWallpaper(ownImage, "own image", fit);
                App.Logger?.Information("[Collage] Own image up (fit {Fit})", App.Settings?.Current?.CollageWallpaperFit);
                RaiseSavableWallpaperChanged();
                return;
            }

            // Recolour at once when the active mod changes.
            if (App.Mods != null) App.Mods.ModChanged += OnModChanged;

            // Fit "Windows setting" rides the user's own Windows fit; any other writes ours with each picture
            // and the user's comes back on stop (#1544).
            App.Logger?.Information("[Collage] Started (rotate={Rotate}, every {Sec}s, fit {Fit}, desktop fit {Style})",
                RotateOn(), CollageInterval(), App.Settings?.Current?.CollageWallpaperFit, ReadWallpaperStyle());
            TriggerCollageCompose();   // the first one immediately
            RefreshCollageSchedule();  // the rotation timer only if rotation is on
        }

        /// <summary>Stop composing and restore the user's original wallpaper. Safe anytime; never waits, so
        /// a panic is not held up.</summary>
        public void StopCollage()
        {
            App.DynamicWallpaper?.Stop();

            bool wasRunning;
            lock (_lock)
            {
                wasRunning = _collageRunning;
                _collageRunning = false;
                _ownImageRunning = false;
            }
            if (!wasRunning) return;

            try { _collageTimer?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex, "collage timer stop"); }
            if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
            try { Deactivate(); }
            catch (Exception ex) { App.Logger?.Warning("[Collage] Restore on stop failed: {Error}", ex.Message); }

            string? held;
            lock (_lock) { held = _collageCurrentFile; _collageCurrentFile = null; }
            TryDeleteCollageFile(held);
            RaiseSavableWallpaperChanged();
            App.Logger?.Information("[Collage] Stopped; original wallpaper restored");
        }

        /// <summary>Exit and Windows logoff: take everything down and wait for the restore to land.</summary>
        public void StopAndFlush(TimeSpan timeout)
        {
            StopCollage();
            Deactivate();   // a Takeover wallpaper is up without the collage: same rule at logoff
            App.DynamicWallpaper?.WaitStopped(timeout);
            _setQueue.Flush(timeout);
        }

        /// <summary>Arm or disarm the rotation timer to match the Rotate + interval settings, live.</summary>
        public void RefreshCollageSchedule()
        {
            App.DynamicWallpaper?.RefreshSchedule();
            if (!_collageRunning) return;
            if (_ownImageRunning) { _collageTimer?.Stop(); return; }
            if (_collageTimer == null)
            {
                _collageTimer = new System.Windows.Threading.DispatcherTimer();
                _collageTimer.Tick += (_, _) => TriggerCollageCompose();
            }
            if (RotateOn())
            {
                _collageTimer.Interval = TimeSpan.FromSeconds(CollageInterval());
                _collageTimer.Start();
            }
            else
            {
                _collageTimer.Stop();
            }
        }

        private void OnModChanged(object? sender, ConditioningControlPanel.Models.ModPackage e)
        {
            if (_collageRunning) TriggerCollageCompose();
        }

        private void TriggerCollageCompose()
        {
            if (!_collageRunning || _ownImageRunning) return;
            if (Interlocked.CompareExchange(ref _collageComposing, 1, 0) != 0) return;
            CollageSources.Selection selection;
            try { selection = CollageSources.Snapshot(); }   // copy the selection sets on this thread
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _collageComposing, 0);
                App.Logger?.Warning("[Collage] Could not read the asset selection: {Error}", ex.Message);
                return;
            }
            _ = Task.Run(() =>
            {
                try { ComposeAndApplyCollage(selection); }
                catch (Exception ex) { App.Logger?.Warning("[Collage] Compose tick failed: {Error}", ex.Message); }
                finally { Interlocked.Exchange(ref _collageComposing, 0); }
            });
        }

        /// <summary>Should the wallpaper be up? Switched on, and either the engine runs or "Run with CCP"
        /// keeps it up while CCP is open. The engine state is passed in because the start/stop paths call
        /// this before App.IsEngineRunning flips.</summary>
        public static bool ShouldBeUp(Models.AppSettings? s, bool engineRunning) =>
            s != null && s.CollageWallpaperEnabled && (engineRunning || s.CollageRunWithCcp);

        /// <summary>Bring it up or take it down to match <see cref="ShouldBeUp"/> right now. UI thread.</summary>
        public void ApplyRunPolicy()
        {
            if (ShouldBeUp(App.Settings?.Current, App.IsEngineRunning)) StartCollage();
            else StopCollage();
        }

        /// <summary>Re-apply in whatever mode is now selected: a mode switch, a performance-mode hand-over,
        /// Dynamic's Static fallback.</summary>
        public void RestartCollage()
        {
            StopCollage();
            if (ShouldBeUp(App.Settings?.Current, App.IsEngineRunning)) StartCollage();
        }

        /// <summary>A new own image was picked while the wallpaper is up: show it straight away.</summary>
        public void ApplyOwnImageNow()
        {
            var s = App.Settings?.Current;
            var path = s?.CollageOwnImagePath;
            if (s == null || !s.CollageWallpaperEnabled || s.CollageMode != Models.WallpaperMode.OwnImage ||
                string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            bool up;
            string? fit;
            lock (_lock) { up = _ownImageRunning; fit = RunningFitStyle(); }
            if (!up) { StartCollage(); return; }
            SetWallpaper(path, "own image", fit);
            App.Logger?.Information("[Collage] Own image changed");
            RaiseSavableWallpaperChanged();
        }

        private static readonly CollageLayout[] AllCollageLayouts =
            { CollageLayout.SideHero, CollageLayout.FillerOnly, CollageLayout.CenterHero };

        /// <summary>Which layout to render this compose: a random roll across all three.</summary>
        internal CollageLayout PickCollageLayout() => AllCollageLayouts[_random.Next(AllCollageLayouts.Length)];

        /// <summary>Dynamic's desktop repaint on stop. Through the queue, so it can never land in the middle
        /// of another wallpaper write.</summary>
        internal void QueueDesktopRepaint(string path) => _setQueue.Enqueue(path);

        private void ComposeAndApplyCollage(CollageSources.Selection selection)
        {
            if (!_collageRunning) return;

            // Only the images the user has SELECTED under assets/images. Deliberately NOT
            // WallpaperSourceFolder: that belongs to the Takeover shuffle.
            var active = CollageSources.ActiveImages(selection);
            if (active.Count < 2)
            {
                App.Logger?.Warning("[Collage] Only {N} selected image(s) - need at least 2; desktop left alone", active.Count);
                return;
            }
            var pack = CollageSources.PickPack(active, selection.ImagesRoot, ref _lastCollagePack, _random);

            string outDir;
            try { outDir = App.GetMediaTempPath(); }
            catch { outDir = Path.Combine(App.EffectiveAssetsPath, ".temp"); }

            var accent = App.Mods?.GetAccentColorHex() ?? "#E84393";
            var background = App.Mods?.GetBackgroundColorHex() ?? "#08080C";
            var layout = PickCollageLayout();

            var newFile = CollageComposer.Compose(pack, outDir, accent, background, layout);
            // A theme folder with under 2 DECODABLE images yields null: fall back to everything once.
            if (string.IsNullOrEmpty(newFile) && pack.Count != active.Count)
                newFile = CollageComposer.Compose(active, outDir, accent, background, layout);
            if (string.IsNullOrEmpty(newFile) || !File.Exists(newFile))
                return;

            // Check + set + bookkeeping under _lock, atomic with Stop's flag flip + restore and Start's
            // capture: a compose can never land between a Stop and the next Start's capture and get saved
            // as the user's "original" (#692).
            string? previous;
            lock (_lock)
            {
                if (!_collageRunning || _ownImageRunning) { TryDeleteCollageFile(newFile); return; }

                // A rotation never stacks behind a write still in flight, same rule as Shuffle.
                if (_collageCurrentFile != null && _setQueue.IsBusy) { TryDeleteCollageFile(newFile); return; }

                SetWallpaper(newFile, "collage", RunningFitStyle());

                previous = _collageCurrentFile;
                _collageCurrentFile = newFile;
                _currentImagePath = newFile;
            }

            if (!string.IsNullOrEmpty(previous) &&
                !string.Equals(previous, newFile, StringComparison.OrdinalIgnoreCase))
                TryDeleteCollageFile(previous);
            RaiseSavableWallpaperChanged();
        }

        private static void TryDeleteCollageFile(string? path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { App.Logger?.Debug("[Collage] Could not delete temp {File}: {Error}", path, ex.Message); }
        }
    }
}
