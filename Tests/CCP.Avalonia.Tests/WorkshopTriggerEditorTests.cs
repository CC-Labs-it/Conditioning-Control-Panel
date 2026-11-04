using Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Skia;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public class WorkshopTriggerEditorTests
{
    [Fact]
    public void EditorFiltersEmptyAndDuplicateInputs()
    {
        var data = WorkshopAccordion.TriggerEditorData(new[] { "", " ", "Obey", "OBEY", "Kneel" });
        Assert.Equal(2, data.Count);
        Assert.True(data["Obey"]);
        Assert.Empty(WorkshopAccordion.TriggerEditorData(null));
    }

    [Fact]
    public void SaveKeepsEnabledAndTracksOnlyNewNonDefaults()
    {
        var settings = new AppSettings();
        settings.UserAddedCustomTriggers.Add("removed");
        var old = WorkshopAccordion.TriggerEditorData(new[] { "removed", "kept" });
        var result = new Dictionary<string, bool> { ["kept"] = true, ["removed"] = false,
            ["default"] = true, ["new"] = true, ["disabled"] = false };
        WorkshopAccordion.ApplyTriggerEditorData(settings, old, result, new[] { "DEFAULT" });
        Assert.Equal(new[] { "kept", "default", "new" }, settings.CustomTriggers);
        Assert.Equal(new[] { "new" }, settings.UserAddedCustomTriggers);
    }
}

