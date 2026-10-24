using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Bark;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CompanionPhraseEditorTests
{
    [Fact]
    public void RealVoiceAndBarkRowsFilterAndPersistThroughTheToolbar() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var old = (CoreSettings.ServiceProvider, CoreMods.ActiveModIdProvider,
            CoreMods.ActiveModPackageProvider, CoreBark.AllLinesProvider);
        var root = Path.Combine(Path.GetTempPath(), "ccp-phrase-" + Guid.NewGuid());
        var dir = Path.Combine(root, "resources", "sounds", "flashes_audio");
        Directory.CreateDirectory(dir);
        CompanionPhraseEditorDialog? dialog = null;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Alpha.mp3"), "audio");
            var service = new SettingsService(); // real settings.json in the CCP_USERDATA_DIR sandbox
            CoreSettings.ServiceProvider = () => service;
            CoreSettings.Current.DisabledPhraseIds.Clear();
            CoreSettings.Current.RemovedPhraseIds.Clear();
            CoreMods.ActiveModIdProvider = () => "test";
            CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest { Id = "test" }, root, false);
            CoreBark.AllLinesProvider = () => new[] { new BarkLineInfo("Bark:test:hello", "test", "Greeting", "Hello", "hello.mp3", dir) };
            dialog = new CompanionPhraseEditorDialog();
            var rows = Rows(dialog);
            Assert.Contains(rows, p => p.Id == "VoiceLine:Alpha" && p.HasAudio);
            Assert.Contains(rows, p => p.Id == "Bark:test:hello" && p.GroupLabel == "Bark · Greeting");
            var filter = dialog.FindControl<ComboBox>("CmbCategoryFilter")!;
            filter.SelectedItem = filter.Items.OfType<ComboBoxItem>().Single(x => Equals(x.Tag, "VoiceLine"));
            Assert.Equal("VoiceLine:Alpha", Assert.Single(Rows(dialog)).Id);
            Click(dialog, "BtnSelectAll");
            Click(dialog, "BtnDisableSelected");
            Assert.Contains("VoiceLine:Alpha", CoreSettings.Current.DisabledPhraseIds);
            Assert.DoesNotContain("Bark:test:hello", CoreSettings.Current.DisabledPhraseIds);
            Assert.True(service.HasPendingSave);  // the dialog asked to persist, not just mutate memory
            service.SaveImmediate();
            var reloaded = new SettingsService(); // from disk
            Assert.Contains("VoiceLine:Alpha", reloaded.Current.DisabledPhraseIds);
            CoreSettings.ServiceProvider = () => reloaded;
            dialog.Close();
            dialog = new CompanionPhraseEditorDialog(); // persisted model is re-read on open
            Assert.False(Assert.Single(Rows(dialog), p => p.Id == "VoiceLine:Alpha").IsEnabled);
            Click(dialog, "BtnSelectAll");
            Click(dialog, "BtnEnableSelected");
            Assert.DoesNotContain("VoiceLine:Alpha", CoreSettings.Current.DisabledPhraseIds);
            filter = dialog.FindControl<ComboBox>("CmbCategoryFilter")!;
            filter.SelectedItem = filter.Items.OfType<ComboBoxItem>().Single(x => Equals(x.Tag, "Bark"));
            Assert.Equal("Bark:test:hello", Assert.Single(Rows(dialog)).Id);
            dialog.FindControl<TextBox>("TxtSearch")!.Text = "missing";
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(Rows(dialog));
        }
        finally
        {
            dialog?.Close();
            (CoreSettings.ServiceProvider, CoreMods.ActiveModIdProvider,
                CoreMods.ActiveModPackageProvider, CoreBark.AllLinesProvider) = old;
            Directory.Delete(root, true);
        }
    });

    [Fact]
    public void WorkshopManageButtonOpensThePhraseManager() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var settings = service.Current;
        settings.Welcomed = true;
        settings.HasAcceptedAgeVerification = true;
        settings.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
        settings.AwarenessModeEnabled = false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            // v2 user path (WPF AiPage): Companion > AI adopts the Workshop Triggers cell.
            var room = shell.GetLogicalDescendants().OfType<CompanionRoomView>().Single();
            var workshop = (WorkshopRuntimeVm)room.FindControl<WorkshopAccordion>("WorkshopZone")!.DataContext!;
            shell.ShowTab("companionai");
            Dispatcher.UIThread.RunJobs();
            var cell = workshop.Parts.Triggers;
            Assert.True(shell.GetLogicalDescendants().OfType<ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages.AiPage>().Single().IsVisualAncestorOf(cell));
            cell.FindControl<Button>("BtnManagePhrases")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Single(shell.OwnedWindows.OfType<CompanionPhraseEditorDialog>());
        }
        finally
        {
            foreach (var owned in shell.OwnedWindows.ToArray()) owned.Close();
            shell.RequestExit();
            CoreSettings.ServiceProvider = oldProvider;
        }
    });

    private static CompanionPhrase[] Rows(CompanionPhraseEditorDialog dialog) =>
        dialog.FindControl<ItemsControl>("PhraseList")!.Items.OfType<PhraseGroup>().SelectMany(g => g.Items).ToArray();
    private static void Click(CompanionPhraseEditorDialog dialog, string name) =>
        dialog.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Setup()
    {
        var builder = global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp();
        if (Application.Current == null)
            builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        else
            builder.AfterSetupCallback(builder); // a prior suite may have started headless without registering fonts
    }
}
