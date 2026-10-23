using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Helpers;

/// <summary>WPF LabTab.UpdateWebcamStatusChips: display-only chips agree with the Devices engine bar.</summary>
internal static class WebcamStatusPresentation
{
    internal static void Paint(Control owner, string dotName, string labelName, WebcamTrackingState state)
    {
        if (owner.FindControl<TextBlock>(labelName) is { } text) text.Text = Loc.Get(WebcamStatus.TextKey(state));
        var key = WebcamStatus.IsLive(state) ? "SuccessGreenBrush" : "TextMutedBrush";
        if (owner.FindControl<Ellipse>(dotName) is { } dot && owner.TryFindResource(key, out var value) && value is IBrush brush)
            dot.Fill = brush;
    }
}
