using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Dialogs
{
    public partial class UpdateProgressDialog : Window
    {
        public UpdateProgressDialog()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new UpdateProgressViewModel();
        }

        /// <summary>Drives the fill width, exactly as the WPF code-behind does.</summary>
        public void SetProgress(double fraction, double trackWidth)
        {
            var fill = this.FindControl<Border>("ProgressFill");
            var text = this.FindControl<TextBlock>("TxtProgress");
            // The caller passes the inner Grid width (track minus its 2 px padding each side). WPF
            // UpdateProgressDialog.xaml.cs:23-28 fills the track's ActualWidth - 6, so 2 px less than that,
            // and paints nothing while there is no room.
            if (fill is not null && trackWidth > 2)
                fill.Width = System.Math.Clamp(fraction, 0, 1) * (trackWidth - 2);
            if (text is not null) text.Text = $"{System.Math.Clamp(fraction, 0, 1) * 100:0}%";
        }
    }

    /// <summary>Strings from CCP.Core's Loc. See the porting notes in the repo-root CLAUDE.md
    /// for why {loc:Str} becomes a binding.</summary>
    public sealed class UpdateProgressViewModel
    {
        public string LocTitle => Loc.Get("dialog_downloading_update");
        public string LocHeading => Loc.Get("label_downloading_update");
    }
}
