using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed partial class AccountSeedTests
{
    [Fact]
    public void StartupSyncUnlocksOwnedTiles_WithoutCounterOrOwnershipOverride() => WithFreshInstall(async () =>
    {
        PrizeOwnership.ResetHeld();
        try
        {
            var (wire, _, _) = SignIn("u1", L40Profile("u1"));
            wire.SyncReply = """{"success":true,"prizes":{"revision":4,"grants":["cos.profile.frame.gilt","cos.profile.avatar.emi-pixel"]}}""";
            Assert.Null(PrizeOwnership.KnownGrants());
            Assert.True(await AccountSeed.LoadProfileAsync());
            Assert.NotEmpty(wire.Syncs);
            Assert.True(PrizeOwnership.Held("cos.profile.frame.gilt"));
            Assert.True(PrizeOwnership.IsGranted("cos.profile.avatar.emi-pixel"));
            await AvaloniaTestDispatcher.RunAsync(() =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
                var dialog = new ProfileCustomizeDialog(new ProfileCosmetics(), null);
                try
                {
                    foreach (var (host, id) in new[] { ("FrameHost", "profile.frame.gilt"), ("AvatarHost", "profile.avatar.emi-pixel") })
                    {
                        var items = host == "FrameHost" ? ShopCosmeticsRegistry.Frames : ShopCosmeticsRegistry.Avatars;
                        var tile = dialog.FindControl<WrapPanel>(host)!.Children.OfType<Border>()
                            .Single(t => (ToolTip.GetTip(t) as string) == ConditioningControlPanel.Localization.Loc.Get(items.Single(i => i.Id == id).NameKey));
                        Assert.NotEqual("locked", tile.Tag);
                        tile.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
                    }
                    Assert.Equal("profile.frame.gilt", dialog.Result.FrameId);
                    Assert.Equal("profile.avatar.emi-pixel", dialog.Result.AvatarId);
                }
                finally { dialog.Close(); }
                return Task.CompletedTask;
            });
            // Old or absent snapshots cannot revoke grants. A newer empty snapshot can.
            PrizeOwnership.ApplySyncSnapshot("u1", new PrizesBlock { Revision = 3, Grants = new() });
            PrizeOwnership.ApplySyncSnapshot("u1", null);
            Assert.True(PrizeOwnership.Held("cos.profile.frame.gilt"));
            CoreSettings.Current.UnifiedId = "u2";
            Assert.False(PrizeOwnership.Held("cos.profile.frame.gilt"));
            PrizeOwnership.ApplySyncSnapshot("u1", new PrizesBlock { Revision = 9, Grants = new() });
            CoreSettings.Current.UnifiedId = "u1";
            Assert.True(PrizeOwnership.Held("cos.profile.frame.gilt"));
            PrizeOwnership.ApplySyncSnapshot("u1", new PrizesBlock { Revision = 5, Grants = new() });
            Assert.Empty(PrizeOwnership.KnownGrants()!);
            await AccountSeed.Logout();
            Assert.Null(PrizeOwnership.KnownGrants());
        }
        finally { PrizeOwnership.ResetHeld(); }
    });
    [Fact]
    public void LateSyncCannotGrantCosmeticsToAnotherAccount() => WithFreshInstall(async () =>
    {
        PrizeOwnership.ResetHeld();
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        wire.SyncReply = """{"success":true,"prizes":{"revision":4,"grants":["cos.profile.frame.gilt"]}}""";
        wire.Hold = new TaskCompletionSource();
        sync.MarkLoaded(null);
        var pending = sync.PushAsync("late reply");
        try
        {
            Assert.NotEmpty(wire.Syncs);
            CoreSettings.Current.UnifiedId = "u2";
            wire.Hold.SetResult();
            Assert.False(await pending);
            Assert.Null(PrizeOwnership.KnownGrants());
            CoreSettings.Current.UnifiedId = "u1";
            Assert.False(PrizeOwnership.Held("cos.profile.frame.gilt"));
        }
        finally { wire.Hold.TrySetResult(); await pending; PrizeOwnership.ResetHeld(); }
    });

}
