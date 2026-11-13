using Avalonia.Controls;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ColorEditorDialogFaceTests
{
    [Theory]
    [InlineData("Fredoka (bundled)", AppFonts.Fredoka)]
    [InlineData("DejaVu Sans", "DejaVu Sans, Arial")]
    [InlineData("", "Arial")]
    public void PreviewUsesSelectedFace(string selected, string expected) => AvaloniaTestDispatcher.Run(() =>
    {
        CoreSettings.Current.SubliminalFont = selected;
        var dialog = new ColorEditorDialog();
        Assert.Equal(new FontFamily(expected), dialog.FindControl<TextBlock>("PreviewText")!.FontFamily);
    });
}
