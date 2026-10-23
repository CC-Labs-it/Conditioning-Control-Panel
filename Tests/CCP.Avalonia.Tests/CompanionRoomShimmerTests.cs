using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Views.Controls.Companion;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class CompanionRoomShimmerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnteringDormantAfterLoadSweepsOnce_AndUnloadedNotificationsWaitForReload(bool unloadBeforeDispatch)
        => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var previousBrain = AvApp.Brain;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        AvApp.Brain = null; // No transport: enabling AI resolves to WPF's Dormant state.
        var settings = CoreSettings.Current;
        settings.AiChatEnabled = false;
        settings.CompanionPrompt.AiProvider = AiProviderType.Local;
        var vm = ChatThresholdViewModel.CreateLive();
        var chat = new ChatThresholdView { ViewModel = vm };
        var window = new Window { Content = chat, Width = 800, Height = 600 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var band = chat.FindControl<Border>("DormantShimmer")!;
            Assert.Equal(CompanionZoneState.Disabled, vm.State);
            Assert.Equal(0, band.Opacity);

            settings.AiChatEnabled = true;
            vm.Sync();
            Assert.Equal(CompanionZoneState.Dormant, vm.State);
            if (unloadBeforeDispatch) window.Content = null;
            Dispatcher.UIThread.RunJobs();
            if (unloadBeforeDispatch)
            {
                Assert.Equal(0, band.Opacity); // The queued notification cannot reach the detached tree.
                window.Content = chat;
                Dispatcher.UIThread.RunJobs(); // OnLoaded re-reads the current state.
            }
            Assert.Equal(1, band.Opacity);
            Assert.Equal(new RelativePoint(0, 0, RelativeUnit.Relative), band.RenderTransformOrigin);
            var shift = ((TransformGroup)band.RenderTransform!).Children.OfType<TranslateTransform>().Single();
            var sweep = Assert.IsType<DoubleTransition>(Assert.Single(shift.Transitions!));
            Assert.Equal(TimeSpan.FromMilliseconds(250), sweep.Delay);
            Assert.Equal(TimeSpan.FromSeconds(1.4), sweep.Duration);

            // Returning to Dormant must not replay the one-shot or restore its opacity.
            band.Opacity = 0;
            settings.AiChatEnabled = false;
            vm.Sync();
            Dispatcher.UIThread.RunJobs();
            settings.AiChatEnabled = true;
            vm.Sync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, band.Opacity);
        }
        finally
        {
            window.Close();
            Dispatcher.UIThread.RunJobs();
            vm.Detach();
            AvApp.Brain = previousBrain;
            CoreSettings.ServiceProvider = oldSettings;
        }
    });
}
