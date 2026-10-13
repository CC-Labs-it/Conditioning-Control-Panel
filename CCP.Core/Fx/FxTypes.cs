namespace ConditioningControlPanel.Fx
{
    /// <summary>A neutral 2D point / vector (WPF Point, Avalonia Point).</summary>
    public readonly record struct Vec2(double X, double Y);

    /// <summary>A neutral rectangle (WPF Rect, Avalonia Rect).</summary>
    public readonly record struct RectD(double Left, double Top, double Width, double Height)
    {
        public double Right => Left + Width;
        public double Bottom => Top + Height;
    }
}
