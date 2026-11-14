using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using Xunit;

namespace CCP.Avalonia.Tests;

// Restack b175 (#3723): native engine mute first, page script as the fallback, reapplied to each new engine.
// The tests use their own SettingsService and put settings.json back: the toolbar click saves.
public sealed class BrowserNativeMuteTests
{
    private static Task WithSettings(Func<SettingsService, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var settingsPath = Path.Combine(CorePaths.UserData, "settings.json");
        var saved = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try { await body(service); }
        finally
        {
            CoreSettings.ServiceProvider = oldProvider;
            if (saved is null) File.Delete(settingsPath); else File.WriteAllBytes(settingsPath, saved);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task ToolbarUsesNativeMuteEvenWhenPageScriptFails(bool initial) => WithSettings(async service =>
    {
        service.Current.BrowserVideoMuted = initial;
        var tab = new SettingsTabView();
        var host = tab.FindControl<WebHost>("BrowserWebHost")!;
        var calls = new List<bool>();
        host.NativeMuteInvoker = muted => { calls.Add(muted); return Task.FromResult(true); };
        host.ScriptInvoker = _ => throw new InvalidOperationException("page script unavailable");
        tab.FindControl<Button>("BtnMuteBrowser")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(new[] { !initial }, calls);
        Assert.Equal(!initial, service.Current.BrowserVideoMuted);
        Assert.True(await host.SetMutedAsync(initial));
    });

    [Fact]
    public Task SavedMuteIsReappliedBeforeEveryNewEngineCanNavigate() => WithSettings(async service =>
    {
        service.Current.BrowserVideoMuted = true;
        var tab = new SettingsTabView(); // seeds mute while there is no engine
        var host = tab.FindControl<WebHost>("BrowserWebHost")!;
        var calls = new List<bool>();
        var pending = new TaskCompletionSource<bool>();
        host.NativeMuteInvoker = muted => { calls.Add(muted); return pending.Task; };
        var old = host.NewScrolllerEngine();
        old(true);
        Assert.Null(host.ScrolllerGate); // navigation remains held while native mute is pending
        var current = host.NewScrolllerEngine();
        pending.SetResult(true);
        await Task.Yield();
        Assert.Null(host.ScrolllerGate); // old engine's completion cannot release the new engine
        current(true);
        Assert.True(host.ScrolllerGate);
        Assert.Equal(new[] { true, true }, calls);
    });

    [Fact]
    public Task MissingNativeApiKeepsScriptFallback() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChatThresholdLinksTests.Setup();
        var host = new WebHost();
        host.NativeMuteInvoker = _ => Task.FromResult(false);
        string? sent = null;
        host.ScriptInvoker = script => { sent = script; return Task.FromResult<string?>("muted"); };
        Assert.True(await host.SetMutedAsync(true));
        Assert.Equal(WebHostMedia.MuteScript(true), sent);
    });
}
