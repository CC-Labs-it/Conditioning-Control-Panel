using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiDeskItemsTests
{
    [Fact]
    public void KnownGrantsAreRevisionedAndScopedToTheSignedInAccount()
    {
        var s = CoreSettings.Current;
        var old = (s.UnifiedId, s.OfflineMode);
        var retrieve = CoreSecrets.RetrieveProvider;
        CoreSecrets.RetrieveProvider = _ => "test";
        int changes = 0;
        void Changed() => changes++;
        try
        {
            s.UnifiedId = "desk-test"; s.OfflineMode = false;
            PrizeOwnership.ResetHeld();
            PrizeOwnership.GrantsChanged += Changed;
            Assert.Null(PrizeOwnership.KnownGrants());
            PrizeOwnership.ApplySnapshot("other", 1, new[] { "cos.desk.hat.party" });
            Assert.Equal(0, changes);
            PrizeOwnership.ApplySnapshot("desk-test", 2, new[] { "cos.desk.hat.party" });
            Assert.Contains("cos.desk.hat.party", PrizeOwnership.KnownGrants()!);
            PrizeOwnership.ApplySnapshot("desk-test", 1, Array.Empty<string>());
            Assert.Equal(1, changes);
            Assert.Contains("cos.desk.hat.party", PrizeOwnership.KnownGrants()!);
            PrizeOwnership.ApplySnapshot("desk-test", 3, Array.Empty<string>());
            Assert.Empty(PrizeOwnership.KnownGrants()!);
            Assert.Equal(2, changes);
            s.UnifiedId = "other";
            Assert.Null(PrizeOwnership.KnownGrants());
        }
        finally
        {
            PrizeOwnership.GrantsChanged -= Changed;
            PrizeOwnership.ResetHeld();
            (s.UnifiedId, s.OfflineMode) = old;
            CoreSecrets.RetrieveProvider = retrieve;
        }
    }

    [Fact]
    public Task LayersFollowPoseStandDownForMissingArtAndReleaseOnShutdown() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.EmiDeskHat, s.EmiDeskToy);
        var account = (s.UnifiedId, s.OfflineMode);
        var retrieve = CoreSecrets.RetrieveProvider;
        CoreSecrets.RetrieveProvider = _ => "test";
        s.UnifiedId = "desk-render"; s.OfflineMode = false;
        PrizeOwnership.ResetHeld();
        s.EmiDeskHat = null; s.EmiDeskToy = null;
        var desk = new EmiDeskWindow();
        try
        {
            var root = desk.FindControl<Grid>("BodyRoot")!;
            var hat = desk.FindControl<Image>("DeskHatOverImage")!;
            var toy = desk.FindControl<Image>("DeskToyOverImage")!;
            Assert.True(root.Children.IndexOf(hat) > root.Children.IndexOf(desk.FindControl<Image>("OutfitOverImage")!));
            Assert.True(root.Children.IndexOf(toy) > root.Children.IndexOf(hat));
            Assert.False(hat.IsHitTestVisible);
            Assert.False(hat.IsVisible);
            foreach (var item in EmiDeskItemRules.Items)
                foreach (var pose in EmiChains.BodyFrameFile.Keys)
                    Assert.NotNull(EmiDeskItemRules.OverPath(item.Id, pose));
            desk.SetDeskItems(new DeskItemLook("desk.hat.party", "desk.toy.rubber-duck"));
            Assert.True(hat.IsVisible); Assert.NotNull(hat.Source);
            Assert.True(toy.IsVisible); Assert.NotNull(toy.Source);
            var idle = hat.Source;
            desk.SetPose("pet");
            Assert.NotSame(idle, hat.Source);
            desk.SetPose("idle");
            Assert.Same(idle, hat.Source);
            desk.SetDeskItems(new DeskItemLook("unknown", null));
            Assert.Null(hat.Source); Assert.False(hat.IsVisible);
            Assert.Null(toy.Source); Assert.False(toy.IsVisible);
            s.EmiDeskHat = "desk.hat.top-hat";
            desk.RefreshDeskItems(); // unknown grants preserve a valid saved pick
            Assert.NotNull(hat.Source);
            PrizeOwnership.ApplySnapshot("desk-render", 1, Array.Empty<string>());
            Assert.Null(hat.Source); // live grant refresh removes an unowned pick
            PrizeOwnership.ApplySnapshot("desk-render", 2, new[] { "cos.desk.hat.top-hat" });
            Assert.NotNull(hat.Source);
            desk.ShutDown();
            Assert.Null(hat.Source); Assert.Null(toy.Source);
            desk.SetDeskItems(new DeskItemLook("desk.hat.party", null));
            Assert.Null(hat.Source);
        }
        finally
        {
            desk.ShutDown();
            PrizeOwnership.ResetHeld();
            (s.EmiDeskHat, s.EmiDeskToy) = old;
            (s.UnifiedId, s.OfflineMode) = account;
            CoreSecrets.RetrieveProvider = retrieve;
        }
        return Task.CompletedTask;
    });
}
