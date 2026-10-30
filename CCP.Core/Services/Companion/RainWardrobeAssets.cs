using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.Companion
{
    public static class RainWardrobeAssets
    {
        public const string EnvVar = "CCP_RAIN_ASSETS_DIR";
        public const string FolderName = "rain-assets";
        public static readonly IReadOnlyList<string> Files = new[] { "avatar.glb", "pelvis-data.js" };

        /// <summary>The folder, pure: a non-blank <paramref name="env"/> wins, else &lt;userData&gt;/rain-assets.</summary>
        public static string ResolveDir(string? env, string userData) =>
            string.IsNullOrWhiteSpace(env) ? Path.Combine(userData, FolderName) : Path.GetFullPath(env.Trim());

        /// <summary>The files absent from <paramref name="dir"/> (empty = ready). A missing folder misses both.</summary>
        public static IReadOnlyList<string> Missing(string? dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return Files;
            try { return Files.Where(f => !File.Exists(Path.Combine(dir, f))).ToList(); }
            catch (Exception) { return Files; }
        }
    }
}
