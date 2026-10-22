using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WorkshopTriggersWiringTests
{
    private static Task Run(Func<SettingsService, WorkshopTriggersCell, Window, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.UserAddedCustomTriggers = new HashSet<string>();
        service.Current.TriggerModeEnabled = true;
        service.Current.TriggerIntervalSeconds = 90;
        var cell = new WorkshopTriggersCell();
        var window = new Window { Content = cell, Width = 400, Height = 600 };
        try { window.Show(); Dispatcher.UIThread.RunJobs(); await body(service, cell, window); }
        finally { foreach (var dialog in window.OwnedWindows.ToArray()) dialog.Close(); window.Close(); CoreSettings.ServiceProvider = old; }
    });

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Until(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && clock.Elapsed < TimeSpan.FromSeconds(10)) { await Task.Yield(); Dispatcher.UIThread.RunJobs(); }
        Assert.True(done());
    }

    [Fact]
    public Task SettingsRoundTripAndCloudRestoreRebindWithoutWritingDisplayDefaults() => Run((service, cell, window) =>
    {
        var toggle = cell.FindControl<CheckBox>("ChkTriggerModeCompanion")!;
        var slider = cell.FindControl<Slider>("SliderTriggerIntervalCompanion")!;
        var label = cell.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!;
        Assert.True(toggle.IsChecked);
        Assert.True(cell.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!.IsVisible);
        Assert.Equal(90, slider.Value);
        Assert.Equal("90s", label.Text);
        slider.Value = 73.9;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(73.9, slider.Value); // own write must not echo back and snap the drag
        toggle.IsChecked = false;
        Assert.Equal(73, service.Current.TriggerIntervalSeconds);
        Assert.False(service.Current.TriggerModeEnabled);
        service.Current.TriggerModeEnabled = true; // avatar quick-menu writes the same model
        service.Current.TriggerIntervalSeconds = 110;
        Dispatcher.UIThread.RunJobs();
        Assert.True(toggle.IsChecked);
        Assert.Equal("110s", label.Text);
        window.Content = null;
        service.Current.TriggerIntervalSeconds = 125;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("110s", label.Text);
        window.Content = cell;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("125s", label.Text);
        var outgoing = service.Current;
        service.RestoreFrom(new AppSettings { TriggerModeEnabled = false, TriggerIntervalSeconds = 155 });
        Dispatcher.UIThread.RunJobs();
        Assert.False(toggle.IsChecked);
        Assert.Equal("155s", label.Text);
        outgoing.TriggerIntervalSeconds = 220;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("155s", label.Text);
        return Task.CompletedTask;
    });

    [Fact]
    public Task EditorDeduplicatesAndPersistsEnabledRowsWithUserPhraseCustody() => Run(async (service, cell, window) =>
    {
        service.Current.CustomTriggers = new List<string> { "KEEP", "keep", " ", "remove" };
        service.Current.UserAddedCustomTriggers = new HashSet<string> { "remove" };
        Click(cell.FindControl<Button>("BtnEditTriggersCompanion")!);
        var dialog = Assert.Single(window.OwnedWindows.OfType<TextEditorDialog>());
        var items = dialog.FindControl<ItemsControl>("ItemList")!.ItemsSource!.Cast<TextItem>().ToArray();
        Assert.Equal(2, items.Length);
        items.Single(i => i.Text == "remove").IsEnabled = false;
        dialog.FindControl<TextBox>("TxtNewItem")!.Text = "my trigger";
        Click(dialog.FindControl<Button>("BtnAdd")!);
        Click(dialog.FindControl<Button>("BtnSave")!);
        await Until(() => service.Current.CustomTriggers.Contains("MY TRIGGER"));
        Assert.Equal(new[] { "KEEP", "MY TRIGGER" }, service.Current.CustomTriggers);
        Assert.Contains("my trigger", service.Current.UserAddedCustomTriggers);
        Assert.DoesNotContain("remove", service.Current.UserAddedCustomTriggers);
    });

    [Fact]
    public Task EmptyEditorDoesNotReseedDefaultsAndCancelLeavesSettingsUntouched() => Run(async (service, cell, window) =>
    {
        service.Current.CustomTriggers = new List<string>();
        Click(cell.FindControl<Button>("BtnEditTriggersCompanion")!);
        var dialog = Assert.Single(window.OwnedWindows.OfType<TextEditorDialog>());
        Assert.Empty(dialog.FindControl<ItemsControl>("ItemList")!.ItemsSource!.Cast<TextItem>());
        dialog.FindControl<TextBox>("TxtNewItem")!.Text = "discard me";
        Click(dialog.FindControl<Button>("BtnAdd")!);
        Click(dialog.FindControl<Button>("BtnCancel")!);
        await Until(() => dialog.OwnedWindows.Any());
        var discard = Assert.Single(dialog.OwnedWindows);
        Click(discard.GetLogicalDescendants().OfType<Button>().Single(b => (b.Content as TextBlock)?.Text == "Yes"));
        await Until(() => !dialog.IsVisible);
        Assert.Empty(service.Current.CustomTriggers);
        Assert.Empty(service.Current.UserAddedCustomTriggers);
    });
    [Fact]
    public Task SavingAnEditorOpenedBeforeCloudRestoreDoesNotOverwriteEitherPool() => Run(async (service, cell, window) =>
    {
        var original = service.Current;
        original.CustomTriggers = new List<string> { "OLD" };
        Click(cell.FindControl<Button>("BtnEditTriggersCompanion")!);
        var dialog = Assert.Single(window.OwnedWindows.OfType<TextEditorDialog>());
        var closed = false;
        dialog.Closed += (_, _) => closed = true;
        dialog.FindControl<TextBox>("TxtNewItem")!.Text = "stale edit";
        Click(dialog.FindControl<Button>("BtnAdd")!);
        service.RestoreFrom(new AppSettings { CustomTriggers = new List<string> { "RESTORED" } });
        Click(dialog.FindControl<Button>("BtnSave")!);
        await Until(() => closed);
        Dispatcher.UIThread.RunJobs(); // let EditTriggers' continuation after the dialog finish
        Assert.False(dialog.IsVisible);
        Assert.Equal(new[] { "RESTORED" }, service.Current.CustomTriggers);
        Assert.Equal(new[] { "OLD" }, original.CustomTriggers);
    });

    // WPF MainWindow.Assets.cs:1837-1967 and MainWindow.Patreon.cs:1320-1331.
    [Fact]
    public Task PhrasePresetsSaveApplyAndDeleteAndTheCountFollows() => Run(async (service, cell, window) =>
    {
        var s = service.Current;
        s.CustomCompanionPhrases = new List<CustomCompanionPhrase>
        {
            new() { Id = "c1", Text = "one", Enabled = true },
            new() { Id = "c2", Text = "two", Enabled = true },
        };
        s.PhrasePresets = new List<PhrasePreset>();
        s.CurrentPhrasePresetId = null;
        var combo = cell.FindControl<ComboBox>("CmbPhrasePresets")!;
        var count = cell.FindControl<TextBlock>("TxtPhraseCount")!;
        var all = CompanionPhraseEditorDialog.ActivePhraseCount();

        string? asked = null;
        cell.AskText = (_, _, _, def) => { asked = def; return Task.FromResult<string?>("  Evening "); };
        Click(cell.FindControl<Button>("BtnSavePhrasePreset")!);
        await Until(() => s.PhrasePresets.Count == 1);
        var evening = s.PhrasePresets[0];
        Assert.Equal("Preset 1", asked);
        Assert.Equal("Evening", evening.Name);
        Assert.Equal(all, evening.ActivePhraseCount);
        Assert.Equal(evening.Id, s.CurrentPhrasePresetId);
        Assert.Same(evening, combo.SelectedItem);

        s.CustomCompanionPhrases[0].Enabled = false;          // the Phrase Manager mutes one ...
        cell.AskText = (_, _, _, _) => Task.FromResult<string?>("Quiet");
        Click(cell.FindControl<Button>("BtnSavePhrasePreset")!);
        await Until(() => s.PhrasePresets.Count == 2);
        Assert.Equal(all - 1, s.PhrasePresets[1].ActivePhraseCount);

        combo.SelectedItem = evening;                         // ... and choosing a preset applies it
        Assert.True(s.CustomCompanionPhrases[0].Enabled);
        Assert.Equal(evening.Id, s.CurrentPhrasePresetId);
        Assert.Equal($"{all} active", count.Text);

        cell.Confirm = (_, _, _) => Task.FromResult(false);
        Click(cell.FindControl<Button>("BtnDeletePhrasePreset")!);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, s.PhrasePresets.Count);
        string? confirmed = null;
        cell.Confirm = (_, _, msg) => { confirmed = msg; return Task.FromResult(true); };
        Click(cell.FindControl<Button>("BtnDeletePhrasePreset")!);
        await Until(() => s.PhrasePresets.Count == 1);
        Assert.Contains("'Evening'", confirmed);
        Assert.Null(s.CurrentPhrasePresetId);
        Assert.Null(combo.SelectedItem);
        string? informed = null;
        cell.Inform = (_, title, _) => { informed = title; return Task.CompletedTask; };
        Click(cell.FindControl<Button>("BtnDeletePhrasePreset")!);
        await Until(() => informed != null);
        Assert.Equal("No Preset Selected", informed);
        Assert.Single(s.PhrasePresets);
    });

    [Fact]
    public Task ManagePhrasesOpensThePhraseManagerAndRecountsOnClose() => Run(async (service, cell, window) =>
    {
        var s = service.Current;
        s.CustomCompanionPhrases = new List<CustomCompanionPhrase> { new() { Id = "m1", Text = "hi", Enabled = true } };
        window.Content = null;                                // reattach: the count is read on attach
        window.Content = cell;
        Dispatcher.UIThread.RunJobs();
        var count = cell.FindControl<TextBlock>("TxtPhraseCount")!;
        var all = CompanionPhraseEditorDialog.ActivePhraseCount();
        Assert.Equal($"{all} active", count.Text);
        Click(cell.FindControl<Button>("BtnManagePhrases")!);
        var dialog = Assert.Single(window.OwnedWindows.OfType<CompanionPhraseEditorDialog>());
        s.CustomCompanionPhrases[0].Enabled = false;
        Assert.Equal($"{all} active", count.Text);
        dialog.Close();
        await Until(() => count.Text == $"{all - 1} active");
    });
}
