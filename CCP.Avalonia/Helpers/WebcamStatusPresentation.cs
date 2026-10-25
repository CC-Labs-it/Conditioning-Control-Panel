using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Helpers;

/// <summary>WPF LabTab.UpdateWebcamStatusChips: display-only chips agree with the Devices engine bar.</summary>
internal static class WebcamStatusPresentation
{
    internal static void Paint(Control owner, string dotName, string labelName, WebcamTrackingState state)
    {
        // Bind the key, never set .Text: a {loc:Str} on the label would put its old key back on a
        // language change, and a set text would freeze in the old language.
        if (owner.FindControl<TextBlock>(labelName) is { } text)
            text.Bind(TextBlock.TextProperty,
                (global::Avalonia.Data.Binding)new global::ConditioningControlPanel.Avalonia.Localization.StrExtension(WebcamStatus.TextKey(state)).ProvideValue(null!));
        var key = WebcamStatus.IsLive(state) ? "SuccessGreenBrush" : "TextMutedBrush";
        if (owner.FindControl<Ellipse>(dotName) is { } dot && owner.TryFindResource(key, out var value) && value is IBrush brush)
            dot.Fill = brush;
    }
}
