using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class ShopProfileTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private static void Activate(Control tile) => tile.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });

    [Fact]
    public Task LockedTilesCannotEquip_OwnedTilesEquip_ResetAndCancelKeepOriginal() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var old = PrizeOwnership.IsGranted;
        var raw = new ProfileCosmetics();
        ProfileCustomizeDialog? dialog = null;
        try
        {
            Assert.Equal(7, ShopCosmeticsRegistry.Items.Count);
            Assert.All(ShopCosmeticsRegistry.Frames, item => Assert.NotNull(ModArt.ShopFrame(item.Id)));
            Assert.All(ShopCosmeticsRegistry.Avatars, item => Assert.NotNull(ModArt.AvatarPreset(item.Id)));
            PrizeOwnership.IsGranted = _ => false;
            dialog = new ProfileCustomizeDialog(raw, null);
            var frames = dialog.FindControl<WrapPanel>("FrameHost")!;
            var avatars = dialog.FindControl<WrapPanel>("AvatarHost")!;
            Assert.Equal(6, frames.Children.Count);
            Activate(frames.Children[1]);
            Activate(avatars.Children.Last());
            Assert.Null(dialog.Result.FrameId);
            Assert.Null(dialog.Result.AvatarId);
            dialog.Close();
            PrizeOwnership.IsGranted = _ => true;
            dialog = new ProfileCustomizeDialog(raw, null);
            Activate(dialog.FindControl<WrapPanel>("FrameHost")!.Children[1]);
            Activate(dialog.FindControl<WrapPanel>("AvatarHost")!.Children.Last());
            Assert.NotNull(dialog.Result.FrameId);
            Assert.NotNull(dialog.Result.AvatarId);
            dialog.FindControl<Button>("BtnReset")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Null(dialog.Result.FrameId);
            Assert.Null(raw.FrameId);
            Assert.Null(raw.AvatarId);
        }
        finally { dialog?.Close(); PrizeOwnership.IsGranted = old; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FrameGeometryAndWireLooks_ClearUnknownArt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var frame = ShopCosmeticsRegistry.Frames.First().Id;
        var avatar = ShopCosmeticsRegistry.Avatars.First().Id;
        var adorned = new AdornedAvatar { FrameId = frame, AvatarSize = 40 };
        var layer = adorned.FindControl<Image>("FrameLayer")!;
        Assert.NotNull(layer.Source);
        Assert.Equal(40 / ShopCosmeticsRegistry.FrameWindowRatio, layer.Width, 6);
        adorned.FrameId = "unknown";
        Assert.Null(layer.Source);
        Assert.False(layer.IsVisible);
        var parsed = PlayerLook.From(Newtonsoft.Json.Linq.JObject.Parse($$"""{"avatar_id":"{{avatar}}","frame_id":"{{frame}}"}"""));
        Assert.Equal(frame, parsed.FrameId); // App seeds the wire allowlist.
        var drawn = Assert.IsType<Grid>(FriendsDrawer.Avatar("Other", 28, null, parsed));
        Assert.Null(Assert.IsType<Border>(drawn.Children[0]).Child);
        Assert.NotNull(Assert.Single(drawn.Children.OfType<Image>()).Source);
        var plain = Assert.IsType<Grid>(FriendsDrawer.Avatar("Old server", 28, null, PlayerLook.None));
        Assert.Empty(plain.Children.OfType<Image>());
        Assert.NotNull(Assert.IsType<Border>(plain.Children[0]).Child);
        return Task.CompletedTask;
    });
}
