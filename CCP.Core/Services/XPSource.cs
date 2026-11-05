namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Source of XP for tracking companion bonuses.
    /// </summary>
    public enum XPSource
    {
        Flash,
        Video,
        Subliminal,
        Bubble,
        LockCard,
        Session,
        BubbleCount,
        BouncingText,
        AvatarInteraction,
        KeywordTrigger,
        Mantra,
        AttentionCheck,
        Chaos,
        Fyp,
        Other,

        /// <summary>
        /// A daily or weekly quest paying out on completion. APPENDED, never reordered, so no
        /// stored ordinal moves. Split out of <see cref="Other"/> because it is a fixed
        /// earned-by-finishing-something award, which is what makes it one of the four sources
        /// THE BANK celebrates - see <c>BankAccumulator.IsBankable</c>.
        /// </summary>
        Quest
    }

}
