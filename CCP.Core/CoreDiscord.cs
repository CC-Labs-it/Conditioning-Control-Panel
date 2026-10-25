using System;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel;

/// <summary>The shared engine's activity sink. Both heads seed their existing opt-in RPC service;
/// an unseeded head updates nothing and never creates a client or enables presence.</summary>
public static class CoreDiscord
{
    public static volatile Func<DiscordRichPresenceService?>? ServiceProvider;
    public static DiscordRichPresenceService? Service
    {
        get { try { return ServiceProvider?.Invoke(); } catch { return null; } }
    }
}
