using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.CompanionTab.cs:367 BtnCompanionPersonality_Click + CompanionService.cs:214 ApplyCompanionPrompt.</summary>
public sealed class WorkshopRosterPersonalityTests
{
    [Fact]
    public Task PersonalityButtonImportsAssignsLabelsAndSwitchActivates() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        Assert.False(CoreMods.Service?.IsCompanionSupported((CompanionId)1) == false, "active mod hides companion card 1");
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.ActiveCompanionId = 0;
        s.ActiveCommunityPromptId = null;
        s.SlutModeEnabled = false;
        var dir = Path.Combine(Path.GetTempPath(), "ccp-roster-personality-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        CompanionCore.Prompts = new CommunityPromptService(userDataPath: dir, validatorProvider: () => null);
        string Write(string name)
        {
            var p = Path.Combine(dir, name + ".json");
            File.WriteAllText(p, "{\"Id\":\"x\",\"Name\":\"ignored\",\"PromptSettings\":{\"UseCustomPrompt\":true,\"Personality\":\"" + name + "\"}}");
            return p;
        }
        var cell = new WorkshopRosterCell();
        var window = new Window { Content = cell, Width = 400, Height = 600 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            void ClickPersonality(string file)
            {
                cell.PickPromptFileForTests = _ => Task.FromResult<string?>(Write(file));
                cell.FindControl<Button>("BtnCompanion1Personality")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                foreach (var w in window.OwnedWindows.ToArray()) w.Close();   // the confirmation box
                Dispatcher.UIThread.RunJobs();
            }

            // Assign to a companion that is not active: stored and labelled, not activated.
            ClickPersonality("Strict");
            var strictId = s.GetCompanionPromptId(1);
            Assert.False(string.IsNullOrEmpty(strictId));
            Assert.Null(s.ActiveCommunityPromptId);
            var label = cell.FindControl<TextBlock>("TxtCompanion1Prompt")!;
            Assert.Equal("Strict", label.Text);
            Assert.Equal(Loc.GetF("tooltip_ai_personality", "Strict"), ToolTip.GetTip(label));
            Assert.Contains("Strict", cell.LastDialogForTests);

            // Switching to her activates the assigned prompt (WPF SwitchCompanion -> ApplyCompanionPrompt).
            CompanionCore.SwitchCompanion((CompanionId)1);
            Assert.Equal(strictId, s.ActiveCommunityPromptId);

            // Assigning to the already-active companion activates at once.
            ClickPersonality("Soft");
            Assert.NotEqual(strictId, s.GetCompanionPromptId(1));
            Assert.Equal(s.GetCompanionPromptId(1), s.ActiveCommunityPromptId);
            Assert.Equal("Soft", label.Text);
        }
        finally
        {
            window.Close();
            CompanionCore.Prompts.Dispose();
            CompanionCore.Prompts = null!;
            CoreSettings.ServiceProvider = oldProvider;
            try { Directory.Delete(dir, true); } catch { }
        }
    });
}
