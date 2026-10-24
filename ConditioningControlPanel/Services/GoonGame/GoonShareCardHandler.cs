using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.GoonGame
{
    /// <summary>
    /// The WPF half of <c>share-card</c>: decode (WIC), clipboard and the save dialog. The frame
    /// rules (id, action, PNG magic, size caps, file name) are <see cref="GoonShareCard"/> in CCP.Core.
    /// </summary>
    internal static class GoonShareCardHandler
    {
        /// <summary>UI thread. Answers through <paramref name="reply"/> exactly once.</summary>
        public static void Handle(JObject o, Window? owner, Action<object> reply)
        {
            var req = GoonShareCard.Parse(o, out var error);
            var id = req?.Id ?? ((string?)o["id"] ?? "");
            if (req == null)
            {
                App.Logger?.Debug("GoonShareCard: refused ({E})", error);
                reply(new { type = "share-card-result", id, ok = false, error });
                return;
            }
            try
            {
                var bmp = Decode(req.Png);
                if (bmp == null) { reply(new { type = "share-card-result", id, ok = false, error = "bad-format" }); return; }
                if (req.Action == "copy")
                {
                    var ok = CopyToClipboard(req.Png, bmp);
                    reply(new { type = "share-card-result", id, ok, error = ok ? "" : "clipboard-busy" });
                }
                else
                {
                    var r = SaveWithDialog(req.Png, req.FileName, owner);
                    reply(new { type = "share-card-result", id, ok = r == "", error = r });
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("GoonShareCard: {A} failed: {E}", req.Action, ex.Message);
                reply(new { type = "share-card-result", id, ok = false, error = "failed" });
            }
        }

        private static BitmapSource? Decode(byte[] png)
        {
            using var ms = new MemoryStream(png);
            var dec = new PngBitmapDecoder(ms, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (dec.Frames.Count == 0) return null;
            var f = dec.Frames[0];
            if (f.PixelWidth <= 0 || f.PixelHeight <= 0 || f.PixelWidth > GoonShareCard.MaxPixels || f.PixelHeight > GoonShareCard.MaxPixels) return null;
            f.Freeze();
            return f;
        }

        /// <summary>
        /// PNG format (what Discord, browsers and most chat apps read, alpha intact) plus the
        /// classic bitmap formats (everything else). Retried: the clipboard is a shared lock and
        /// another app holding it for a moment is normal.
        /// </summary>
        private static bool CopyToClipboard(byte[] png, BitmapSource bmp)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    var data = new DataObject();
                    data.SetData("PNG", new MemoryStream(png), false);
                    data.SetImage(bmp);
                    Clipboard.SetDataObject(data, true);
                    return true;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    System.Threading.Thread.Sleep(60);
                }
            }
            return false;
        }

        private static string SaveWithDialog(byte[] png, string fileName, Window? owner)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = fileName,
                DefaultExt = ".png",
                Filter = ConditioningControlPanel.Localization.Loc.Get("goon_share_png_filter") + " (*.png)|*.png",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                AddExtension = true,
                OverwritePrompt = true,
            };
            var picked = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
            if (picked != true) return "cancelled";
            File.WriteAllBytes(dlg.FileName, png);
            App.Logger?.Information("GoonShareCard: saved a match card ({N} bytes)", png.Length);
            return "";
        }
    }
}
