using System;

namespace ConditioningControlPanel.Services.Audio
{
    /// <summary>Names a web page in the recent sounds ring.</summary>
    public static class WebPageSound
    {
        public static (string Player, string File) Describe(string? source, bool muted)
        {
            string host = "page", file = "";
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            {
                host = string.IsNullOrEmpty(uri.Host) ? uri.Scheme : uri.Host;
                var path = uri.AbsolutePath.TrimEnd('/');
                var slash = path.LastIndexOf('/');
                file = slash >= 0 ? path.Substring(slash + 1) : path;
            }
            if (string.IsNullOrEmpty(file)) file = "(page)";
            return ("web:" + host + (muted ? ":muted" : ""), file);
        }
    }
}
