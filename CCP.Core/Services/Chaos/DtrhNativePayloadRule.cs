using System;

namespace ConditioningControlPanel.Services.Chaos
{
    /// <summary>
    /// Which <c>fire-payload</c> kinds the DtRH host still plays natively (ccp-bugs #1470).
    ///
    /// <para>Since the 2026-07 cutover every visual effect, the video card included, renders
    /// in the page, and the page sends only <c>audio</c> over the bridge. The host used to accept
    /// <c>video</c> as well and open a mandatory video window for it. That window carries no run
    /// token, so nothing at run end, at the hub or at host close could call it back: a stale page
    /// or a future one asking for it would leave a tape over the hub. The host refuses it, like the
    /// Racing Thoughts host does.</para>
    /// </summary>
    public static class DtrhNativePayloadRule
    {
        /// <summary>True when the host may fire this kind natively.</summary>
        public static bool Accepts(string? kind)
            => string.Equals(kind, "audio", StringComparison.OrdinalIgnoreCase);
    }
}
