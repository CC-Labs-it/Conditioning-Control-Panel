using System;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services.Speech
{
    /// <summary>Locates the optional sherpa wake model, as in WPF SherpaWakeService.FindModel.</summary>
    public static class SherpaWakeService
    {
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
