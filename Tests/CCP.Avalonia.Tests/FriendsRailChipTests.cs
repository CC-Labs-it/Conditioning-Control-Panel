using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class FriendsDrawerTests
{
    // Headless does not transfer activation between windows. Deliver the native callbacks,
    // as AwarenessStatusPulseTests does, while exercising the actual event subscriptions.
    private static void OwnerActive(Window owner, bool active) => typeof(WindowBase)
        .GetMethod(active ? "HandleActivated" : "HandleDeactivated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .Invoke(owner, null);

    private static void ChipApp()
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    // WPF FriendsRailChip.cs:407,428 and FriendsSfx.cs:23-24.
    [Fact]
    public Task RailOpenAndClosePlayTheDrawerSounds() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc);
        var owner = new Window { Content = chip, Width = 400, Height = 400 };
        owner.Show();
        var oldPlay = CoreAudio.PlayOneShotProvider;
        var oldVolume = CoreSettings.Current.MasterVolume;
        var tags = new List<string>();
        try
        {
            CoreSettings.Current.MasterVolume = 80;
            CoreAudio.PlayOneShotProvider = (_, _, tag, _, _) => tags.Add(tag);
            FriendsLanding.SfxGate = new FriendsSfxGate();
            chip.Toggle(); Assert.True(chip.IsOpen);
            FriendsLanding.SfxGate = new FriendsSfxGate();
            chip.Toggle(); Assert.False(chip.IsOpen);
            Assert.Equal(new[] { "friends-open", "friends-close" }, tags);
            await Task.CompletedTask;
        }
        finally { CoreAudio.PlayOneShotProvider = oldPlay; CoreSettings.Current.MasterVolume = oldVolume; owner.Close(); svc.Dispose(); }
    });

    // WPF FriendsRailChip.cs:388,501: a dismissing click cannot reopen the drawer.
    [Fact]
    public Task RailDismissCooldownAndOwnerMovement() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc);
        var owner = new Window { Width = 500, Height = 600, Content = chip };
        owner.Show();
        try
        {
            chip.Toggle(); Assert.True(chip.IsOpen);
            if (TopLevel.GetTopLevel(chip.Drawer) is WindowBase popup) Assert.False(popup.Topmost);
            owner.Position = new PixelPoint(40, 50);
            Assert.False(chip.IsOpen);
            chip.Toggle(); Assert.False(chip.IsOpen);
            await Task.Delay(260);
            chip.Toggle(); Assert.True(chip.IsOpen);
            owner.WindowState = WindowState.Minimized;
            Assert.False(chip.IsOpen);
            owner.Content = null;
            owner.Activate();
            Assert.False(chip.IsOpen);
        }
        finally { owner.Close(); svc.Dispose(); }
    });

    // Each close path gets its own assertion, independent of the topmost check below.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task RailOwnerLossClosesIndependently(bool minimize) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc);
        var owner = new Window { Width = 500, Height = 600, Content = chip };
        owner.Show();
        try
        {
            chip.Toggle(); Assert.True(chip.IsOpen);
            if (minimize) owner.WindowState = WindowState.Minimized;
            else { OwnerActive(owner, false); Dispatcher.UIThread.RunJobs(); }
            Assert.False(chip.IsOpen);
            await Task.CompletedTask;
        }
        finally { owner.Close(); svc.Dispose(); }
    });

    // WPF FriendsRailChip.cs:228,235,501,505: topmost is reset on activation;
    // minimizing or leaving the panel closes the drawer.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task RailOwnerActivationAndLoss(bool minimize) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc);
        var owner = new Window { Width = 500, Height = 600, Content = chip };
        var other = new Window { Width = 100, Height = 100 };
        owner.Show();
        try
        {
            chip.Toggle();
            var popup = Assert.IsAssignableFrom<WindowBase>(TopLevel.GetTopLevel(chip.Drawer));
            popup.Topmost = true;
            // Re-deliver the native activation notification without deactivating the owner:
            // deactivation correctly light-dismisses the popup before it could be reactivated.
            OwnerActive(owner, true);
            Assert.False(popup.Topmost);
            Assert.True(chip.IsOpen);
            if (minimize) owner.WindowState = WindowState.Minimized;
            else
            {
                Assert.False(chip.Drawer.IsKeyboardFocusWithin);
                OwnerActive(owner, false);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.False(chip.IsOpen);
            await Task.CompletedTask;
        }
        finally { other.Close(); owner.Close(); svc.Dispose(); }
    });

    // WPF FriendsRailChip.cs:501-508: leaving folds the drawer; returning permits code input.
    [Fact]
    public Task RailCodeInputAfterOwnerReturns() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        ChipApp();
        var (svc, _) = Service();
        var chip = new FriendsRailChip(svc);
        var owner = new Window { Width = 500, Height = 600, Content = chip };
        var other = new Window { Width = 100, Height = 100 };
        owner.Show();
        try
        {
            await svc.RefreshAsync();
            chip.Toggle();
            Click(chip.Drawer, "friends-add-open");
            Dispatcher.UIThread.RunJobs();
            var box = Tagged<TextBox>(chip.Drawer, "friends-code-box")!;
            Assert.NotNull(box);
            other.Show(); other.Activate();
            OwnerActive(owner, false);
            Dispatcher.UIThread.RunJobs();
            Assert.False(chip.IsOpen);
            OwnerActive(owner, true);
            await Task.Delay(260);
            chip.Toggle();
            Click(chip.Drawer, "friends-add-open");
            Dispatcher.UIThread.RunJobs();
            box = Tagged<TextBox>(chip.Drawer, "friends-code-box")!;
            Assert.NotNull(box);
            Click(TopLevel.GetTopLevel(box)!, box);
            Dispatcher.UIThread.RunJobs();
            Assert.True(owner.IsActive);
            Assert.True(box.IsFocused);
            Assert.True(chip.IsOpen);
        }
        finally { other.Close(); owner.Close(); svc.Dispose(); }
    });
}
