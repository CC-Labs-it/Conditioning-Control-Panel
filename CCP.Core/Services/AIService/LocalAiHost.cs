using System;

namespace ConditioningControlPanel.Services.AIService
{
    /// <summary>
    /// One rule for the saved Ollama host (<c>CompanionPrompt.AiOllamaHost</c>), shared by the local
    /// provider, the Engine Room host box and its Test button.
    ///
    /// <para>Issue #5197: a host saved without a scheme (<c>127.0.0.1:11434</c>) or half typed made
    /// <c>new Uri(host)</c> throw inside the <see cref="LocalAiService"/> constructor, and that escaped
    /// through <c>App.Ai.IsAvailable</c> into the MainWindow constructor: the panel never opened.
    /// Here a missing scheme gets <c>http://</c>, and anything that still is not an absolute http(s)
    /// address falls back to <see cref="Default"/>. Nothing in this class throws.</para>
    /// </summary>
    public static class LocalAiHost
    {
        public const string Default = "http://localhost:11434/";

        /// <summary>
        /// The host as a usable base address: trimmed, <c>http://</c> added when there is no scheme,
        /// one trailing slash. Blank or invalid gives <see cref="Default"/>.
        /// </summary>
        public static string Normalize(string? host) => Normalize(host, out _);

        /// <param name="fellBack">True when <paramref name="host"/> was not blank and could not be
        /// used, so <see cref="Default"/> came back instead.</param>
        public static string Normalize(string? host, out bool fellBack)
        {
            fellBack = false;
            if (string.IsNullOrWhiteSpace(host)) return Default;
            if (TryNormalize(host, out var normalized)) return normalized;
            fellBack = true;
            return Default;
        }

        /// <summary>
        /// True when <paramref name="host"/> is a usable address (scheme optional). Blank is false:
        /// the caller decides what blank means.
        /// </summary>
        public static bool TryNormalize(string? host, out string normalized)
        {
            normalized = Default;
            if (string.IsNullOrWhiteSpace(host)) return false;

            var text = host.Trim();
            if (text.IndexOf("://", StringComparison.Ordinal) < 0)
                text = "http://" + text;

            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            if (string.IsNullOrEmpty(uri.Host)) return false;

            var abs = uri.AbsoluteUri;
            normalized = abs.EndsWith("/", StringComparison.Ordinal) ? abs : abs + "/";
            return true;
        }
    }
}
