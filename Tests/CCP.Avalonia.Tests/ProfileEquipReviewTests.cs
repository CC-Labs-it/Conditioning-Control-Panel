using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ProfileSlots = ConditioningControlPanel.Services.ProfileEquip.ProfileSlots;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    [Fact]
    public void SanitizeOwnKeepsShopIds_OnTheTwoSlotProbe() => WithFreshInstall(async () =>
    {
        PrizeOwnership.ResetHeld();
        try
        {
            var (wire, _, _) = SignIn("u1", L40Profile("u1"));
            var slots = new ProfileCosmetics { FrameId = "prime.frame.room-host", AvatarId = "profile.avatar.emi-pixel" };
            Assert.Null(PrizeOwnership.KnownGrants());
            Assert.Equal(ProfileSlots.Of(slots), ProfileSlots.Of(MainShellWindow.SanitizeOwnWardrobe(slots)));
            Assert.Equal(default, ProfileSlots.Of(MainShellWindow.SanitizeOwnWardrobe(
                new ProfileCosmetics { FrameId = "not.a.frame", AvatarId = "not-an-avatar" })));

            wire.SyncReply = """{"success":true,"prizes":{"revision":4,"grants":["cos.prime.frame.room-host","cos.profile.avatar.emi-pixel"]}}""";
            Assert.True(await AccountSeed.LoadProfileAsync());
            DrainPrizes();   // the receipt posts to the UI thread once a headless app exists (class order)
            Assert.Contains("cos.profile.avatar.emi-pixel", PrizeOwnership.KnownGrants()!);
            Assert.Equal(ProfileSlots.Of(slots), ProfileSlots.Of(MainShellWindow.SanitizeOwnWardrobe(slots)));
            PrizeOwnership.ApplySnapshot("u1", 5, new[] { "cos.prime.frame.room-host" });
            Assert.Equal(new ProfileSlots("prime.frame.room-host", null), ProfileSlots.Of(MainShellWindow.SanitizeOwnWardrobe(slots)));
            PrizeOwnership.ApplySnapshot("u1", 6, System.Array.Empty<string>());
            Assert.Equal(default, ProfileSlots.Of(MainShellWindow.SanitizeOwnWardrobe(slots)));
        }
        finally { PrizeOwnership.ResetHeld(); }
    });

    [Fact]
    public void EquipAdoptionAndClearPaintTheVisibleCard() => WithFreshInstall(() => WithEquipWire(wire =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var s = CoreSettings.Current;
            var heldUid = CoreAccount.UnifiedUserId; CoreAccount.UnifiedUserId = s.UnifiedId;   // grants known for this account: the refusal below is real
            PrizeOwnership.ResetHeld();
            PrizeOwnership.ApplySnapshot(s.UnifiedId!, 2, new[] { "cos.prime.frame.room-host", "cos.profile.avatar.emi-pixel" });
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                wire.Reply = _ => Task.FromResult(Response("""{"ok":true,"equip":{"profile":{"frame_id":"prime.frame.room-host","avatar_id":"profile.avatar.emi-pixel"},"updatedAt":500}}"""));
                await ProfileEquipSync.PullOnceAsync();
                var avatar = shell.ProfilePage!.ProfileHeroAvatar;
                var frame = avatar.FindControl<Image>("FrameLayer")!;
                Assert.Equal("prime.frame.room-host", s.ProfileCosmetics.FrameId);
                Assert.Equal("profile.avatar.emi-pixel", s.ProfileCosmetics.AvatarId);
                Assert.Equal("prime.frame.room-host", avatar.FrameId);
                Assert.NotNull(avatar.AvatarBrush.Source);
                Assert.NotNull(frame.Source);
                Assert.True(frame.IsVisible);

                ProfileEquipSync.ResetForTests(); ProfileEquipSync.PostForTests = a => a(); ProfileEquipSync.ReadyForTests = _ => true;
                wire.Reply = _ => Task.FromResult(Response("""{"ok":true,"equip":{"profile":{"frame_id":null,"avatar_id":null},"updatedAt":1000}}"""));
                await ProfileEquipSync.PullOnceAsync();
                Assert.Null(s.ProfileCosmetics.FrameId);
                Assert.Null(s.ProfileCosmetics.AvatarId);
                Assert.Null(avatar.FrameId);
                Assert.Null(avatar.AvatarBrush.Source);
                Assert.Null(frame.Source);
                Assert.False(frame.IsVisible);

                PrizeOwnership.ApplySnapshot(s.UnifiedId!, 3, System.Array.Empty<string>());
                ProfileEquipSync.ResetForTests(); ProfileEquipSync.PostForTests = a => a(); ProfileEquipSync.ReadyForTests = _ => true;
                wire.Reply = _ => Task.FromResult(Response("""{"ok":true,"equip":{"profile":{"frame_id":"prime.frame.room-host","avatar_id":"profile.avatar.emi-pixel"},"updatedAt":1500}}"""));
                await ProfileEquipSync.PullOnceAsync();
                Assert.Null(s.ProfileCosmetics.FrameId);   // refused: the slots stay clear
                Assert.Null(s.ProfileCosmetics.AvatarId);
                Assert.Null(avatar.FrameId);
                Assert.Null(avatar.AvatarBrush.Source);
                Assert.False(frame.IsVisible);
                Assert.Equal(1000, s.ProfileEquipUpdatedMs); // retry after grants catch up
            }
            finally { shell.Close(); PrizeOwnership.ResetHeld(); CoreAccount.UnifiedUserId = heldUid; }
        })));
}
