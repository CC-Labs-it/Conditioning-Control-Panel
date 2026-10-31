using System;
using CCP.Avalonia.Testing;
using Avalonia;
using Avalonia.Headless;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SpiralNativePolicyTests
{
    private sealed class Core : SpiralWebView2Policy.ICore
    {
        public bool AreDevToolsEnabled { get; set; } = true;
        public bool AreDefaultContextMenusEnabled { get; set; } = true;
        public bool IsStatusBarEnabled { get; set; } = true;
        public bool AreBrowserAcceleratorKeysEnabled { get; set; } = true;
        public bool IsZoomControlEnabled { get; set; } = true;
        public bool IsBuiltInErrorPageEnabled { get; set; } = true;
        public bool IsWebMessageEnabled { get; set; }
        private Action<string>? _processFailed;
        public bool ThrowOnRemove { get; set; }
        public int RemoveAttempts { get; private set; }
        public event Action<string>? ProcessFailed
        {
            add => _processFailed += value;
            remove
            {
                RemoveAttempts++;
                if (ThrowOnRemove) throw new InvalidOperationException("dead browser");
                _processFailed -= value;
            }
        }
        public void Crash() => _processFailed?.Invoke("RenderProcessExited");
        public bool Subscribed => _processFailed != null;
    }

    // WPF SpiralEmbedView.cs:178-190. Start with the opposite of every required switch.
    [Theory]
    [InlineData(nameof(Core.AreDevToolsEnabled), false)]
    [InlineData(nameof(Core.AreDefaultContextMenusEnabled), false)]
    [InlineData(nameof(Core.IsStatusBarEnabled), false)]
    [InlineData(nameof(Core.AreBrowserAcceleratorKeysEnabled), false)]
    [InlineData(nameof(Core.IsZoomControlEnabled), false)]
    [InlineData(nameof(Core.IsBuiltInErrorPageEnabled), false)]
    [InlineData(nameof(Core.IsWebMessageEnabled), true)]
    public void DecorativePolicyDisablesBrowserChromeButKeepsTheCarrier(string setting, bool expected)
    {
        var core = new Core();
        var property = typeof(Core).GetProperty(setting)!;
        Assert.Equal(!expected, property.GetValue(core));
        using var lease = SpiralWebView2Policy.Apply(core, _ => { });
        Assert.Equal(expected, property.GetValue(core));
    }

    [Fact]
    public void LeaseDropsNativeFailureAfterDisposal()
    {
        var core = new Core();
        int failures = 0;
        var lease = SpiralWebView2Policy.Apply(core, _ => failures++);
        core.Crash();
        Assert.Equal(1, failures);
        lease.Dispose();
        lease.Dispose();
        Assert.False(core.Subscribed);
        core.Crash();
        Assert.Equal(1, failures);
    }

    // WPF SpiralEmbedView.cs:229-233, Dispose: no recovery and no callback after teardown.
    [Fact]
    public Task NativeCrashFailsOnceAndReleasesTheSubscription() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var core = new Core();
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var host = new WebHost { ScriptInvoker = _ => Task.FromResult<string?>(null) };
        // Replacing a core, then destroying its adapter, releases only that adapter's lease.
        var previous = new Core();
        host.ConfigureWindowsCore = _ => SpiralWebView2Policy.Apply(previous, host.OnBrowserProcessFailed);
        host.OnWindowsCore((IntPtr)1);
        Assert.True(previous.Subscribed);
        host.NewScrolllerEngine();
        Assert.False(previous.Subscribed);
        host.OnWindowsCore((IntPtr)2);
        Assert.True(previous.Subscribed);
        using var embed = new SpiralEmbedView("map") { CreateHost = () => host };
        embed.Start();
        Assert.Same(host, embed.Host);
        Assert.False(previous.Subscribed);
        int failures = 0;
        embed.Failed += (_, reason) => { Assert.Contains("RenderProcessExited", reason); failures++; };
        host.ConfigureWindowsCore = _ => SpiralWebView2Policy.Apply(core, host.OnBrowserProcessFailed);
        host.OnWindowsCore((IntPtr)1);
        core.Crash();
        core.Crash();
        Assert.True(embed.HasFailed);
        Assert.False(embed.IsReady);
        Assert.Equal(1, failures);
        Assert.Empty(embed.Children);
        Assert.Null(embed.Host);
        Assert.False(core.Subscribed); // Spiral disposal releases before adapter detach
        host.OnWindowsCore((IntPtr)2);
        Assert.False(core.Subscribed);
        core.Crash();
        Assert.Equal(1, failures);
        return Task.CompletedTask;
    });

    // WPF SpiralEmbedView.cs:320-325,334-353: teardown errors never suppress failure reporting.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ThrowingNativeUnsubscribeStillRemovesTheHost(bool crash) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var core = new Core { ThrowOnRemove = true };
        var host = new WebHost { ScriptInvoker = _ => Task.FromResult<string?>(null) };
        using var embed = new SpiralEmbedView("map") { CreateHost = () => host };
        embed.Start();
        host.ConfigureWindowsCore = _ => SpiralWebView2Policy.Apply(core, host.OnBrowserProcessFailed);
        host.OnWindowsCore((IntPtr)1);
        embed.Receive("{\"type\":\"spiral:ready\"}");
        Assert.True(embed.IsReady);
        int failures = 0;
        embed.Failed += (_, _) => { Assert.Empty(embed.Children); failures++; };
        Assert.Null(Record.Exception(() => { if (crash) core.Crash(); else embed.Dispose(); }));
        Assert.Equal(1, core.RemoveAttempts);
        Assert.Null(host.ConfigureWindowsCore);
        Assert.Null(host.Source);
        Assert.Null(embed.Host);
        Assert.Empty(embed.Children);
        Assert.False(embed.IsReady);
        Assert.Equal(crash, embed.HasFailed);
        Assert.Equal(crash ? 1 : 0, failures);
        // Removal threw before detaching the event. The disposed lease must make late events inert.
        core.Crash();
        host.NewScrolllerEngine();
        embed.Dispose();
        Assert.Equal(1, core.RemoveAttempts);
        Assert.Equal(crash ? 1 : 0, failures);
        return Task.CompletedTask;
    });

}
