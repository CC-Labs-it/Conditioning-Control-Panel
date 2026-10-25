using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.Bureau
{
    /// <summary>
    /// The WPF head's frame decoder for the Bureau. The index itself moved to CCP.Core
    /// (<see cref="BureauIndexService"/>); decoding stays here because it is GDI+ (Windows-only).
    /// </summary>
    internal static class BureauFrameDecoder
    {
        private const int MaxSide = 1280;   // decode cap: keeps bridge messages light; boxes are normalized so labels are unaffected

        /// <summary>
        /// Decode one keyframe of a catalogued image as a JPEG data URI. GDI+ SelectActiveFrame
        /// composites GIF frames the same cumulative way the catalogue's PIL sampler did, so
        /// frame indices line up; a count mismatch (divergent decoder) returns null and the
        /// target is simply skipped client-side.
        /// </summary>
        public static (string DataUri, int W, int H)? DecodeFrame(string sha256, int frameIndex)
        {
            if (!BureauIndexService.TryResolve(sha256, out var packId, out var entry) || entry == null) return null;
            try
            {
                using var stream = App.ContentPacks?.GetPackFileStream(packId, entry);
                if (stream == null) return null;
                using var img = System.Drawing.Image.FromStream(stream);

                if (frameIndex > 0)
                {
                    if (img.FrameDimensionsList == null || img.FrameDimensionsList.Length == 0) return null;
                    var fd = new FrameDimension(img.FrameDimensionsList[0]);
                    int count;
                    try { count = img.GetFrameCount(fd); } catch { count = 1; }
                    if (frameIndex >= count) return null;
                    img.SelectActiveFrame(fd, frameIndex);
                }

                var scale = Math.Min(1.0, (double)MaxSide / Math.Max(img.Width, img.Height));
                var dw = Math.Max(1, (int)Math.Round(img.Width * scale));
                var dh = Math.Max(1, (int)Math.Round(img.Height * scale));

                using var bmp = new Bitmap(dw, dh, PixelFormat.Format24bppRgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    // Charcoal behind any transparency — matches the game's light-table.
                    g.Clear(System.Drawing.Color.FromArgb(14, 13, 26));
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(img, 0, 0, dw, dh);
                }

                var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using var ep = new EncoderParameters(1);
                ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                using var ms = new MemoryStream();
                bmp.Save(ms, codec, ep);
                return ("data:image/jpeg;base64," + Convert.ToBase64String(ms.ToArray()), dw, dh);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("BureauIndex: decode failed for {Hash}:{Frame}: {E}", sha256, frameIndex, ex.Message);
                return null;
            }
        }
    }
}
