// PORTED from ConditioningControlPanel/Controls/Billboard/HouseScenes.cs: the house cards draw their
// own animated scene; the card's art data is still the poster path and its file stem picks the scene.

using System.IO;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Controls.Billboard
{
    /// <summary>WPF HouseScenes. A stem with no scene on this head yet returns null, and the card
    /// keeps its poster picture (WPF PosterArtView).</summary>
    public static class HouseScenes
    {
        public static Control? Create(string? posterPath) =>
            (posterPath == null ? null : Path.GetFileNameWithoutExtension(posterPath)) switch
            {
                "discord" => new DiscordHouseArt(),
                "webapp" => new WebAppHouseArt(),
                "remix" => new RemixHouseArt(),
                "loom" => new LoomHouseArt(),
                "support" => new SupportHouseArt(),
                "backroom" => new WheelArtView(),
                _ => null,
            };

        /// <summary>Stems that only ever existed as a drawn scene: their poster path is a scene key with no
        /// file behind it (the Daily Daze wheel, 2026-10-07).</summary>
        public static bool DrawnOnly(string? posterPath) =>
            posterPath != null && Path.GetFileNameWithoutExtension(posterPath) == "backroom";
    }
}
