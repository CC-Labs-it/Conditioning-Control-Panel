using System;
using System.IO;
using System.Reflection;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

[Collection(CCP.Core.Tests.SessionStatics.Name)]
public class DtrhMetaBridgeTests : IDisposable
{
    private readonly string _path = Path.Combine(CorePaths.UserData, "chaos_meta.test.json");
    private readonly byte[]? _original;

    public DtrhMetaBridgeTests()
    {
        Directory.CreateDirectory(CorePaths.UserData);
        _original = File.Exists(_path) ? File.ReadAllBytes(_path) : null;
    }
    public void Dispose()
    {
        if (_original == null) File.Delete(_path); else File.WriteAllBytes(_path, _original);
    }

    private static (DtrhMetaBridge bridge, ChaosMetaState state) Make()
    {
        var bridge = new DtrhMetaBridge(true, _ => { });
        var state = (ChaosMetaState)typeof(DtrhMetaBridge).GetField("_testState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bridge)!;
        return (bridge, state);
    }
    private static bool Send(DtrhMetaBridge b, string json) => b.Handle(JObject.Parse(json));

    [Fact]
    public void Upgrade_requires_funds_and_increments_revision_only_on_purchase()
    {
        var (b, s) = Make(); s.Sparks = 9; s.PurchasedUpgrades.Clear();
        Assert.False(Send(b, "{op:'purchase-upgrade',id:'extreme_tier',cost:10}"));
        Assert.Equal(0, b.Rev);
        s.Sparks = 10;
        Assert.True(Send(b, "{op:'purchase-upgrade',id:'extreme_tier',cost:10}"));
        Assert.Equal(1, b.Rev); Assert.Equal(0, s.Sparks); Assert.True(s.ExtremeUnlocked);
        b.FlushSave();
    }

    [Fact]
    public void Dial_checks_rank_before_the_one_time_gift()
    {
        var (b, s) = Make(); var runs = ChaosMeta.State.RunsCompleted;
        try
        {
            ChaosMeta.State.RunsCompleted = 0; s.Gold = 0; s.GiftGiven = false; s.PurchasedDials.Clear();
            Assert.False(Send(b, "{op:'purchase-dial',id:'hydra',cost:10}"));
            Assert.False(s.GiftGiven);
            Assert.True(Send(b, "{op:'purchase-dial',id:'first',cost:10}"));
            Assert.True(s.GiftGiven);
            Assert.False(Send(b, "{op:'purchase-dial',id:'second',cost:10}"));
            ChaosMeta.State.RunsCompleted = 25; s.Gold = 10;
            Assert.True(Send(b, "{op:'purchase-dial',id:'hydra',cost:10}"));
            Assert.Equal(0, s.Gold);
        }
        finally { ChaosMeta.State.RunsCompleted = runs; b.FlushSave(); }
    }

    [Fact]
    public void First_dial_gift_raises_bark_once()
    {
        var s = ChaosMeta.State;
        var oldGold = s.Gold; var oldGift = s.GiftGiven;
        var oldDials = new System.Collections.Generic.HashSet<string>(s.PurchasedDials);
        var oldBark = ChaosLessons.GiftGivenBark;
        var path = ChaosMetaStore.SlotFilePath(ChaosMetaStore.ActiveSlot);
        var oldFile = File.Exists(path) ? File.ReadAllBytes(path) : null;
        var calls = 0;
        var b = new DtrhMetaBridge(false, _ => { });
        try
        {
            s.Gold = 0; s.GiftGiven = false; s.PurchasedDials.Clear();
            ChaosLessons.GiftGivenBark = () => calls++;
            Assert.True(Send(b, "{op:'purchase-dial',id:'first',cost:10}"));
            Assert.False(Send(b, "{op:'purchase-dial',id:'second',cost:10}"));
            Assert.Equal(1, calls);
        }
        finally
        {
            b.FlushSave();
            s.Gold = oldGold; s.GiftGiven = oldGift;
            s.PurchasedDials.Clear(); s.PurchasedDials.UnionWith(oldDials);
            ChaosLessons.GiftGivenBark = oldBark;
            if (oldFile == null) File.Delete(path); else File.WriteAllBytes(path, oldFile);
        }
    }

    [Fact]
    public void Craft_spends_materials_and_consumable_charge()
    {
        var (b, s) = Make(); s.Materials.Clear(); s.CraftedItems.Clear();
        Assert.False(Send(b, "{op:'craft',id:'slippery_slope',cost:{silicone:2}}"));
        s.Materials["silicone"] = 2;
        Assert.True(Send(b, "{op:'craft',id:'slippery_slope',cost:{silicone:2}}"));
        Assert.Equal(0, s.Materials["silicone"]); Assert.Contains("slippery_slope", s.DiscoveredRecipes);
        Assert.True(Send(b, "{op:'consume-crafted',id:'slippery_slope'}"));
        Assert.False(Send(b, "{op:'consume-crafted',id:'slippery_slope'}"));
        b.FlushSave();
    }

    [Fact]
    public void Presets_cap_at_five_and_filter_setup()
    {
        var (b, s) = Make(); s.RunPresets.Clear();
        for (int i = 0; i < 5; i++)
            Assert.True(Send(b, $"{{op:'save-preset',name:'p{i}',setup:{{difficulty:'Hard',secret:123}}}}"));
        Assert.False(Send(b, "{op:'save-preset',name:'extra',setup:{}}"));
        Assert.Equal(5, s.RunPresets.Count);
        Assert.Equal("Hard", (string?)s.RunPresets[0].Setup["difficulty"]);
        Assert.Null(s.RunPresets[0].Setup["secret"]);
        b.FlushSave();
    }

    [Fact]
    public void Reset_onboarding_rearms_lessons_but_preserves_grants()
    {
        var (b, s) = Make(); s.SeenWarrenWelcome = true; s.GiftGiven = true;
        s.FirstTimesAwarded.Add("first_play"); s.LessonProgress["test"] = 5;
        Assert.True(Send(b, "{op:'reset-onboarding'}"));
        Assert.False(s.SeenWarrenWelcome); Assert.True(s.GiftGiven); Assert.Contains("first_play", s.FirstTimesAwarded);
        Assert.Equal(5, s.LessonProgress["test"]); Assert.True(s.ForceScriptedRun);
        b.FlushSave();
    }

    [Fact]
    public void Flush_persists_pending_changes()
    {
        Directory.CreateDirectory(CorePaths.UserData);
        var path = Path.Combine(CorePaths.UserData, "chaos_meta.test.json");
        var old = File.Exists(path) ? File.ReadAllBytes(path) : null;
        try
        {
            var (b, s) = Make(); s.Sparks = 0;
            Assert.True(Send(b, "{op:'add-gold',amount:37}"));
            int expected = s.Gold;
            b.FlushSave();
            Assert.Equal(expected, (int)JObject.Parse(File.ReadAllText(path))["Gold"]!);
        }
        finally { if (old == null) File.Delete(path); else File.WriteAllBytes(path, old); }
    }
}
