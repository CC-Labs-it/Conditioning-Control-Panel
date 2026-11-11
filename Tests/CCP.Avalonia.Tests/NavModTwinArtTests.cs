using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public class NavModTwinArtTests
{
    private static void Run(Action body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        WithRestoredProviders(body);
    });

    private static void WithRestoredProviders(Action body)
    {
        var mod = CoreMods.ActiveModIdProvider;
        var art = CoreModArt.OverridePathProvider;
        try { body(); }
        finally
        {
            CoreMods.ActiveModIdProvider = mod;
            CoreModArt.OverridePathProvider = art;
        }
    }

    private static byte[] Bytes(Bitmap? bitmap)
    {
        Assert.NotNull(bitmap);
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public void ProvidersAreRestoredWhenTheBodyThrows() => Run(() =>
    {
        Func<string?> mod = () => "before";
        Func<string, string?> art = _ => "before";
        CoreMods.ActiveModIdProvider = mod;
        CoreModArt.OverridePathProvider = art;
        Assert.Throws<InvalidOperationException>(() => WithRestoredProviders(() =>
        {
            CoreMods.ActiveModIdProvider = () => "temporary";
            CoreModArt.OverridePathProvider = _ => "temporary";
            throw new InvalidOperationException();
        }));
        Assert.Same(mod, CoreMods.ActiveModIdProvider);
        Assert.Same(art, CoreModArt.OverridePathProvider);
    });

    [Theory]
    [InlineData("builtin-locked")]
    [InlineData("infection-control")]
    [InlineData("drone-mode")]
    public void ShippedTwinResolvesAndOverrideWins(string id) => Run(() =>
    {
        CoreMods.ActiveModIdProvider = () => id;
        CoreModArt.OverridePathProvider = _ => null;
        var twin = Bytes(ModArt.TryLoadBuiltIn($"nav/mods/{id}/door_social.png"));
        Assert.Equal(twin, Bytes(ModArt.TryLoad("nav/door_social.png")));
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Bytes(ModArt.TryLoadBuiltIn("nav/door_home.png")));
            CoreModArt.OverridePathProvider = _ => path;
            Assert.Equal(File.ReadAllBytes(path), Bytes(ModArt.TryLoad("nav/door_social.png")));
        }
        finally { File.Delete(path); }
    });

    [Theory]
    [InlineData("unknown")]
    [InlineData("../drone-mode")]
    [InlineData("drone-mode/..")]
    public void UnknownOrUnsafeIdFallsBack(string id) => Run(() =>
    {
        CoreMods.ActiveModIdProvider = () => id;
        CoreModArt.OverridePathProvider = _ => null;
        Assert.Equal(Bytes(ModArt.TryLoadBuiltIn("nav/door_home.png")), Bytes(ModArt.TryLoad("nav/door_home.png")));
        Assert.Equal(Bytes(ModArt.TryLoadBuiltIn("nav/door_social.png")), Bytes(ModArt.TryLoad("nav/door_social.png")));
    });

    [Fact]
    public void SwitchingArtRestoresSocialPlaceholder() => Run(() =>
    {
        var id = "drone-mode";
        CoreMods.ActiveModIdProvider = () => id;
        CoreModArt.OverridePathProvider = _ => null;
        var w = new MainShellWindow();
        try
        {
            w.ApplyDoorArt();
            var image = w.FindControl<Image>("ImgDoorSocial")!;
            var shade = w.FindControl<Border>("ShadeDoorSocial")!;
            Assert.False(shade.IsVisible);
            Assert.Equal(Bytes(ModArt.TryLoadBuiltIn("nav/mods/drone-mode/door_social.png", 128)), Bytes((Bitmap?)image.Source));
            id = "unknown";
            CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, true));
            Dispatcher.UIThread.RunJobs();
            Assert.False(shade.IsVisible);
            Assert.Equal(Bytes(ModArt.TryLoadBuiltIn("nav/door_social.png", 128)), Bytes((Bitmap?)image.Source));
        }
        finally { w.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
