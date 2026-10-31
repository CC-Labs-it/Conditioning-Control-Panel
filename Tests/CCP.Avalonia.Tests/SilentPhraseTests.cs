using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public class SilentPhraseTests
{
    private static void Setup()
    {
        if (Application.Current != null) return;
        AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>()
            .WithInterFont().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }

    [Fact]
    public Task Editor_marks_silent_lines_and_refreshes_for_a_new_voice_lookup() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var dialog = new TextEditorDialog("phrases", new Dictionary<string, bool>
        {
            ["spoken"] = true, ["silent"] = false, [" "] = true
        });
        var items = dialog.FindControl<ItemsControl>("ItemList")!.ItemsSource!.Cast<TextItem>().ToArray();
        Assert.All(items, x => Assert.False(x.IsSilent)); // non-spoken lists stay unmarked
        dialog.MarkSilentLines(x => x == "spoken");
        Assert.True(items.Single(x => x.Text == "silent").IsSilent);
        Assert.False(items.Single(x => x.Text == "spoken").IsSilent);
        Assert.False(items.Single(x => x.Text == " ").IsSilent);
        Assert.True(dialog.FindControl<TextBlock>("TxtSilentHint")!.IsVisible);
        dialog.FindControl<TextBox>("TxtNewItem")!.Text = "new silent phrase";
        typeof(TextEditorDialog).GetMethod("AddNewItem", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(dialog, null);
        var added = dialog.FindControl<ItemsControl>("ItemList")!.ItemsSource!.Cast<TextItem>().Single(x => x.Text == "NEW SILENT PHRASE");
        Assert.True(added.IsSilent);
        dialog.MarkSilentLines(_ => true);
        Assert.All(dialog.FindControl<ItemsControl>("ItemList")!.ItemsSource!.Cast<TextItem>(), x => Assert.False(x.IsSilent));
        Assert.False(dialog.FindControl<TextBlock>("TxtSilentHint")!.IsVisible);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(400)]
    [InlineData(550)]
    public Task Long_phrase_leaves_room_for_the_silent_mark_and_remove(int width) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var phrase = string.Join(" ", Enumerable.Repeat("a long phrase", 40));
        var dialog = new TextEditorDialog("phrases", new Dictionary<string, bool> { [phrase] = true }) { Width = width };
        dialog.MarkSilentLines(_ => false);
        try
        {
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            var list = dialog.FindControl<ItemsControl>("ItemList")!;
            var row = list.GetVisualDescendants().OfType<Border>().Single(x => x.Name == "ItemBorder");
            var text = row.GetVisualDescendants().OfType<TextBlock>().Single(x => x.Text == phrase);
            var remove = row.GetVisualDescendants().OfType<IconGlyph>().Single(x => x.Name == "BtnRemove");
            var mark = row.GetVisualDescendants().OfType<IconGlyph>().Single(x => x.Kind == IconKind.SpeakerMute);
            Assert.True(text.Bounds.Width > 0);
            Assert.True(mark.IsVisible);
            var textRight = text.TranslatePoint(new Point(text.Bounds.Width, 0), row)!.Value.X;
            var markLeft = mark.TranslatePoint(default, row)!.Value.X;
            var markRight = mark.TranslatePoint(new Point(mark.Bounds.Width, 0), row)!.Value.X;
            var removeLeft = remove.TranslatePoint(default, row)!.Value.X;
            Assert.True(textRight <= removeLeft, $"phrase ends at {textRight}, Remove starts at {removeLeft}");
            Assert.True(textRight <= markLeft, $"phrase ends at {textRight}, mark starts at {markLeft}");
            Assert.True(markRight <= removeLeft, $"mark ends at {markRight}, Remove starts at {removeLeft}");
            Assert.True(removeLeft + remove.Bounds.Width <= row.Bounds.Width);
        }
        finally { dialog.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Silent_hint_sits_above_the_list_with_Wpf_spacing() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var dialog = new TextEditorDialog("phrases", new Dictionary<string, bool> { ["silent"] = true });
        dialog.MarkSilentLines(_ => false);
        try
        {
            dialog.Show();
            Dispatcher.UIThread.RunJobs();
            var hint = dialog.FindControl<TextBlock>("TxtSilentHint")!;
            var list = dialog.FindControl<ItemsControl>("ItemList")!;
            var hintBottom = hint.TranslatePoint(new Point(0, hint.Bounds.Height), dialog)!.Value.Y;
            var listTop = list.TranslatePoint(default, dialog)!.Value.Y;
            Assert.True(hintBottom <= listTop, $"hint ends at {hintBottom}, list starts at {listTop}");
            Assert.Equal(11, hint.FontSize);
            Assert.Equal(new Thickness(2, 0, 0, 8), hint.Margin);
        }
        finally { dialog.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Whispers_hint_requires_audio_and_an_enabled_silent_phrase() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        s.SubliminalPool = new Dictionary<string, bool> { ["no-recording-s7b-unique"] = false };
        s.SubAudioEnabled = true;
        var control = new SubliminalFeatureControl();
        var hint = control.FindControl<TextBlock>("TxtWhispersSilentHint")!;
        Assert.False(hint.IsVisible);
        var window = new Window { Content = control };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        s.SubliminalPool["no-recording-s7b-unique"] = true;
        CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false));
        Dispatcher.UIThread.RunJobs();
        Assert.True(hint.IsVisible);
        s.SubAudioEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(hint.IsVisible);
        window.Close();
        return Task.CompletedTask;
    });
}
