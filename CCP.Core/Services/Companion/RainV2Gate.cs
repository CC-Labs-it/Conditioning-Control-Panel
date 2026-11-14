namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// THE Rain V2 switch (owner 2026-10-30): the 3D avatar "V2", its dressing room and the Rain shop
    /// items are postponed out of 7.2.0. Release-only on purpose (no remote switch): to ship V2, flip
    /// <see cref="Enabled"/> to true. Every player-facing V2 door reads <see cref="On"/>: the tube's
    /// V1 | V2 pill, the effective generation (a saved "v2" plays V1 and is kept), the wardrobe doors
    /// (tube menu, companion hero card, <c>RainWardrobeHostService.Open</c>, the asset install card),
    /// the Prize Counter's Rain tabs, buys and Wear, the Help row and the EMI book page. All Rain code,
    /// clips and tests stay in the build; GameSurfaces keeps its panic registration.
    /// </summary>
    public static class RainV2Gate
    {
        /// <summary>Flip to true to ship Rain V2.</summary>
        public const bool Enabled = false;

        /// <summary>The gate as code reads it (a property, so a false constant leaves no unreachable code).</summary>
        public static bool On => Enabled;
    }
}
