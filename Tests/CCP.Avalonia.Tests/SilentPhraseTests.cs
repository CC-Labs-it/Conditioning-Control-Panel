using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;

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
