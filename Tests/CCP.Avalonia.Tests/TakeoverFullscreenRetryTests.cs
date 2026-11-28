using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public class TakeoverFullscreenRetryTests
{
    private const string Ask = "{\"type\":\"fsAsk\"}";
    private const string Denied = "{\"type\":\"fsDenied\",\"activation\":false}";

    [Fact]
    public Task RefusalFocusesThenRetriesOnlyOnce() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = new MainShellWindow();
        var host = new WebHost();
        var calls = new List<string>();
        var wait = new TaskCompletionSource();
        try
        {
            shell.TakeoverFocusForTest = _ => calls.Add("focus");
            shell.TakeoverFocusDelay = () => { calls.Add("delay"); return wait.Task; };
            host.FullscreenGestureInvoker = p => { calls.Add(p); return Task.CompletedTask; };
            shell.ArmTakeoverGestureRetry(host);
            shell.OnBrowserMediaMessage(Denied); // no ask yet
            Assert.Empty(calls);
            shell.OnBrowserMediaMessage(Ask);
            shell.OnBrowserMediaMessage("{\"type\":\"fsDenied\",\"activation\":true}");
            Assert.Empty(calls);
            shell.OnBrowserMediaMessage(Denied);
            shell.OnBrowserMediaMessage(Denied);
            Assert.Equal(new[] { "focus", "delay" }, calls);
            wait.SetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(3, calls.Count);
            using var json = JsonDocument.Parse(calls[2]);
            Assert.True(json.RootElement.GetProperty("userGesture").GetBoolean());
            Assert.Equal(WebHostTakeover.FullscreenRecheckScript, json.RootElement.GetProperty("expression").GetString());
            shell.OnBrowserMediaMessage("{\"type\":\"fsDenied\",\"reason\":\"recheck TypeError\",\"activation\":false}");
            Assert.Equal(3, calls.Count);
        }
        finally { shell.CancelTakeoverGestureRetry(); shell.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task StopDuringFocusDelayCancelsRetryAndLateAsk() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = new MainShellWindow();
        var host = new WebHost();
        var wait = new TaskCompletionSource();
        var calls = 0;
        try
        {
            shell.TakeoverFocusForTest = _ => { };
            shell.TakeoverFocusDelay = () => wait.Task;
            host.FullscreenGestureInvoker = _ => { calls++; return Task.CompletedTask; };
            shell.ArmTakeoverGestureRetry(host);
            shell.OnBrowserMediaMessage(Ask);
            shell.OnBrowserMediaMessage(Denied);
            shell.EndWebVideoTakeover();
            wait.SetResult();
            Dispatcher.UIThread.RunJobs();
            shell.OnBrowserMediaMessage(Ask);
            shell.OnBrowserMediaMessage(Denied);
            Assert.Equal(0, calls);
        }
        finally { shell.CancelTakeoverGestureRetry(); shell.Close(); }
        return Task.CompletedTask;
    });
}
