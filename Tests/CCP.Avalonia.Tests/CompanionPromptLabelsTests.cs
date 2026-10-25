using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionPromptLabelsTests
{
    [Fact]
    public Task ShellShowAndModSwitchRefreshEveryAssignedPromptAndClearStaleTooltips() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont().With(AppFonts.Options(systemEmoji: false))
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var settings = CoreSettings.Current;
        settings.Welcomed = settings.HasAcceptedAgeVerification = true;
        settings.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        settings.AwarenessModeEnabled = false;
        settings.CompanionPromptAssignments.Clear();
        var previousRewrite = CoreMods.MakeModAwareProvider;
        CoreMods.MakeModAwareProvider = text => text.Replace("Persona", "First");
        var folder = Path.Combine(CorePaths.UserData, InstalledCommunityPrompts.FolderName);
        Directory.CreateDirectory(folder);
        var ids = Enumerable.Range(0, 5).Select(_ => "label-" + Guid.NewGuid().ToString("N")).ToArray();
        for (var i = 0; i < ids.Length; i++)
        {
            settings.SetCompanionPromptId(i, ids[i]);
            File.WriteAllText(Path.Combine(folder, ids[i] + ".json"), "{\"Name\":\"Persona " + i + "\"}");
        }
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var room = CollapsedRoom.Show(shell);   // v2 collapses the room; the roster lives in its Workshop drawer
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            var workshop = (WorkshopRuntimeVm)room.FindControl<WorkshopAccordion>("WorkshopZone")!.DataContext!;
            var roster = workshop.Parts.Roster;
            void AssertLabels(string prefix)
            {
                for (var i = 0; i < 5; i++)
                {
                    var label = roster.FindControl<TextBlock>($"TxtCompanion{i}Prompt")!;
                    Assert.Equal(prefix + i, label.Text);
                    Assert.Equal(Loc.GetF("tooltip_ai_personality", prefix + i), ToolTip.GetTip(label));
                }
            }
            AssertLabels("First ");

            // WPF OnCompanionFxModChanged marshals a background mod event before repainting.
            CoreMods.MakeModAwareProvider = text => text.Replace("Persona", "Second");
            await Task.Run(() => CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false)));
            Dispatcher.UIThread.RunJobs();
            AssertLabels("Second ");

            // Changes made while away are read on return, including blank/missing/broken prompts.
            shell.ShowTab("achievements");
            Dispatcher.UIThread.RunJobs();
            settings.SetCompanionPromptId(0, null);
            File.Delete(Path.Combine(folder, ids[1] + ".json"));
            File.WriteAllText(Path.Combine(folder, ids[2] + ".json"), "broken json");
            File.WriteAllText(Path.Combine(folder, ids[3] + ".json"), "null");
            File.WriteAllText(Path.Combine(folder, ids[4] + ".json"), "{\"Name\":\"\"}");
            shell.ShowTab("companion");
            Dispatcher.UIThread.RunJobs();
            for (var i = 0; i < 5; i++)
            {
                var label = roster.FindControl<TextBlock>($"TxtCompanion{i}Prompt")!;
                Assert.Equal("", label.Text);
                Assert.Null(ToolTip.GetTip(label));
            }
        }
        finally
        {
            foreach (var window in shell.OwnedWindows.ToList()) window.Close();
            shell.RequestExit();
            Dispatcher.UIThread.RunJobs();
            foreach (var id in ids) File.Delete(Path.Combine(folder, id + ".json"));
            settings.CompanionPromptAssignments.Clear();
            CoreMods.MakeModAwareProvider = previousRewrite;
            CoreSettings.ServiceProvider = null;
        }
    });
}