public class WorkshopTriggerEditorFlowTests
{
    private static void Click(global::Avalonia.Controls.Button button) =>
        button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));

    [Fact]
    public System.Threading.Tasks.Task WorkshopSaveCancelAndPersistence() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (global::Avalonia.Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var service = new ConditioningControlPanel.Services.SettingsService();
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.CustomTriggers = new() { "", "keep", "KEEP", "remove" };
        s.UserAddedCustomTriggers.Clear(); s.UserAddedCustomTriggers.Add("remove");
        var localization = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var previousLanguage = localization.CurrentLanguage;
        localization.SetLanguage("fr");
        var drawer = new WorkshopAccordion();
        var vm = Assert.IsType<ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime.WorkshopRuntimeVm>(drawer.DataContext);
        vm.IsExpanded = true;
        var owner = new global::Avalonia.Controls.Window { Content = drawer, Width = 1000, Height = 700 };
        owner.Show(); owner.UpdateLayout(); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        try
        {
            var button = vm.Parts.Triggers.FindControl<global::Avalonia.Controls.Button>("BtnEditTriggersCompanion")!;
            Click(button); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var dialog = Assert.IsType<ConditioningControlPanel.Avalonia.Views.Dialogs.TextEditorDialog>(Assert.Single(owner.OwnedWindows));
            Assert.Contains("Phrases de déclenchement", dialog.Title);
            var list = dialog.FindControl<global::Avalonia.Controls.ItemsControl>("ItemList")!;
            var items = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.TextItem>(list.Items));
            Assert.Equal(2, items.Count);
            Assert.All(items, item => Assert.Equal(!ConditioningControlPanel.Avalonia.Views.Overlays.SubliminalWhisperShow.HasRecordedVoice(item.Text), item.IsSilent));
            System.Linq.Enumerable.Single(items, x => x.Text == "remove").IsEnabled = false;
            dialog.FindControl<global::Avalonia.Controls.TextBox>("TxtNewItem")!.Text = "new phrase";
            Click(dialog.FindControl<global::Avalonia.Controls.Button>("BtnAdd")!);
            Click(dialog.FindControl<global::Avalonia.Controls.Button>("BtnSave")!);
            await System.Threading.Tasks.Task.Yield(); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "keep", "NEW PHRASE" }, s.CustomTriggers);
            Assert.Contains("NEW PHRASE", s.UserAddedCustomTriggers);
            Assert.DoesNotContain("remove", s.UserAddedCustomTriggers);
            Assert.True(service.HasPendingSave);
            service.SaveImmediate();
            var reloaded = new ConditioningControlPanel.Services.SettingsService();
            Assert.Equal(s.CustomTriggers, reloaded.Current.CustomTriggers);
            Assert.Contains("NEW PHRASE", reloaded.Current.UserAddedCustomTriggers);
            reloaded.SealForReset();

            Click(button); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            dialog = Assert.IsType<ConditioningControlPanel.Avalonia.Views.Dialogs.TextEditorDialog>(Assert.Single(owner.OwnedWindows));
            dialog.FindControl<global::Avalonia.Controls.TextBox>("TxtNewItem")!.Text = "discard";
            Click(dialog.FindControl<global::Avalonia.Controls.Button>("BtnAdd")!);
            Click(dialog.FindControl<global::Avalonia.Controls.Button>("BtnCancel")!);
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var prompt = Assert.Single(dialog.OwnedWindows);
            var yes = System.Linq.Enumerable.Single(System.Linq.Enumerable.OfType<global::Avalonia.Controls.Button>(global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(prompt)),
                b => b.Content is global::Avalonia.Controls.TextBlock text && text.Text == "Yes");
            Click(yes); await System.Threading.Tasks.Task.Yield(); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("DISCARD", s.CustomTriggers);
        }
        finally
        {
            foreach (var child in System.Linq.Enumerable.ToArray(owner.OwnedWindows)) child.Close();
            owner.Close(); localization.SetLanguage(previousLanguage); service.SealForReset(); ConditioningControlPanel.CoreSettings.ServiceProvider = previous;
        }
    });

    [Fact]
    public System.Threading.Tasks.Task SilentMarksAndHintFollowEdits() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (global::Avalonia.Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var owner = new global::Avalonia.Controls.Window(); owner.Show();
        var dialog = new ConditioningControlPanel.Avalonia.Views.Dialogs.TextEditorDialog("Test", new() { ["VOICED"] = true, ["SILENT"] = true });
        dialog.MarkSilentLines(t => t == "VOICED");
        var task = dialog.ShowDialog<bool>(owner); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var list = dialog.FindControl<global::Avalonia.Controls.ItemsControl>("ItemList")!;
        var items = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.TextItem>(list.Items));
        Assert.True(System.Linq.Enumerable.Single(items, x => x.Text == "SILENT").IsSilent);
        Assert.False(System.Linq.Enumerable.Single(items, x => x.Text == "VOICED").IsSilent);
        Assert.True(dialog.FindControl<global::Avalonia.Controls.TextBlock>("TxtSilentHint")!.IsVisible);
        System.Linq.Enumerable.Single(items, x => x.Text == "SILENT").IsSelected = true;
        Click(dialog.FindControl<global::Avalonia.Controls.Button>("BtnRemoveSelected")!);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var prompt = Assert.Single(dialog.OwnedWindows);
        var yes = System.Linq.Enumerable.Single(System.Linq.Enumerable.OfType<global::Avalonia.Controls.Button>(global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(prompt)),
            b => b.Content is global::Avalonia.Controls.TextBlock text && text.Text == "Yes");
        Click(yes); await System.Threading.Tasks.Task.Yield(); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(dialog.FindControl<global::Avalonia.Controls.TextBlock>("TxtSilentHint")!.IsVisible);
        dialog.FindControl<global::Avalonia.Controls.TextBox>("TxtNewItem")!.Text = "new silent";
        Click(dialog.FindControl<global::Avalonia.Controls.Button>("BtnAdd")!);
        Assert.True(dialog.FindControl<global::Avalonia.Controls.TextBlock>("TxtSilentHint")!.IsVisible);
        Assert.True(System.Linq.Enumerable.Single(System.Linq.Enumerable.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.TextItem>(list.Items), x => x.Text == "NEW SILENT").IsSilent);
        Click(dialog.FindControl<global::Avalonia.Controls.Button>("BtnSave")!); await task;
        owner.Close();
    });
}
