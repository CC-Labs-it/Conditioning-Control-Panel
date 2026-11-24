using System;
using System.Text.Json;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform.Deeper;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class SettingsTabView
{
    private readonly EnhancementHostService _browserEnhanceHost = new();
    private static readonly SpeakPromptRuntime BrowserSpeakRuntime = SpeakPromptRuntime.CreateProduction(() => Views.Windows.MainShellWindow.Current);
    internal Func<TriggerEffectAction, IPlaybackTimeSource, SpeakPromptSession>? BrowserSpeakSessionFactory;
    internal WebHostVideoTimeSource? BrowserEnhanceSource => _browserEnhanceSource;
    internal bool BrowserAutomaticPolling = true;
    private WebHostVideoTimeSource? _browserEnhanceSource;
    private Uri? _browserResumeUrl;
    internal string? BrowserEnhanceResumeToken { get; private set; }
    internal EnhancementHostService BrowserEnhanceHost => _browserEnhanceHost;

    private void HookBrowserEnhance()
    {
        BrowserWebHost.NavigationStarting += StopBrowserEnhancement;
        BrowserWebHost.WebMessage += OnBrowserEnhanceResume;
        BrowserWebHost.NavigationCompleted += url => EvaluateBrowserEnhancement(url.ToString());
        AttachedToVisualTree += (_, _) => { if (BrowserEnhanceResumeToken == null) EvaluateBrowserEnhancement(BrowserWebHost.CurrentUrl?.ToString()); };
        DetachedFromVisualTree += (_, _) => StopBrowserEnhancement();
        UpdateBrowserEnhanceStatus(null);
    }

    private void ToggleEnhanceIfPossible_Changed(object? sender, RoutedEventArgs e)
    {
        if (_browserCardSyncing) return;
        CoreSettings.Current.BrowserEnhanceIfPossible = ToggleEnhanceIfPossible.IsChecked == true;
        CoreSettings.Save();
        EvaluateBrowserEnhancement(BrowserWebHost.CurrentUrl?.ToString());
    }

    internal void StopBrowserEnhancement()
    {
        BrowserEnhanceResumeToken = null;
        _browserResumeUrl = null;
        _browserEnhanceHost.Unload();
        _browserEnhanceSource?.Dispose();
        _browserEnhanceSource = null;
        UpdateBrowserEnhanceStatus(null);
    }

    internal void StopBrowserEnhancementForPanic()
    {
        bool rearm = _browserEnhanceHost.IsRunning || BrowserEnhanceResumeToken != null;
        var url = BrowserWebHost.CurrentUrl;
        _browserEnhanceSource?.RetirePlayback();
        StopBrowserEnhancement();
        if (rearm && url != null)
        {
            _browserResumeUrl = url;
            BrowserEnhanceResumeToken = Guid.NewGuid().ToString("N");
        }
    }

    // Appended to the panic pause in the same page script: only a later playing event
    // from the first video can rebind. The nonce rejects delayed messages from old pages.
    internal string BrowserEnhanceResumeScript()
    {
        if (BrowserEnhanceResumeToken is not { } token) return "";
        var id = JsonSerializer.Serialize(token);
        return "(function(){var id=" + id + ";function resume(e){var v=document.querySelector('video');" +
            "if(e.target!==v||v.paused)return;document.removeEventListener('playing',resume,true);" +
            "var m=JSON.stringify({type:'ccpEnhancementResume',id:id});" +
            "if(typeof window.invokeCSharpAction==='function')window.invokeCSharpAction(m);" +
            "else if(window.chrome&&window.chrome.webview)window.chrome.webview.postMessage(m);}" +
            "document.addEventListener('playing',resume,true);})();";
    }

    private void OnBrowserEnhanceResume(string body)
    {
        if (BrowserEnhanceResumeToken is not { } token || BrowserWebHost.ActiveNavigation != null ||
            !Equals(_browserResumeUrl, BrowserWebHost.CurrentUrl)) return;
        try
        {
            using var outer = JsonDocument.Parse(body);
            using var json = JsonDocument.Parse(outer.RootElement.ValueKind == JsonValueKind.String ? outer.RootElement.GetString()! : body);
            var root = json.RootElement;
            if (root.GetProperty("type").GetString() != "ccpEnhancementResume" || root.GetProperty("id").GetString() != token) return;
            EvaluateBrowserEnhancement(_browserResumeUrl?.ToString());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or System.Collections.Generic.KeyNotFoundException) { }
    }

    private void EvaluateBrowserEnhancement(string? url)
    {
        StopBrowserEnhancement();
        if (!CoreSettings.Current.BrowserEnhanceIfPossible || string.IsNullOrEmpty(url)) return;
        try
        {
            var match = Shell?.BrowserEnhancementLibrary?.FindMatch(url, MediaTypes.Video);
            if (match == null || !_browserEnhanceHost.LoadFromFile(match.FilePath)) return;
            if (!BrowserEnhancementDispatcher.Supports(_browserEnhanceHost.LoadedEnhancement!))
            {
                StopBrowserEnhancement();
                TxtEnhanceMatchStatus.Text = Loc.Get("browser_enhance_unsupported");
                return;
            }
            var source = _browserEnhanceSource = new WebHostVideoTimeSource(BrowserWebHost, BrowserAutomaticPolling);
            var webcam = new BrowserEnhancementWebcam();
            if (!_browserEnhanceHost.Bind(source, source.Attach, source.Detach, new SpeakActionDispatcher(BrowserSpeakSessionFactory ?? BrowserSpeakRuntime.CreateSession, new BrowserEnhancementDispatcher()), webcam))
            {
                webcam.Dispose();
                StopBrowserEnhancement();
                return;
            }
            UpdateBrowserEnhanceStatus(match);
            try { BarkHead.Engine?.Raise("EnhancementApplied", c => c.Set("enhancement_id", match.Name)); }
            catch (Exception ex) { Log.Debug("Browser enhancement bark: {Error}", ex.Message); }
        }
        catch (Exception ex) { Log.Warning(ex, "Browser enhancement failed"); StopBrowserEnhancement(); }
    }

    private void UpdateBrowserEnhanceStatus(EnhancementLibraryEntry? match)
    {
        TxtEnhanceMatchStatus.Text = !CoreSettings.Current.BrowserEnhanceIfPossible
            ? Loc.Get("browser_enhance_match_off") : match == null ? Loc.Get("browser_enhance_match_none")
            : string.Format(Loc.Get("browser_enhance_match_fmt"), string.IsNullOrEmpty(match.Name) ? "(untitled)" : match.Name);
    }
}
