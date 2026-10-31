using System; using System.Collections.Generic; using System.IO; using System.Linq;
using Avalonia.Controls; using Avalonia.Threading; using ConditioningControlPanel.Avalonia.Views.Controls; using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion.Rain; using Newtonsoft.Json; using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Avalonia.Platform;
internal sealed class RainWardrobeHost : IRainGrantSource, IRainProgressSource, IDisposable
{
    internal static RainWardrobeHost? Active;
    internal readonly WebHost Web = new(); internal readonly Window Window;
    readonly WebAssetServer _server; bool _closed;
    public int Level => Math.Max(CoreSettings.Current.PlayerLevel, CoreSettings.Current.HighestLevelEver);
    public IReadOnlyCollection<string> UnlockedAchievements => App.Achievements?.Progress?.UnlockedAchievements?.ToArray() ?? Array.Empty<string>();
    public bool IsGranted(string grant) => PrizeOwnership.IsGranted(grant);
    RainItemAccess Access => new(RainItemManifest.Shipped, this, this);
    internal static string AssetsDir => Environment.GetEnvironmentVariable("CCP_RAIN_ASSETS_DIR") is { } dir && !string.IsNullOrWhiteSpace(dir)
        ? Path.GetFullPath(dir.Trim()) : Path.Combine(CorePaths.UserData, "rain-assets");
    internal static void Open(Window? owner)
    {
        if (Active != null) { Active.Window.Activate(); return; }
        if (!File.Exists(Path.Combine(AssetsDir, "avatar.glb")) || !File.Exists(Path.Combine(AssetsDir, "pelvis-data.js")))
        { App.Notifications?.Show(Loc.Get("rain_wardrobe_missing")); return; }
        try { Active = new RainWardrobeHost(Path.Combine(AppContext.BaseDirectory, "Resources", "web"), AssetsDir); if (owner == null) Active.Window.Show(); else Active.Window.Show(owner); }
        catch { Active?.Dispose(); throw; }
    }
    internal RainWardrobeHost(string root, string assets)
    {
        _server = new WebAssetServer(root) { RainAssetsRoot = assets };
        Window = new Window { Title = Loc.Get("rain_wardrobe_title"), Width = 1180, Height = 760, Content = Web, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Web.AllowNavigation = uri => uri.GetLeftPart(UriPartial.Authority) == new Uri(_server.Url("")).GetLeftPart(UriPartial.Authority)
            && uri.AbsolutePath == "/rain-wardrobe/index.html";
        Web.EnvironmentRequested += e => { if (e is global::Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs w)
            w.UserDataFolder = Path.Combine(CorePaths.UserData, "browser_data_rainwardrobe_avalonia"); };
        Web.WebMessage += Message;
        Window.Closed += (_, _) => Dispose();
        PrizeOwnership.Changed += OwnershipChanged;
        Commit(RainLookClamp.Clamp(CoreSettings.Current.RainLook, Access));   // WPF Open:88-92, the stored look clamped on open
        Web.Source = new Uri(_server.Url("rain-wardrobe/index.html", "host=ccp&look=" + Uri.EscapeDataString(CoreSettings.Current.RainLook)
            + "&locked=" + Uri.EscapeDataString(string.Join(',', Locked())) + "&lockNote=" + Uri.EscapeDataString(Loc.Get("rain_wardrobe_locked"))));
    }
    internal const string CarrierPrelude = "(()=>{const w=window;if(w.chrome&&w.chrome.webview&&w.chrome.webview.postMessage)return;const t=new EventTarget();"
        + "t.postMessage=m=>{const s=typeof m==='string'?m:JSON.stringify(m);let n=0;const go=()=>{if(typeof w.invokeCSharpAction==='function')w.invokeCSharpAction(s);else if(++n<50)setTimeout(go,100);};go();};"
        + "w.chrome=w.chrome||{};w.chrome.webview=t;w.__ccpRnPush=j=>{let m;try{m=JSON.parse(j);}catch{return;}t.dispatchEvent(new MessageEvent('message',{data:m}));};})();\n";
    IEnumerable<string> Locked() => Access.Manifest.Items.Where(i => !Access.StateOf(i).Wearable).Select(i => i.Id);
    IEnumerable<string> Grants() => Access.Manifest.Items.Where(i => i.Grant != null && IsGranted(i.Grant)).Select(i => i.Grant!).Distinct().Order(StringComparer.Ordinal);
    void Post(object frame) => _ = Web.PostJsonAsync(JsonConvert.SerializeObject(frame));
    internal void Message(string json)
    {
        if (_closed || json.Length > RainLookCodec.MaxChars + 256) return;
        JObject o; try { o = JObject.Parse(json); } catch (JsonException) { return; }
        switch ((string?)o["type"])
        {
            case "rain-ready": Post(Init()); break;
            case "rain-apply": Apply(o["look"]); break;
            case "open-counter": App.Notifications?.Show(Loc.Get("rain_wardrobe_counter_soon")); break;   // WPF ShowCounterSoon, no counter host yet
        }
    }
    // WPF RainApplyController.OnApply:80. shortcut: commits without baking a pack (pack store row 603, tube playback row 606), bake when those land.
    void Apply(JToken? token)
    {
        var raw = token is JObject look ? look.ToString(Formatting.None) : "";
        var clamped = RainLookClamp.Clamp(raw, Access);
        if (clamped != RainLookCodec.Normalize(raw))
        { Post(new { type = "rain-applied", failed = true, reason = "clamped", look = clamped.Length > 0 ? JToken.Parse(clamped) : JObject.Parse("{\"rev\":1,\"items\":{},\"colors\":{}}") }); return; }
        Commit(clamped);
        Post(new { type = "rain-applied", lookHash = "" });
    }
    static void Commit(string look)   // RainPackHash cleared: no pack was baked for this look
    { if (look != RainLookCodec.Normalize(CoreSettings.Current.RainLook)) { CoreSettings.Current.RainLook = look; CoreSettings.Current.RainPackHash = ""; CoreSettings.Save(); } }
    internal JObject Init() => JObject.FromObject(new { type = "init", applyMode = true,
        look = RainLookClamp.Clamp(CoreSettings.Current.RainLook, Access) is { Length: > 0 } look ? JObject.Parse(look) : null,
        grants = Grants(), level = Level, achievements = UnlockedAchievements, locale = LocalizationManager.Instance.CurrentLanguage,
        motion = global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level.ToString().ToLowerInvariant(), gentle = CoreSettings.Current.GentleVisuals });
    void OwnershipChanged() { if (Dispatcher.UIThread.CheckAccess()) PostOwnership(); else Dispatcher.UIThread.Post(PostOwnership); }
    void PostOwnership() { if (!_closed) { Post(new { type = "rain-owned", grants = Grants() }); Post(new { type = "rain-locked", locked = Locked() }); } }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        PrizeOwnership.Changed -= OwnershipChanged;
        Web.WebMessage -= Message; Web.Source = null; Window.Content = null; _server.Dispose();
        if (ReferenceEquals(Active, this)) Active = null;
        Window.Close();
    }
}
