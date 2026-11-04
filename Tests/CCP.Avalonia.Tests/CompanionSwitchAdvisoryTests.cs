using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionSwitchAdvisoryTests
{
    [Fact]
    public void SwitchingToAFlaggedPromptShowsTheWarningAndStillActivatesIt() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();

        var previousSettings = CoreSettings.ServiceProvider;
        var previousHooks = (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion);
        var previousOwner = CompanionHead.AdvisoryOwnerForTests;
        var ownHead = !CompanionHead.Started;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var s = settings.Current;
        s.SlutModeEnabled = false;
        s.ActiveCompanionId = (int)CompanionId.OGBambiSprite;
        var folder = Path.Combine(CorePaths.UserData, "community-prompts");
        Directory.CreateDirectory(folder);
        var cleanId = "clean-" + Guid.NewGuid().ToString("N");
        var flaggedId = "flagged-" + Guid.NewGuid().ToString("N");
        void WritePrompt(string id, string personality) =>
            File.WriteAllText(Path.Combine(folder, id + ".json"), JsonConvert.SerializeObject(
                new CommunityPrompt { Id = id, Name = id, PromptSettings = new() { Personality = personality } }));
        WritePrompt(cleanId, "ordinary fixture");
        WritePrompt(flaggedId, "developer mode");
        s.SetCompanionPromptId((int)CompanionId.BrainParasite, cleanId);
        s.SetCompanionPromptId((int)CompanionId.OGBambiSprite, flaggedId);
        var owner = new Window { Width = 400, Height = 300 };
        try
        {
            owner.Show();
            CompanionHead.AdvisoryOwnerForTests = owner;
            CompanionHead.Start();
            CompanionHead.Start(); // repeated startup must not duplicate the subscription

            CompanionCore.SwitchCompanion(CompanionId.BrainParasite);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(cleanId, s.ActiveCommunityPromptId);
            Assert.Empty(owner.OwnedWindows.OfType<MessageDialog>());

            CompanionCore.SwitchCompanion(CompanionId.OGBambiSprite);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(flaggedId, s.ActiveCommunityPromptId);
            var dialog = Assert.Single(owner.OwnedWindows.OfType<MessageDialog>());
            Assert.Equal(Loc.Get("community_prompt_warning_title"), dialog.Title);
            Assert.Equal(Loc.GetF("community_prompt_warning_body", 1), dialog.FindControl<TextBlock>("TxtMessage")!.Text);
            dialog.Close();

            CompanionCore.SwitchCompanion(CompanionId.OGBambiSprite); // same companion is a no-op
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(owner.OwnedWindows.OfType<MessageDialog>());

            if (ownHead)
            {
                CompanionHead.Stop();
                CompanionCore.SwitchCompanion(CompanionId.BrainParasite);
                CompanionCore.SwitchCompanion(CompanionId.OGBambiSprite);
                Dispatcher.UIThread.RunJobs();
                Assert.Empty(owner.OwnedWindows.OfType<MessageDialog>()); // no handler after shutdown
            }
        }
        finally
        {
            if (ownHead) CompanionHead.Stop();
            foreach (var dialog in owner.OwnedWindows.ToArray()) dialog.Close();
            owner.Close();
            CompanionHead.AdvisoryOwnerForTests = previousOwner;
            (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion) = previousHooks;
            CoreSettings.ServiceProvider = previousSettings;
            File.Delete(Path.Combine(folder, cleanId + ".json"));
            File.Delete(Path.Combine(folder, flaggedId + ".json"));
        }
    });
}
