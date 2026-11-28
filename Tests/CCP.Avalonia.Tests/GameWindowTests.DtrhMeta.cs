using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void Dtrh_Direct_launch_loads_active_save_before_any_command() => RunDtrh(true, (_, _, _, _) =>
    {
        const int slot = 2;
        var settings = CoreSettings.Current;
        var oldSlot = settings.ChaosActiveSlot;
        var path = ChaosMetaStore.SlotFilePath(slot);
        var oldFile = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var original = ChaosMeta.State;
        var oldSparks = original.Sparks;
        try
        {
            settings.ChaosActiveSlot = slot;
            ChaosMetaStore.Save(new ChaosMetaState { Sparks = 127, Gold = 31, RunsCompleted = 7 }, slot);
            original.Sparks = 2; // a fresh process has not loaded the file yet
            var w = GameWindow.Launch("dtrh")!;
            w.Web.OnNavigationCompleted(w.PageUrl!);
            var sent = new List<JObject>(); w.Posted += json => sent.Add(JObject.Parse(json));
            w.Web.OnWebMessage("{type:'ready'}");
            var snapshot = Assert.Single(sent, x => (string?)x["type"] == "meta");
            Assert.Equal(127, (int)snapshot["state"]!["sparks"]!);
            Assert.Equal(31, (int)snapshot["state"]!["gold"]!);
            Assert.Equal(7, (int)snapshot["state"]!["runsCompleted"]!);
            w.Web.OnWebMessage("{type:'meta-command',op:'purchase-upgrade',id:'meta_load_test',cost:7}");
            w.Close();
            var saved = ChaosMetaStore.Load(slot);
            Assert.Equal(120, saved.Sparks);
            Assert.Equal(31, saved.Gold);
            Assert.Equal(7, saved.RunsCompleted);
            Assert.Contains("meta_load_test", saved.PurchasedUpgrades);
        }
        finally
        {
            original.Sparks = oldSparks;
            settings.ChaosActiveSlot = oldSlot;
            if (oldFile == null) File.Delete(path); else File.WriteAllBytes(path, oldFile);
        }
    });

    [Fact]
    public void Dtrh_Launch_keeps_live_hub_state() => RunDtrh(true, (_, _, _, _) =>
    {
        ChaosMetaStore.Save(new ChaosMetaState { Sparks = 41 }, ChaosMetaStore.ActiveSlot);
        var hub = new ConditioningControlPanel.Avalonia.Views.Chaos.ChaosHubWindow();
        hub.Show();
        try
        {
            var live = ChaosMeta.State;
            live.Sparks = 77; // newer than the file, as with a live hub edit
            var w = GameWindow.Launch("dtrh")!;
            Assert.Same(live, ChaosMeta.State);
            w.Web.OnNavigationCompleted(w.PageUrl!);
            var sent = new List<JObject>(); w.Posted += json => sent.Add(JObject.Parse(json));
            w.Web.OnWebMessage("{type:'ready'}");
            var snapshot = Assert.Single(sent, x => (string?)x["type"] == "meta");
            Assert.Equal(77, (int)snapshot["state"]!["sparks"]!);
            live.Sparks = 79;
            Assert.Same(w, GameWindow.Launch("dtrh"));
            Assert.Same(live, ChaosMeta.State);
            Assert.Equal(79, ChaosMeta.State.Sparks);
            w.Close();
        }
        finally { hub.Close(); }
    });

    [Fact]
    public void Dtrh_Launch_keeps_active_classic_run_state() => RunDtrh(true, (_, _, _, _) =>
    {
        ChaosMetaStore.Save(new ChaosMetaState { Sparks = 41 }, ChaosMetaStore.ActiveSlot);
        var live = ChaosMeta.State;
        var oldSparks = live.Sparks;
        try
        {
            ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost.StartRun(new ChaosRunConfig
                { DurationSec = 60, WaveCount = 3, BoonDraftEnabled = false });
            Assert.True(ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost.IsActive);
            live.Sparks = 77;
            var w = GameWindow.Launch("dtrh")!;
            Assert.Same(live, ChaosMeta.State);
            Assert.Equal(77, ChaosMeta.State.Sparks);
            w.Close();
        }
        finally
        {
            ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost.ForceShutdown();
            live.Sparks = oldSparks;
        }
    });

    [Fact]
    public void Dtrh_Meta_ready_purchase_and_close_flush() => RunDtrh(true, (_, _, _, _) =>
    {
        var s = ChaosMeta.State;
        var oldSparks = s.Sparks;
        var owned = s.PurchasedUpgrades.Contains("meta_test");
        var path = ChaosMetaStore.SlotFilePath(ChaosMetaStore.ActiveSlot);
        var oldFile = File.Exists(path) ? File.ReadAllBytes(path) : null;
        try
        {
            s.Sparks = 10; s.PurchasedUpgrades.Remove("meta_test");
            ChaosMeta.Save();
            var w = GameWindow.Launch("dtrh")!;
            w.Web.OnNavigationCompleted(w.PageUrl!);
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.Web.OnWebMessage("{type:'ready'}");
            Assert.Equal(new[] { "init", "meta" }, sent.Take(2).Select(x => (string?)x["type"]));
            var rev = (int)sent[1]["rev"]!;
            w.Web.OnWebMessage("{type:'meta-command',op:'purchase-upgrade',id:'meta_test',cost:10}");
            var purchase = sent.Last();
            Assert.Equal("meta", (string?)purchase["type"]);
            Assert.True((int)purchase["rev"]! > rev);
            Assert.Equal(0, (int)purchase["state"]!["sparks"]!);
            w.Close();
            var saved = JObject.Parse(File.ReadAllText(path));
            Assert.Contains("meta_test", saved["PurchasedUpgrades"]!.Values<string>());
        }
        finally
        {
            s.Sparks = oldSparks;
            if (owned) s.PurchasedUpgrades.Add("meta_test"); else s.PurchasedUpgrades.Remove("meta_test");
            if (oldFile == null) File.Delete(path); else File.WriteAllBytes(path, oldFile);
        }
    });

    [Fact]
    public void Dtrh_Run_end_broadcasts_new_pending_reveals() => RunDtrh(true, (_, _, _, _) =>
    {
        var s = ChaosMeta.State;
        var oldPending = s.PendingReveals.ToArray(); var oldSeen = s.SeenReveals.ToArray();
        var path = ChaosMetaStore.SlotFilePath(ChaosMetaStore.ActiveSlot);
        var oldFile = File.Exists(path) ? File.ReadAllBytes(path) : null;
        try
        {
            s.RunsCompleted = 1;
            s.PendingReveals.Remove(RevealIds.Dollhouse); s.SeenReveals.Remove(RevealIds.Dollhouse);
            ChaosMeta.Save();
            var w = GameWindow.Launch("dtrh")!;
            w.Web.OnNavigationCompleted(w.PageUrl!);
            var sent = new List<JObject>(); w.Posted += json => sent.Add(JObject.Parse(json));
            w.Web.OnWebMessage("{type:'ready'}"); sent.Clear();
            w.Web.OnWebMessage("{type:'run-started'}");
            w.Web.OnWebMessage("{type:'run-ended',score:100,durationSec:180}");
            var meta = Assert.Single(sent.Where(x => (string?)x["type"] == "meta"));
            Assert.Contains(RevealIds.Dollhouse, meta["state"]!["pendingReveals"]!.Values<string>());
            w.Close();
        }
        finally
        {
            s.PendingReveals.Clear(); s.PendingReveals.UnionWith(oldPending);
            s.SeenReveals.Clear(); s.SeenReveals.UnionWith(oldSeen);
            if (oldFile == null) File.Delete(path); else File.WriteAllBytes(path, oldFile);
        }
    });
}
