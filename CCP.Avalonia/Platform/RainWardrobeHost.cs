using System; using System.Collections.Generic; using System.IO; using System.Linq;
using Avalonia.Controls; using Avalonia.Threading; using ConditioningControlPanel.Avalonia.Views.Controls; using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion.Rain; using Newtonsoft.Json; using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Avalonia.Platform;
// Row 599's window protocol, with row 604's live access/clamp; bake-on-Apply is a separate lane.
internal sealed class RainWardrobeHost : IRainGrantSource, IRainProgressSource, IDisposable
{
    internal static RainWardrobeHost? Active;
    internal readonly WebHost Web = new();
    internal readonly Window Window;
    readonly WebAssetServer _server;
    bool _closed;
    readonly Models.AppSettings _settings = CoreSettings.Current;
    readonly Services.AchievementEngine? _achievements = App.Achievements;
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
        _settings.PropertyChanged += ProgressChanged;
        if (_achievements != null) _achievements.Unlocked += AchievementChanged;
        Store(CoreSettings.Current.RainLook);
        var locked = Access.Manifest.Items.Where(i => !Access.StateOf(i).Wearable).Select(i => i.Id);
        Web.Source = new Uri(_server.Url("rain-wardrobe/index.html", "host=ccp&look=" + Uri.EscapeDataString(CoreSettings.Current.RainLook)
            + "&locked=" + Uri.EscapeDataString(string.Join(',', locked)) + "&lockNote=" + Uri.EscapeDataString(Loc.Get("rain_wardrobe_locked"))));
    }
    internal void Message(string json)
    {
        if (_closed || json.Length > RainLookCodec.MaxChars + 256) return;
        try
        {
            var o = JObject.Parse(json);
            if ((string?)o["type"] == "rain-look" && o["look"] is JObject look) Store(look.ToString(Formatting.None));
            if ((string?)o["type"] is "ready" or "rain-ready") Refresh();
        }
        catch (JsonException) { }
    }
    void Store(string? look)
    {
        var clamped = RainLookClamp.Clamp(look, Access);
        if (clamped == CoreSettings.Current.RainLook) return;
        CoreSettings.Current.RainLook = clamped; CoreSettings.Save();
    }
    internal JObject Init() => JObject.FromObject(new { type = "init", applyMode = false,
        look = string.IsNullOrEmpty(CoreSettings.Current.RainLook) ? null : JObject.Parse(CoreSettings.Current.RainLook),
        grants = Access.Manifest.Items.Where(i => i.Grant != null && IsGranted(i.Grant)).Select(i => i.Grant),
        level = Level, achievements = UnlockedAchievements });
    void Refresh()
    {
        if (_closed) return;
        Store(CoreSettings.Current.RainLook);
        _ = Web.PostJsonAsync(Init().ToString(Formatting.None));
        _ = Web.PostJsonAsync(JsonConvert.SerializeObject(new { type = "rain-locked", locked = Access.Manifest.Items.Where(i => !Access.StateOf(i).Wearable).Select(i => i.Id) }));
    }
    void OwnershipChanged() { if (Dispatcher.UIThread.CheckAccess()) Refresh(); else Dispatcher.UIThread.Post(Refresh); }
    void AchievementChanged(object? sender, Models.Achievement e) => OwnershipChanged();
    void ProgressChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (e.PropertyName is "PlayerLevel" or "HighestLevelEver") OwnershipChanged(); }
    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        PrizeOwnership.Changed -= OwnershipChanged; _settings.PropertyChanged -= ProgressChanged;
        if (_achievements != null) _achievements.Unlocked -= AchievementChanged;
        Web.WebMessage -= Message; Web.Source = null; Window.Content = null; _server.Dispose();
        if (ReferenceEquals(Active, this)) Active = null;
        Window.Close();
    }
}
