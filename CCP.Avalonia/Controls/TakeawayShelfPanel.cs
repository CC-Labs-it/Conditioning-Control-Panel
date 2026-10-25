using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>The WPF shelf's intended narrow-window rule: clip pinned receipts while keeping
    /// the tray toggle and shop reachable. A normal StackPanel clips those trailing doors too.</summary>
    public sealed class TakeawayShelfPanel : StackPanel
    {
        private static bool IsDoor(Control child) => child.Classes.Contains("takeaway-affordance");

        protected override Size MeasureOverride(Size availableSize)
        {
            double width = 0, height = 0;
            foreach (var child in Children.Where(c => c.IsVisible))
            {
                child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                width += child.DesiredSize.Width;
                height = Math.Max(height, child.DesiredSize.Height);
            }
            return new Size(Math.Min(width, availableSize.Width), height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var children = Children.Where(c => c.IsVisible).ToArray();
            double doors = Math.Min(finalSize.Width, children.Where(IsDoor).Sum(c => c.DesiredSize.Width));
            double receiptRoom = Math.Max(0, finalSize.Width - doors), x = 0;
            foreach (var child in children.Where(c => !IsDoor(c)))
            {
                double width = Math.Min(child.DesiredSize.Width, Math.Max(0, receiptRoom - x));
                child.ClipToBounds = true;
                child.Arrange(new Rect(x, 0, width, finalSize.Height));
                x += width;
            }
            x = receiptRoom;
            foreach (var child in children.Where(IsDoor))
            {
                double width = Math.Min(child.DesiredSize.Width, Math.Max(0, finalSize.Width - x));
                child.ClipToBounds = true;
                child.Arrange(new Rect(x, 0, width, finalSize.Height));
                x += width;
            }
            return finalSize;
        }
    }
}
