using System;
using System.Runtime.CompilerServices;
using Microsoft.Web.WebView2.Core;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF Controls/SpiralEmbedView.cs:178-190,229-233. Only Spiral opts into this policy.
internal static class SpiralWebView2Policy
{
    internal interface ICore
    {
        bool AreDevToolsEnabled { set; }
        bool AreDefaultContextMenusEnabled { set; }
        bool IsStatusBarEnabled { set; }
        bool AreBrowserAcceleratorKeysEnabled { set; }
        bool IsZoomControlEnabled { set; }
        bool IsBuiltInErrorPageEnabled { set; }
        bool IsWebMessageEnabled { set; }
        event Action<string> ProcessFailed;
    }

    // Keep SDK activation behind the Windows adapter boundary, as ScrolllerWebView2Gate does.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static IDisposable Attach(IntPtr core, Action<string> failed)
        => Apply(new NativeCore(CoreWebView2.CreateFromComICoreWebView2(core)), failed);

    internal static IDisposable Apply(ICore core, Action<string> failed)
    {
        core.AreDevToolsEnabled = false;
        core.AreDefaultContextMenusEnabled = false;
        core.IsStatusBarEnabled = false;
        core.AreBrowserAcceleratorKeysEnabled = false;
        core.IsZoomControlEnabled = false;
        core.IsBuiltInErrorPageEnabled = false;
        core.IsWebMessageEnabled = true;
        return new Lease(core, failed);
    }

    private sealed class Lease : IDisposable
    {
        private ICore? _core;
        private readonly Action<string> _failed;
        public Lease(ICore core, Action<string> failed)
        {
            _core = core;
            _failed = failed;
            core.ProcessFailed += OnFailed;
        }
        private void OnFailed(string reason) { if (_core != null) _failed(reason); }
        public void Dispose()
        {
            var core = _core;
            _core = null;
            if (core != null) core.ProcessFailed -= OnFailed;
        }
    }

    private sealed class NativeCore(CoreWebView2 core) : ICore
    {
        public bool AreDevToolsEnabled { set => core.Settings.AreDevToolsEnabled = value; }
        public bool AreDefaultContextMenusEnabled { set => core.Settings.AreDefaultContextMenusEnabled = value; }
        public bool IsStatusBarEnabled { set => core.Settings.IsStatusBarEnabled = value; }
        public bool AreBrowserAcceleratorKeysEnabled { set => core.Settings.AreBrowserAcceleratorKeysEnabled = value; }
        public bool IsZoomControlEnabled { set => core.Settings.IsZoomControlEnabled = value; }
        public bool IsBuiltInErrorPageEnabled { set => core.Settings.IsBuiltInErrorPageEnabled = value; }
        public bool IsWebMessageEnabled { set => core.Settings.IsWebMessageEnabled = value; }
        private Action<string>? _failed;
        public event Action<string> ProcessFailed
        {
            add { if (_failed == null) core.ProcessFailed += OnFailed; _failed += value; }
            remove { _failed -= value; if (_failed == null) core.ProcessFailed -= OnFailed; }
        }
        private void OnFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
            => _failed?.Invoke(e.ProcessFailedKind.ToString());
    }
}
