using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services;

public enum KeywordInputKey { Clear, Backspace, Space, None }

/// <summary>WPF KeywordTriggerService's in-memory typed buffer. Never persists captured text.</summary>
public sealed class KeywordTextInput
{
    private readonly StringBuilder _text = new(200);
    private DateTime _lastKey = DateTime.MinValue;
    public string Text => _text.ToString();
    public void Clear() => _text.Clear();

    private void Touch(DateTime now, int timeoutMs)
    {
        if ((now - _lastKey).TotalMilliseconds > timeoutMs) Clear();
        _lastKey = now;
    }

    public void Append(char ch, DateTime now, int timeoutMs)
    {
        Touch(now, timeoutMs);
        _text.Append(ch);
        if (_text.Length > 200) _text.Remove(0, _text.Length - 200);
    }

    /// <returns>Whether WPF checks for matches after this key.</returns>
    public bool Key(KeywordInputKey key, DateTime now, int timeoutMs)
    {
        Touch(now, timeoutMs);
        switch (key)
        {
            case KeywordInputKey.Clear: Clear(); break;
            case KeywordInputKey.Backspace: if (_text.Length > 0) _text.Remove(_text.Length - 1, 1); break;
            case KeywordInputKey.Space: _text.Append(' '); return true;
        }
        return false;
    }
}

/// <summary>Shared by the WPF service and the Avalonia keyboard/audio slice.</summary>
public static class KeywordMatchRules
{
    public static bool ContainsWholeWord(string text, string keyword)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword)) return false;
        var words = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Match(text, @"\b" + string.Join(@"\s+", words.Select(Regex.Escape)) + @"\b", true);
    }

    public static bool Match(string text, string pattern, bool cultureInvariant = false)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase |
                (cultureInvariant ? RegexOptions.CultureInvariant : RegexOptions.None), TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException) { return false; }
        catch (RegexMatchTimeoutException) { return false; }
    }

    public static IEnumerable<KeywordTrigger> Ordered(List<KeywordTrigger> triggers)
    {
        foreach (var t in triggers) if (t?.Id?.StartsWith("preset:", StringComparison.Ordinal) == true) yield return t;
        foreach (var t in triggers) if (t?.Id?.StartsWith("preset:", StringComparison.Ordinal) != true) yield return t!;
    }

    public static List<KeywordTrigger> Collect(List<KeywordTrigger> triggers, string text, DateTime now,
        Func<string, bool> isMuted) => Ordered(triggers).Where(t => t != null && t.Enabled &&
            !string.IsNullOrEmpty(t.Keyword) && (now - t.LastTriggeredAt).TotalSeconds >= t.CooldownSeconds &&
            !isMuted(t.Keyword) && (t.MatchType == KeywordMatchType.Regex
                ? Match(text, t.Keyword) : ContainsWholeWord(text, t.Keyword))).ToList();

    public static bool MatchesAppList(IEnumerable<string>? list, string processName) =>
        !string.IsNullOrWhiteSpace(processName) && list != null && list.Any(raw =>
        {
            var entry = raw?.Trim() ?? "";
            if (entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) entry = entry[..^4];
            return entry.Length > 0 && string.Equals(entry, processName, StringComparison.OrdinalIgnoreCase);
        });

    /// <summary>WPF KeywordTriggerService :677-681: the default Everywhere scope never resolves the foreground app.</summary>
    public static bool NeedsForeground(AppSettings settings) =>
        settings.KeywordTriggerAppScope != AwarenessAppScope.Everywhere || settings.KeywordTriggerIgnoreOwnFocus;

    public static bool AppAllowed(AppSettings settings, string? process, bool isOwn) =>
        !NeedsForeground(settings) ||
        (process == null ? settings.KeywordTriggerAppScope != AwarenessAppScope.OnlyListed :
         !(isOwn && settings.KeywordTriggerIgnoreOwnFocus) && (settings.KeywordTriggerAppScope switch
         {
             AwarenessAppScope.OnlyListed => MatchesAppList(settings.KeywordTriggerApps, process),
             AwarenessAppScope.ExceptListed => !MatchesAppList(settings.KeywordTriggerApps, process),
             _ => true,
         }));

    public static float AudioVolume(int volume, int master) =>
        Math.Max(0.05f, (float)Math.Pow(volume / 100f * (master / 100f), 1.5));
}

/// <summary>WPF global cooldown and cross-source keyword echo mute; expiry extends, never shrinks.</summary>
public sealed class KeywordCooldowns
{
    private readonly Dictionary<string, DateTime> _mutes = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    public DateTime LastGlobal { get; set; } = DateTime.MinValue;

    public bool IsMuted(string keyword, DateTime utcNow)
    {
        lock (_gate)
        {
            if (!_mutes.TryGetValue(keyword, out var until)) return false;
            if (utcNow < until) return true;
            _mutes.Remove(keyword);
            return false;
        }
    }

    public void Mute(string keyword, int milliseconds, DateTime utcNow)
    {
        if (string.IsNullOrEmpty(keyword) || milliseconds <= 0) return;
        lock (_gate)
        {
            var until = utcNow.AddMilliseconds(milliseconds);
            if (!_mutes.TryGetValue(keyword, out var old) || old < until) _mutes[keyword] = until;
        }
    }

    public void Record(KeywordTrigger trigger, AppSettings settings, DateTime utcNow) => Mute(trigger.Keyword,
        Math.Max(settings.AwarenessLoopProtectionEnabled ? settings.AwarenessLoopProtectionMs : 0,
            settings.KeywordPerKeywordCooldownSeconds * 1000), utcNow);
}
