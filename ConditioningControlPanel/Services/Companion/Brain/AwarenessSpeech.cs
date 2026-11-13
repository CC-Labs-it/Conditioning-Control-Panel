namespace ConditioningControlPanel.Services.Awareness
{
    public sealed partial class AwarenessReactionService
    {
        // AwarenessReactionService.Parse delegates to the shared parser.
        internal static AwarenessReaction Parse(string? raw) => AwarenessReply.ParseReaction(raw);
    }
}
