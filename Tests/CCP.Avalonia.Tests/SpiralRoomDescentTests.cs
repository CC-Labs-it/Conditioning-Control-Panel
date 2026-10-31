using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Descent;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;
using Head = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class SpiralRoomDescentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ServerBlockFlowsThroughTheRealDoor_AndExitReleasesTheReader(bool detach) => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        s.UnifiedId = "spiral-test";
        CoreSecrets.RetrieveProvider = _ => "test-token";
        s.OfflineMode = false;
        s.DescentMigrationOffered = false;
        s.DescentMigrationCompleted = false;
        s.PendingDescentMigrationChoice = null;
        using var fuse = new DescentCountdownService();
        Head.DescentCountdown = fuse;
        JObject? reply = Profile(3);
        var reader = new DescentService(_ => Task.FromResult(reply));
        Head.Descent = reader;
        var embeds = new List<SpiralEmbedView>();
        var states = new List<JObject>();
        SpiralTabView.EngineAvailable = () => true;
        SpiralTabView.StartEmbed = embed =>
        {
            embeds.Add(embed);
            embed.SendJson = json => { states.Add(JObject.Parse(json)); return Task.CompletedTask; };
            embed.Receive("{\"type\":\"spiral:ready\"}");
        };
        void Read() { Assert.True(reader.RefreshAsync("test", force: true).GetAwaiter().GetResult()); Dispatcher.UIThread.RunJobs(); }
        Read(); // the server truth can precede entry
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            fuse.ApplyCeremonyAt(DateTime.UtcNow.AddMinutes(30).ToString("yyyy-MM-ddTHH:mm:ssZ"));
            Dispatcher.UIThread.RunJobs();
            var chip = shell.Named<DescentFuseRailChip>("FuseRailChip")!;
            Assert.True(chip.IsVisible);
            chip.RaiseEvent(new global::Avalonia.Input.KeyEventArgs
                { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Enter });
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(embeds); // the countdown outranks even a server block
            fuse.ApplyCeremonyAt(null); // the open room repaints when the fuse goes dark
            Dispatcher.UIThread.RunJobs();
            var tab = shell.Named<SpiralTabView>("SpiralTab")!;
            var host = tab.FindControl<Grid>("EmbedHost")!;
            Assert.Single(host.Children);
            Assert.Equal(3, (int)Assert.Single(states)["devotion_days"]!);
            reply = Profile(4);
            Read();
            Assert.Single(embeds); // a block repaint must not recreate the browser
            Assert.Equal(4, (int)states[^1]["devotion_days"]!);
            reply = null;
            Read(); // failed transport keeps the last server truth
            Assert.Single(host.Children);
            reply = new JObject();
            Read(); // an accepted withdrawal destroys the browser
            Assert.Empty(host.Children);
            Assert.True(tab.FindControl<Border>("WaitingPanel")!.IsVisible);
            reply = Profile(5);
            Read();
            Assert.Single(host.Children);
            reader.Reset(); // sign-out must clear another account's canvas immediately
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(host.Children);
            s.DescentMigrationOffered = true;
            Read(); // even a server block must not bypass the unanswered-offer gate
            Assert.True(tab.FindControl<Grid>("FogHost")!.IsVisible);
            Assert.Empty(host.Children);
            s.PendingDescentMigrationChoice = DescentMigrationChoices.Restore;
            Dispatcher.UIThread.RunJobs();
            Assert.Single(host.Children);
            var count = embeds.Count;
            // Queue a block update, then leave before its dispatcher callback can land.
            reader.Reset();
            var parent = (Panel)tab.Parent!;
            var index = parent.Children.IndexOf(tab);
            if (detach) parent.Children.Remove(tab); else tab.IsVisible = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(host.Children);
            reply = Profile(6);
            Read();
            Assert.Equal(count, embeds.Count);
            if (detach) parent.Children.Insert(index, tab); else tab.IsVisible = true;
            tab.OnTabShown();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(host.Children);
            Assert.Equal(6, (int)states[^1]["devotion_days"]!);
        }
        finally { shell.Close(); reader.Reset(); }
    });

    private static JObject Profile(int days) => JObject.Parse(
        "{\"descent\":{\"devotion_days\":" + days + ",\"vat\":{\"cap\":4000,\"today_xp\":1000}}}");
}
