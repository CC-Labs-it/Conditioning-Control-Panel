using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Companion
{
    /// <summary>
    /// The live app: the higher of the current and the highest-ever level (a re-priced curve never takes an
    /// earned piece back off Rain) and <c>App.Achievements</c>.
    /// </summary>
    public sealed class AppRainProgressSource : IRainProgressSource
    {
        public static RainItemAccess AccessForApp()
            => new(RainItemManifest.Shipped, new AppRainGrantSource(), new AppRainProgressSource());

        public int Level
        {
            get
            {
                var s = App.Settings?.Current;
                return s == null ? 0 : Math.Max(s.PlayerLevel, s.HighestLevelEver);
            }
        }

        public IReadOnlyCollection<string> UnlockedAchievements
            => (IReadOnlyCollection<string>?)App.Achievements?.Progress?.UnlockedAchievements?.ToArray() ?? Array.Empty<string>();
    }

}
