using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
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
