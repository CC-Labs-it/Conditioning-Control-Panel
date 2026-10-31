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
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
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
            shell.ShowTab("companion");
            // WPF v2: Companion > AI adopts the Workshop Triggers cell (Pages/AiPage.xaml.cs:32).
            shell.Named<SectionTabStrip>("SectionStrip")!.PillFor("companionai")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            // The active room is now the AI page; the legacy CompanionRoomView stays collapsed.
            var room = shell.Named<AiPage>("CompanionAiTab")!;
            var cell = (WorkshopTriggersCell)room.FindControl<ContentControl>("TriggersHost")!.Content!;
            Assert.True(room.IsVisualAncestorOf(cell));
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

    [Fact]
    public void TriggerControlsLoadWithoutSavingAndPersistUserChanges() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        try
        {
            service.Current.TriggerModeEnabled = true;
            service.Current.TriggerIntervalSeconds = 137;
            var cell = new WorkshopTriggersCell();
            var toggle = cell.FindControl<CheckBox>("ChkTriggerModeCompanion")!;
            var slider = cell.FindControl<Slider>("SliderTriggerIntervalCompanion")!;
            var panel = cell.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!;
            var label = cell.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!;
            Assert.True(toggle.IsChecked);
            Assert.True(panel.IsVisible);
            Assert.Equal(137, slider.Value);
            Assert.Equal("137s", label.Text);
            Assert.False(service.HasPendingSave);
            slider.Value = 183.9;
            Assert.Equal(183, service.Current.TriggerIntervalSeconds);
            Assert.Equal("183s", label.Text);
            Assert.False(service.HasPendingSave); // WPF saves the toggle, not each slider tick.
            toggle.IsChecked = false;
            Assert.False(panel.IsVisible);
            Assert.False(service.Current.TriggerModeEnabled);
            Assert.True(service.HasPendingSave);
            service.SaveImmediate();
            var reload = new SettingsService();
            Assert.False(reload.Current.TriggerModeEnabled);
            Assert.Equal(183, reload.Current.TriggerIntervalSeconds);
            // Reopening the page re-reads externally changed state without writing it.
            service.Current.TriggerModeEnabled = true;
            service.Current.TriggerIntervalSeconds = 245;
            cell.SyncSettings();
            Assert.True(toggle.IsChecked);
            Assert.True(panel.IsVisible);
            Assert.Equal("245s", label.Text);
            Assert.False(service.HasPendingSave);
        }
        finally { CoreSettings.ServiceProvider = oldProvider; }
    });

    [Fact]
    public void AiPageTriggerEditorNormalizesSavesAndCancelsWithoutRestoringDefaults() => AvaloniaTestDispatcher.Run(() =>
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
        settings.TriggerModeEnabled = true;
        settings.CustomTriggers = new() { "KEEP", "keep", " ", "REMOVE" };
        settings.UserAddedCustomTriggers.Clear();
        settings.UserAddedCustomTriggers.UnionWith(new[] { "REMOVE", "KEEP" });
        var shell = new MainShellWindow();
        var oldLanguage = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            shell.Show();
            shell.ShowTab("companionai");
            Dispatcher.UIThread.RunJobs();
            var page = shell.Named<AiPage>("CompanionAiTab")!;
            var cell = (WorkshopTriggersCell)page.FindControl<ContentControl>("TriggersHost")!.Content!;
            Assert.True(page.IsVisualAncestorOf(cell));
            Assert.True(cell.FindControl<Button>("BtnEditTriggersCompanion")!.IsEffectivelyVisible);
            settings.TriggerIntervalSeconds = 229;
            shell.ShowTab("companion");
            shell.ShowTab("companionai");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("229s", cell.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!.Text);
            void Open() => cell.FindControl<Button>("BtnEditTriggersCompanion")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            TextEditorDialog Editor() => Assert.Single(shell.OwnedWindows.OfType<TextEditorDialog>());
            TextItem[] Items(TextEditorDialog d) => d.FindControl<ItemsControl>("ItemList")!.Items.OfType<TextItem>().ToArray();
            void ClickEditor(TextEditorDialog d, string name) => d.FindControl<Button>(name)!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Open();
            var editor = Editor();
            Assert.Equal(new[] { "KEEP", "REMOVE" }, Items(editor).Select(x => x.Text));
            Items(editor).Single(x => x.Text == "REMOVE").IsEnabled = false;
            editor.FindControl<TextBox>("TxtNewItem")!.Text = "hand typed";
            ClickEditor(editor, "BtnAdd");
            ClickEditor(editor, "BtnSave");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "KEEP", "HAND TYPED" }, settings.CustomTriggers);
            Assert.Contains("HAND TYPED", settings.UserAddedCustomTriggers);
            Assert.Contains("KEEP", settings.UserAddedCustomTriggers);
            Assert.DoesNotContain("REMOVE", settings.UserAddedCustomTriggers);
            Assert.True(service.HasPendingSave);
            service.SaveImmediate();
            Assert.Equal(settings.CustomTriggers, new SettingsService().Current.CustomTriggers);

            Open();
            editor = Editor();
            ClickEditor(editor, "BtnCancel");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "KEEP", "HAND TYPED" }, settings.CustomTriggers);
            Assert.False(service.HasPendingSave);
            settings.CustomTriggers.Clear();
            Open();
            editor = Editor();
            Assert.Empty(Items(editor));
            ClickEditor(editor, "BtnSave");
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(settings.CustomTriggers);

            // Verify the real button route in every language after the existing save/cancel checks.
            foreach (var (language, title) in new[]
            {
                ("en", "Trigger Phrases"),
                ("de", "Auslösesätze"),
                ("es", "Frases de activación"),
                ("fr", "Phrases de déclenchement"),
                ("it", "Frasi di attivazione"),
                ("ja", "トリガーフレーズ"),
                ("ko", "트리거 문구"),
                ("pt-BR", "Frases de ativação"),
                ("ru", "Триггерные фразы"),
                ("zh-CN", "触发短语"),
            })
            {
                LocalizationManager.Instance.SetLanguage(language);
                Open();
                editor = Editor();
                Assert.Equal(title, editor.FindControl<ConditioningControlPanel.Avalonia.Controls.IconLabel>("TxtTitle")!.Text);
                Assert.Equal(Loc.GetF("title_manager", title), editor.Title);
                ClickEditor(editor, "BtnCancel");
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            foreach (var owned in shell.OwnedWindows.ToArray()) owned.Close();
            shell.RequestExit();
            LocalizationManager.Instance.SetLanguage(oldLanguage);
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
