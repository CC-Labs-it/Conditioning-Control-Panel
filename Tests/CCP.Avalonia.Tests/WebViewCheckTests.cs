using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>--webview-check (WebViewCheck.cs): argument parsing, the exit rule and the failure path. A headless
/// WebHost has no engine, so every page must come back as a FAIL line with its reason, never a pass.</summary>
public sealed class WebViewCheckTests
{
    [Fact]
    public void ParsesExpectBlocked()
    {
        Assert.True(WebViewCheck.ExpectBlocked(new[] { "--webview-check", "--expect-blocked" }));
        Assert.False(WebViewCheck.ExpectBlocked(new[] { "--webview-check" }));
    }

    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(2, false, 1)]
    [InlineData(-1, false, 1)]
    [InlineData(2, true, 0)]
    [InlineData(0, true, 1)]
    [InlineData(-1, true, 1)]
    public void ExitPassesOnlyOnTheExpectedOutcome(int fails, bool expectBlocked, int exit)
    {
        var lines = new List<string>();
        Assert.Equal(exit, WebViewCheck.Exit(fails, expectBlocked, lines.Add));
        Assert.Single(lines);
        Assert.StartsWith(exit == 0 ? "PASS" : "FAIL", lines[0]);
    }

    [Theory]
    [InlineData("\"Example Domain\"", "Example Domain")]
    [InlineData("\"\"", "")]
    [InlineData(null, null)]
    public void ReadsTheScriptTitle(string? json, string? title) => Assert.Equal(title, WebViewCheck.Title(json));

    [Fact]
    public void LinuxSkipsWithExitZero()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Windows runs the real check");
        Assert.Equal(0, WebViewCheck.Run(new[] { "--webview-check", "--expect-blocked" }));
    }

    [Fact]
    public async Task AHostWithoutAnEngineFailsEveryPageWithAReason() =>
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var host = new WebHost { GateScrolller = false, BlockWhenOffline = true, RetryFailedConstruction = true, BlockAds = true };
            var lines = new List<string>();
            var fails = await WebViewCheck.Check(host, new[] { new Uri("http://127.0.0.1:9/page.html?ccp_t=secret"), new Uri(WebViewCheck.HttpsPage) },
                _ => true, lines.Add, TimeSpan.FromMilliseconds(50));
            Assert.Equal(2, fails);
            Assert.All(lines, l => Assert.Contains("[FAIL]", l));
            Assert.Contains(lines, l => l.Contains("http://127.0.0.1:9: Navigate refused"));
            Assert.DoesNotContain(lines, l => l.Contains("secret"));
        });
}
