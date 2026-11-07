using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Browser;
using Xunit;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace CCP.Avalonia.Tests;

public class BrowserAdCssTests
{
    [Fact]
    public Task BrowserCompletionSendsCssThroughHost() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var host = tab.FindControl<WebHost>("BrowserWebHost")!;
            var scripts = new List<string>();
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            host.OnNavigationCompleted(new Uri("https://example.com"));
            host.OnNavigationCompleted(new Uri("https://hypnotube.com"));
            host.OnNavigationCompleted(new Uri("https://example.com"), false);
            host.OnNavigationCompleted(new Uri("https://example.com/again"));
            Assert.Equal(2, scripts.FindAll(s => s == BrowserAdCss.Script).Count);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public async Task SuccessfulNonPartnerNavigationsInjectEachTime()
    {
        var scripts = new List<string>();
        Task<string?> Execute(string script) { scripts.Add(script); return Task.FromResult<string?>(null); }
        await BrowserAdCss.ApplyAsync(new Uri("https://example.com"), true, Execute);
        await BrowserAdCss.ApplyAsync(new Uri("https://example.com/again"), true, Execute);
        Assert.Equal(2, scripts.Count);
        Assert.All(scripts, script => { Assert.Contains("document.head.appendChild(style)", script); Assert.Contains("display: none !important", script); });
    }

    [Theory]
    [InlineData("https://hypnotube.com", true)]
    [InlineData("https://sub.HYPNOTUBE.com", true)]
    [InlineData("https://example.com", false)]
    public async Task PartnerAndFailedNavigationsDoNotInject(string url, bool success)
    {
        var scripts = new List<string>();
        await BrowserAdCss.ApplyAsync(new Uri(url), success, script => { scripts.Add(script); return Task.FromResult<string?>(null); });
        Assert.Empty(scripts);
    }

    [Fact]
    public async Task SimilarDomainIsNotPartner()
    {
        var called = false;
        await BrowserAdCss.ApplyAsync(new Uri("https://nothypnotube.com"), true, _ => { called = true; return Task.FromResult<string?>(null); });
        Assert.True(called);
    }
}
