using System;
using System.IO;
using ConditioningControlPanel.Services.Browser;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1699 #1710: the gesture retry was refused too ("recheck TypeError"), because
/// Chromium also wants the requesting view focused. The retry now raises the panel and focuses
/// the WebView first, and both refusals report the error message as well as its name.
/// </summary>
public class TakeoverFullscreenFocusTests
{
    [Fact]
    public void The_panel_is_raised_only_when_on_screen_and_not_already_active()
    {
        Assert.True(TakeoverFullscreenRetry.ShouldRaiseHost(visible: true, minimized: false, active: false));
        Assert.False(TakeoverFullscreenRetry.ShouldRaiseHost(visible: true, minimized: false, active: true));
        Assert.False(TakeoverFullscreenRetry.ShouldRaiseHost(visible: true, minimized: true, active: false));
        Assert.False(TakeoverFullscreenRetry.ShouldRaiseHost(visible: false, minimized: false, active: false));
    }

    [Fact]
    public void A_recheck_refusal_that_now_reports_activation_is_still_never_retried()
    {
        const string recheck = "{\"type\":\"fsDenied\",\"reason\":\"recheck TypeError: not granted\",\"activation\":false,\"focus\":false}";
        Assert.False(TakeoverFullscreenRetry.ShouldRetryWithGesture(recheck, false));
    }

    [Fact]
    public void The_retry_focuses_the_WebView_before_the_gesture()
    {
        var src = File.ReadAllText(Path.Combine(AppDir(), "MainWindow", "MainWindow.Browser.cs"));
        int at = src.IndexOf("private async void RetryTakeoverFullscreenWithGesture()", StringComparison.Ordinal);
        Assert.True(at > 0);
        int end = src.IndexOf("CallDevToolsProtocolMethodAsync", at, StringComparison.Ordinal);
        Assert.True(end > at);
        var before = src.Substring(at, end - at);
        Assert.Contains("web.Focus()", before);
        Assert.Contains("TakeForegroundForTakeover", before);
        Assert.Contains("FocusSettleMs", before);
    }

    [Fact]
    public void Both_refusals_report_the_error_message()
    {
        var src = File.ReadAllText(Path.Combine(AppDir(), "MainWindow", "MainWindow.Browser.cs"));
        Assert.DoesNotContain("(err.name || err.message)", src);
        Assert.Contains("'recheck ' + String((err && ((err.name || '') + ': ' + (err.message || '')))", src);
        Assert.Contains("reason: String((err && ((err.name || '') + ': ' + (err.message || '')))", src);
    }

    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Services")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }
}
