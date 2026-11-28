using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Xunit;

namespace CCP.Avalonia.Tests;

public class QuestVideoTooltipTests
{
    [Fact]
    public void CardAppliesAndClearsDescriptionTooltip() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var card = new DailyQuestCard();
        var description = card.FindControl<TextBlock>("TxtDesc")!;
        card.Apply(new DailyQuestCardModel { Description = "Video", DescriptionTooltip = "Details" });
        Assert.Equal("Details", ToolTip.GetTip(description));
        card.ShowEmpty(0, "Empty", "No quest");
        Assert.Null(ToolTip.GetTip(description));
    });
}
