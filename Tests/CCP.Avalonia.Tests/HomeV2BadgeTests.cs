using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class HomeV2BadgeTests
{
    // WPF FeatureCard.xaml:184-227 and FeatureCard.xaml.cs:413-419.
    [Fact]
    public Task CardPillSharesThePriceColumnAndTracksItsProperty() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var card = new FeatureCard { Width = 180, Height = 140 };
        var window = new Window { Content = card };
        try
        {
            window.Show();
            var pill = card.FindControl<Border>("V2BadgeHost")!;
            var price = card.FindControl<Border>("TierBadgeHost")!;
            Assert.False(pill.IsVisible);
            card.ShowV2Badge = true;
            window.UpdateLayout();
            Assert.True(pill.IsVisible);
            Assert.Equal("v2", Assert.IsType<TextBlock>(pill.Child).Text);
            Assert.Equal(Color.Parse("#FFE08A"), Assert.IsAssignableFrom<ISolidColorBrush>(pill.BorderBrush).Color);
            Assert.Equal(price.Parent, pill.Parent);
            Assert.Equal(0, pill.Bounds.Y);
            card.TierBadge = "TIER 1";
            window.UpdateLayout();
            Assert.True(pill.Bounds.Y >= price.Bounds.Bottom + 4);
            card.ShowV2Badge = false;
            Assert.False(pill.IsVisible);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    // WPF SettingsTabView.xaml.cs:60,424-437 and MainWindow.Presets.cs:1131-1147.
    [Fact]
    public Task HomeTracksAcceptedSnapshotsIdentityAndReattachment() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldProvider = CoreSettings.ServiceProvider;
        var oldSecrets = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        string? token = null;
        CoreSecrets.RetrieveProvider = _ => token;
        CoreSecrets.StoreProvider = (_, value) => token = value;
        var oldCheck = PrizeOwnership.IsGranted;
        var oldId = CoreAccount.UnifiedUserId;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.UnifiedId = "badge-account";
        service.Current.AuthToken = "test-token";
        service.Current.OfflineMode = false;
        CoreAccount.UnifiedUserId = "badge-account";
        PrizeOwnership.ResetHeld();
        PrizeOwnership.IsGranted = PrizeOwnership.Held;
        var view = new SettingsTabView();
        var window = new Window { Content = view };
        try
        {
            PrizeOwnership.ApplySnapshot("badge-account", 1, new[] { PrizeOwnership.JackpotRemix });
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.CardFlash.ShowV2Badge);
            Assert.False(view.CardBubblePop.ShowV2Badge);
            Assert.True(view.CardFlash.FindControl<Border>("V2BadgeHost")!.IsVisible);

            // WPF MainWindow.UiUpdates.cs:2454-2473 and OwnershipService.cs:77-82:
            // offline keeps ownership, including a response already in flight.
            service.Current.OfflineMode = true;
            view.IsVisible = false; view.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.CardFlash.ShowV2Badge);
            PrizeOwnership.ApplySnapshot("badge-account", 2, new[] { PrizeOwnership.FlashPendulum });
            Assert.True(PrizeOwnership.Held(PrizeOwnership.FlashPendulum));
            Assert.False(PrizeOwnership.Held(PrizeOwnership.JackpotRemix));
            service.Current.OfflineMode = false;

            Task.Run(() => PrizeOwnership.ApplySnapshot("badge-account", 3, new[] { PrizeOwnership.BubbleSpiralIn })).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.CardFlash.ShowV2Badge);
            Assert.True(view.CardBubblePop.ShowV2Badge);
            PrizeOwnership.ApplySnapshot("badge-account", 1, new[] { PrizeOwnership.FlashPendulum });
            PrizeOwnership.ApplySnapshot("other-account", 3, new[] { PrizeOwnership.FlashPendulum });
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.CardBubblePop.ShowV2Badge);
            Assert.False(view.CardFlash.ShowV2Badge);

            // Logout raises the identity event before clearing the stored fields.
            CoreAccount.UnifiedUserId = null;
            service.Current.UnifiedId = null;
            service.Current.AuthToken = null;
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.CardBubblePop.ShowV2Badge);
            service.Current.UnifiedId = "other-account";
            service.Current.AuthToken = "test-token";
            CoreAccount.UnifiedUserId = "other-account";
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.CardBubblePop.ShowV2Badge);
            PrizeOwnership.ApplySnapshot("other-account", 1, new[] { PrizeOwnership.FlashDriftBounce, PrizeOwnership.BubbleRain });
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.CardFlash.ShowV2Badge);
            Assert.True(view.CardBubblePop.ShowV2Badge);

            window.Content = null;
            PrizeOwnership.ApplySnapshot("other-account", 2, Array.Empty<string>());
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.CardFlash.ShowV2Badge); // detached view is not subscribed
            window.Content = view;
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.CardFlash.ShowV2Badge);
            Assert.False(view.CardBubblePop.ShowV2Badge);
        }
        finally
        {
            window.Close();
            PrizeOwnership.ResetHeld();
            PrizeOwnership.IsGranted = oldCheck;
            CoreSettings.ServiceProvider = oldProvider;
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = oldSecrets;
            CoreAccount.UnifiedUserId = oldId;
        }
        return Task.CompletedTask;
    });

    // The purchase response can arrive off the UI thread. The accepted snapshot notifies after
    // releasing the ownership lock; unchanged sets, other accounts and stale revisions do not notify.
    [Fact]
    public Task SnapshotNotifiesOnlyWhenAccepted() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var oldProvider = CoreSettings.ServiceProvider;
        var oldSecrets = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        string? token = null;
        CoreSecrets.RetrieveProvider = _ => token;
        CoreSecrets.StoreProvider = (_, value) => token = value;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.UnifiedId = "badge-account";
        service.Current.AuthToken = "test-token";
        service.Current.OfflineMode = false;
        var oldId = CoreAccount.UnifiedUserId;
        CoreAccount.UnifiedUserId = "badge-account";
        PrizeOwnership.ResetHeld();
        int calls = 0;
        void Changed() { calls++; Assert.True(PrizeOwnership.Held(PrizeOwnership.FlashPendulum)); }
        PrizeGrants.GrantsChanged += Changed;
        try
        {
            PrizeOwnership.ApplySnapshot("badge-account", 2, new[] { PrizeOwnership.FlashPendulum });
            PrizeOwnership.ApplySnapshot("badge-account", 2, new[] { PrizeOwnership.FlashPendulum });
            PrizeOwnership.ApplySnapshot("badge-account", 1, Array.Empty<string>());
            PrizeOwnership.ApplySnapshot("other-account", 3, Array.Empty<string>());
            Assert.Equal(1, calls);
        }
        finally
        {
            PrizeGrants.GrantsChanged -= Changed;
            PrizeOwnership.ResetHeld();
            CoreSettings.ServiceProvider = oldProvider;
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = oldSecrets;
            CoreAccount.UnifiedUserId = oldId;
        }
        return Task.CompletedTask;
    });

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
}
