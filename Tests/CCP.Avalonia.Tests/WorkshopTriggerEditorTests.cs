using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class WorkshopTriggerEditorTests
{
    [Fact]
    public Task SilentMarksAndHintTrackAddedAndRemovedLines() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dialog = new TextEditorDialog("Test", new Dictionary<string, bool> { ["VOICE"] = true, ["QUIET"] = false });
        var items = (ObservableCollection<TextItem>)dialog.FindControl<ItemsControl>("ItemList")!.ItemsSource!;
        var hint = dialog.FindControl<TextBlock>("TxtSilentHint")!;
        Assert.False(hint.IsVisible);
        dialog.MarkSilentLines(text => text == "VOICE");
        Assert.False(items.Single(x => x.Text == "VOICE").IsSilent);
        Assert.True(items.Single(x => x.Text == "QUIET").IsSilent);
        Assert.True(hint.IsVisible);
        items.Remove(items.Single(x => x.Text == "QUIET"));
        Assert.False(hint.IsVisible);
        dialog.FindControl<TextBox>("TxtNewItem")!.Text = "new silent";
        dialog.FindControl<Button>("BtnAdd")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(items.Single(x => x.Text == "NEW SILENT").IsSilent);
        Assert.True(hint.IsVisible);
        items.Remove(items.Single(x => x.Text == "NEW SILENT"));
        Assert.False(hint.IsVisible);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ButtonCancelAndSavePreserveEnabledAndUserAddedPhrases() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previousProvider = CoreSettings.ServiceProvider;
        try
        {
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var settings = CoreSettings.Current;
            settings.CustomTriggers = new List<string> { "OLD", "old", "KEEP", "DROP" };
            settings.UserAddedCustomTriggers = new HashSet<string> { "OLD" };
            var cell = new WorkshopTriggersCell();
            var owner = new Window { Content = cell };
            try
            {
                owner.Show();
                void Click(Control parent, string name) => parent.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                void Pump() { for (int i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs(); }
                Click(cell, "BtnEditTriggersCompanion"); Pump();
                var dialog = Assert.Single(owner.OwnedWindows.OfType<TextEditorDialog>());
                Assert.Equal(Loc.GetF("title_manager", Loc.Get("title_trigger_phrases")), dialog.Title);
                var firstItems = dialog.FindControl<ItemsControl>("ItemList")!.ItemsSource!.Cast<TextItem>();
                Assert.NotNull(SubliminalWhisperShow.FindLinkedAudio("DROP"));
                foreach (var item in firstItems)
                    Assert.Equal(SubliminalWhisperShow.FindLinkedAudio(item.Text) == null, item.IsSilent);
                Click(dialog, "BtnCancel"); Pump();
                Assert.Equal(new[] { "OLD", "old", "KEEP", "DROP" }, settings.CustomTriggers);
                Assert.Contains("OLD", settings.UserAddedCustomTriggers);
                Click(cell, "BtnEditTriggersCompanion"); Pump();
                dialog = Assert.Single(owner.OwnedWindows.OfType<TextEditorDialog>());
                var items = dialog.FindControl<ItemsControl>("ItemList")!.ItemsSource!.Cast<TextItem>().ToList();
                Assert.Equal(3, items.Count);
                items.Single(x => x.Text == "OLD").IsEnabled = false;
                dialog.FindControl<TextBox>("TxtNewItem")!.Text = "unique typed phrase";
                Click(dialog, "BtnAdd");
                Click(dialog, "BtnSave"); Pump();
                Assert.Equal(new[] { "DROP", "KEEP", "UNIQUE TYPED PHRASE" }, settings.CustomTriggers);
                Assert.DoesNotContain("OLD", settings.UserAddedCustomTriggers);
                Assert.Contains("unique typed phrase", settings.UserAddedCustomTriggers);
            }
            finally { foreach (var w in owner.OwnedWindows.ToArray()) w.Close(); owner.Close(); }
        }
        finally { CoreSettings.ServiceProvider = previousProvider; }
        return Task.CompletedTask;
    });
}
