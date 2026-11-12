using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionDropTests
{
    private static PossessionTarget Target(Control control, PossessionRole role = PossessionRole.Title) =>
        new() { Visual = control, Role = role, Key = "title" };

    [Fact]
    public void RefusesBoundTextAndTimer() => AvaloniaTestDispatcher.Run(() =>
    {
        var text = new TextBlock { Text = "title" };
        Assert.Same(text, DropEffect.FindTarget(Target(text)));
        Assert.Null(DropEffect.FindTarget(Target(text, PossessionRole.Timer)));
        text.Bind(TextBlock.TextProperty, new Binding("Name") { Source = new { Name = "title" } });
        Assert.Null(DropEffect.FindTarget(Target(text)));
    });

    [Fact]
    public void RefusesTextThatCannotBeSafelyRewritten() => AvaloniaTestDispatcher.Run(() =>
    {
        var text = new TextBlock();
        foreach (var value in new[] { "", "abc", "a   ", "abc\ndef", "abc\rdef" })
        {
            text.Text = value;
            Assert.Null(DropEffect.FindTarget(Target(text)));
        }
        text.Text = "four";
        Assert.Same(text, DropEffect.FindTarget(Target(text, PossessionRole.Label)));
        text.Inlines = new InlineCollection { new Run("four"), new Run("more") };
        Assert.Null(DropEffect.FindTarget(Target(text)));
    });
}
