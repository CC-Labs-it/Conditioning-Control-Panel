using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>This host is a CCP_USERDATA_DIR sandbox (TestUserDataProfile): a real site is never handed to the shell,
/// loopback and local folders still are. The shell seam records instead of launching anything.</summary>
[Collection("ExternalOpener")]
public sealed class ExternalOpenerTests
{
    [Theory]
    [InlineData("https://app.cclabs.app/policies/prohibited-content", false)]
    [InlineData("https://discord.gg/example", false)]
    [InlineData("http://loopback:3001/", false)]
    [InlineData("http://127.0.0.1:3001/auth", true)]
    [InlineData("http://localhost:3001/", true)]
    public async Task OnlyLoopbackOrLocalReachesTheShell(string target, bool opened)
    {
        var launched = new List<string>();
        var previous = ExternalOpener.Shell;
        ExternalOpener.Shell = t => { launched.Add(t); return true; };
        try
        {
            Assert.Equal(opened, ExternalOpener.Open(target));
            Assert.Equal(opened, await ExternalOpener.OpenAsync(null, target));
            Assert.Equal(opened ? 2 : 0, launched.Count);
        }
        finally { ExternalOpener.Shell = previous; }
    }

    [Fact]
    public void LocalFoldersStillOpen()
    {
        var launched = new List<string>();
        var previous = ExternalOpener.Shell;
        ExternalOpener.Shell = t => { launched.Add(t); return true; };
        try { Assert.True(ExternalOpener.Open(Path.GetTempPath())); }
        finally { ExternalOpener.Shell = previous; }
        Assert.Equal(new[] { Path.GetTempPath() }, launched);
    }

    [Theory]
    [InlineData("relative/notes.txt", false, true)]        // production: the shell decides, as before
    [InlineData("mailto:someone@example.com", false, true)]
    [InlineData("https://app.cclabs.app/", false, true)]
    [InlineData(@"\\server\share\x.txt", true, false)]   // sandbox: a UNC path is a network share
    [InlineData("//server/share/x.txt", true, false)]
    [InlineData("file://server/share/x.txt", true, false)]
    [InlineData("file:///tmp/x.txt", true, true)]
    [InlineData("/tmp", true, true)]
    [InlineData("relative/notes.txt", true, false)]
    public void SandboxRefusesSharesAndProductionKeepsTheShell(string target, bool sandboxed, bool allowed) =>
        Assert.Equal(allowed, ExternalOpener.Allowed(target, sandboxed));

    /// <summary>The real fallback must not launch even a loopback URL from a test sandbox.
    /// An empty FileName would throw if it reached Process.Start, without starting a process.</summary>
    [Fact]
    public void NativeBrowserFallbackDoesNotStartInSandbox()
    {
        Assert.True(SandboxNet.Active);
        Assert.False(ExternalOpener.StartBrowserFallback(new ProcessStartInfo()));
    }

    /// <summary>WPF BrowserLauncher.cs:33-51: the banner's web URL tries the Windows handlers in
    /// order after the association fails, stopping at the first success. No process starts in this test.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task WindowsBrowserFallbacksStopAtFirstSuccess(int successAt)
    {
        const string url = "http://127.0.0.1:9/page?a=1&b=2";
        var attempts = new List<ProcessStartInfo>();
        var old = (ExternalOpener.Shell, ExternalOpener.IsWindows, ExternalOpener.StartBrowserFallback);
        try
        {
            ExternalOpener.Shell = _ => throw new InvalidOperationException("association missing");
            ExternalOpener.IsWindows = () => true;
            ExternalOpener.StartBrowserFallback = info =>
            {
                attempts.Add(info);
                if (attempts.Count == 1 && successAt != 0) throw new InvalidOperationException("explorer missing");
                return attempts.Count - 1 == successAt;
            };
            Assert.Equal(successAt >= 0, await ExternalOpener.OpenAsync(null, url));
            Assert.Equal(successAt < 0 ? 3 : successAt + 1, attempts.Count);
            Assert.Equal(new[] { "explorer.exe", "cmd.exe", "rundll32.exe" }.Take(attempts.Count), attempts.Select(a => a.FileName));
            Assert.All(attempts, a => { Assert.False(a.UseShellExecute); Assert.True(a.CreateNoWindow); });
            Assert.Equal(new[] { url }, attempts[0].ArgumentList);
            if (attempts.Count > 1) Assert.Equal($"/c start \"\" \"{url}\"", attempts[1].Arguments);
            if (attempts.Count > 2) Assert.Equal(new[] { "url.dll,FileProtocolHandler", url }, attempts[2].ArgumentList);
        }
        finally { (ExternalOpener.Shell, ExternalOpener.IsWindows, ExternalOpener.StartBrowserFallback) = old; }
    }

