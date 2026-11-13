using System;
using Avalonia.Controls;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ProgramsTabView
{
    // WPF MainWindow.ProgramsTab.cs:1694-1760: two panels followed by the rail.
    private void StartRunEntrance()
    {
        var header = Find<Border>("RunHeaderPanel");
        var today = Find<Border>("TodayPanel");
        var rail = (ScaleTransform)Find<Border>("RailProgressFill").RenderTransform!;
        var headerSlide = new TranslateTransform();
        var todaySlide = new TranslateTransform();
        header.RenderTransform = headerSlide;
        today.RenderTransform = todaySlide;

        void Apply(double t)
        {
            static double Out(double t, double duration)
            {
                var x = Math.Clamp(t / duration, 0, 1);
                return 1 - (1 - x) * (1 - x);
            }
            header.Opacity = Out(t, .3);
            headerSlide.Y = 14 * (1 - Out(t, .34));
            today.Opacity = Out(t - .09, .3);
            todaySlide.Y = 14 * (1 - Out(t - .09, .34));
            var x = Math.Clamp((t - .15) / .55, 0, 1);
            rail.ScaleX = 1 - Math.Pow(1 - x, 3);
        }

        Apply(0);
        Moment(header, .7, Apply, () => Apply(.7));
    }
}
