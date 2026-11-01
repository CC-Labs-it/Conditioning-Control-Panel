using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BrowserDocumentCarrierTests
{
    [Fact]
    public Task CarrierIsInstalledBeforeNavigationAndOnEachEngine() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var host = new WebHost { InjectCarrier = true };
        var calls = new List<string>();
        var pending = new TaskCompletionSource<bool>();
        host.NativeCarrierInvoker = script => { calls.Add(script); return pending.Task; };
        var first = host.NewScrolllerEngine();
        first(true);
        first(true); // a late gate report must not register a second user script
        Assert.Null(host.ScrolllerGate);
        Assert.Equal(WebHost.CarrierShim, Assert.Single(calls));
        var second = host.NewScrolllerEngine();
        pending.SetResult(true);
        await Task.Yield();
        Assert.Null(host.ScrolllerGate); // the detached engine cannot release this one
        second(true);
        Assert.True(host.ScrolllerGate);
        Assert.Equal(2, calls.Count);
    });

    [Fact]
    public Task LateGateSuccessWinsWhileEarlierReportIsWaiting() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var host = new WebHost { InjectCarrier = true };
        host.NativeMuteInvoker = _ => Task.FromResult(true);
        await host.SetEngineMutedAsync(true);
        var pending = new TaskCompletionSource<bool>();
        var calls = 0;
        host.NativeMuteInvoker = _ => ++calls == 1 ? pending.Task : Task.FromResult(true);
        host.NativeCarrierInvoker = _ => Task.FromResult(true);
        var report = host.NewScrolllerEngine();
        report(false);
        report(true);
        Assert.True(host.ScrolllerGate);
        pending.SetResult(true);
        await Task.Yield();
        Assert.True(host.ScrolllerGate);
    });

    [Fact]
    public Task UnrequestedCarrierIsNotInstalled() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var host = new WebHost();
        var calls = 0;
        host.NativeCarrierInvoker = _ => { calls++; return Task.FromResult(true); };
        host.NewScrolllerEngine()(true);
        Assert.True(host.ScrolllerGate);
        Assert.Equal(0, calls);
        return Task.CompletedTask;
    });

    [Fact]
    public Task UnavailableNativeInstallStillReleasesGateAndUsesLateFallback() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var host = new WebHost { InjectCarrier = true };
        var attempted = false;
        host.NativeCarrierInvoker = _ => { attempted = true; throw new InvalidOperationException("no native API"); };
        string? sent = null;
        host.ScriptInvoker = script => { sent = script; return Task.FromResult<string?>(null); };
        host.NewScrolllerEngine()(true);
        Assert.True(host.ScrolllerGate);
        Assert.True(attempted);
        host.OnNavigationCompleted(new Uri("https://example.test/"));
        Assert.Equal(WebHost.CarrierShim, sent);
        return Task.CompletedTask;
    });
}
