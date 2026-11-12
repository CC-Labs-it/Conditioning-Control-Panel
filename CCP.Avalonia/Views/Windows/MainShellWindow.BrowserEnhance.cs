using System;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models.Deeper;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private BrowserEnhancementBridge? _browserEnhancement;
        internal BrowserEnhancementBridge? BrowserEnhancement => _browserEnhancement;

        internal void InitializeBrowserEnhancement(SettingsTabView page)
        {
            if (_browserEnhancement != null || page.FindControl<WebHost>("BrowserWebHost") is not { } web) return;
            EnsureDeeperLibrary();
            if (_deeperLibrary == null) return;
            EnhancementHostService.DispatcherFactory ??= () => new BrowserEnhancementDispatcher();
            EnhancementHostService.WebcamProvider ??= () => new BrowserEnhancementWebcam();
            _browserEnhancement = new BrowserEnhancementBridge(web, _deeperLibrary, page.SetBrowserEnhanceMatch);
            Closed += (_, _) => { _browserEnhancement?.Dispose(); _browserEnhancement = null; };
            _browserEnhancement.Refresh(); // WPF BrowserEnhancementBridge.cs:35 backfills the live URL.
        }

        internal void RefreshBrowserEnhancement() => _browserEnhancement?.Refresh();
    }

    /// <summary>WPF Services/Deeper/BrowserEnhancementBridge.cs:17. Owns a host separate from player and editor.</summary>
    internal sealed class BrowserEnhancementBridge : IDisposable
    {
        private readonly WebHost _web;
        private readonly EnhancementLibrary _library;
        private readonly Action<EnhancementLibraryEntry?> _changed;
        private readonly EnhancementHostService _host = new();
        private WebHostVideoClock? _clock;
        private string? _boundPath;
        internal bool IsRunning => _host.IsRunning;

        internal BrowserEnhancementBridge(WebHost web, EnhancementLibrary library, Action<EnhancementLibraryEntry?> changed)
        {
            _web = web; _library = library; _changed = changed;
            _web.NavigationCompleted += OnNavigation;
        }
        private void OnNavigation(Uri _) => Refresh();
        internal void Refresh()
        {
            try
            {
                var url = _web.CurrentUrl?.AbsoluteUri;
                var match = CoreSettings.Current.BrowserEnhanceIfPossible && !string.IsNullOrEmpty(url)
                    ? _library.FindMatch(url, MediaTypes.Video) : null;
                if (match == null) { Unbind(); _changed(null); return; }
                if (_host.IsRunning && string.Equals(_boundPath, match.FilePath, StringComparison.OrdinalIgnoreCase))
                { _changed(match); return; }
                Unbind();
                if (!_host.LoadFromFile(match.FilePath)) { _changed(null); return; }
                var clock = new WebHostVideoClock(_web);
                _clock = clock;
                if (!_host.Bind(clock, clock.Attach, clock.Detach)) { Unbind(); _changed(null); return; }
                _boundPath = match.FilePath;
                _changed(match);
            }
            catch (Exception ex) { Log.Warning(ex, "Browser enhancement failed"); Unbind(); _changed(null); }
        }
        private void Unbind()
        {
            _host.UnbindEngine();
            _host.Unload();
            _clock?.Dispose();
            _clock = null; _boundPath = null;
        }
        public void Dispose() { _web.NavigationCompleted -= OnNavigation; Unbind(); _host.Dispose(); }
    }
}
