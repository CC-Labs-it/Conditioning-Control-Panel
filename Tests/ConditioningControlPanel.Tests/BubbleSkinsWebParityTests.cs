using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.BubbleCore;
using ConditioningControlPanel.Services.BackRoom;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The bubble skins on the web: web trees never fetch from each other, so the Back Room's Breakout carries a COPY of
/// the shop page's skins (<c>prizecounter/sheet/skins.js</c> -> <c>backroom/stations/breakout/bubble-skins.js</c>) and
/// of its shell picture (<c>sheet/art/soap.png</c> -> <c>breakout/assets/skin-shell.png</c>). The copies must stay the
/// same (line endings aside), and the host must hand the worn skin to the page (CONTRACT 10.23).
/// </summary>
public class BubbleSkinsWebParityTests
{
    private static string WebDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "web")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Assets", "web");
    }

    private static string Text(params string[] parts) => File.ReadAllText(Path.Combine(new[] { WebDir() }.Concat(parts).ToArray())).Replace("\r\n", "\n");

    [Fact]
    public void Breakout_carries_the_same_skins_as_the_shop_page()
    {
        var shop = Text("prizecounter", "sheet", "skins.js");
        var room = Text("backroom", "stations", "breakout", "bubble-skins.js");
        Assert.True(shop == room, "backroom/stations/breakout/bubble-skins.js differs from prizecounter/sheet/skins.js: copy it again");
    }

    [Fact]
    public void Breakout_carries_the_same_shell_picture()
    {
        var shop = File.ReadAllBytes(Path.Combine(WebDir(), "prizecounter", "sheet", "art", "soap.png"));
        var room = File.ReadAllBytes(Path.Combine(WebDir(), "backroom", "stations", "breakout", "assets", "skin-shell.png"));
        Assert.True(shop.SequenceEqual(room), "breakout/assets/skin-shell.png differs from prizecounter/sheet/art/soap.png: copy it again");
    }

    [Fact]
    public void The_wear_module_never_imports_from_another_tree()
    {
        var wear = Text("backroom", "stations", "breakout", "bubble-wear.js");
        Assert.DoesNotContain("prizecounter/", wear.Split('\n').Where(l => l.TrimStart().StartsWith("import")).Aggregate("", (a, l) => a + l));
        Assert.Contains("from './bubble-skins.js'", wear);
    }

    [Fact]
    public void The_host_sends_the_worn_skin_and_soap_for_anything_else()
    {
        Assert.Equal("heart", BackRoomHostService.BubbleSkinWire(() => "heart"));
        Assert.Equal("champagne", BackRoomHostService.BubbleSkinWire(() => " Champagne "));
        Assert.Equal("soap", BackRoomHostService.BubbleSkinWire(() => "latex"));
        Assert.Equal("soap", BackRoomHostService.BubbleSkinWire(() => null));
        Assert.Contains(BackRoomHostService.BubbleSkinWire(), BubbleSkinIds.All);
        Assert.Equal("heart", WornLooks_SkinOf("fx.bubble.heart"));
    }

    private static string WornLooks_SkinOf(string row) => Services.Prizes.WornLooks.SkinOf(row);

    [Fact]
    public void Init_and_settings_both_carry_the_field()
    {
        var dir = new DirectoryInfo(WebDir()).Parent!.Parent!.FullName;
        var src = File.ReadAllText(Path.Combine(dir, "ConditioningControlPanel", "Services", "BackRoom", "BackRoomHostService.cs"));
        Assert.Equal(2, src.Split("bubbleSkin = BubbleSkinWire()").Length - 1);
    }
}
