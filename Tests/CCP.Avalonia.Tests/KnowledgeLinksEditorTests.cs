using System.Linq;
using System.Threading.Tasks;
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

public sealed class KnowledgeLinksEditorTests
{
    private static SettingsService Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.GlobalKnowledgeBaseLinks.Clear();
        return service;
    }

    private static void Click(Control control, string name) =>
        control.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // WPF KnowledgeLinksEditor.xaml.cs:36-60, LinksPage.xaml:25 and .xaml.cs:33.
    [Fact]
    public Task PageReloadsAndCommitsEachEdit() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var service = Setup();
        var page = new LinksPage();
        page.OnShown(null);
        var editor = page.FindControl<KnowledgeLinksEditor>("KnowledgeLinks")!;
        var empty = editor.FindControl<TextBlock>("TxtKnowledgeLinksEmpty")!;
        Assert.True(editor.SaveImmediately);
        Assert.True(empty.IsVisible);
        int changed = 0;
        editor.Changed += (_, _) => changed++;
        var link = new KnowledgeBaseLink { Title = "Guide", Url = "https://example.org/guide" };
        editor.PromptForLink = _ => Task.FromResult<KnowledgeBaseLink?>(link);
        Click(editor, "BtnAddKnowledgeLink");
        Assert.Same(link, Assert.Single(service.Current.GlobalKnowledgeBaseLinks));
        Assert.True(service.HasPendingSave);
        Assert.False(empty.IsVisible);
        Assert.Equal(1, changed);
        editor.FindControl<ListBox>("LstKnowledgeLinks")!.SelectedItem = link;
        Click(editor, "BtnRemoveKnowledgeLink");
        Assert.Empty(service.Current.GlobalKnowledgeBaseLinks);
        Assert.True(empty.IsVisible);
        Assert.Equal(2, changed);
        editor.PromptForLink = _ => Task.FromResult<KnowledgeBaseLink?>(null);
        Click(editor, "BtnAddKnowledgeLink");
        Assert.Empty(editor.Links);
        Assert.Equal(2, changed);
        service.Current.GlobalKnowledgeBaseLinks.Add(link);
        page.OnShown(null);
        page.OnShown(null);
        Assert.Same(link, Assert.Single(editor.Links));
        Assert.Equal(2, changed); // Load is not an edit.
        return Task.CompletedTask;
    });

    // WPF CompanionPromptEditorDialog.xaml:381 and .xaml.cs:122-134, 234, 392.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task DialogStagesUntilSaveOrDiscards(bool save) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var service = Setup();
        var dialog = new CompanionPromptEditorDialog();
        var editor = dialog.FindControl<KnowledgeLinksEditor>("KnowledgeLinks")!;
        Assert.False(editor.SaveImmediately);
        editor.PromptForLink = _ => Task.FromResult<KnowledgeBaseLink?>(new() { Title = "Guide", Url = "https://example.org" });
        Click(editor, "BtnAddKnowledgeLink");
        Assert.Empty(service.Current.GlobalKnowledgeBaseLinks);
        Assert.False(service.HasPendingSave);
        Assert.True((bool)typeof(CompanionPromptEditorDialog).GetField("_hasUnsavedChanges",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(dialog)!);
        dialog.Show();
        // Exercise the actual Save and Cancel buttons, including the discard confirmation.
        if (save) Click(dialog, "BtnSave");
        else
        {
            Click(dialog, "BtnCancel");
            Dispatcher.UIThread.RunJobs();
            var confirm = Assert.IsType<MessageDialog>(Assert.Single(dialog.OwnedWindows));
            confirm.Close(true);
            await Task.Yield();
            Dispatcher.UIThread.RunJobs();
        }
        Assert.False(dialog.IsVisible);
        Assert.Equal(save ? 1 : 0, service.Current.GlobalKnowledgeBaseLinks.Count);
    });

    // WPF KnowledgeLinksEditor.xaml.cs:74-88: no selection informs without mutating.
    [Fact]
    public Task RemoveWithoutSelectionShowsNotice() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var service = Setup();
        var editor = new KnowledgeLinksEditor { SaveImmediately = true };
        var owner = new Window { Content = editor };
        owner.Show();
        try
        {
            var pending = editor.RemoveAsync();
            Dispatcher.UIThread.RunJobs();
            var notice = Assert.IsType<MessageDialog>(Assert.Single(owner.OwnedWindows));
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("btn_remove_selected"), notice.Title);
            notice.Close();
            await pending;
            Assert.Empty(service.Current.GlobalKnowledgeBaseLinks);
            Assert.False(service.HasPendingSave);
        }
        finally { owner.Close(); }
    });
    [Fact]
    public Task PanicClosesTheLinkDialog() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var dialog = new KnowledgeLinkEditorDialog();
        dialog.Show();
        var stop = Assert.Single(ConditioningControlPanel.Avalonia.Views.Windows.PanicSurfaces.All,
            surface => surface.Id == "companion-knowledge-editor");
        stop.Stop(null);
        Assert.False(dialog.IsVisible);
        Assert.Null(dialog.Result);
        stop.Stop(null);
        return Task.CompletedTask;
    });

}
