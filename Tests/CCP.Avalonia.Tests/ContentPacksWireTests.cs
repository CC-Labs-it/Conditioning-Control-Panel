using System;
using System.IO;
using System.Text.RegularExpressions;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
using PackService = ConditioningControlPanel.Services.Packs.ContentPackService;

namespace CCP.Avalonia.Tests;

/// <summary>
/// content-packs-wire: the startup seed (WPF App.xaml.cs:3163 / :448) and the flash, video and bubble
/// pools drawing active content-pack media (WPF FlashService / VideoService / BubbleCountService).
/// </summary>
public sealed class ContentPacksWireTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-packwire-" + Guid.NewGuid().ToString("N"));
    private readonly AppSettings _settings = new();
    private readonly ContentPackStore? _oldStore = ContentPackStore.Current;
    private readonly PackService? _oldService = PackService.Current;
    private readonly Func<string?> _oldBaseUrl = PackService.DefaultBaseUrl;
    private readonly Func<int>? _oldVideoCount = CoreProgram.ActivePackVideoCountProvider;

    public ContentPacksWireTests() => Directory.CreateDirectory(_root);

    public void Dispose()   // P02: every static this class touches goes back
    {
        FlashOverlay.PackImages.CleanupTemps();
        ContentPackStore.Current = _oldStore;
        PackService.Current = _oldService;
        PackService.DefaultBaseUrl = _oldBaseUrl;
        CoreProgram.ActivePackVideoCountProvider = _oldVideoCount;
        try { Directory.Delete(_root, true); } catch { }
    }

    /// <summary>One installed pack in the WPF layout (manifest + encrypted content), auto-activated by Rescan.</summary>
    private ContentPackStore StoreWithPack(params (string File, string Type)[] files)
    {
        var guid = Guid.NewGuid().ToString("N");
        var dir = Path.Combine(_root, ".packs", guid);
        Directory.CreateDirectory(Path.Combine(dir, "content"));
        var manifest = new InstalledPackManifest { PackId = "p1", PackGuid = guid, PackName = "One", InstalledDate = DateTime.UtcNow };
        foreach (var (file, type) in files)
        {
            var obf = PackEncryptionService.GenerateObfuscatedFilename() + ".enc";
            File.WriteAllBytes(Path.Combine(dir, "content", obf), PackEncryptionService.Encrypt(System.Text.Encoding.UTF8.GetBytes("p1/" + file)));
            manifest.Files.Add(new PackFileEntry { OriginalName = file, ObfuscatedName = obf, FileType = type, Extension = Path.GetExtension(file) });
        }
        PackEncryptionService.SaveEncryptedManifest(JsonConvert.SerializeObject(manifest), Path.Combine(dir, ".manifest.enc"));
        return new ContentPackStore(() => _root, () => _settings, () => { }, tempDir: () => Path.Combine(_root, ".temp"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlashLifecycleRetriesRefusedPackDeletes(bool closing)
    {
        var store = StoreWithPack(("a.png", "image"));
        ContentPackStore.Current = store;
        var path = FlashOverlay.NextFlashPath(Array.Empty<string>())!;
        File.Delete(path);
        Directory.CreateDirectory(path);
        void Sweep()
        {
            if (closing) FlashOverlay.CloseAll(final: false);
            else typeof(ConditioningControlPanel.Avalonia.Views.Tabs.AssetsTabView)
                .GetMethod("InvalidateAssetPoolsAfterSelectionChange", System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic)!.Invoke(null, null);
        }
        Sweep();
        Assert.True(FlashOverlay.PackImages.TempCount > 0);
        Directory.Delete(path);
        File.WriteAllText(path, "released");
        Sweep();
        Assert.False(File.Exists(path));
        Assert.Equal(0, FlashOverlay.PackImages.TempCount);
    }

    [Fact]
    public void StartContentPacks_SeedsStoreServiceAndVideoCount_AndASandboxSendsNothing()
    {
        var store = StoreWithPack(("v.mp4", "video"), ("a.png", "image"));

        AvApp.StartContentPacks(store, userDataDir: _root, overrideUrl: null);
        Assert.Same(store, ContentPackStore.Current);
        Assert.NotNull(PackService.Current);
        Assert.Null(PackService.DefaultBaseUrl());                        // sandbox, no loopback: nothing sent
        Assert.Equal(1, CoreProgram.ActivePackVideoCount());               // WPF App.xaml.cs:448

        AvApp.StartContentPacks(store, _root, "http://127.0.0.1:9/");
        Assert.Equal("http://127.0.0.1:9", PackService.DefaultBaseUrl());  // honoured loopback
        AvApp.StartContentPacks(store, null, null);
        Assert.Equal("https://codebambi-proxy.vercel.app", PackService.DefaultBaseUrl());   // WPF ProxyBaseUrl
    }

    [Fact]
    public void Startup_SeedsThePacks_ExitSweepsDecrypts_AndPoolsAreWired()
    {
        static string Code(params string[] rel) => Regex.Replace(   // P23: comments and string literals never count
            File.ReadAllText(Path.Combine(Root(), Path.Combine(rel))),
            @"//[^\n]*|/\*.*?\*/|@?""(?:[^""\\]|\\.)*""", " ", RegexOptions.Singleline);

        var app = Code("CCP.Avalonia", "App.axaml.cs");
        Assert.Matches(@"StartContentPacks\(new ContentPackStore\(\),", app);
        Assert.Contains("ContentPackStore.Current?.CleanupTempFiles()", app);
        Assert.Matches(@"ContentPackStore\.SweepStaleTemps\(Path\.Combine\(CorePaths\.EffectiveAssets,\s*\),\s*sandboxed \? null : Path\.GetTempPath\(\)\)", app);
        Assert.Contains("ContentPackStore.Current?.Rescan()", Code("CCP.Avalonia", "Views", "Windows", "MainShellWindow.Settings.cs"));
        var pool = @"PackVideos\s*=\s*new ConditioningControlPanel\.Services\.PackMediaPool\(ConditioningControlPanel\.Services\.ContentPackStore\.VideoType\)";
        Assert.Matches(pool, Code("CCP.Avalonia", "Views", "Overlays", "MandatoryVideoOverlay.cs"));
        Assert.Matches(pool, Code("CCP.Avalonia", "Views", "Windows", "BubbleCountHost.cs"));
    }

    [Fact]
    public void Flash_DrawsActivePackImages_AndFallsBackToTheFolder()
    {
        var store = StoreWithPack(("a.png", "image"));
        ContentPackStore.Current = store;

        var picked = FlashOverlay.NextFlashPath(Array.Empty<string>());   // packs only: always a pack image
        Assert.NotNull(picked);
        Assert.Equal("p1/a.png", File.ReadAllText(picked!));

        store.DeactivatePack("p1");
        var disk = Path.Combine(_root, "d.png");
        Assert.Equal(disk, FlashOverlay.NextFlashPath(new[] { disk }));
        Assert.True(store.IsPackInstalled("p1"));   // a deactivated pack stays installed
    }

    [Fact]
    public void NoVideosMessage_CountsInstalledAndActivePacks_FromTheStore()
    {
        var store = StoreWithPack(("a.png", "image"));
        ContentPackStore.Current = store;
        var s = new MandatoryVideoScheduler(MandatoryVideoOverlay.Instance, library: Array.Empty<string>);
        string Expected(int installed, int active) => NoVideosReason.Message(Path.Combine(CorePaths.EffectiveAssets, "videos"),
            s.LastFunnelEnabled, s.LastFunnelDuration, CoreSettings.Current.VideoMinDurationSeconds,
            CoreSettings.Current.VideoMaxDurationSeconds, installed, active);

        Assert.Equal(Expected(1, 1), MandatoryVideoOverlay.NoVideosMessage(s));   // active pack without videos
        store.DeactivatePack("p1");
        Assert.Equal(Expected(1, 0), MandatoryVideoOverlay.NoVideosMessage(s));   // installed, none active
        Assert.NotEqual(Expected(1, 1), Expected(1, 0));
    }

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
