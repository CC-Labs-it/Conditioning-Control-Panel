using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
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
        var settings = CoreSettings.Current;
        var savedDisabled = settings.DisabledPhraseIds.ToArray();
        var savedRemoved = settings.RemovedPhraseIds.ToArray();
        try
        {
            File.WriteAllText(Path.Combine(dir, "Alpha.mp3"), "audio");
            CoreSettings.ServiceProvider = () => null; // real rows, in-memory sandbox settings
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
            Click(dialog, "BtnEnableSelected");
            Assert.DoesNotContain("VoiceLine:Alpha", CoreSettings.Current.DisabledPhraseIds);
            dialog.Close();
            dialog = new CompanionPhraseEditorDialog(); // persisted model is re-read on open
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
            CoreSettings.Current.DisabledPhraseIds.Clear();
            CoreSettings.Current.RemovedPhraseIds.Clear();
            settings.DisabledPhraseIds.UnionWith(savedDisabled);
            settings.RemovedPhraseIds.UnionWith(savedRemoved);
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
        var cell = new WorkshopTriggersCell();
        shell.Content = cell; // isolate the actual cell-to-shell forwarding from unrelated Home media
        try
        {
            shell.Show();
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
            builder.UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        else
            builder.AfterSetupCallback(builder); // a prior suite may have started headless without registering fonts
    }
}
