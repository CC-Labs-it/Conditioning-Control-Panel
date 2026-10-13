using System.Linq;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>ChaosToyRules against WPF ChaosModeService BuildActiveToys / OnToyKey / UseToy / TickActiveToys.</summary>
public class ChaosToyRulesTests
{
    private static ChaosRunState Run(params (string id, double power)[] toys)
    {
        var s = new ChaosRunState(new ChaosRunConfig());
        foreach (var (id, p) in toys) s.ToyPower[id] = p;
        return s;
    }

    [Fact]
    public void Build_follows_catalogue_order_pockets_and_keys()
    {
        var s = Run(("snap_field", 45), ("vibe_popping", 5), ("freeze_trigger", 2));
        ChaosToyRules.Build(s, pockets: 2, isActive: _ => true, "D1", null);
        Assert.Equal(new[] { "vibe_popping", "freeze_trigger" }, s.ActiveToys.Select(t => t.Id));
        Assert.Equal(new[] { "D1", "E" }, s.ActiveToys.Select(t => t.KeyLabel));
        Assert.Equal(-1, s.ActiveToys[0].ChargesLeft);   // cooldown-based
        Assert.Equal(2, s.ActiveToys[1].ChargesLeft);    // charge-based: power = charges

        ChaosToyRules.Build(s, pockets: 0, isActive: _ => true, "Q", "E");
        Assert.Empty(s.ActiveToys);                      // zero pockets = no buttons
        ChaosToyRules.Build(s, pockets: 2, isActive: id => id == "snap_field", "Q", "E");
        Assert.Equal("snap_field", Assert.Single(s.ActiveToys).Id);
    }

    [Fact]
    public void Panic_key_outranks_a_toy_on_the_same_key()
    {
        var toys = new[] { new ChaosToyState { Id = "a", KeyLabel = "1" }, new ChaosToyState { Id = "b", KeyLabel = "E" } };
        Assert.Equal("a", ChaosToyRules.Match(toys, "D1", panicEnabled: true, panicKey: "Escape")?.Id);
        Assert.Equal("b", ChaosToyRules.Match(toys, "e", panicEnabled: true, panicKey: "Escape")?.Id);
        Assert.Null(ChaosToyRules.Match(toys, "E", panicEnabled: true, panicKey: "E"));
        Assert.Null(ChaosToyRules.Match(toys, "D1", panicEnabled: true, panicKey: "1"));
        Assert.Equal("b", ChaosToyRules.Match(toys, "E", panicEnabled: false, panicKey: "E")?.Id);
        Assert.Null(ChaosToyRules.Match(toys, "Q", panicEnabled: false, panicKey: null));
    }

    [Fact]
    public void Use_sets_cooldowns_and_spends_charges_then_tick_readies()
    {
        var s = Run(("vibe_popping", 5), ("freeze_trigger", 1), ("snap_field", 45));
        ChaosToyRules.Build(s, 2, _ => true, "Q", "E");
        var vibe = s.ActiveToys[0];
        var freeze = s.ActiveToys[1];

        Assert.Equal(ChaosToyRules.UseResult.Ignored, ChaosToyRules.Use(s, vibe, running: false));
        Assert.Equal(ChaosToyRules.UseResult.Fired, ChaosToyRules.Use(s, vibe, running: true));
        Assert.Equal(20, vibe.CooldownRemainingSec);
        Assert.True(vibe.IsEffectActive);
        Assert.Equal("20s", vibe.StatusText);
        Assert.Equal(ChaosToyRules.UseResult.Denied, ChaosToyRules.Use(s, vibe, running: true));   // cooling

        Assert.Equal(ChaosToyRules.UseResult.Fired, ChaosToyRules.Use(s, freeze, running: true));
        Assert.Equal(0, freeze.ChargesLeft);
        Assert.Equal(3, freeze.CooldownRemainingSec);   // anti-doubletap

        var ready = ChaosToyRules.Tick(s.ActiveToys, 19.75);
        Assert.Empty(ready);                            // freeze lapsed but spent: stays quiet
        Assert.Equal("spent", freeze.StatusText);
        ready = ChaosToyRules.Tick(s.ActiveToys, 0.25);
        Assert.Same(vibe, Assert.Single(ready));
        Assert.Equal("ready", vibe.StatusText);
        Assert.Empty(ChaosToyRules.Tick(s.ActiveToys, 0.25));   // a ready toy never re-dings

        var snap = new ChaosToyState { Id = "snap_field", CooldownSec = 60 };
        Assert.Equal(ChaosToyRules.UseResult.Fired, ChaosToyRules.Use(s, snap, running: true));
        Assert.Equal(45, snap.CooldownRemainingSec);    // level value IS the cooldown
    }

    [Fact]
    public void The_urge_sin_disables_every_toy()
    {
        var s = Run(("vibe_popping", 5));
        ChaosToyRules.Build(s, 1, _ => true, "Q", "E");
        s.ActivesDisabled = true;
        Assert.Equal(ChaosToyRules.UseResult.Denied, ChaosToyRules.Use(s, s.ActiveToys[0], running: true));
        Assert.Equal(0, s.ActiveToys[0].CooldownRemainingSec);
        Assert.Contains("no toys", s.RecentEvents[0]);
    }
}
