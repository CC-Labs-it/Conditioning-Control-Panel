namespace ConditioningControlPanel.Services.Super
{
    /// <summary>
    /// The eight Super effects. Each is an ADD-ON layered on a base effect (or, for Creep, a new
    /// effect beside the pink filter), never a replacement. Order is stable: it is the weekly free
    /// rotation's wheel, so append, never reorder. See Services/Super/CONTRACT.md.
    /// Lives in Core so the rules and both heads share one list.
    /// </summary>
    public enum SuperEffect
    {
        FlickerDeck,   // flashes
        InnerBloom,    // bubbles
        Afterglow,     // subliminals
        Vortex,        // spiral overlay
        Creep,         // pink fog, new, beside the pink filter
        LightsDown,    // mandatory video
        Scrawl,        // bouncing text
        Undertow,      // brain drain and melt
    }
}
