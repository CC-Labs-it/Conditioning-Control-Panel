namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The size a flash is drawn at. A caller that names a size (the Back Room's authored 100 %, a
    /// Flash effect bubble's intensity-scaled size, the AI's flash_image command) gets that size; a
    /// caller that does not, or names zero (an AI command that left the field out deserializes as 0),
    /// gets the player's Image Scale. Before this the random-pick burst computed the named size and
    /// then dropped it, so every one of those callers drew at the player's scale instead. Pure.
    /// </summary>
    internal static class FlashScaleRule
    {
        /// <summary>The scale factor (1.0 = 100 %) for a flash asked for at <paramref name="sizePercent"/>.</summary>
        internal static double Resolve(int? sizePercent, int imageScalePercent)
            => (sizePercent is > 0 ? sizePercent.Value : imageScalePercent) / 100.0;
    }
}
