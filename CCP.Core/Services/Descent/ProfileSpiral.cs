using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Services.Descent;

/// <summary>The native profile doors share WPF's block and migration gates, independent of the web rail flag.</summary>
public static class ProfileSpiral
{
    public static bool IsVisible(DescentBlock? block, bool withheld, bool viewingSelf = true) =>
        block is not null && !withheld && viewingSelf;

    public static bool IsWithheld(AppSettings? settings, bool offerInHand = false, bool ceremonyOpen = false)
    {
        if (settings is null || settings.DescentMigrationCompleted
            || DescentMigrationChoices.IsValid(settings.PendingDescentMigrationChoice)) return false;
        return offerInHand || ceremonyOpen || settings.DescentMigrationOffered;
    }

    public static string Summary(DescentBlock block) =>
        string.Format(Loc.Get("programs_card_day"), block.DevotionDays)
        + " · " + DescentStageCopy.Name(block.Stage?.N ?? 0);
}