    [Fact]
    public void BrowserFallbacksRespectSandboxPlatformAndTargets()
    {
        var attempts = new List<ProcessStartInfo>();
        var old = (ExternalOpener.Shell, ExternalOpener.IsWindows, ExternalOpener.StartBrowserFallback);
        try
        {
            ExternalOpener.Shell = _ => false;
            ExternalOpener.IsWindows = () => false;
            ExternalOpener.StartBrowserFallback = info => { attempts.Add(info); return false; };
            Assert.False(ExternalOpener.Open("http://127.0.0.1:9/"));
            ExternalOpener.IsWindows = () => true;
            Assert.False(ExternalOpener.Open("https://app.cclabs.app/")); // sandbox refuses before any process
            Assert.False(ExternalOpener.Open(Path.GetTempPath()));        // not a browser URL
            Assert.Empty(attempts);

            // cmd's variable expansion is unsafe for such URLs; the other two receive it intact.
            const string escaped = "http://127.0.0.1:9/%25PATH%25";
            Assert.False(ExternalOpener.Open(escaped));
            Assert.Equal(new[] { "explorer.exe", "rundll32.exe" }, attempts.Select(a => a.FileName));
            Assert.Equal(escaped, attempts[0].ArgumentList[0]);
            Assert.Equal(escaped, attempts[1].ArgumentList[1]);

            attempts.Clear();
            ExternalOpener.Shell = _ => true;
            Assert.True(ExternalOpener.Open("http://127.0.0.1:9/"));
            Assert.Empty(attempts);
        }
        finally { (ExternalOpener.Shell, ExternalOpener.IsWindows, ExternalOpener.StartBrowserFallback) = old; }
    }

    /// <summary>A click on a SafeHyperlinkButton launches once, through ExternalOpener, never through the base
    /// HyperlinkButton's own launcher call. The window's platform impl is wrapped so TopLevel.Launcher is a counter.</summary>
    [Fact]
    public async Task SafeHyperlinkButtonLaunchesOnlyThroughExternalOpener()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var link = new SafeHyperlinkButton
            {
                NavigateUri = new Uri("http://127.0.0.1:9/page"),   // loopback: allowed in this sandbox
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch,
                Content = "link",
            };
            var w = new Window { Width = 200, Height = 100, Content = link };
            w.Show();
            var field = typeof(TopLevel).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(f => f.FieldType == typeof(ITopLevelImpl));
            var real = field.GetValue(w)!;
            var launcher = new CountingLauncher();
            var proxy = DispatchProxy.Create<IWindowImpl, LauncherSwap>();
            ((LauncherSwap)(object)proxy).Inner = real;
            ((LauncherSwap)(object)proxy).Launcher = launcher;
            field.SetValue(w, proxy);
            try
            {
                Assert.Same(launcher, w.Launcher);
                // The automation Invoke is a real click (Button.OnClick); headless mouse input needs the unwrapped impl.
                ((global::Avalonia.Automation.Provider.IInvokeProvider)
                    global::Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(link)).Invoke();
                for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
                Assert.Equal(new[] { new Uri("http://127.0.0.1:9/page") }, launcher.Uris);
                Assert.True(link.IsVisited);
                Assert.Equal(new Uri("http://127.0.0.1:9/page"), link.NavigateUri);   // restored after the click

                // The One Account banner uses this same control. If the platform launcher and
                // shell fail, a click reaches the Windows fallback and marks the link visited.
                var old = (ExternalOpener.Shell, ExternalOpener.IsWindows, ExternalOpener.StartBrowserFallback);
                try
                {
                    launcher.Result = false;
                    link.IsVisited = false;
                    ExternalOpener.Shell = _ => false;
                    ExternalOpener.IsWindows = () => true;
                    var attempts = new List<string>();
                    ExternalOpener.StartBrowserFallback = info => { attempts.Add(info.FileName); return true; };
                    ((global::Avalonia.Automation.Provider.IInvokeProvider)
                        global::Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(link)).Invoke();
                    for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
                    Assert.Equal(new[] { "explorer.exe" }, attempts);
                    Assert.True(link.IsVisited);
                }
                finally { (ExternalOpener.Shell, ExternalOpener.IsWindows, ExternalOpener.StartBrowserFallback) = old; }
            }
            finally { field.SetValue(w, real); w.Close(); }
        });
    }

    private sealed class CountingLauncher : ILauncher
    {
        public readonly List<Uri> Uris = new();
        public bool Result = true;
        public Task<bool> LaunchUriAsync(Uri uri) { Uris.Add(uri); return Task.FromResult(Result); }
        public Task<bool> LaunchFileAsync(IStorageItem storageItem) => Task.FromResult(false);
    }

    public class LauncherSwap : DispatchProxy
    {
        internal object Inner = null!;
        internal ILauncher Launcher = null!;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == "TryGetFeature" && args is [Type t] && t == typeof(ILauncher)) return Launcher;
            try { return method.Invoke(Inner, args); }
            catch (TargetInvocationException e) { throw e.InnerException!; }
        }
    }
}
