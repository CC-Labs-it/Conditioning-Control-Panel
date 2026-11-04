using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Themes.Fluent;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace CCP.Avalonia.NativeTests;

internal static class NativeProfile
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        var root = Directory.CreateTempSubdirectory("ccp-native-profile-").FullName;
        Environment.SetEnvironmentVariable("CCP_USERDATA_DIR", root);
        Environment.SetEnvironmentVariable("CCP_LINUX_WEBVIEW", "gtk");
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(root, true); } catch { } };
    }
}

// Run in its own process with a real X11 display and WebKitGTK, not Avalonia.Headless.
public sealed class JustDropNativeTests
{
    private const string Page = """
        <!doctype html><html><body><button id="order" autofocus>Open order</button>
        <script>
        window.chrome.webview.postMessage({type:'boot', installed:window.__ccpHostEscape===true});
        document.addEventListener('fullscreenchange', function() {
          if (!document.fullscreenElement) window.chrome.webview.postMessage({type:'left'});
        });
        document.getElementById('order').addEventListener('click', function() {
          document.documentElement.requestFullscreen().then(
            () => window.chrome.webview.postMessage({type:'entered'}),
            e => window.chrome.webview.postMessage({type:'error', message:String(e)}));
        });
        </script></body></html>
        """;

    public sealed class NativeTestApp : Application
    {
        public override void Initialize() => Styles.Add(new FluentTheme());
    }

    private static async Task Run(Func<GameWindow, TaskCompletionSource<string>, Task> check)
    {
        Assert.True(Environment.GetEnvironmentVariable("CCP_NATIVE_TESTS") == "1", "Run on an isolated display with CCP_NATIVE_TESTS=1");
        var dir = Directory.CreateTempSubdirectory("ccp-native-web-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "index.html"), Page);
            using var server = new WebAssetServer(dir);
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<NativeTestApp>().UsePlatformDetect().WithInterFont()
                        .SetupWithLifetime(new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown });
                var window = new GameWindow(GameWindow.Games[GameWindow.JustDropId]);
                var boot = NewMessage();
                var next = NewMessage();
                window.Web.WebMessage += body =>
                {
                    try
                    {
                        var msg = JObject.Parse(GameWindow.Unwrap(body));
                        if ((string?)msg["type"] == "boot") boot.TrySetResult(body);
                        else next.TrySetResult(body);
                    }
                    catch { }
                };
                window.Web.InjectCarrier = true;
                window.Web.AllowNavigation = uri => uri.IsLoopback;
                window.Web.Navigate(new Uri(server.Url("index.html")));
                window.Show();
                try
                {
                    Assert.True(window.Web.HasEngine, WebHostReason());
                    var first = JObject.Parse(await boot.Task.WaitAsync(TimeSpan.FromSeconds(25)));
                    Assert.True((bool?)first["installed"] == true, "document-start listener missing");
                    await check(window, next);
                }
                finally { window.Close(); }
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    private static string WebHostReason() => ConditioningControlPanel.Avalonia.Views.Controls.WebHost.UnavailableReason ?? "No native engine";
    private static TaskCompletionSource<string> NewMessage() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public Task OrderFullscreenStaysInsideTheWebView() => Run(async (w, next) =>
    {
        var xid = await ObserveUnhandledFullscreen(w);
        await FocusPage(w, xid);
        Key("Return");
        Assert.Equal("entered", (string?)JObject.Parse(await next.Task.WaitAsync(TimeSpan.FromSeconds(15)))["type"]);
        Assert.Equal("true", (await w.Web.InvokeScriptAsync("String(!!document.fullscreenElement)"))?.Trim('"'));
        Assert.Equal(WindowState.Normal, w.WindowState);
        Assert.True(((Grid)w.Content!).Children[0].IsVisible);
        Assert.Equal(0, Volatile.Read(ref _unhandledFullscreen));
        var left = NewMessage();
        w.Web.WebMessage += body =>
        {
            try { if ((string?)JObject.Parse(GameWindow.Unwrap(body))["type"] == "left") left.TrySetResult(body); }
            catch { }
        };
        await w.Web.InvokeScriptAsync("document.exitFullscreen()");
        await left.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, Volatile.Read(ref _unhandledFullscreen));
    });

