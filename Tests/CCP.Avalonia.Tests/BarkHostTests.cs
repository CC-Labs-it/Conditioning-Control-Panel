using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Bark;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The bark host on this head: a bouncing-text wall bounce and a corner hit reach the Core
/// engine through BouncingTextOverlay.Advance (WPF BarkService.cs:728-733) and a pick goes to the
/// speaker; the CoreBark doorbell is seeded by Start.</summary>
public sealed class BarkHostTests
{
    private static BarkRule Rule(string id, string trigger) => new()
    {
        Id = id + "_" + System.Guid.NewGuid().ToString("N")[..8], Trigger = trigger,
        VariantPool = new() { new BarkVariant(id + " line", id + "_1.mp3") },
    };

    [Fact]
    public void ABounceAndACornerHitEachFireABark_AndTheDoorbellIsSeeded()
    {
        var speaker = BarkHost.Speaker;
        var eng = BouncingTextOverlay.Engine;
        var hit = BouncingTextOverlay.CornerHit;
        var spoken = new List<string>();
        try
        {
            BarkHost.Stop();
            BarkHost.Start();
            BarkHost.Speaker = p => spoken.Add(p.Context.Trigger);
            BouncingTextOverlay.CornerHit = () => { };
            BouncingTextOverlay.Stop();
            eng.SetBounds(0, 0, 1000, 800);
            eng.Measure = (_, _) => (200, 60);
            eng.Start(new AppSettings());
            var l = eng.Logos[0];

            BarkHost.Engine!.Load(new BarkRuleSet(new[] { Rule("bounce", "BouncingTextBounce") }));
            (l.PosX, l.PosY, l.VelX, l.VelY) = (400, 300, 100, 100);
            BouncingTextOverlay.Advance(0.05);                       // mid-screen: no bounce, no bark
            Assert.Empty(spoken);
            (l.PosX, l.PosY, l.VelX, l.VelY) = (2, 300, -100, 0);
            BouncingTextOverlay.Advance(0.05);                       // left wall
            Assert.Equal(new[] { "BouncingTextBounce" }, spoken);

            BarkHost.Stop();                                         // a fresh engine: the 60 s global gap is per engine
            BarkHost.Start();
            BarkHost.Speaker = p => spoken.Add(p.Context.Trigger);
            BarkHost.Engine!.Load(new BarkRuleSet(new[] { Rule("corner", "BouncingTextCorner") }));
            (l.PosX, l.PosY, l.VelX, l.VelY) = (2, 2, -100, -100);
            BouncingTextOverlay.Advance(0.05);                       // both walls: a corner hit
            Assert.Equal("BouncingTextCorner", spoken.Last());

            Assert.NotNull(CoreBark.TabNavigated);
            Assert.NotNull(CoreModsHooks.ReloadBarkRules);
        }
        finally
        {
            BarkHost.Speaker = speaker;
            BouncingTextOverlay.CornerHit = hit;
            BouncingTextOverlay.Stop();
            eng.Measure = null;
            BarkHost.Stop();
        }
    }

    [Fact]
    public void PanicHoldsTheFloor_NoBarkFiresIntoTheStop()
    {
        var speaker = BarkHost.Speaker;
        var spoken = new List<string>();
        try
        {
            BarkHost.Stop();
            BarkHost.Start();
            BarkHost.Speaker = p => spoken.Add(p.Context.Trigger);
            BarkHost.Engine!.Load(new BarkRuleSet(new[] { Rule("bounce", "BouncingTextBounce") }));
            ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All.Single(s => s.Id == "barks").Stop(null);
            Assert.False(BarkHost.Raise("BouncingTextBounce"));
            Assert.Empty(spoken);
        }
        finally
        {
            BarkHost.Speaker = speaker;
            BarkHost.Stop();
        }
    }

    [Fact]
    public void TheShippedCcpDefaultManifestCarriesTheCornerEgg()
    {
        var provider = CoreMods.ActiveModIdProvider;
        try
        {
            CoreMods.ActiveModIdProvider = () => BuiltInMods.CCPDefaultId;
            var rules = BarkRuleLoader.Load();
            var egg = Assert.Single(rules.ForTrigger("BouncingTextCorner"));
            Assert.Equal("egg_corner", egg.Id);
            Assert.Equal(BarkClass.EasterEgg, egg.Class);
            var audio = BarkHost.ResolveAudio(egg.VariantPool![0].Audio);   // the voiceline is linked and resolvable
            Assert.EndsWith(System.IO.Path.Combine("builtin-ccp-default", "egg_corner_1.mp3"), audio);
        }
        finally { CoreMods.ActiveModIdProvider = provider; }
    }
}
