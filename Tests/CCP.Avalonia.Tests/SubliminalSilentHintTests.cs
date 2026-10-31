using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;
namespace CCP.Avalonia.Tests;
[Collection(RunsAloneCollection.Name)]
public sealed class SubliminalSilentHintTests {
    internal static void EnsureAvalonia() {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }
    [Fact]
    public void Editor_marks_only_unrecorded_nonblank_lines_without_changing_enablement() => AvaloniaTestDispatcher.Run(() => {
        EnsureAvalonia();
        var editor = new TextEditorDialog("Messages", new Dictionary<string, bool> {
            ["voiced"] = true, ["silent"] = false, [" "] = true,
        });
        editor.MarkSilentLines(t => t == "voiced");
        var items = editor.FindControl<ItemsControl>("ItemList")!.Items.Cast<TextItem>().ToList(); Assert.True(items.Single(x => x.Text == "silent").IsSilent); Assert.False(items.Single(x => x.Text == "silent").IsEnabled);
        Assert.False(items.Single(x => x.Text == " ").IsSilent); Assert.True(editor.FindControl<TextBlock>("TxtSilentHint")!.IsVisible);
        editor.MarkSilentLines(_ => true);
        Assert.All(items, x => Assert.False(x.IsSilent)); Assert.False(editor.FindControl<TextBlock>("TxtSilentHint")!.IsVisible);
        editor.Close();
    });
    [Fact]
    public void Panel_hint_requires_whispers_and_an_enabled_unrecorded_phrase() => AvaloniaTestDispatcher.Run(() => {
        EnsureAvalonia();
        var s = CoreSettings.Current; var (enabled, pool, family) = (s.SubAudioEnabled, s.SubliminalPool, s.SubliminalFont);
        try {
            s.SubAudioEnabled = true;
            s.SubliminalPool = new() { ["unrecorded test phrase"] = false };
            var panel = new SubliminalFeatureControl(); var font = Assert.IsType<ComboBoxItem>(panel.FindControl<ComboBox>("CmbFont")!.Items[0]); Assert.Equal("Fredoka (bundled)", font.Tag); Assert.Equal("Fredoka", font.FontFamily.Name);
            s.SubliminalFont = "Fredoka (bundled)";
            var card = SubliminalOverlayWindow.Build("TEST", s); Assert.Equal(font.FontFamily, card.Children.OfType<TextBlock>().Last().FontFamily); var hint = panel.FindControl<TextBlock>("TxtWhispersSilentHint")!;
            Assert.False(hint.IsVisible);
            s.SubliminalPool["unrecorded test phrase"] = true;
            panel.RefreshSilentHint();
            Assert.True(hint.IsVisible);
            s.SubAudioEnabled = false;
            panel.RebindToCurrentSettings();
            Assert.False(hint.IsVisible);
        }
        finally { (s.SubAudioEnabled, s.SubliminalPool, s.SubliminalFont) = (enabled, pool, family); }
    });
}
