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
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Descent;
using Newtonsoft.Json.Linq;
using Xunit;
using HeadApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class SpiralFirstLightBlockTests
{
    // WPF SpiralTabView.xaml.cs:307, :790, :892: null re-raises hold, arrival releases,
    // a block already in hand skips the wait, and the ordinary room seeds/updates its embed.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public Task BlockHandoffAndWithdrawal(bool reduced, bool alreadyPresent) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.UnifiedId, s.OfflineMode, s.DescentCeremonyAtUtc, s.DescentMigrationCompleted,
            s.DescentMigrationOffered, s.PendingDescentMigrationChoice, s.MotionLevel, s.GentleVisuals,
            CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, HeadApp.Descent, HeadApp.DescentCountdown,
            SpiralTabView.EngineAvailable, SpiralTabView.StartEmbed, BeatLoop.Time);
        JObject reply = new();
        var descent = new DescentService(_ => Task.FromResult<JObject?>(reply));
        var sent = new List<string>();
        var time = new SteppedTime();
        MainShellWindow? shell = null;
        try
        {
            // Synthetic credentials are local to the injected fetch. No network or secret store.
            CoreSecrets.RetrieveProvider = name => name == CoreSecrets.AuthToken ? "test-token" : null;
            CoreSecrets.StoreProvider = (_, _) => { };
            s.UnifiedId = "first-light-test";
            s.OfflineMode = false;
            s.DescentCeremonyAtUtc = null;
            s.DescentMigrationCompleted = true;
            s.DescentMigrationOffered = false;
            s.PendingDescentMigrationChoice = null;
            s.MotionLevel = reduced ? MotionLevel.Reduced : MotionLevel.Full;
            s.GentleVisuals = false;
            HeadApp.Descent = descent;
            HeadApp.DescentCountdown = null;
            BeatLoop.Time = time;
            SpiralTabView.EngineAvailable = () => true;
            SpiralTabView.StartEmbed = embed => embed.SendJson = json => { sent.Add(json); return Task.CompletedTask; };
            if (alreadyPresent)
            {
                reply = Block(3);
                await descent.RefreshAsync("already in hand", force: true);
            }
            shell = new MainShellWindow();
            shell.Show();
            shell.BeginSpiralFirstLight();
            Dispatcher.UIThread.RunJobs();
            var tab = shell.Named<SpiralTabView>("SpiralTab")!;
            var fog = tab.FindControl<Grid>("FogHost")!;
            var host = tab.FindControl<Grid>("EmbedHost")!;
            var visual = Assert.IsType<SpiralFirstLightVisual>(Assert.Single(fog.Children));
            Assert.Empty(host.Children);

            if (!alreadyPresent)
            {
                await descent.RefreshAsync("null re-raise", force: true);
                Dispatcher.UIThread.RunJobs();
                time.Advance(TimeSpan.FromSeconds(5)); BeatLoop.StepAll();
                Assert.Equal(SpiralFirstLightTimeline.HandoffStartSeconds(reduced), visual.ElapsedSeconds, 3);
                Assert.True(fog.IsVisible);
                Assert.Empty(host.Children);
                reply = Block(3);
                await descent.RefreshAsync("first block", force: true);
                Dispatcher.UIThread.RunJobs();
                // Arrival does not repaint over the last half second of the reveal.
                Assert.True(fog.IsVisible);
                Assert.Empty(host.Children);
                Assert.Null(typeof(SpiralTabView).GetField("_blockWait", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)!.GetValue(tab));
            }
            time.Advance(TimeSpan.FromSeconds(5)); BeatLoop.StepAll();
            Assert.False(fog.IsVisible);
            Assert.True(tab.IsShowingSpiral);
            var embed = Assert.IsType<SpiralEmbedView>(Assert.Single(host.Children));
            embed.Receive("{\"type\":\"spiral:ready\"}");
            Assert.Equal(3, (int)JObject.Parse(Assert.Single(sent))["devotion_days"]!);
            reply = Block(8);
            await descent.RefreshAsync("update", force: true);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(embed, Assert.Single(host.Children));
            Assert.Equal(8, (int)JObject.Parse(sent[^1])["devotion_days"]!);
            Assert.Equal(2, sent.Count);
            reply = new JObject();
            await descent.RefreshAsync("withdrawal", force: true);
            Dispatcher.UIThread.RunJobs();
            Assert.False(tab.IsShowingSpiral);
            Assert.Empty(host.Children);
            Assert.True(tab.FindControl<Border>("WaitingPanel")!.IsVisible);

            // Hidden room must unsubscribe; re-entry reads the latest block.
            shell.ShowTab("settings");
            reply = Block(9);
            await descent.RefreshAsync("hidden arrival", force: true);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(host.Children);
            shell.ShowTab(SpiralRoom.TabKey);
            Assert.True(tab.IsShowingSpiral);
            Assert.Single(host.Children);
            shell.Close();
            descent.Reset();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(host.Children);
        }
        finally
        {
            shell?.Close();
            (s.UnifiedId, s.OfflineMode, s.DescentCeremonyAtUtc, s.DescentMigrationCompleted,
                s.DescentMigrationOffered, s.PendingDescentMigrationChoice, s.MotionLevel, s.GentleVisuals,
                CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, HeadApp.Descent, HeadApp.DescentCountdown,
                SpiralTabView.EngineAvailable, SpiralTabView.StartEmbed, BeatLoop.Time) = saved;
        }
    });

    private static JObject Block(int days) => new() { ["descent"] = new JObject { ["devotion_days"] = days } };

}
