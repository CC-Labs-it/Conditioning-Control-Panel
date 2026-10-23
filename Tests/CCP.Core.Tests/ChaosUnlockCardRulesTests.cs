using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>What each hub purchase announces (WPF ChaosUnlockCardOverlay.cs ChaosUnlockCards,
/// moved to Core ChaosUnlockCardRules): ribbon, accent, the auto-equip context line, the
/// capstone-only deepen card, pockets-only bench cards and the cue.</summary>
public class ChaosUnlockCardRulesTests
{
    private static ChaosLifetimeBoon Boon(ChaosBoonCategory c, bool capstone = false) =>
        ChaosLifetimeBoons.All.First(b => b.Category == c && (!capstone || b.CapstoneDesc.Length > 0));

    private static void WithState(ChaosMetaState state, System.Action body)
    {
        var old = CoreSettings.Current.ChaosActiveSlot;
        try
        {
            ChaosMetaStore.Delete(2);
            ChaosMetaStore.Save(state, 2);
            ChaosMeta.SwitchSlot(2);
            body();
        }
        finally
        {
            ChaosMetaStore.Delete(2);
            CoreSettings.Current.ChaosActiveSlot = old;
            ChaosMeta.Init();
        }
    }

    [Fact]
    public void Boon_unlock_context_follows_the_pocket_state()
    {
        var toy = Boon(ChaosBoonCategory.Skill);
        var acc = Boon(ChaosBoonCategory.Accessory);
        var charm = Boon(ChaosBoonCategory.Utility);
        WithState(new ChaosMetaState
        {
            ToyPockets = 1,
            LifetimeBoonLevels = new Dictionary<string, int> { [toy.Id] = 1, [acc.Id] = 1, [charm.Id] = 1 },
            ActiveLifetimeBoons = new HashSet<string> { toy.Id },
        }, () =>
        {
            var t = ChaosUnlockCardRules.ForBoonUnlock(toy.Id)!;
            Assert.Equal(("NEW TOY UNLOCKED", ChaosUnlockCardRules.ToyAccent), (t.Ribbon, t.AccentRgb));
            Assert.Equal((toy.Name, toy.Desc, toy.Glyph, "boons"), (t.Title, t.Desc, t.Glyph, t.IconFolder));
            Assert.StartsWith("slipped straight into a pocket", t.Context);

            var a = ChaosUnlockCardRules.ForBoonUnlock(acc.Id)!;
            Assert.Equal(("NEW ACCESSORY UNLOCKED", ChaosUnlockCardRules.AccessoryAccent), (a.Ribbon, a.AccentRgb));
            Assert.StartsWith("no pocket to carry it yet", a.Context);   // zero accessory pockets

            var c = ChaosUnlockCardRules.ForBoonUnlock(charm.Id)!;
            Assert.Equal(("NEW CHARM UNLOCKED", "switched on — it works every descent."), (c.Ribbon, c.Context));
            Assert.Equal(("unlock_card", 0.65f), c.Cue);
            Assert.Null(ChaosUnlockCardRules.ForBoonUnlock("no_such_boon"));
        });
        // A pocket exists but is taken by another toy -> swap it in from the BAG.
        var other = ChaosLifetimeBoons.All.First(b => b.Category == ChaosBoonCategory.Skill && b.Id != toy.Id);
        WithState(new ChaosMetaState
        {
            ToyPockets = 1,
            LifetimeBoonLevels = new Dictionary<string, int> { [toy.Id] = 1, [other.Id] = 1 },
            ActiveLifetimeBoons = new HashSet<string> { other.Id },
        }, () => Assert.StartsWith("your pockets are full", ChaosUnlockCardRules.ForBoonUnlock(toy.Id)!.Context));
    }

    [Fact]
    public void Only_the_final_deepen_gets_a_capstone_card()
    {
        var b = Boon(ChaosBoonCategory.Skill, capstone: true);
        WithState(new ChaosMetaState { LifetimeBoonLevels = new Dictionary<string, int> { [b.Id] = b.MaxLevel - 1 } },
            () => Assert.Null(ChaosUnlockCardRules.ForUpgrade(b.Id)));
        WithState(new ChaosMetaState { LifetimeBoonLevels = new Dictionary<string, int> { [b.Id] = b.MaxLevel } }, () =>
        {
            var card = ChaosUnlockCardRules.ForUpgrade(b.Id)!;
            Assert.Equal((ChaosUnlockCardRules.CapstoneRibbon, b.CapstoneDesc), (card.Ribbon, card.Desc));
            Assert.Equal(("capstone_reached", 0.7f), card.Cue);
        });
    }

    [Fact]
    public void Habits_lessons_and_pockets_announce_with_wpf_copy()
    {
        var u = ChaosUpgrades.All.First();
        var h = ChaosUnlockCardRules.ForHabit(u.Id)!;
        Assert.Equal(("HABIT TRAINED", ChaosUnlockCardRules.HabitAccent, u.Name), (h.Ribbon, h.AccentRgb, h.Title));
        Assert.Equal(u.IconPath, h.IconPath);
        Assert.Null(ChaosUnlockCardRules.ForHabit("no_such_habit"));

        var lesson = ChaosUnlockCardRules.ForLesson(Boon(ChaosBoonCategory.Accessory).Id)!;
        Assert.Equal(("LESSON LEARNED", ChaosUnlockCardRules.AccessoryAccent), (lesson.Ribbon, lesson.AccentRgb));
        Assert.Equal(ChaosUnlockCardRules.HabitAccent, ChaosUnlockCardRules.ForLesson(u.Id)!.AccentRgb);
        Assert.Null(ChaosUnlockCardRules.ForLesson("nothing"));

        WithState(new ChaosMetaState { ToyPockets = 2, AccessoryPockets = 1 }, () =>
        {
            var toy = ChaosUnlockCardRules.ForBench(BenchIds.ToyPocket2)!;
            Assert.Equal((ChaosUnlockCardRules.PocketRibbon, "second toy pocket"), (toy.Ribbon, toy.Title));
            Assert.Equal("you can now carry 2 toys into the descent at once. pick yours from the BAG.", toy.Desc);
            Assert.Equal(("pocket_sewn", 0.7f), toy.Cue);
            Assert.Equal("you can now carry one accessory into the descent. unlocked accessorys equip from the BAG.",
                ChaosUnlockCardRules.ForBench(BenchIds.AccPocket1)!.Desc);
            Assert.Null(ChaosUnlockCardRules.ForBench(BenchIds.Diary));   // other rows reveal their own surface
        });
    }
}
