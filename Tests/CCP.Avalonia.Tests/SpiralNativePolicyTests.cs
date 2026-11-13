using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

// Restack b117 (#3525): Spiral's native browser policy on the trunk's WebHost seams.
public sealed class SpiralNativePolicyTests
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
    }

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
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
        SpiralWebView2Policy.Apply(core);
        Assert.Equal(expected, property.GetValue(core));
    }

    [Fact]
    public void NonWindowsHandlesAreLeftAlone() => SpiralWebView2Policy.Attach(null);

    // WPF SpiralEmbedView.cs:229-233 and Dispose: a dead browser fails the embed once, and teardown
    // leaves no hook on the host.
    [Fact]
    public Task BrowserProcessFailureFailsTheEmbedOnceAndReleasesTheHost() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var host = new WebHost { ScriptInvoker = _ => Task.FromResult<string?>(null) };
        using var embed = new SpiralEmbedView("map") { CreateHost = () => host };
        embed.Start();
        Assert.Same(host, embed.Host);
        Assert.NotNull(host.ConfigureNative);
        embed.Receive("{\"type\":\"spiral:ready\"}");
        int failures = 0;
        embed.Failed += (_, reason) => { Assert.Contains("browser process failed", reason); failures++; };
        host.OnBrowserProcessFailed();
        host.OnBrowserProcessFailed();
        Assert.True(embed.HasFailed);
        Assert.False(embed.IsReady);
        Assert.Equal(1, failures);
        Assert.Null(embed.Host);
        Assert.Empty(embed.Children);
        Assert.Null(host.ConfigureNative);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DisposeDetachesTheNativeHooks() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var host = new WebHost { ScriptInvoker = _ => Task.FromResult<string?>(null) };
        var embed = new SpiralEmbedView("map") { CreateHost = () => host };
        embed.Start();
        int failures = 0;
        embed.Failed += (_, _) => failures++;
        embed.Dispose();
        Assert.Null(host.ConfigureNative);
        host.OnBrowserProcessFailed();
        Assert.Equal(0, failures);
        Assert.False(embed.HasFailed);
        return Task.CompletedTask;
    });
}
