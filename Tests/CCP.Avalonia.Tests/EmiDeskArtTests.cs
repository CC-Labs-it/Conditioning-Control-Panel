using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiDeskArtTests
{
    // WPF EmiDeskWindow.xaml.cs:350-359, 820-855; EmiDeskService.cs:288-317.
    [Fact]
    public Task SummonPaintsIdleAndPoseChangesUseTheShippedArt() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        CoreSettings.Current.EmiDeskEnabled = true;
        CoreSettings.Current.EmiDeskMuteAvatar = false;
        var service = EmiDeskService.Instance;
        try
        {
            await service.Summon();
            var desk = Assert.IsType<EmiDeskWindow>(service.Window);
            Assert.True(service.IsOut);
            var body = desk.FindControl<Image>("BodyImage")!;
            var placeholder = desk.FindControl<Border>("BodyPlaceholder")!;
            Assert.False(placeholder.IsVisible);
            AssertArt(EmiChains.BodyPath("idle")!, body);
            var idle = body.Source;
            desk.SetPose("pet");
            AssertArt(EmiChains.BodyPath("pet")!, body);
            Assert.NotSame(idle, body.Source);
            desk.SetPose("idle");
            Assert.Same(idle, body.Source);
            desk.SetPose("not-a-pose");
            Assert.Same(idle, body.Source);
        }
        finally { service.Window?.ShutDown(); }
    });

    // WPF EmiDeskWindow.xaml.cs:885-902, 938-1010: dress both layers, then restore standard.
    [Fact]
    public Task OutfitChangesRepaintBodyAndOverlayForTheCurrentPose() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        try
        {
            var body = desk.FindControl<Image>("BodyImage")!;
            var over = desk.FindControl<Image>("OutfitOverImage")!;
            desk.SetOutfit("labcoat");
            Assert.Equal("labcoat", desk.Outfit);
            AssertArt(EmiChains.BodyPath("idle", "labcoat")!, body);
            Assert.True(over.IsVisible);
            AssertArt(EmiChains.OverPath("labcoat", "idle")!, over);
            desk.SetPose("pet");
            AssertArt(EmiChains.BodyPath("pet", "labcoat")!, body);
            AssertArt(EmiChains.OverPath("labcoat", "pet")!, over);
            desk.SetOutfit("../unknown");
            Assert.Null(desk.Outfit);
            AssertArt(EmiChains.BodyPath("pet")!, body);
            Assert.False(over.IsVisible);
            Assert.Null(over.Source);
        }
        finally { desk.ShutDown(); }
        return Task.CompletedTask;
    });

    // WPF EmiDeskWindow.xaml.cs:831-840, 955-974: missing art never blanks the body,
    // and a partial overlay is disabled for the sitting.
    [Fact]
    public Task MissingArtKeepsBodyAndDisablesAnIncompleteOverlay() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var bodyPath = EmiChains.BodyPath("sway1")!;
        var overPath = EmiChains.OverPath("labcoat", "pet")!;
        var bodyBackup = bodyPath + "." + Guid.NewGuid().ToString("N");
        var overBackup = overPath + "." + Guid.NewGuid().ToString("N");
        var desk = new EmiDeskWindow();
        try
        {
            var body = desk.FindControl<Image>("BodyImage")!;
            var over = desk.FindControl<Image>("OutfitOverImage")!;
            var idle = body.Source;
            File.Move(bodyPath, bodyBackup);
            desk.SetPose("sway1");
            Assert.Same(idle, body.Source);
            desk.SetPose("idle");
            desk.SetOutfit("labcoat");
            Assert.True(over.IsVisible);
            File.Move(overPath, overBackup);
            desk.SetPose("pet");
            Assert.False(over.IsVisible);
            Assert.Null(over.Source);
            File.Move(overBackup, overPath);
            desk.SetPose("idle");
            Assert.False(over.IsVisible);
        }
        finally
        {
            if (File.Exists(bodyBackup)) File.Move(bodyBackup, bodyPath);
            if (File.Exists(overBackup)) File.Move(overBackup, overPath);
            desk.ShutDown();
        }
        return Task.CompletedTask;
    });

    private static void AssertArt(string path, Image image)
    {
        Assert.True(File.Exists(path), path);
        using var expected = new Bitmap(path);
        using var a = new MemoryStream();
        using var b = new MemoryStream();
        expected.Save(a);
        Assert.IsType<Bitmap>(image.Source).Save(b);
        Assert.Equal(a.ToArray(), b.ToArray());
    }

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}
