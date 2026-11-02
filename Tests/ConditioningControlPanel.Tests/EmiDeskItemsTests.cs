using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ConditioningControlPanel.Services.EmiDesk;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Prize Counter's EMI desk family: the owned clamp, the art on disk, the equip wire.</summary>
public class EmiDeskItemsTests
{
    private static string AppDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "repo root not found");
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    [Fact]
    public void Owned_items_stay_on()
    {
        var look = EmiDeskItems.Clamp(new DeskLook("desk.hat.party", "desk.toy.snow-globe"),
            new[] { "cos.desk.hat.party", "cos.desk.toy.snow-globe" });
        Assert.Equal(new DeskLook("desk.hat.party", "desk.toy.snow-globe"), look);
    }

    [Fact]
    public void Unowned_wrong_slot_and_unknown_items_come_off()
    {
        Assert.Equal(new DeskLook(null, "desk.toy.rubber-duck"),
            EmiDeskItems.Clamp(new DeskLook("desk.hat.top-hat", "desk.toy.rubber-duck"), new[] { "cos.desk.toy.rubber-duck" }));
        Assert.Same(DeskLook.Empty,
            EmiDeskItems.Clamp(new DeskLook("desk.toy.rubber-duck", "desk.hat.party"),
                new[] { "cos.desk.toy.rubber-duck", "cos.desk.hat.party" }));
        Assert.Same(DeskLook.Empty, EmiDeskItems.Clamp(new DeskLook("desk.hat.crown", "../x"), null));
        Assert.Same(DeskLook.Empty, EmiDeskItems.Clamp(null, null));
    }

    [Fact]
    public void Unknown_grants_never_strip_a_pick()
    {
        Assert.Equal(new DeskLook("desk.hat.top-hat", null), EmiDeskItems.Clamp(new DeskLook("desk.hat.top-hat", null), null));
    }

    [Fact]
    public void Every_item_has_all_ten_pose_sheets()
    {
        var root = AppDir();
        Assert.Equal(4, EmiDeskItems.Items.Count);
        foreach (var item in EmiDeskItems.Items)
            foreach (var pose in EmiChains.BodyFrameFile.Keys)
                Assert.True(EmiDeskItems.OverPath(item.Id, pose, root) != null, item.Id + " " + pose);
        Assert.Null(EmiDeskItems.OverPath("desk.hat.crown", "idle", root));
    }

    [Fact]
    public void Registry_and_names_cover_every_item()
    {
        var root = AppDir();
        var reg = JObject.Parse(File.ReadAllText(Path.Combine(root, "Resources", "emi", "desk-items", "registry.json")));
        var ids = ((JArray)reg["items"]!).Select(i => (string)i["id"]!).OrderBy(x => x);
        Assert.Equal(EmiDeskItems.Items.Select(i => i.Id).OrderBy(x => x), ids);
        var en = JObject.Parse(File.ReadAllText(Path.Combine(root, "Localization", "Languages", "en.json")));
        foreach (var item in EmiDeskItems.Items)
            Assert.False(string.IsNullOrWhiteSpace((string?)en[item.NameKey]), item.NameKey);
    }
}

/// <summary>THE SKIN LAW for the desk items: the hat and toy layers draw above the face, the glass and the outfit.</summary>
[Collection(CompanionWpfRenderCollection.Name)]
public class EmiDeskItemLayerOrderTests
{
    [Fact]
    public void Desk_item_layers_sit_above_the_outfit_overlay()
    {
        WpfRenderHarness.OnStaThread(() =>
        {
            EmiDeskWindow? w = null;
            try
            {
                w = new EmiDeskWindow();
                var root = (Grid)w.FindName("BodyRoot")!;
                var kids = root.Children.Cast<UIElement>().ToList();
                int face = kids.IndexOf((UIElement)w.FindName("FaceLayer")!);
                int outfit = kids.IndexOf((UIElement)w.FindName("OutfitOverImage")!);
                int hat = kids.IndexOf((UIElement)w.FindName("DeskHatOverImage")!);
                int toy = kids.IndexOf((UIElement)w.FindName("DeskToyOverImage")!);
                Assert.True(face >= 0 && outfit > face, "outfit overlay must follow the face");
                Assert.True(hat > outfit, "hat must draw above the outfit overlay");
                Assert.True(toy > outfit, "toy must draw above the outfit overlay");
                Assert.True(w.DeskItems.IsEmpty);
            }
            finally
            {
                try { w?.Close(); } catch (Exception ex) { Assert.Fail("widget teardown threw: " + ex); }
            }
        });
    }
}
