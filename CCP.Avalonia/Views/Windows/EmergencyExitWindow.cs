using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Quiz;
using ConditioningControlPanel.Services.Possession;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

// WPF Services/EmergencyExit/EmergencyExitHostService.cs. Games and their clocks remain
// the exact shared Assets/web/emergency-exit scripts, not a second implementation.
internal sealed class EmergencyExitWindow : Window
{
    internal static EmergencyExitWindow? Current { get; private set; }
    private static LockdownService? _session;
    private static int _attempt;
    private static string _last = "";
    private readonly LockdownService _lockdown;
    private readonly WebHost _web = new();
    private readonly string _game;
    private Uri? _page;
    private bool _closed, _verdict, _ready;
    private IDisposable? _deadline;
    internal TimeSpan OutroTimeout { get; init; } = TimeSpan.FromSeconds(8);
    internal Func<double> Roll { get; set; } = Random.Shared.NextDouble;

    internal static void Open(Window? owner, LockdownService lockdown)
    {
        if (!lockdown.IsActive) return;
        if (Current is { } existing) { existing.Activate(); return; }
        if (!ReferenceEquals(_session, lockdown))
        {
            if (_session != null) _session.LockdownDeactivated -= ResetSession;
            _session = lockdown;
            _session.LockdownDeactivated += ResetSession;
            ResetSession();
        }
        try { lockdown.NotifyEscapeAttempt(EscapeKinds.EmergencyExit); } catch (Exception ex) { Log.Debug(ex, "EmergencyExit tripwire"); }
        try { ChasterHead.Service?.NoteSafetyExit(); } catch (Exception ex) { Log.Debug(ex, "EmergencyExit chaster hold"); }
        _attempt++;
        _last = EmergencyExitRules.Pick(_last, Random.Shared);
        var window = new EmergencyExitWindow(lockdown, _last);
        Current = window;
        try
        {
            var server = WebAssetServer.Shared;
            window._page = new Uri(server.Url("emergency-exit/index.html", "game=" + _last + "&avalonia=1"));
            if (owner?.IsVisible == true) window.Show(owner); else window.Show();
            window._web.Navigate(window._page);
        }
        catch (Exception ex) { Log.Warning(ex, "Emergency Exit open failed"); window.Close(); }
    }

    private static void ResetSession() { _attempt = 0; _last = ""; }

    internal EmergencyExitWindow(LockdownService lockdown, string game)
    {
        _lockdown = lockdown;
        _game = game;
        Title = Loc.Get("ee_window_title");
        Width = 960; Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = _web;
        _web.AllowNavigation = url => _page != null && IntakeRun.SameDocument(url, _page);
        _web.WebMessage += json =>
        {
            if (_page != null && _web.CurrentUrl != null && IntakeRun.SameDocument(_web.CurrentUrl, _page))
                HandleMessage(json);
        };
        _web.NavigationCompleted += _ => HandleMessage("{\"type\":\"ready\"}");
        _web.NavigationFinished += (_, ok) => { if (!ok) Close(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        _lockdown.LockdownDeactivated += OnEnded;
        Closed += (_, _) =>
        {
            _closed = true;
            _deadline?.Dispose();
            _lockdown.LockdownDeactivated -= OnEnded;
            if (ReferenceEquals(Current, this)) Current = null;
        };
    }

    private void OnEnded()
    {
        if (!_verdict) Dispatcher.UIThread.Post(() => { if (!_closed) Close(); });
    }

    internal void HandleMessage(string json)
    {
        if (_closed) return;
        JObject message;
        try { message = JObject.Parse(json); } catch (JsonException) { return; }
        switch ((string?)message["type"])
        {
            case "ready":
                if (!_lockdown.IsActive || _verdict || _ready) return;
                _ready = true;
                Post(new
                {
                    type = "init", game = _game, attempt = Math.Max(1, _attempt),
                    restarts = _lockdown.RestartCount,
                    remainingSec = Math.Max(0, (int)_lockdown.Remaining.TotalSeconds),
                    durationSec = Math.Max(0, (int)_lockdown.Duration.TotalSeconds),
                    photosafe = CoreSettings.Current.LockdownPhotosafe,
                    lang = LocalizationManager.Instance.CurrentLanguage,
                    mod = Mod(), assets = new { gifs = Gifs() }
                });
                break;
            case "game-finished":
                if (_verdict || !_lockdown.IsActive) return;
                _verdict = true;
                var back = EmergencyExitRules.SendBack(_game, (string?)message["result"], Roll());
                // Post first, apply synchronously, close on outro or the instance-owned 8 s deadline.
                Post(new { type = "verdict", outcome = back ? "sendback" : "escape" });
                _deadline = DispatcherTimer.RunOnce(() => { if (!_closed) Close(); }, OutroTimeout);
                if (back) _lockdown.RestartTimer(_game); else _lockdown.Deactivate();
                break;
            case "quit":
            case "outro-done":
                Close();
                break;
        }
    }

    private async void Post(object message)
    {
        try { await _web.PostJsonAsync(JsonConvert.SerializeObject(message)); }
        catch (Exception ex) { Log.Debug(ex, "Emergency Exit post failed"); }
    }

    private static object Mod()
    {
        var mod = App.Mods?.ActiveMod;
        var id = App.Mods?.ActiveModId ?? "";
        var name = mod?.Manifest?.Name ?? "";
        var (honorific, subject) = id switch
        {
            "builtin-bambisleep" => ("good girl", "Bambi"),
            "builtin-sissyhypno" => ("sissy", "sissy"),
            "builtin-locked" => ("pet", "pet"),
            "" or "builtin-ccp-default" => ("sweetie", "sweetie"),
            _ => (string.IsNullOrWhiteSpace(mod?.Manifest?.Identity?.PetName) ? "sweetie" : mod.Manifest.Identity.PetName.Trim(),
                  string.IsNullOrWhiteSpace(name) ? "sweetie" : name)
        };
        return new { id, name = string.IsNullOrWhiteSpace(name) ? id : name, honorific, subject };
    }

    private static string[] Gifs()
    {
        try
        {
            var root = CorePaths.EffectiveAssets;
            var folder = Path.Combine(root, "images");
            if (!Directory.Exists(folder)) return Array.Empty<string>();
            return Directory.EnumerateFiles(folder, "*.gif", SearchOption.AllDirectories)
                .OrderBy(_ => Random.Shared.Next()).Take(12)
                .Select(f => WebAssetServer.Shared.AssetUrl(Path.GetRelativePath(root, f).Replace('\\', '/'))).ToArray();
        }
        catch (Exception ex) { Log.Debug(ex, "Emergency Exit media unavailable"); return Array.Empty<string>(); }
    }
}
