using Avalonia;
using Avalonia.Headless;
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Prizes;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class PrizeOwnershipTests
{
    [Fact]
    public void Snapshot_reaches_shared_facade_for_every_grant_without_a_transport_token()
    {
        var saved = CoreAccount.UnifiedUserId;
        var (id, token) = (CoreSettings.Current.UnifiedId, CoreSettings.Current.AuthToken);
        try
        {
            CoreAccount.UnifiedUserId = CoreSettings.Current.UnifiedId = "ownership-test";
            CoreSettings.Current.AuthToken = null;
            PrizeOwnership.ResetHeld();
            PrizeOwnership.ApplySnapshot("ownership-test", 4, PrizeGrants.All);
            Assert.All(PrizeGrants.All, grant => Assert.True(PrizeGrants.IsGranted(grant), grant));
            Assert.True(PrizeOwnership.IsGranted(PrizeGrants.FlashDriftBounce));
            CoreAccount.UnifiedUserId = "other-account";
            Assert.All(PrizeGrants.All, grant => Assert.False(PrizeGrants.IsGranted(grant)));
        }
        finally
        {
            PrizeOwnership.ResetHeld();
            CoreAccount.UnifiedUserId = saved;
            (CoreSettings.Current.UnifiedId, CoreSettings.Current.AuthToken) = (id, token);
        }
    }
    [Fact]
    public async Task Grant_and_revoke_rebuild_the_attached_flash_picker_without_changing_the_saved_style()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var (saved, style, seam) = (CoreAccount.UnifiedUserId, s.FlashMotionStyle, PrizeOwnership.IsGranted);
            Window? host = null;
            try
            {
                CoreAccount.UnifiedUserId = "ownership-ui-test";
                PrizeOwnership.ResetHeld();
                PrizeOwnership.IsGranted = PrizeGrants.IsGranted;
                s.FlashMotionStyle = FlashMotionStyle.DriftBounce;
                var card = new FlashFeatureControl();
                host = new Window { Content = card };
                host.Show();
                var picker = card.FindControl<ComboBox>("CmbMotion")!;
                var row = card.FindControl<V2GetItRow>("RowGetFlashesV2")!;
                Assert.False(card.FindControl<Control>("RowMotion")!.IsVisible);
                PrizeOwnership.ApplySnapshot("ownership-ui-test", 1, new[] { PrizeGrants.FlashDriftBounce });
                Dispatcher.UIThread.RunJobs();
                Assert.True(row.IsRowHidden);
                Assert.Equal(FlashMotionStyle.DriftBounce, ((ComboBoxItem)picker.SelectedItem!).Tag);
                PrizeOwnership.ApplySnapshot("ownership-ui-test", 2, Array.Empty<string>());
                Dispatcher.UIThread.RunJobs();
                Assert.False(row.IsRowHidden);
                Assert.False(card.FindControl<Control>("RowMotion")!.IsVisible);
                Assert.Equal(FlashMotionStyle.Still, ((ComboBoxItem)picker.SelectedItem!).Tag);
                Assert.Equal(FlashMotionStyle.DriftBounce, s.FlashMotionStyle);
                host.Content = null;
                PrizeOwnership.ApplySnapshot("ownership-ui-test", 3, new[] { PrizeGrants.FlashDriftBounce });
                Dispatcher.UIThread.RunJobs();
                Assert.False(card.FindControl<Control>("RowMotion")!.IsVisible); // detached subscriptions are gone
                host.Content = card;
                Assert.True(card.FindControl<Control>("RowMotion")!.IsVisible);
            }
            finally
            {
                host?.Close();
                PrizeOwnership.ResetHeld();
                (CoreAccount.UnifiedUserId, s.FlashMotionStyle, PrizeOwnership.IsGranted) = (saved, style, seam);
                Dispatcher.UIThread.RunJobs();
            }
            return Task.CompletedTask;
        });
    }
}
