using System.Collections.Generic;
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
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class WorkshopTriggerEditorTests
{
    [Fact]
    public Task ButtonCancelAndSavePreserveEnabledAndUserAddedPhrases() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var settings = CoreSettings.Current;
        settings.CustomTriggers = new List<string> { "OLD", "old", "KEEP" };
        settings.UserAddedCustomTriggers = new HashSet<string> { "OLD" };
        var cell = new WorkshopTriggersCell();
        var owner = new Window { Content = cell };
        owner.Show();
        try
        {
            void Click(Control parent, string name) => parent.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            void Pump() { for (int i = 0; i < 5; i++) Dispatcher.UIThread.RunJobs(); }
            Click(cell, "BtnEditTriggersCompanion"); Pump();
            var dialog = Assert.Single(owner.OwnedWindows.OfType<TextEditorDialog>());
            Click(dialog, "BtnCancel"); Pump();
            Assert.Equal(new[] { "OLD", "old", "KEEP" }, settings.CustomTriggers);
            Assert.Contains("OLD", settings.UserAddedCustomTriggers);
            Click(cell, "BtnEditTriggersCompanion"); Pump();
            dialog = Assert.Single(owner.OwnedWindows.OfType<TextEditorDialog>());
            var items = dialog.FindControl<ItemsControl>("ItemList")!.ItemsSource!.Cast<TextItem>().ToList();
            Assert.Equal(2, items.Count);
            items.Single(x => x.Text == "OLD").IsEnabled = false;
            dialog.FindControl<TextBox>("TxtNewItem")!.Text = "unique typed phrase";
            Click(dialog, "BtnAdd");
            Click(dialog, "BtnSave"); Pump();
            Assert.Equal(new[] { "KEEP", "UNIQUE TYPED PHRASE" }, settings.CustomTriggers);
            Assert.DoesNotContain("OLD", settings.UserAddedCustomTriggers);
            Assert.Contains("unique typed phrase", settings.UserAddedCustomTriggers);
        }
        finally { foreach (var w in owner.OwnedWindows.ToArray()) w.Close(); owner.Close(); }
        return Task.CompletedTask;
    });
}
