using System;
using System.IO;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Studio > Wallpaper > "Save this wallpaper" (ccp-bugs #1542). A Static collage is a temp file the next
    /// rotation deletes, so this copies the picture on the desktop into Pictures\CCP Wallpapers, where the
    /// player can keep it or set it in Windows themselves. Copies only: the running wallpaper and the
    /// restore path never see the saved file.
    /// </summary>
    public static class WallpaperSaver
    {
        internal const string FolderName = "CCP Wallpapers";

        /// <summary>Pictures\CCP Wallpapers (the Pictures folder follows a moved library; the profile's
        /// Pictures is the fallback).</summary>
        public static string DefaultFolder()
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(pictures))
                pictures = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
            return Path.Combine(pictures, FolderName);
        }

        /// <summary>Copy <paramref name="source"/> into <paramref name="folder"/> (created if missing) under
        /// a dated name that never overwrites an earlier save. Returns the new file's path.</summary>
        public static string SaveCopy(string source, string folder, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                throw new FileNotFoundException("Nothing to save", source);
            Directory.CreateDirectory(folder);

            var ext = Path.GetExtension(source);
            if (string.IsNullOrEmpty(ext)) ext = ".jpg";
            var stem = "CCP wallpaper " + now.ToString("yyyy-MM-dd HHmmss", System.Globalization.CultureInfo.InvariantCulture);

            var dest = UniquePath(folder, stem, ext, File.Exists);
            File.Copy(source, dest, overwrite: false);
            return dest;
        }

        /// <summary>stem.ext, then "stem (2).ext", "stem (3).ext", ... until a free name.</summary>
        internal static string UniquePath(string folder, string stem, string ext, Func<string, bool> exists)
        {
            var path = Path.Combine(folder, stem + ext);
            for (int n = 2; exists(path); n++)
                path = Path.Combine(folder, $"{stem} ({n}){ext}");
            return path;
        }
    }
}
