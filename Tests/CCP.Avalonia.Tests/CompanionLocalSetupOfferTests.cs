using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using Xunit;
using Mode = ConditioningControlPanel.Views.Controls.Companion.CompanionProviderMode;
using Status = ConditioningControlPanel.Services.AIService.OllamaSetupService.InstallStatus;

namespace CCP.Avalonia.Tests;

public sealed class CompanionLocalSetupOfferTests
{
    private static void SetupApp()
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Theory]
    [InlineData(Status.NotInstalled, false)]
    [InlineData(Status.InstalledNotRunning, false)]
    [InlineData(Status.RunningNoModel, false)]
    [InlineData(Status.NotInstalled, true)]
    [InlineData(Status.Ready, false)]
    public Task SelectingLocalOffersSetupUnlessReady(Status status, bool accept) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        SetupApp();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldDetect = EngineRoomVm.DetectLocal;
        var oldWizardDetect = LocalAiSetupWizard.Detect;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.Welcomed = settings.Current.HasAcceptedAgeVerification = true;
        settings.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        settings.Current.CompanionPrompt.AiModel = "offer-probe-model";
        settings.Current.CompanionPrompt.AiProvider = ConditioningControlPanel.Models.AiProviderType.Cloud;
        settings.Current.AiChatEnabled = true;
        string? detectedModel = null;
        EngineRoomVm.DetectLocal = model =>
        {
            detectedModel = model;
            return Task.FromResult(new OllamaSetupService.StatusSnapshot { Status = status });
        };
        LocalAiSetupWizard.Detect = (_, _) => Task.FromResult(new OllamaSetupService.StatusSnapshot { Status = Status.NotInstalled });
        var host = new EngineRoomDrawer();
        var shell = new Window { Content = host };
        try
        {
            shell.Show();
            var vm = new EngineRoomVm(host);
            host.DataContext = vm;
            vm.Provider = Mode.LocalOllama;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("offer-probe-model", detectedModel);
            if (status == Status.Ready)
            {
                Assert.Empty(shell.OwnedWindows.OfType<MessageDialog>());
                Assert.Empty(shell.OwnedWindows.OfType<LocalAiSetupWizard>());
            }
            else
            {
                var offer = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
                Assert.Equal(Loc.Get("dialog_local_ai_setup_offer_title"), offer.Title);
                Assert.Equal(Loc.Get("dialog_local_ai_setup_offer_body"), offer.FindControl<TextBlock>("TxtMessage")!.Text);
                var button = offer.FindControl<Button>(accept ? "BtnOk" : "BtnCancel")!;
                Assert.Equal(Loc.Get(accept ? "label_yes" : "label_no"), ((TextBlock)button.Content!).Text);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(accept ? 1 : 0, shell.OwnedWindows.OfType<LocalAiSetupWizard>().Count());
            }
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            EngineRoomVm.DetectLocal = oldDetect;
            LocalAiSetupWizard.Detect = oldWizardDetect;
            CoreSettings.ServiceProvider = oldSettings;
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task PendingDetectionDoesNotDuplicateOrOfferAfterLeaving(bool closeOwner) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        SetupApp();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldDetect = EngineRoomVm.DetectLocal;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.AiChatEnabled = true;
        settings.Current.CompanionPrompt.AiProvider = ConditioningControlPanel.Models.AiProviderType.Cloud;
        var gate = new TaskCompletionSource<OllamaSetupService.StatusSnapshot>();
        int probes = 0;
        EngineRoomVm.DetectLocal = _ => { probes++; return gate.Task; };
        var host = new EngineRoomDrawer();
        var owner = new Window { Content = host };
        try
        {
            owner.Show();
            var vm = new EngineRoomVm(host);
            vm.Provider = Mode.LocalOllama;
            vm.Provider = Mode.Cloud;
            vm.Provider = Mode.LocalOllama;
            Assert.Equal(1, probes);
            if (closeOwner) owner.Close(); else vm.Provider = Mode.Cloud;
            gate.SetResult(new OllamaSetupService.StatusSnapshot { Status = Status.NotInstalled });
            // P57: await the offer itself; a shown dialog would keep it pending and time out here.
            await vm.LocalOffer!.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(owner.OwnedWindows.OfType<MessageDialog>());
        }
        finally
        {
            gate.TrySetResult(new OllamaSetupService.StatusSnapshot { Status = Status.Ready });
            owner.Close();
            EngineRoomVm.DetectLocal = oldDetect;
            CoreSettings.ServiceProvider = oldSettings;
        }
    });
}
