using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>The Spiral-only ready/state handshake over the shared native WebHost carrier.</summary>
    public sealed class SpiralEmbedView : Grid, IDisposable
    {
        private readonly string _mode;
        private readonly SpiralEmbedBridge _bridge = new();
        private WebHost? _web;
        private bool _started, _disposed;
        public bool IsReady => _bridge.IsReady;
        public bool HasFailed { get; private set; }
        public string Url => SpiralEmbedBridge.BuildUrl(_mode);
        public event EventHandler? Ready;
        public event EventHandler? Navigated;
        public event EventHandler<string>? Failed;

        // Headless tests exercise exactly the script payload sent to WebHost without a browser.
        internal Func<string, Task>? SendJson { get; set; }
        internal WebHost? Host => _web;

        public SpiralEmbedView(string mode)
        {
            _mode = mode == "map" ? "map" : "mini";
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x36));
        }

        public void Start()
        {
            if (_started || _disposed) return;
            _started = true;
            _web = new WebHost { AllowNavigation = SpiralEmbedBridge.AllowsNavigation, InjectCarrier = true };
            _web.WebMessage += Receive;
            _web.NavigationFinished += OnNavigated;
            Children.Add(_web);
            if (!_web.HasEngine)
            {
                Fail(WebHost.UnavailableReason ?? "browser unavailable");
                return;
            }
            _web.Source = new Uri(Url);
        }

        public void PostState(DescentBlock? block)
        {
            if (_disposed) return;
            var state = SpiralEmbedBridge.BuildState(block,
                CoreSettings.Current.MotionLevel != MotionLevel.Full, AmbientFxCanvas.Env.CurrentTier);
            if (_bridge.Post(state.ToString(Formatting.None)) is { } message) _ = Send(message);
        }

        internal void Receive(string message)
        {
            if (_disposed) return;
            var wasReady = IsReady;
            if (_bridge.Receive(message) is { } latest) _ = Send(latest);
            if (IsReady && !wasReady) Ready?.Invoke(this, EventArgs.Empty);
        }

        private Task Send(string message) => SendJson?.Invoke(message) ??
            _web?.PostJsonAsync(message) ?? Task.CompletedTask;

        internal void OnNavigated(Uri url, bool success)
        {
            if (_disposed || !SpiralEmbedBridge.AllowsNavigation(url)) return;
            if (!success)
            {
                Fail("navigation failed");
                return;
            }
            Navigated?.Invoke(this, EventArgs.Empty);
        }

        private void Fail(string reason)
        {
            HasFailed = true;
            Dispose();
            Failed?.Invoke(this, reason);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bridge.Reset();
            if (_web is { } web)
            {
                web.WebMessage -= Receive;
                web.NavigationFinished -= OnNavigated;
                web.Source = null;
                Children.Remove(web);
                _web = null;
            }
        }
    }
}
