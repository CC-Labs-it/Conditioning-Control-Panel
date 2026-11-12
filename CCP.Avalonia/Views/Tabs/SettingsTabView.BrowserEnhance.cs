using System;
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
    private WebHostVideoTimeSource? _browserEnhanceSource;
    internal EnhancementHostService BrowserEnhanceHost => _browserEnhanceHost;

    private void HookBrowserEnhance()
    {
        BrowserWebHost.NavigationStarting += StopBrowserEnhancement;
        BrowserWebHost.NavigationCompleted += url => EvaluateBrowserEnhancement(url.ToString());
        AttachedToVisualTree += (_, _) => EvaluateBrowserEnhancement(BrowserWebHost.CurrentUrl?.ToString());
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
        _browserEnhanceHost.Unload();
        _browserEnhanceSource?.Dispose();
        _browserEnhanceSource = null;
        UpdateBrowserEnhanceStatus(null);
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
            var source = _browserEnhanceSource = new WebHostVideoTimeSource(BrowserWebHost);
            var webcam = new BrowserEnhancementWebcam();
            if (!_browserEnhanceHost.Bind(source, source.Attach, source.Detach, new BrowserEnhancementDispatcher(), webcam))
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
