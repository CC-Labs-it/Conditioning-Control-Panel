using System;
using System.IO;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SocialDoorArtTests
{
    [Fact]
    public void EmbeddedTwinsFollowModSwitchAndDiskOverridesStillWin() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        const string path = "nav/door_social.png";
        var previousMod = CoreMods.ActiveModIdProvider;
        var previousOverride = CoreModArt.OverridePathProvider;
        var file = Path.GetTempFileName();
        try
        {
            CoreModArt.OverridePathProvider = _ => null;
            foreach (var mod in new[] { "drone-mode", "infection-control", "builtin-locked" })
            {
                CoreMods.ActiveModIdProvider = () => mod;
                Assert.Equal("nav/mods/" + mod + "/door_social.png", ModArt.EmbeddedPathFor(path));
                using var art = ModArt.TryLoad(path);
                Assert.NotNull(art);
                Assert.Equal(256, art.PixelSize.Width);
            }
            CoreMods.ActiveModIdProvider = () => "ccp-default";
            Assert.Equal(path, ModArt.EmbeddedPathFor(path));
            using (var art = ModArt.TryLoad(path))
            {
                Assert.NotNull(art);
                Assert.Equal(64, art.PixelSize.Width);
            }
            // A default-size image supplied by the disk provider beats the 256px mod twin.
            using (var source = AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/" + path)))
            using (var target = File.Create(file)) source.CopyTo(target);
            CoreMods.ActiveModIdProvider = () => "drone-mode";
            CoreModArt.OverridePathProvider = p => p == path ? file : null;
            using var overridden = ModArt.TryLoad(path);
            Assert.NotNull(overridden);
            Assert.Equal(64, overridden.PixelSize.Width);
            foreach (var id in new[] { "door.social", "tab.availablesubjects" })
            {
                var mapping = FavoritesRailArt.For(id);
                Assert.NotNull(mapping);
                Assert.Equal(path, mapping.ResourcePath);
                Assert.Equal(RailArtFit.Plate, mapping.Fit);
            }
        }
        finally
        {
            CoreMods.ActiveModIdProvider = previousMod;
            CoreModArt.OverridePathProvider = previousOverride;
            File.Delete(file);
        }
    });
}
