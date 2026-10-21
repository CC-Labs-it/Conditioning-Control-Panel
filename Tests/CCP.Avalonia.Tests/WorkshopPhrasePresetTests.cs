using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class WorkshopPhrasePresetTests
{
    [Fact]
    public Task SaveButtonOpensTheWpfNamePromptAndStoresAnIndependentSnapshot() => Run(async (drawer, parts, owner, s) =>
    {
        s.DisabledPhraseIds.Add("original");
        Click(CellButton(parts.Triggers, "Save"));
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(owner.OwnedWindows);
        Assert.Equal("Save Phrase Preset", dialog.Title);
        Assert.Equal(350, dialog.Width);
        Assert.Equal(150, dialog.Height);
        var input = dialog.GetVisualDescendants().OfType<TextBox>().Single();
        Assert.Equal("Preset 1", input.Text);
        input.Text = "  My phrases  ";
        Click(CellButton(dialog, "Save"));
        await Task.Yield(); Dispatcher.UIThread.RunJobs();
        var preset = Assert.Single(s.PhrasePresets);
        Assert.Equal("My phrases", preset.Name);
        Assert.Equal(preset.Id, s.CurrentPhrasePresetId);
        Assert.Contains("original", preset.DisabledPhraseIds);
        s.DisabledPhraseIds.Add("later");
        Assert.DoesNotContain("later", preset.DisabledPhraseIds);
    });

    [Fact]
    public Task RestoreOnlySelectsTheSavedId_UserSelectionAppliesTheCoreCopy() => Run((drawer, parts, owner, s) =>
    {
        var saved = new PhrasePreset { Name = "Saved", DisabledPhraseIds = new() { "saved" } };
        var other = new PhrasePreset { Name = "Other", DisabledPhraseIds = new() { "other" } };
        s.PhrasePresets.AddRange(new[] { saved, other });
        s.CurrentPhrasePresetId = saved.Id;
        s.DisabledPhraseIds.Add("live");
        // A real remount refreshes the already-built control from the settings the host restored.
        owner.Content = null; owner.Content = drawer; owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var combo = parts.Triggers.FindControl<ComboBox>("CmbPhrasePresets")!;
        Assert.Equal(saved, combo.SelectedItem);
        Assert.Contains("live", s.DisabledPhraseIds);
        Assert.DoesNotContain("saved", s.DisabledPhraseIds);
        combo.SelectedItem = other;
        Assert.Equal(other.Id, s.CurrentPhrasePresetId);
        Assert.Contains("other", s.DisabledPhraseIds);
        s.DisabledPhraseIds.Add("later");
        Assert.DoesNotContain("later", other.DisabledPhraseIds);
        return Task.CompletedTask;
    });

    [Fact]
    public Task DeleteRequiresConfirmationAndCancellationKeepsThePreset() => Run(async (drawer, parts, owner, s) =>
    {
        var preset = new PhrasePreset { Name = "Keep until confirmed" };
        s.PhrasePresets.Add(preset); s.CurrentPhrasePresetId = preset.Id;
        owner.Content = null; owner.Content = drawer; owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        Click(CellButton(parts.Triggers, "Delete")); Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(owner.OwnedWindows);
        Assert.Equal("Delete Preset", dialog.Title);
        Click(dialog.FindControl<Button>("BtnCancel")!);
        await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.Single(s.PhrasePresets);
        Click(CellButton(parts.Triggers, "Delete")); Dispatcher.UIThread.RunJobs();
        dialog = Assert.Single(owner.OwnedWindows);
        Click(dialog.FindControl<Button>("BtnOk")!);
        await Task.Yield(); Dispatcher.UIThread.RunJobs();
        Assert.Empty(s.PhrasePresets);
        Assert.Null(s.CurrentPhrasePresetId);
        Assert.Null(parts.Triggers.FindControl<ComboBox>("CmbPhrasePresets")!.SelectedItem);
    });

    [Fact]
    public Task ReturningToTheCompanionTabRereadsPresets() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.PhrasePresets.Clear(); s.CurrentPhrasePresetId = null;
        var shell = new global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        try
        {
            shell.Show(); shell.ShowTab("companion"); Dispatcher.UIThread.RunJobs();
            var drawer = shell.GetVisualDescendants().OfType<WorkshopAccordion>().Single();
            var combo = ((WorkshopRuntimeVm)drawer.DataContext!).Parts.Triggers.FindControl<ComboBox>("CmbPhrasePresets")!;
            shell.ShowTab("achievements"); Dispatcher.UIThread.RunJobs();
            var added = new PhrasePreset { Name = "Added elsewhere" };
            s.PhrasePresets.Add(added); s.CurrentPhrasePresetId = added.Id;
            shell.ShowTab("companion"); Dispatcher.UIThread.RunJobs();   // CompanionRoomView.ResumeClocks
            Assert.Contains(added, combo.Items.OfType<PhrasePreset>());
            Assert.Equal(added, combo.SelectedItem);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.Close(); Dispatcher.UIThread.RunJobs();
            service.SealForReset(); CoreSettings.ServiceProvider = previous;
        }
        return Task.CompletedTask;
    });

    private static Task Run(Func<WorkshopAccordion, WorkshopShelfParts, Window, AppSettings, Task> body) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var service = new SettingsService(); CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.PhrasePresets.Clear(); s.CurrentPhrasePresetId = null; s.DisabledPhraseIds.Clear();
        var drawer = new WorkshopAccordion();
        var vm = Assert.IsType<WorkshopRuntimeVm>(drawer.DataContext); vm.IsExpanded = true;
        var owner = new Window { Content = drawer, Width = 1160, Height = 700 };
        owner.Show(); owner.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        try { await body(drawer, vm.Parts, owner, s); }
        finally
        {
            foreach (var child in owner.OwnedWindows.ToArray()) child.Close();
            owner.Close(); service.SealForReset(); CoreSettings.ServiceProvider = previous;
        }
    });

    private static Button CellButton(Control cell, string label) => cell.GetVisualDescendants().OfType<Button>()
        .Single(b => b.Content is TextBlock t && t.Text == label || b.Content is string text && text == label);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
