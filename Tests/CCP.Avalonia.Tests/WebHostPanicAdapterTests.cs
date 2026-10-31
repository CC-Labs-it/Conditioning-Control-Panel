using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WebHostPanicAdapterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PanicUnloadsTheNativeDocumentAndReattachmentReplaysBlank(bool detached) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            var media = new Uri("http://127.0.0.1:5555/scrolller-media/images/a.mp4");
            var blank = new Uri("about:blank");
            var host = new WebHost { Source = media };
            var native = new NativeWebView();
            typeof(WebHost).GetField("_web", Private)!.SetValue(host, native);
            native.NavigationStarted += host.OnNavigationStarted;
            native.NavigationCompleted += (_, e) => host.OnNavigationCompleted(e.Request);
            var adapter = new Adapter();
            object? active = adapter.Proxy;
            BindControl(native, () => active);
            NativeEvent(native, "ControlHostImplOnAdapterCreated", adapter.Proxy);
            native.Source = media;
            Assert.True(adapter.Playing); // a live document, not merely a managed Source
            native.Source = null!;
            native.Stop();
            Assert.True(adapter.Playing); // reproduce WebView 12.1: neither operation unloads it
            native.Source = media;

            host.AllowNavigation = _ => false; // the page allowlist must not block panic's blank
            if (detached)
            {
                NativeEvent(native, "ControlHostImplOnAdapterDeinitialized", adapter.Proxy);
                active = null;
                host.NewScrolllerEngine();
            }
            PanicSurfaces.All.Single(s => s.Id == "scrolller-web").Stop(null);
            Assert.Null(host.Source);
            if (!detached)
            {
                Assert.False(adapter.Playing);
                Assert.Equal(blank, adapter.Navigations.Last());
                // Late redirects/completions cannot revive the old document or its managed URL.
                adapter.Navigate(media);
                Assert.Equal(blank, adapter.Current);
                adapter.Complete(media); // the native control also updates its private replay URL
                Assert.Null(host.CurrentUrl);
                NativeEvent(native, "ControlHostImplOnAdapterDeinitialized", adapter.Proxy);
                active = null;
                host.NewScrolllerEngine();
            }
            var replacement = new Adapter();
            active = replacement.Proxy;
            NativeEvent(native, "ControlHostImplOnAdapterCreated", replacement.Proxy);
            // The real NativeWebView callback replays its private _lastSource into this adapter.
            Assert.Equal(new[] { blank }, replacement.Navigations);
            Assert.False(replacement.Playing);
            host.NewScrolllerEngine()(true);
            Assert.All(replacement.Navigations, url => Assert.Equal(blank, url));
            Assert.False(replacement.Playing);
            host.AllowNavigation = null;
            host.Navigate(media); // explicit use after panic can start a new document
            Assert.True(replacement.Playing);
            host.Source = null;
            return Task.CompletedTask;
        });

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    // WebView's adapter interfaces are internal. DispatchProxy keeps the test at its actual
    // adapter boundary without a production factory override or a second native-control builder.
    private static object Proxy(string name, Func<MethodInfo, object?[]?, object?> call)
    {
        var type = typeof(NativeWebView).Assembly.GetType("Avalonia.Controls." + name, true)!;
        var proxy = DispatchProxy.Create(type, typeof(WebViewDispatch));
        ((WebViewDispatch)proxy).Call = call;
        return proxy;
    }

    private static void BindControl(NativeWebView native, Func<object?> adapter)
    {
        var impl = Proxy("INativeWebViewControlImpl", (method, _) =>
            method.Name == "TryGetAdapter" ? adapter() : null);
        var type = typeof(NativeWebView).Assembly.GetType("Avalonia.Controls.INativeWebViewControlImpl", true)!;
        var tcsType = typeof(TaskCompletionSource<>).MakeGenericType(type);
        var tcs = Activator.CreateInstance(tcsType)!;
        tcsType.GetMethod("SetResult")!.Invoke(tcs, new[] { impl });
        typeof(NativeWebView).GetField("_controlHostImplTcs", Private)!.SetValue(native, tcs);
    }

    private static void NativeEvent(NativeWebView native, string name, object adapter) =>
        typeof(NativeWebView).GetMethod(name, Private)!.Invoke(native, new object?[] { null, adapter });

    private sealed class Adapter
    {
        internal readonly object Proxy;
        internal readonly List<Uri> Navigations = new();
        internal Uri? Current;
        internal bool Playing;
        private Delegate? _starting, _completed;

        internal Adapter() => Proxy = WebHostPanicAdapterTests.Proxy("IWebViewAdapter", (method, args) =>
        {
            switch (method.Name)
            {
                case "add_NavigationStarted": _starting = Delegate.Combine(_starting, (Delegate)args![0]!); break;
                case "remove_NavigationStarted": _starting = Delegate.Remove(_starting, (Delegate)args![0]!); break;
                case "add_NavigationCompleted": _completed = Delegate.Combine(_completed, (Delegate)args![0]!); break;
                case "remove_NavigationCompleted": _completed = Delegate.Remove(_completed, (Delegate)args![0]!); break;
                case "Navigate": Navigate((Uri)args![0]!); break;
                case "Stop": return true; // stopping a load does not pause a playing document
                case "get_Source": return Current;
                case "get_Handle": return IntPtr.Zero;
                case "get_HandleDescriptor": return "panic-test";
                case "get_CanGoBack": case "get_CanGoForward": return false;
            }
            return null;
        });

        internal void Complete(Uri uri) => _completed?.DynamicInvoke(Proxy,
            new WebViewNavigationCompletedEventArgs { Request = uri, IsSuccess = true });

        internal void Navigate(Uri uri)
        {
            var request = new WebViewNavigationStartingEventArgs { Request = uri };
            _starting?.DynamicInvoke(Proxy, request);
            if (request.Cancel) return;
            Navigations.Add(uri);
            Current = uri;
            Playing = uri != new Uri("about:blank");
        }
    }
}

public class WebViewDispatch : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Call = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!, args);
}
