using System;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.Speech
{
    /// <summary>Locates the optional sherpa wake model, as in WPF SherpaWakeService.FindModel.</summary>
    public static class SherpaWakeService
    {
        // WPF SherpaWakeService.cs:596-622: choose the strictest reliable threshold,
        // preferring quiet room tone, then recall, then the best available count.
        internal static (double threshold, int caught, bool ambientRisk)? SelectCalibrationThreshold(int utterances, int[] caught, bool[] ambient)
        {
            double[] candidates = { 0.28, 0.24, 0.20, 0.16, 0.13, 0.10, 0.08, 0.06 };
            if (utterances < 2 || caught.Length != candidates.Length || ambient.Length != candidates.Length)
                throw new ArgumentException("Invalid calibration sweep.");
            int needed = Math.Max(2, (int)Math.Ceiling(utterances * 0.7));
            int pick = -1;
            for (int k = 0; k < candidates.Length; k++)
                if (caught[k] >= needed && !ambient[k]) { pick = k; break; }
            bool risk = false;
            if (pick < 0)
            {
                for (int k = 0; k < candidates.Length; k++)
                    if (caught[k] >= needed) { pick = k; risk = true; break; }
            }
            if (pick < 0)
            {
                pick = 0;
                for (int k = 1; k < candidates.Length; k++) if (caught[k] > caught[pick]) pick = k;
                risk = ambient[pick];
            }
            return caught[pick] < 2 ? null : (candidates[pick], caught[pick], risk);
        }

        public static string ModelRoot => Path.Combine(AppContext.BaseDirectory, "Resources", "Models", "sherpa-kws");
        public readonly record struct ModelFiles(string Encoder, string Decoder, string Joiner, string Tokens, string Keywords);

        public static ModelFiles? FindModel(string root)
        {
            try
            {
                if (!Directory.Exists(root)) return null;
                var onnx = Directory.EnumerateFiles(root, "*.onnx", SearchOption.AllDirectories).ToList();
                string? Pick(string part) => onnx
                    .Where(f => Path.GetFileName(f).Contains(part, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => Path.GetFileName(f).Contains("int8", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
                var encoder = Pick("encoder");
                var decoder = Pick("decoder");
                var joiner = Pick("joiner");
                var tokens = Directory.EnumerateFiles(root, "tokens.txt", SearchOption.AllDirectories).FirstOrDefault();
                var keywords = Directory.EnumerateFiles(root, "keywords.txt", SearchOption.AllDirectories).FirstOrDefault();
                return encoder == null || decoder == null || joiner == null || tokens == null || keywords == null
                    ? null : new ModelFiles(encoder, decoder, joiner, tokens, keywords);
            }
            catch { return null; }
        }
    }
}
