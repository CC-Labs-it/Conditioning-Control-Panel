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
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WorkshopTriggersCellTests
{
    [Fact]
    public Task SettingsLoadWithoutWritesAndUserControlsUpdateTheModel() => Run((cell, owner, service) =>
    {
        var s = service.Current;
        Assert.True(cell.FindControl<CheckBox>("ChkTriggerModeCompanion")!.IsChecked);
        Assert.True(cell.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!.IsVisible);
        Assert.Equal("123s", cell.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!.Text);
        Assert.False(service.HasPendingSave);
        cell.FindControl<Slider>("SliderTriggerIntervalCompanion")!.Value = 87.9;
        Assert.Equal(87, s.TriggerIntervalSeconds);
        Assert.Equal("87s", cell.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!.Text);
        cell.FindControl<CheckBox>("ChkTriggerModeCompanion")!.IsChecked = false;
        Assert.False(s.TriggerModeEnabled);
        Assert.False(cell.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!.IsVisible);
        Assert.True(service.HasPendingSave);
        return Task.CompletedTask;
    });

    [Fact]
    public Task EditorFiltersSeedAndSaveKeepsOnlyEnabledLinesWithUserBookkeeping() => Run(async (cell, owner, service) =>
    {
        var s = service.Current;
        s.CustomTriggers = new() { "Keep", "keep", " ", "Remove" };
        s.UserAddedCustomTriggers.Add("Remove");
        Click(cell.FindControl<Button>("BtnEditTriggersCompanion")!);
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.IsType<TextEditorDialog>(Assert.Single(owner.OwnedWindows));
        var items = dialog.FindControl<ItemsControl>("ItemList")!.ItemsSource!.Cast<TextItem>().ToList();
        Assert.Equal(new[] { "Keep", "Remove" }, items.Select(i => i.Text));
        items.Single(i => i.Text == "Remove").IsEnabled = false;
        dialog.FindControl<TextBox>("TxtNewItem")!.Text = "new line";
        Click(dialog.FindControl<Button>("BtnAdd")!);
        Click(dialog.FindControl<Button>("BtnSave")!);
        await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "Keep", "NEW LINE" }, s.CustomTriggers);
        Assert.Contains("NEW LINE", s.UserAddedCustomTriggers);
        Assert.DoesNotContain("Remove", s.UserAddedCustomTriggers);
        Assert.True(service.HasPendingSave);
    });

    [Fact]
    public Task CancelDoesNotChangeTriggersAndEmptyPoolsDoNotRepopulate() => Run(async (cell, owner, service) =>
    {
        service.Current.CustomTriggers.Clear();
        Click(cell.FindControl<Button>("BtnEditTriggersCompanion")!);
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.IsType<TextEditorDialog>(Assert.Single(owner.OwnedWindows));
        Assert.Empty(dialog.FindControl<ItemsControl>("ItemList")!.ItemsSource!);
        Click(dialog.FindControl<Button>("BtnCancel")!);
        await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.Empty(service.Current.CustomTriggers);
        Assert.False(service.HasPendingSave);
    });

    [Theory]
    [InlineData("en", "Trigger Phrases")]
    [InlineData("de", "Triggerphrasen")]
    [InlineData("es", "Frases de activación")]
    [InlineData("fr", "Phrases de déclenchement")]
    [InlineData("it", "Frasi di attivazione")]
    [InlineData("ja", "トリガーフレーズ")]
    [InlineData("ko", "트리거 문구")]
    [InlineData("pt-BR", "Frases de gatilho")]
    [InlineData("ru", "Триггерные фразы")]
    [InlineData("zh-CN", "触发短语")]
    public Task EditorUsesTheTranslatedTitle(string language, string expected) => Run((cell, owner, service) =>
    {
        var loc = LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage(language);
            Click(cell.FindControl<Button>("BtnEditTriggersCompanion")!);
            Dispatcher.UIThread.RunJobs();
            var dialog = Assert.IsType<TextEditorDialog>(Assert.Single(owner.OwnedWindows));
            Assert.Equal(loc.GetF("title_manager", expected), dialog.Title);
            Assert.Equal(expected, Loc.Get("title_trigger_phrases"));
            Click(dialog.FindControl<Button>("BtnCancel")!);
        }
        finally { loc.SetLanguage(previous); }
        return Task.CompletedTask;
    });

    [Fact]
    public async Task HelperRestoresThePreviousSettingsProvider()
    {
        var previous = CoreSettings.ServiceProvider;
        try
        {
            await Run((cell, owner, service) => Task.CompletedTask);
            Assert.Same(previous, CoreSettings.ServiceProvider);
        }
        finally { CoreSettings.ServiceProvider = previous; }
    }

    private static Task Run(Func<WorkshopTriggersCell, Window, SettingsService, Task> body) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.TriggerModeEnabled = true;
        service.Current.TriggerIntervalSeconds = 123;
        var cell = new WorkshopTriggersCell();
        var owner = new Window { Content = cell };
        owner.Show(); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        try { await body(cell, owner, service); }
        finally
        {
            foreach (var child in owner.OwnedWindows.ToArray()) child.Close();
            owner.Close(); service.SealForReset();
            CoreSettings.ServiceProvider = previous;
        }
    });

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
