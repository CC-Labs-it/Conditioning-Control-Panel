using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class Sync6CompanionLinksTests
{
    [Fact]
    public void PageSavesRemovalImmediatelyAndReloadsOnEveryVisit() => Run(() =>
    {
        var link = new KnowledgeBaseLink { Title = "Example", Url = "https://example.org" };
        CoreSettings.Current.GlobalKnowledgeBaseLinks.Add(link);
        var page = new LinksPage();
        page.OnShown(null);
        var editor = page.FindControl<KnowledgeLinksEditor>("KnowledgeLinks")!;
        Assert.True(editor.SaveImmediately);
        Assert.Same(link, Assert.Single(editor.Links));
        var list = editor.FindControl<ListBox>("LstKnowledgeLinks")!;
        list.SelectedItem = link;
        editor.FindControl<Button>("BtnRemoveKnowledgeLink")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(CoreSettings.Current.GlobalKnowledgeBaseLinks);
        Assert.True(editor.FindControl<TextBlock>("TxtKnowledgeLinksEmpty")!.IsVisible);
        CoreSettings.Current.GlobalKnowledgeBaseLinks.Add(link);
        page.OnShown(null);
        Assert.Same(link, Assert.Single(editor.Links));
        Assert.False(editor.FindControl<TextBlock>("TxtKnowledgeLinksEmpty")!.IsVisible);
    });

    [Fact]
    public void DialogUsesTheSameEditorButDefersChangesUntilSave() => Run(() =>
    {
        var link = new KnowledgeBaseLink { Title = "Example", Url = "https://example.org" };
        CoreSettings.Current.GlobalKnowledgeBaseLinks.Add(link);
        var dialog = new CompanionPromptEditorDialog();
        var editor = dialog.FindControl<KnowledgeLinksEditor>("KnowledgeLinks")!;
        Assert.False(editor.SaveImmediately);
        Assert.Same(link, Assert.Single(editor.Links));
        editor.FindControl<ListBox>("LstKnowledgeLinks")!.SelectedItem = link;
        editor.FindControl<Button>("BtnRemoveKnowledgeLink")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(editor.Links);
        Assert.Same(link, Assert.Single(CoreSettings.Current.GlobalKnowledgeBaseLinks));
        editor.SaveToSettings();
        Assert.Empty(CoreSettings.Current.GlobalKnowledgeBaseLinks);
    });

    [Fact]
    public void PersonalityPageReusesItsLivePickerOnReturn() => Run(() =>
    {
        var page = new PersonalityPage();
        page.OnShown(null);
        var host = page.FindControl<ContentControl>("PickerHost")!;
        var picker = Assert.IsType<CompanionPickerCard>(host.Content);
        Assert.True(picker.IsVisible);
        Assert.NotEmpty(picker.FindControl<ComboBox>("CmbPersonality")!.Items.Cast<object>());
        page.OnShown(null);
        Assert.Same(picker, host.Content);
    });

    private static void Run(Action body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.GlobalKnowledgeBaseLinks.Clear();
        body();
        Dispatcher.UIThread.RunJobs();
    });
}