    [Fact]
    public Task PageFocusedEscapeExitsHostFullscreen() => Run(async (w, next) =>
    {
        w.WindowState = WindowState.FullScreen;
        if (OperatingSystem.IsWindows())
        {
            var native = w.Web.FindControl<Panel>("WebSlot")!.Children.OfType<NativeWebView>().Single();
            Assert.NotEqual(IntPtr.Zero, Assert.IsAssignableFrom<IWindowsWebView2PlatformHandle>(native.TryGetPlatformHandle()).CoreWebView2);
            w.Activate();
            Assert.True(SetForegroundWindow(w.TryGetPlatformHandle()!.Handle));
            w.Web.FocusPage();
            await w.Web.InvokeScriptAsync("document.getElementById('order').focus()");
            Assert.Equal("order", (await w.Web.InvokeScriptAsync("document.activeElement.id"))?.Trim('"'));
            await Task.Delay(150);
            keybd_event(0x1b, 0, 0, 0);
            keybd_event(0x1b, 0, 2, 0);
        }
        else
        {
            var xid = await ObserveUnhandledFullscreen(w);
            await FocusPage(w, xid);
            Key("Escape");
        }
        Assert.Equal("ccp-host-escape", (string?)JObject.Parse(await next.Task.WaitAsync(TimeSpan.FromSeconds(15)))["type"]);
        var limit = DateTime.UtcNow.AddSeconds(5);
        while (w.WindowState == WindowState.FullScreen && DateTime.UtcNow < limit) await Task.Delay(25);
        Assert.Equal(WindowState.Normal, w.WindowState);
    });

    private static async Task FocusPage(GameWindow w, nuint xid)
    {
        w.Activate();
        w.Web.FocusPage();
        await w.Web.InvokeScriptAsync("document.getElementById('order').focus()");
        Assert.Equal("order", (await w.Web.InvokeScriptAsync("document.activeElement.id"))?.Trim('"'));
        var display = XOpenDisplay(IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, display);
        XSetInputFocus(display, xid, 1, 0);
        XSync(display, false);
        XCloseDisplay(display);
        await Task.Delay(150);
    }

    private static void Key(string key)
    {
        var display = XOpenDisplay(IntPtr.Zero);
        var code = XKeysymToKeycode(display, XStringToKeysym(key));
        Assert.NotEqual((byte)0, code);
        XTestFakeKeyEvent(display, code, true, 0);
        XTestFakeKeyEvent(display, code, false, 0);
        XSync(display, false);
        XCloseDisplay(display);
    }

    private static int _unhandledFullscreen;
    private delegate int Idle(IntPtr data);
    private delegate int Signal(IntPtr view, IntPtr data);
    private static readonly Signal Unhandled = (_, _) => { Interlocked.Increment(ref _unhandledFullscreen); return 0; };
    private static readonly Idle Attach = data =>
    {
        var handle = GCHandle.FromIntPtr(data);
        var (view, done) = ((IntPtr, TaskCompletionSource<nuint>))handle.Target!;
        try
        {
            var enter = g_signal_connect_data(view, "enter-fullscreen", Unhandled, IntPtr.Zero, IntPtr.Zero, 0);
            var leave = g_signal_connect_data(view, "leave-fullscreen", Unhandled, IntPtr.Zero, IntPtr.Zero, 0);
            if (enter == 0 || leave == 0) throw new InvalidOperationException("Could not observe fullscreen signals");
            var top = gtk_widget_get_toplevel(view);
            done.SetResult(gdk_x11_window_get_xid(gtk_widget_get_window(top)));
        }
        catch (Exception ex) { done.SetException(ex); }
        finally { handle.Free(); }
        return 0;
    };

    private static async Task<nuint> ObserveUnhandledFullscreen(GameWindow w)
    {
        Volatile.Write(ref _unhandledFullscreen, 0);
        var slot = w.Web.FindControl<Panel>("WebSlot")!;
        var native = slot.Children.OfType<NativeWebView>().Single();
        var gtk = Assert.IsAssignableFrom<IGtkWebViewPlatformHandle>(native.TryGetPlatformHandle());
        var done = new TaskCompletionSource<nuint>(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = GCHandle.Alloc((gtk.WebKitWebView, done));
        Assert.NotEqual(0u, g_idle_add(Attach, GCHandle.ToIntPtr(state)));
        return await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);
    [DllImport("libglib-2.0.so.0")] private static extern uint g_idle_add(Idle callback, IntPtr data);
    [DllImport("libgobject-2.0.so.0")] private static extern nuint g_signal_connect_data(IntPtr instance, string signal, Signal callback, IntPtr data, IntPtr destroy, int flags);
    [DllImport("libgtk-3.so.0")] private static extern IntPtr gtk_widget_get_toplevel(IntPtr widget);
    [DllImport("libgtk-3.so.0")] private static extern IntPtr gtk_widget_get_window(IntPtr widget);
    [DllImport("libgdk-3.so.0")] private static extern nuint gdk_x11_window_get_xid(IntPtr window);
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XSetInputFocus(IntPtr display, nuint window, int revert, nuint time);
    [DllImport("libX11.so.6")] private static extern int XSync(IntPtr display, bool discard);
    [DllImport("libX11.so.6")] private static extern nuint XStringToKeysym(string name);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(IntPtr display, nuint keysym);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, bool press, nuint delay);
}
