namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>First-run editor coachmarks defer to a walkthrough already in progress.</summary>
    public static class DeeperEditorIntroRules
    {
        public static bool ShouldStart(bool hasSeenIntro, bool tutorialActive, bool part2Pending) =>
            !hasSeenIntro && !tutorialActive && !part2Pending;
    }
}
