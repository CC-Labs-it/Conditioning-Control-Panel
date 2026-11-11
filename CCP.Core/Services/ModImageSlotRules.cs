using System;
using System.IO;
namespace ConditioningControlPanel
{
    // Shared with WPF ModCreatorWindow.xaml.cs:1977.
    internal static class ModImageSlotRules
    {
        internal const long WarnFileSizeBytes = 4L * 1024 * 1024;
        internal static string? DescribeExtensionMismatch(string slotKey, string filePath)
        {
            var wanted = NormalizeExtension(slotKey);
            if (wanted.Length == 0) return null;
            var got = NormalizeExtension(filePath);
            if (got.Length == 0)
                return $"\"{Path.GetFileName(filePath)}\" has no file extension. This slot needs a {wanted} file.";
            if (string.Equals(wanted, got, StringComparison.OrdinalIgnoreCase)) return null;
            return $"\"{Path.GetFileName(filePath)}\" is a {got} file, but this slot must be {wanted} -- " +
                   $"it is packed as resources/{slotKey} and replaces the app's file of that exact name.\n\n" +
                   $"Renaming does not convert the format. Re-save the image as {wanted} and pick it again.";
        }
        internal static bool IsOversized(long sizeBytes) => sizeBytes > WarnFileSizeBytes;
        internal static string FormatSize(long sizeBytes) =>
            (sizeBytes / 1024d / 1024d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";
        private static string NormalizeExtension(string path)
        {
            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return string.Empty;
            ext = ext.ToLowerInvariant();
            return ext == ".jpeg" ? ".jpg" : ext;
        }
    }
}
