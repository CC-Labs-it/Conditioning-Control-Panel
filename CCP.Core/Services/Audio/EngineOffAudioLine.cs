using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Audio
{
    /// <summary>WPF EngineOffAudioLog line and per-player rate limit.</summary>
    public sealed class EngineOffAudioLine
    {
        private readonly Dictionary<string, DateTime> _last = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        public string? LineFor(string player, string? path, bool engineRunning, bool asked, DateTime now)
        {
            if (engineRunning || asked) return null;
            if (string.IsNullOrEmpty(player)) player = "audio";
            lock (_gate)
            {
                if (_last.TryGetValue(player, out var last) && now >= last && now - last < TimeSpan.FromSeconds(10)) return null;
                _last[player] = now;
            }
            return $"engine off: {player} started '{NameOf(path)}'";
        }
        public static string NameOf(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "?";
            var trimmed = path.TrimEnd('/', '\\');
            var slash = trimmed.LastIndexOfAny(new[] { '/', '\\' });
            var name = trimmed.Substring(slash + 1);
            return name.Length == 0 ? "?" : name;
        }
    }
}
