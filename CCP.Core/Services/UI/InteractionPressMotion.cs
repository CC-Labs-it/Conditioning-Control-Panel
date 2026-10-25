namespace ConditioningControlPanel.Services.UI
{
    /// <summary>WPF MotionFx press response, shared by both heads. Off snaps to the same endpoint.</summary>
    public static class InteractionPressMotion
    {
        public const double Scale = 0.97;
        public const int DurationMs = 80;
        public static double ScaleFor(bool down) => down ? Scale : 1;
    }
}
