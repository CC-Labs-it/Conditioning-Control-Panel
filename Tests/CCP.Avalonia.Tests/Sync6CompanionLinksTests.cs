using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Views.Deeper;
using ConditioningControlPanel.Localization;
using Avalonia;
using Avalonia.Controls;
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
        var page = new LinksPage(); page.Resources["TextSecondaryBrush"] = Brushes.Gray;
        page.OnShown(null);
        Assert.Equal(Color.Parse("#FF69B4"), ((ISolidColorBrush)page.FindControl<TextBlock>("TxtKnownTitle")!.Foreground!).Color);
        var note = page.FindControl<TextBlock>("TxtKnownNote")!;
        Assert.Equal(12, note.FontSize); Assert.Equal(new Thickness(0,2,0,10), note.Margin);
        Assert.Equal(page.FindResource("TextSecondaryBrush"), note.Foreground); page.Resources["TextSecondaryBrush"] = Brushes.Teal; Assert.Equal(Brushes.Teal, note.Foreground); Assert.Equal(0, ((StackPanel)note.Parent!).Spacing);
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
    [Theory]
    [InlineData("save")][InlineData("cancel")][InlineData("closeSave")][InlineData("closeDiscard")][InlineData("closeCancel")]
    public Task DialogButtonsPersistOnlyWhenAccepted(string action) => RunAsync(async () =>
    {
        var link = new KnowledgeBaseLink { Title = "Example", Url = "https://example.org" };
        CoreSettings.Current.GlobalKnowledgeBaseLinks.Add(link);
        var owner = new Window(); owner.Show();
        var dialog = new CompanionPromptEditorDialog(); _ = dialog.ShowDialogSafe(owner);
        try {
            var editor = dialog.FindControl<KnowledgeLinksEditor>("KnowledgeLinks")!;
            editor.FindControl<ListBox>("LstKnowledgeLinks")!.SelectedItem = link;
            Click(editor.FindControl<Button>("BtnRemoveKnowledgeLink")!);
            Assert.Empty(editor.Links); Assert.Same(link, Assert.Single(CoreSettings.Current.GlobalKnowledgeBaseLinks));
            if (action == "save") Click(dialog.FindControl<Button>("BtnSave")!);
            else if (action == "cancel") {
                Click(dialog.FindControl<Button>("BtnCancel")!);
                await Until(() => dialog.OwnedWindows.OfType<MessageDialog>().Any());
                Click(dialog.OwnedWindows.OfType<MessageDialog>().Single().FindControl<Button>("BtnOk")!);
            } else {
                dialog.Close(); await Until(() => dialog.OwnedWindows.OfType<UnsavedChangesDialog>().Any());
                Answer(dialog, action == "closeSave" ? "btn_save" : action == "closeDiscard" ? "btn_discard" : "btn_cancel");
                if (action == "closeCancel") {
                    Assert.True(dialog.IsVisible); Assert.Single(CoreSettings.Current.GlobalKnowledgeBaseLinks);
                    dialog.Close(); await Until(() => dialog.OwnedWindows.OfType<UnsavedChangesDialog>().Any()); Answer(dialog, "btn_discard");
                }
            }
            await Until(() => !dialog.IsVisible);
            if (action is "save" or "closeSave") Assert.Empty(CoreSettings.Current.GlobalKnowledgeBaseLinks);
            else Assert.Same(link, Assert.Single(CoreSettings.Current.GlobalKnowledgeBaseLinks));
        } finally { foreach (var w in dialog.OwnedWindows.ToArray()) w.Close(); owner.Close(); }
    });
    [Fact]
    public void EmptyPopulatedAndLongLinksKeepTheWpfLayout() => Run(() =>
    {
        var editor = new KnowledgeLinksEditor(); var list = editor.FindControl<ListBox>("LstKnowledgeLinks")!;
        var empty = editor.FindControl<TextBlock>("TxtKnowledgeLinksEmpty")!;
        var owner = new Window { Content = editor, Width = 400, Height = 800 }; owner.Show();
        try {
        Assert.Same(list.Parent, empty.Parent); Assert.Equal(global::Avalonia.Layout.VerticalAlignment.Center, empty.VerticalAlignment);
        Assert.False(empty.IsHitTestVisible); Assert.True(double.IsNaN(list.Height)); Assert.Equal(120, list.MinHeight); Assert.Equal(320, list.MaxHeight);
        list.Measure(new Size(280, double.PositiveInfinity)); Assert.Equal(120, list.DesiredSize.Height);
        var link = new KnowledgeBaseLink { Title = new string('T', 500), Url = "https://example.org/" + new string('u', 500) };
        editor.Links.Add(link); Assert.False(empty.IsVisible);
        var row = (Control)list.ItemTemplate!.Build(link)!; row.DataContext = link;
        var texts = row.GetVisualDescendants().OfType<TextBlock>().ToArray();
        Assert.Equal(TextTrimming.CharacterEllipsis, texts[0].TextTrimming); Assert.Equal(TextTrimming.CharacterEllipsis, texts[1].TextTrimming);
        Assert.False(texts[2].IsVisible); link.Description = "Description"; Assert.True(texts[2].IsVisible);
        row.Measure(new Size(280, double.PositiveInfinity)); Assert.True(row.DesiredSize.Width <= 280);
        for (var i = 0; i < 20; i++) editor.Links.Add(new KnowledgeBaseLink { Title = "Link", Url = "https://example.org" });
        Dispatcher.UIThread.RunJobs(); owner.UpdateLayout(); Assert.InRange(list.Bounds.Height, 121, 320);
        editor.Links.Clear(); Assert.True(empty.IsVisible);
        } finally { owner.Close(); }
    });
    [Fact]
    public Task PickerOpensStudioAndRefreshesAfterSaveOrCancel() => RunAsync(async () =>
    {
        var page = new PersonalityPage(); page.OnShown(null); var picker = (CompanionPickerCard)page.FindControl<ContentControl>("PickerHost")!.Content!;
        var owner = new Window { Content = page }; owner.Show();
        try {
            var more = picker.FindControl<Button>("BtnMorePersonality")!; Assert.True(more.IsVisible); Click(more);
            await Until(() => owner.OwnedWindows.OfType<PersonalityStudio>().Any());
            var studio = owner.OwnedWindows.OfType<PersonalityStudio>().Single();
            var source = CoreSettings.Current.ActivePersonalityPresetId; var count = CoreSettings.Current.UserPersonalityPresets.Count;
            ((Panel)((ScrollViewer)((Grid)studio.Content!).Children[1]).Content!).Children.OfType<TextBox>().First(b => b.Name == "Name").Text = "Review preset";
            var save = studio.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "Save"); Click(save);
            await Until(() => !studio.IsVisible);
            Assert.Equal(count + 1, CoreSettings.Current.UserPersonalityPresets.Count); Assert.NotEqual(source, CoreSettings.Current.ActivePersonalityPresetId);
            var combo = picker.FindControl<ComboBox>("CmbPersonality")!; await Until(() => Equals(CoreSettings.Current.ActivePersonalityPresetId, (combo.SelectedItem as ComboBoxItem)?.Tag)); Assert.Equal(CoreSettings.Current.ActivePersonalityPresetId, ((ComboBoxItem)combo.SelectedItem!).Tag);
            Click(more); await Until(() => owner.OwnedWindows.OfType<PersonalityStudio>().Any());
            studio = owner.OwnedWindows.OfType<PersonalityStudio>().Single();
            Click(studio.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "Cancel")); await Until(() => !studio.IsVisible);
            Assert.Equal(count + 1, CoreSettings.Current.UserPersonalityPresets.Count);
            Click(more); await Until(() => owner.OwnedWindows.OfType<PersonalityStudio>().Any()); studio = owner.OwnedWindows.OfType<PersonalityStudio>().Single();
            Click(studio.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "Save")); await Until(() => !studio.IsVisible);
            Assert.Equal(count + 1, CoreSettings.Current.UserPersonalityPresets.Count);
            Click(more); await Until(() => owner.OwnedWindows.OfType<PersonalityStudio>().Any()); studio = owner.OwnedWindows.OfType<PersonalityStudio>().Single();
            Click(studio.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "Delete")); await Until(() => studio.OwnedWindows.OfType<MessageDialog>().Any());
            Click(studio.OwnedWindows.OfType<MessageDialog>().Single().FindControl<Button>("BtnOk")!); await Until(() => !studio.IsVisible);
            Assert.Equal(count, CoreSettings.Current.UserPersonalityPresets.Count);
        } finally { foreach (var w in owner.OwnedWindows.ToArray()) w.Close(); owner.Close(); }
    });
    [Fact]
    public void PersonalityPageReusesItsLivePickerOnReturn() => Run(() =>
    {
        var page = new PersonalityPage();
        page.OnShown(null);
        var host = page.FindControl<ContentControl>("PickerHost")!;
        var card = Assert.IsType<Border>(host.Parent);
        Assert.Equal(new Thickness(16), card.Padding); Assert.Equal(new CornerRadius(10), card.CornerRadius);
        Assert.Equal(new Thickness(0,0,0,16), card.Margin); Assert.Equal(Color.Parse("#14FFFFFF"), ((ISolidColorBrush)card.Background!).Color);
        var picker = Assert.IsType<CompanionPickerCard>(host.Content);
        Assert.True(picker.IsVisible);
        Assert.NotEmpty(picker.FindControl<ComboBox>("CmbPersonality")!.Items.Cast<object>());
        page.OnShown(null);
        Assert.Same(picker, host.Content);
    });
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Answer(Window owner, string key) { Click(owner.OwnedWindows.OfType<UnsavedChangesDialog>().Single().GetLogicalDescendants().OfType<Button>().Single(b => (b.Content as TextBlock)?.Text == Loc.Get(key))); Dispatcher.UIThread.RunJobs(); }
    private static async Task Until(Func<bool> done) {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < end) { await Task.Yield(); Dispatcher.UIThread.RunJobs(); }
        Assert.True(done());
    }
    private static void Run(Action body) => RunAsync(() => { body(); return Task.CompletedTask; }).GetAwaiter().GetResult();
    private static Task RunAsync(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        CoreSettings.Current.GlobalKnowledgeBaseLinks.Clear(); await body(); Dispatcher.UIThread.RunJobs();
    });
}
