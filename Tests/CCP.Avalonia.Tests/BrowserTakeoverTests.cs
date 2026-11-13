using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public class BrowserTakeoverTests
{
    [Fact]
    public Task ForceEndSendsStopScript() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var host = shell.FindControl<SettingsTabView>("SettingsTab")!.FindControl<WebHost>("BrowserWebHost")!;
            var scripts = new List<string>();
            host.ScriptInvoker = script => { scripts.Add(script); return Task.FromResult<string?>(null); };
            shell.EndWebVideoTakeover();
            Assert.Single(scripts);
            Assert.Equal(WebHostTakeover.StopScript, scripts[0]);
            Assert.Contains("_ccpTakeoverStopped = true", scripts[0]);
            Assert.Contains("video,audio", scripts[0]);
            Assert.Contains("v.pause()", scripts[0]);
            Assert.Contains("document.exitFullscreen()", scripts[0]);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });
}
