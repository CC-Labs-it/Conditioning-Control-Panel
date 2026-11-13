using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Bark;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmergencyExitBarkTests
{
    private static BarkEngine Capture(string trigger, Action<string> speak)
    {
        var engine = new BarkEngine(() => new BarkRuleSet(new[] { new BarkRule {
            Id = "exit_test", Trigger = trigger, VariantPool = new() { new BarkVariant(trigger == "EmergencyExitOpened" ? "{game}:{attempt}" : "{game}:{outcome}") }
        }}));
        engine.Speak = s => speak(s.Text);
        engine.Start();
        return engine;
    }

    [Fact]
    public async Task RealOpenRaisesGameAndAttemptButPreviewDoesNot()
    {
        await AvaloniaTestDispatcher.RunAsync(() => {
            var old = (BarkHead.Engine, CoreSettings.Current.AvatarEnabled);
            var lines = new List<string>();
            using var ld = new LockdownService();
            try {
                CoreSettings.Current.AvatarEnabled = true;
                BarkHead.Engine = Capture("EmergencyExitOpened", lines.Add);
                var preview = new EmergencyExitWindow();
                preview.Show();
                preview.HandleMessage("{\"type\":\"game-finished\",\"result\":\"completed\"}");
                preview.Close();
                Assert.Empty(lines);
                ld.Activate(TimeSpan.FromMinutes(30));
                EmergencyExitWindow.Open(null, ld);
                var window = Assert.IsType<EmergencyExitWindow>(EmergencyExitWindow.Current);
                var web = Assert.IsType<ConditioningControlPanel.Avalonia.Views.Controls.WebHost>(window.Content);
                string? init = null;
                web.ScriptInvoker = script => { init = script; return Task.FromResult<string?>(null); };
                web.OnNavigationCompleted(web.Source!);
                var line = Assert.Single(lines);
                Assert.EndsWith(":1", line);
                Assert.NotNull(init);
                Assert.Contains("\"game\":\"" + line.Split(':')[0] + "\"", init);
            } finally { EmergencyExitWindow.Current?.Close(); ld.Deactivate(); (BarkHead.Engine, CoreSettings.Current.AvatarEnabled) = old; }
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("completed", .9, "escape", false, 0)]
    [InlineData("failed", .9, "sendback", true, 1)]
    public async Task VerdictBarkFollowsAppliedLockdownChange(string result, double roll, string outcome, bool active, int restarts)
    {
        await AvaloniaTestDispatcher.RunAsync(() => {
            var old = (BarkHead.Engine, CoreSettings.Current.AvatarEnabled);
            using var ld = new LockdownService();
            var lines = new List<(string, bool, int)>();
            EmergencyExitWindow? window = null;
            try {
                CoreSettings.Current.AvatarEnabled = true;
                BarkHead.Engine = Capture("EmergencyExitVerdict", line => lines.Add((line, ld.IsActive, ld.RestartCount)));
                ld.Activate(TimeSpan.FromMinutes(30));
                window = new EmergencyExitWindow(ld, "password") { Roll = () => roll };
                window.Show();
                window.HandleMessage("{\"type\":\"game-finished\",\"result\":\"" + result + "\"}");
                var spoken = Assert.Single(lines);
                Assert.Equal("password:" + outcome, spoken.Item1);
                Assert.Equal(active, spoken.Item2);
                Assert.Equal(restarts, spoken.Item3);
            } finally { window?.Close(); ld.Deactivate(); (BarkHead.Engine, CoreSettings.Current.AvatarEnabled) = old; }
            return Task.CompletedTask;
        });
    }
}
