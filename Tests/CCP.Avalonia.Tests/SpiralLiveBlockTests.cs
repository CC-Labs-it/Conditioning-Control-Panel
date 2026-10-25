using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class SpiralLiveBlockTests
{
    [Fact]
    public Task ServerBlockReachesEmbed_Updates_WithdrawalAndHiddenTabReleaseIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldReader = AvApp.Descent;
        var oldFuse = AvApp.DescentCountdown;
        var oldEngine = SpiralTabView.EngineAvailable;
        var oldStart = SpiralTabView.StartEmbed;
        var oldBark = BarkHead.Engine;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.DescentMigrationCompleted = true;
        settings.Current.MotionLevel = MotionLevel.Off;
        settings.Current.AvatarEnabled = true;
        settings.Current.MasterVolume = 50;
        settings.Current.BarkChatSuppressionMs = 0;
        AvApp.DescentCountdown = null;
        var reader = AvApp.Descent = new DescentService();
        SpiralTabView.EngineAvailable = () => true;
        SpiralTabView.StartEmbed = _ => { }; // no browser, network or desktop in this test
        var said = new List<BarkSpeech>();
        var rule = new BarkRule
        {
            Id = "test-spiral-first", Trigger = "DescentFirstSpiralOpen", Repeatable = false,
            ScopeRaw = "lifetime", Chance = 1,
            VariantPool = new() { new BarkVariant("hello {stage}", null) }
        };
        var bark = BarkHead.Engine = new BarkEngine(() => new BarkRuleSet(new[] { rule }));
        bark.Speak = said.Add;
        bark.Start();
        var tab = new SpiralTabView();
        var window = new Window { Content = tab };
        try
        {
            window.Show();
            tab.OnTabShown();
            Assert.Empty(said); // waiting does not consume the lifetime welcome
            reader.SeedForTest(new DescentBlock { DevotionDays = 7 });
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.IsShowingSpiral);
            var host = tab.FindControl<Grid>("EmbedHost")!;
            var embed = Assert.IsType<SpiralEmbedView>(Assert.Single(host.Children));
            var sent = new List<string>();
            embed.SendJson = json => { sent.Add(json); return Task.CompletedTask; };
            embed.Receive("{\"type\":\"spiral:ready\"}");
            Assert.Equal(7, (int)JObject.Parse(Assert.Single(sent))["devotion_days"]!);
            reader.SeedForTest(new DescentBlock { DevotionDays = 8 });
            Dispatcher.UIThread.RunJobs();
            Assert.Same(embed, Assert.Single(host.Children));
            Assert.Equal(8, (int)JObject.Parse(sent[1])["devotion_days"]!);
            tab.OnTabShown();
            Assert.Single(said);
            tab.OnTabShown();
            Assert.Single(said); // the bark rule, rather than the view, owns the latch
            reader.Reset();
            Dispatcher.UIThread.RunJobs();
            Assert.False(tab.IsShowingSpiral);
            Assert.Empty(host.Children);
            tab.IsVisible = false;
            reader.SeedForTest(new DescentBlock { DevotionDays = 9 });
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(host.Children);
            tab.IsVisible = true;
            Assert.True(tab.IsShowingSpiral);
            Assert.Single(host.Children);
            Assert.Single(said);
            tab.StopForPanic();
            Assert.Empty(host.Children);
            reader.SeedForTest(new DescentBlock { DevotionDays = 10 });
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(host.Children); // panic dropped the feed as well as the browser
            window.Close();
            reader.Reset();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(host.Children);
        }
        finally
        {
            window.Close();
            CoreSettings.ServiceProvider = oldSettings;
            AvApp.Descent = oldReader; AvApp.DescentCountdown = oldFuse;
            SpiralTabView.EngineAvailable = oldEngine; SpiralTabView.StartEmbed = oldStart;
            BarkHead.Engine = oldBark;
        }
        return Task.CompletedTask;
    });
}
