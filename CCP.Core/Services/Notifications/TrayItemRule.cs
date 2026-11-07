using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Notifications;

/// <summary>What the desktop's tray said about this app's icon. Held means a watcher lists it and a host exists to draw it. Unknown means it gave no proper answer: a refusal is a list without this item.</summary>
internal enum TrayItemState { Held, NotHeld, Unknown }

/// <summary>When the app may publish a tray icon and rely on it. Pure, so every head shares the rules.</summary>
internal static class TrayItemRule
{
    /// <summary>Every desktop side effect checks the sandbox (repo CLAUDE.md, test hygiene): a sandboxed icon
    /// would sit beside the real one, and its Stop everything would leave the real engine running.</summary>
    internal static bool ShouldPublish(bool sandboxed, bool liveProbe) => !sandboxed || liveProbe;

    /// <summary>X may hide unless the tray said no: a busy panel must not end a running session.</summary>
    internal static bool XMayHide(TrayItemState state) => state != TrayItemState.NotHeld;

    /// <summary>Avalonia names its item with the app's own PID and a counter.</summary>
    internal static string ItemNamePrefix(int processId) => $"org.kde.StatusNotifierItem-{processId}-";

    /// <summary>Watchers list "name/path" or a bare name, using either a well-known or a unique bus name,
    /// so <paramref name="ownNames"/> must hold both forms.</summary>
    internal static bool Holds(IEnumerable<string> registered, IReadOnlyCollection<string> ownNames) =>
        registered.Any(item => ownNames.Contains(item.Split('/')[0]));
}
