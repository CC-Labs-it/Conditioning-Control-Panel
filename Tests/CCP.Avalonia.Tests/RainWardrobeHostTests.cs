using System; using System.Linq; using System.IO; using System.Collections.Generic; using System.Threading.Tasks;
using Avalonia; using Avalonia.Controls; using Avalonia.Headless; using Avalonia.Interactivity;
using CCP.Avalonia.Testing; using ConditioningControlPanel; using ConditioningControlPanel.Avalonia.Platform; using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Windows; using Newtonsoft.Json.Linq; using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class RainWardrobeHostTests
{
    static void Setup()
    {
        if (Application.Current == null) AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options())
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    [Fact]
    public Task DraftStaysOnPageUntilApplyAndOwnershipKeepsIt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup(); CoreSettings.Current.PlayerLevel = 1; CoreSettings.Current.HighestLevelEver = 12; CoreSettings.Current.RainLook = "";
        CoreSettings.Current.OfflineMode = false; CoreSettings.Current.UnifiedId = "rain-test"; CoreSecrets.RetrieveProvider = _ => "test";
        var web = Path.Combine(AppContext.BaseDirectory, "Resources", "web"); using var host = new RainWardrobeHost(web, Path.GetTempPath());
        var posts = new List<string>(); host.Web.ScriptInvoker = script => { posts.Add(script); return Task.FromResult<string?>(null); };
        host.Web.OnWebMessage("{\"type\":\"rain-ready\"}"); Assert.Contains(posts, p => p.Contains("\"level\":12") && p.Contains("\"applyMode\":true"));
        const string look = "{\"rev\":1,\"items\":{\"dress\":\"midnight-cocktail\",\"accessories\":[\"velvet-choker\"]}}";
        host.Web.OnWebMessage("{\"type\":\"rain-look\",\"look\":" + look + "}"); Assert.Equal("", CoreSettings.Current.RainLook);   // try-on is a draft
        posts.Clear(); host.Web.OnWebMessage("{\"type\":\"rain-apply\",\"look\":" + look + "}");
        Assert.Equal("", CoreSettings.Current.RainLook); Assert.Contains(posts, p => p.Contains("\"reason\":\"clamped\"") && p.Contains("velvet-choker"));
        posts.Clear(); PrizeOwnership.ApplySnapshot("rain-test", 1, ["cos.rain.dress.midnight-cocktail"]);
        Assert.Contains(posts, p => p.Contains("rain-owned") && p.Contains("cos.rain.dress.midnight-cocktail")); Assert.DoesNotContain(posts, p => p.Contains("\"init\""));
        host.Web.OnWebMessage("{\"type\":\"rain-apply\",\"look\":" + look + "}"); Assert.Contains("midnight-cocktail", CoreSettings.Current.RainLook); Assert.Contains(posts, p => p.Contains("rain-applied") && !p.Contains("failed"));
        using (var server = new WebAssetServer(web) { RainAssetsRoot = Path.GetTempPath() }) using (var http = new System.Net.Http.HttpClient())
        {   // the carrier is in place before the page's modules register their chrome.webview listeners
            var bridge = await http.GetStringAsync(server.Url("rain-wardrobe/host-bridge.js"));
            Assert.StartsWith(RainWardrobeHost.CarrierPrelude, bridge); Assert.Contains("'/ccp.rain-assets/'", bridge);
        }
        host.Window.Show(); host.Window.Close(); posts.Clear(); CoreSettings.Current.RainLook = ""; PrizeOwnership.ApplySnapshot("rain-test", 2, []);
        host.Web.OnWebMessage("{\"type\":\"rain-apply\",\"look\":" + look + "}"); Assert.Empty(posts); Assert.Null(host.Window.Content); Assert.Equal("", CoreSettings.Current.RainLook);
    });
    [Fact]
    public Task TubeDoorOpensOneWindowAndPanicDisposesIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup(); var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var old = Environment.GetEnvironmentVariable("CCP_RAIN_ASSETS_DIR"); Environment.SetEnvironmentVariable("CCP_RAIN_ASSETS_DIR", dir);
        using var server = new WebAssetServer(dir) { RainAssetsRoot = dir };
        Assert.Null(server.ResolveFile("/ccp.rain-assets/../settings.json")); Assert.Null(server.ResolveFile("/ccp.rain-assets/secret.json"));
        File.WriteAllText(Path.Combine(dir, "avatar.glb"), "test"); File.WriteAllText(Path.Combine(dir, "pelvis-data.js"), "test");
        Assert.Equal(Path.Combine(dir, "avatar.glb"), server.ResolveFile("/ccp.rain-assets/avatar.glb"));
        AvatarTubeWindow? tube = null;
        try
        {
            tube = new AvatarTubeWindow(); CoreMods.ActiveModIdProvider = () => "builtin-ccp-default"; CoreSettings.Current.AvatarGeneration = "v2"; tube.UpdateQuickMenuState();
            var door = tube.FindControl<MenuItem>("MenuItemRainWardrobe")!; Assert.True(door.IsVisible);
            door.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); var host = RainWardrobeHost.Active; Assert.NotNull(host);
            door.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); Assert.Same(host, RainWardrobeHost.Active);
            Assert.Contains("host=ccp", host.Web.Source!.Query);
            PanicSurfaces.All.Single(s => s.Id == "rain-wardrobe").Stop(null);
            Assert.Null(RainWardrobeHost.Active); Assert.Null(host.Window.Content);
            CoreSettings.Current.AvatarGeneration = "v1"; tube.UpdateQuickMenuState(); Assert.False(door.IsVisible);
        }
        finally { RainWardrobeHost.Active?.Dispose(); tube?.Close(); Environment.SetEnvironmentVariable("CCP_RAIN_ASSETS_DIR", old); Directory.Delete(dir, true); }
        return Task.CompletedTask;
    });
}
