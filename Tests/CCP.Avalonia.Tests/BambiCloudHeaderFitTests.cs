using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BambiCloudHeaderFitTests
{
    [Fact]
    public Task SuccessfulBambiNavigationInjectsFitOnlyOnBambi() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        ChatThresholdLinksTests.Setup();
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var host = shell.Named<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var scripts = new List<string>();
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            host.OnNavigationCompleted(new Uri("https://bambicloud.com/"), false);
            host.OnNavigationCompleted(new Uri("https://bambicloud.com.example.org/"));
            Assert.DoesNotContain(scripts, s => s.Contains("ccp-bambicloud-header-fit"));
            host.OnNavigationCompleted(new Uri("https://www.bambicloud.com/playlist/123"));
            Assert.Contains(scripts, s => s.Contains("ccp-bambicloud-header-fit") && s.Contains("minmax(13vh,max-content)"));
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });
}
