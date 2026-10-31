using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;
namespace CCP.Avalonia.Tests;
[Collection(RunsAloneCollection.Name)]
public sealed class SubliminalFontPreviewTests {
    // WPF FontPickerHelper.cs:173-174,209-212,246-249: stored selection and fallback
    // both paint the closed picker in the selected face.
    [Theory]
    [InlineData("Fredoka (bundled)", "Fredoka (bundled)")]
    [InlineData("not an installed family 9137", null)]
    public void Closed_picker_previews_loaded_selection_and_missing_font_fallback(string stored, string? expected) =>
        AvaloniaTestDispatcher.Run(() => {
            SubliminalSilentHintTests.EnsureAvalonia();
            CoreSettings.Current.SubliminalFont = stored;
            var panel = new SubliminalFeatureControl(); var picker = panel.FindControl<ComboBox>("CmbFont")!; var selected = Assert.IsType<ComboBoxItem>(picker.SelectedItem);
            var fallback = picker.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, "Arial"))
                ?? (ComboBoxItem)picker.Items[0]!;
            Assert.Equal(expected ?? fallback.Tag, selected.Tag);
            Assert.Equal(stored, CoreSettings.Current.SubliminalFont); // load does not overwrite a missing face
            WithWindow(panel, () => AssertPreview(picker, selected));
        });
    // WPF SubliminalFeatureControl.xaml.cs:159-167 saves the selection, then the settings
    // reload at :64 reapplies the closed preview through FontPickerHelper.cs:174.
    [Fact]
    public void Picking_a_face_updates_closed_preview_and_next_card_setting_without_restyling_siblings() =>
        AvaloniaTestDispatcher.Run(() => {
            SubliminalSilentHintTests.EnsureAvalonia();
            var panel = new SubliminalFeatureControl(); var picker = panel.FindControl<ComboBox>("CmbFont")!; var sibling = panel.FindControl<Button>("BtnManageMessages")!;
            WithWindow(panel, () => {
                var siblingFace = sibling.FontFamily; var pickerFace = picker.FontFamily;
                foreach (var item in new[] { picker.Items.OfType<ComboBoxItem>().Last(), (ComboBoxItem)picker.Items[0]! }) {
                    picker.SelectedItem = item;
                    Assert.Equal(item.Tag, CoreSettings.Current.SubliminalFont);
                    AssertPreview(picker, item);
                    Assert.Equal(siblingFace, sibling.FontFamily); Assert.Equal(pickerFace, picker.FontFamily);
                }
            });
        });
    private static void WithWindow(Control panel, System.Action check) {
        var window = new Window { Content = panel, Width = 800, Height = 700 };
        try { window.Show(); window.UpdateLayout(); check(); }
        finally { window.Close(); }
    }
    private static void AssertPreview(ComboBox picker, ComboBoxItem selected) {
        picker.UpdateLayout();
        Assert.False(picker.IsDropDownOpen);
        var presenter = picker.GetVisualDescendants().OfType<ContentPresenter>()
            .Single(p => Equals(p.Content, selected.Content) && p.ContentTemplate == picker.SelectionBoxItemTemplate);
        var preview = Assert.IsType<TextBlock>(presenter.Child); Assert.Equal(selected.Content, preview.Text); Assert.Equal(selected.FontFamily, preview.FontFamily); Assert.Equal(11, preview.FontSize);
    }
}
