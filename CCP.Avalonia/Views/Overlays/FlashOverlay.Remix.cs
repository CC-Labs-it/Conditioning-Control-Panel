using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Remix;
using ConditioningControlPanel.Services.Flash;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

// WPF FlashService.cs:1041-1109: one cached remix replaces one ordinary burst.
internal static partial class FlashOverlay
{
    private static JackpotRemixBuilder? _remixBuilder;
    private static readonly JackpotRemixBuilder.FailureState RemixFailures = new();
    internal static JackpotRemixDirector? _remix;

    private static JackpotRemixDirector EnsureRemix()
    {
        if (_remix != null) return _remix;
        var folder = Path.Combine(CorePaths.UserData, "cache", "remix");
        _remixBuilder = new JackpotRemixBuilder(Path.Combine(AppContext.BaseDirectory, "Resources", "web"), () => CorePaths.EffectiveAssets, folder, failures: RemixFailures);
        var builder = _remixBuilder;
        _remix = new JackpotRemixDirector(() =>
        {
            var root = CorePaths.EffectiveAssets;
            var dir = Path.Combine(root, "images");
            return Directory.Exists(dir) ? AssetFolderExclusion.Enabled(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)).ToList(), root, CoreSettings.Current) : Array.Empty<string>();
        }, () => Active.Count > 0, RemixVideoBusy,
            () => CoreFlash.IsRunning && CoreSettings.Current.FlashEnabled && CoreSettings.Current.JackpotRemixEnabled,
            folder, () => PrizeOwnership.IsGranted(PrizeOwnership.JackpotRemix), () => CoreSettings.Current.MotionLevel,
            () => builder.Disabled, builder.BuildAsync, AvailableMemory);
        _remix.Start();
        return _remix;
    }

    // The mandatory scheduler owns all its mirrored surfaces; the bubble game has its own player.
    internal static bool RemixVideoBusy() => CoreEngine.Video?.IsPlaying == true
        || BrowserVideoSurface.SessionActive || Views.Windows.BubbleCountWindow.VideoSessionActive
        || Views.Tabs.SettingsTabView.RemixBrowserVideoBusy;

    private static void StopRemix()
    {
        _remix?.Dispose(); _remix = null;
        _remixBuilder?.Dispose(); _remixBuilder = null;
    }

    internal static async Task<bool> TryShowRemix(AppSettings s, IReadOnlyList<Screen> screens, int? durationMs, bool ambient, int generation)
    {
        if (!ambient) return false;
        var director = EnsureRemix();
        var path = director.TakeForFlash();
        if (path == null) return false;
        var (frames, delay) = await Task.Run(() => SpiralOverlay.Decode(path, maxFrames: 60));
        if (frames.Count == 0) { director.LoadFailed(path); return false; }
        director.Consumed(path);
        await Task.Delay(1000);
        if (_closed || generation != _generation) { frames.ForEach(f => f.Dispose()); return true; }
        var primary = Math.Max(0, screens.ToList().FindIndex(x => x.IsPrimary));
        var targets = PinkFilterOverlay.ResolveScreenIndices(Services.UI.MonitorTarget.FollowGlobal, s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary);
        var windows = new List<FlashOverlayWindow>();
        DispatcherTimer? timer = null;
        var creating = true;
        var capped = false;
        // WPF FlashService.cs:1868: only the first copy rolls lucky (mirrors stay 1); each copy then
        // takes the ordinary glow rules (:1903-1909) and motion resolver (:2069).
        var multiplier = FlashFxRules.RollLucky(SkillTreeRules.HasSkill(s, FlashFxRules.LuckySkillId), 0, remixMirror: false, Rng);
        void Cleanup()
        {
            if (creating || windows.Count > 0) return;
            timer?.Stop(); timer = null;
            frames.ForEach(f => f.Dispose());
        }
        try { foreach (var index in targets)
        {
            if (Active.Count >= Cap(CoreSettings.Current, ambient)) { capped = true; break; }
            var screen = screens[index];
            var b = screen.Bounds;
            var (x, y, w, h) = JackpotRemixRoll.CentredGeometry(b.X, b.Y, b.Width, b.Height, frames[0].PixelSize.Width, frames[0].PixelSize.Height);
            var win = new FlashOverlayWindow(frames[0], ownsPicture: false) { IsRemix = true, Ambient = ambient, SpawnScreen = screen };
            var glow = FlashFxRules.ResolveGlow(s.FlashGlowEnabled, ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.CurrentTier,
                multiplier > 1 && windows.Count == 0, SkillTreeRules.GetSparkleBoostTier(s), natasha: false);
            windows.Add(win);
            win.Closed += (_, _) => { windows.Remove(win); Cleanup(); };
            if (!Spawn(win, new PixelRect(x, y, w, h), b, screen.Scaling, glow,
                Math.Clamp(s.FlashOpacity / 100.0, 0, 1), TimeSpan.FromSeconds(s.FadeDuration * FlashPlacement.FadeSecondsPerPercent),
                Lifetime(s, ambient, durationMs), s.FlashClickable)) windows.Remove(win);
        }
        } catch { foreach (var win in windows.ToArray()) win.Close(); creating = false; Cleanup(); throw; }
        creating = false;
        if (windows.Count == 0) { Cleanup(); return capped; }
        Platform.BarkHead.Engine?.Raise("FlashDisplayed");
        Displayed?.Invoke();
        if (multiplier > 1) LuckyProc(App.Mods?.GetLuckyFlashLabel() ?? ConditioningControlPanel.Localization.Loc.Get("skill_lucky_flash"), multiplier);
        Paid(path, s, 0, multiplier); // Mirrors do not pay again.
        var frame = 0;
        timer = new DispatcherTimer(GifFrameDelay(delay, s.FlashGifSpeedMultiplier), DispatcherPriority.Render, (_, _) =>
        {
            frame = (frame + 1) % frames.Count;
            foreach (var win in windows.ToArray()) win.SetPicture(frames[frame]);
        });
        timer.Start();
        return true;
    }

    // Conservative if the platform cannot report free memory: skip the large browser build.
    private static ulong AvailableMemory()
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var line = File.ReadLines("/proc/meminfo").First(s => s.StartsWith("MemAvailable:", StringComparison.Ordinal));
                return ulong.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]) * 1024;
            }
            if (OperatingSystem.IsWindows())
            {
                var s = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
                return GlobalMemoryStatusEx(ref s) ? s.Available : 0;
            }
        }
        catch { }
        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong Total, Available, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, AvailableExtended;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
