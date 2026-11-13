using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;
using Head = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class SpiralRoomBarkTests
{
    [Fact]
    public void NavigationAnnouncesOnlyOnSpiralEntryAndKeepsTheLifetimeLatch() => AvaloniaTestDispatcher.Run(() =>
    {
        var old = (CoreSettings.ServiceProvider, CoreSecrets.RetrieveProvider, Head.DescentCountdown,
            Head.Descent, BarkHead.Engine, SpiralTabView.EngineAvailable, SpiralTabView.StartEmbed);
        var settings = new SettingsService();
        using var fuse = new DescentCountdownService();
        var reader = new DescentService(_ => Task.FromResult<JObject?>(JObject.Parse(
            "{\"descent\":{\"devotion_days\":9,\"stage\":{\"n\":2,\"banked_days\":7}}}")));
        MainShellWindow? shell = null;
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            CoreSecrets.RetrieveProvider = _ => "test-token";
            var s = settings.Current;
            s.UnifiedId = "spiral-bark-test";
            s.OfflineMode = false;
            s.AvatarEnabled = true;
            s.BarkChatSuppressionMs = 0;
            s.DescentMigrationOffered = false;
            s.DescentMigrationCompleted = false;
            s.PendingDescentMigrationChoice = null;
            s.BarkLifetimeFired = new();
            s.BarkVariantRotation = new();
            Head.DescentCountdown = fuse;
            Head.Descent = reader;
            SpiralTabView.EngineAvailable = () => true;
            int attempts = 0;
            SpiralTabView.StartEmbed = _ => { attempts++; throw new InvalidOperationException("test failure"); };
            var spoken = new List<BarkSpeech>();
            var rule = Rule();
            BarkHead.Engine = Engine(rule, spoken);
            shell = new MainShellWindow();
            shell.Show();
            shell.ShowTab("spiral");
            var tab = shell.Named<SpiralTabView>("SpiralTab")!;
            Assert.True(tab.FindControl<Border>("WaitingPanel")!.IsVisible);
            Assert.Empty(spoken);
            s.DescentMigrationOffered = true;
            Assert.True(reader.RefreshAsync("test", force: true).GetAwaiter().GetResult());
            Dispatcher.UIThread.RunJobs();
            shell.ShowTab("spiral");
            Assert.True(tab.FindControl<Grid>("FogHost")!.IsVisible);
            Assert.Empty(spoken);
            Assert.False(s.IsBarkFired(rule.Id));

            s.PendingDescentMigrationChoice = DescentMigrationChoices.Restore;
            Dispatcher.UIThread.RunJobs();
            Assert.True(tab.IsShowingSpiral);
            Assert.Equal(1, attempts);
            Assert.Empty(spoken); // a block or gate repaint is not an entry
            s.AvatarEnabled = false;
            shell.ShowTab("spiral");
            Assert.Equal(2, attempts); // a fresh entry retries a failed embed
            Assert.Empty(spoken);
            Assert.False(s.IsBarkFired(rule.Id)); // a suppressed welcome is not spent
            s.AvatarEnabled = true;
            shell.ShowTab("spiral"); // already visible: ShowTab must call OnTabShown
            Assert.Equal("stage 2 days 7", Assert.Single(spoken).Text);
            Assert.True(s.IsBarkFired(rule.Id));
            BarkHead.Engine = Engine(rule, spoken); // a fresh engine still sees the persisted latch
            shell.ShowTab("spiral");
            Assert.Single(spoken);
        }
        finally
        {
            try { shell?.Close(); reader.Reset(); }
            finally
            {
                (CoreSettings.ServiceProvider, CoreSecrets.RetrieveProvider, Head.DescentCountdown,
                    Head.Descent, BarkHead.Engine, SpiralTabView.EngineAvailable, SpiralTabView.StartEmbed) = old;
            }
        }
    });

    [Fact]
    public void BecomingVisibleAlsoAnnouncesWithTheBlockFallback() => AvaloniaTestDispatcher.Run(() =>
    {
        var old = (CoreSettings.ServiceProvider, CoreSecrets.RetrieveProvider, Head.DescentCountdown,
            Head.Descent, BarkHead.Engine, SpiralTabView.EngineAvailable, SpiralTabView.StartEmbed);
        var settings = new SettingsService();
        using var fuse = new DescentCountdownService();
        var reader = new DescentService(_ => Task.FromResult<JObject?>(JObject.Parse(
            "{\"descent\":{\"devotion_days\":9}}")));
        MainShellWindow? shell = null;
        try
        {
            CoreSettings.ServiceProvider = () => settings;
            CoreSecrets.RetrieveProvider = _ => "test-token";
            var s = settings.Current;
            s.UnifiedId = "spiral-bark-test";
            s.OfflineMode = false;
            s.AvatarEnabled = true;
            s.BarkChatSuppressionMs = 0;
            s.DescentMigrationOffered = false;
            s.DescentMigrationCompleted = false;
            s.PendingDescentMigrationChoice = null;
            s.BarkLifetimeFired = new();
            s.BarkVariantRotation = new();
            Head.DescentCountdown = fuse;
            Head.Descent = reader;
            SpiralTabView.EngineAvailable = () => true;
            SpiralTabView.StartEmbed = _ => { };
            var spoken = new List<BarkSpeech>();
            BarkHead.Engine = Engine(Rule(), spoken);
            Assert.True(reader.RefreshAsync("test", force: true).GetAwaiter().GetResult());
            shell = new MainShellWindow();
            shell.Show();
            var tab = shell.Named<SpiralTabView>("SpiralTab")!;
            tab.IsVisible = false;
            tab.IsVisible = true; // WPF's visibility hook also stands alone
            Assert.Equal("stage 0 days 9", Assert.Single(spoken).Text);
        }
        finally
        {
            try { shell?.Close(); reader.Reset(); }
            finally
            {
                (CoreSettings.ServiceProvider, CoreSecrets.RetrieveProvider, Head.DescentCountdown,
                    Head.Descent, BarkHead.Engine, SpiralTabView.EngineAvailable, SpiralTabView.StartEmbed) = old;
            }
        }
    });

    private static BarkRule Rule() => new()
    {
        Id = "descent_first_spiral_open", Trigger = "DescentFirstSpiralOpen", Priority = 470,
        Repeatable = false, ScopeRaw = "lifetime",
        VariantPool = new() { new BarkVariant("stage {stage} days {banked_days}") },
    };

    private static BarkEngine Engine(BarkRule rule, List<BarkSpeech> spoken)
    {
        var engine = new BarkEngine(() => new BarkRuleSet(new[] { rule })) { Speak = spoken.Add };
        engine.Start();
        return engine;
    }
}
