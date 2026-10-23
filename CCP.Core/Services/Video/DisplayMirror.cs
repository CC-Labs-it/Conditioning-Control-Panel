using System;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>The OS half of <see cref="DisplayMirror"/>: WPF ScreenMirrorService's AllScreens count and its
    /// SetDisplayConfig(SDC_TOPOLOGY_CLONE|EXTEND | SDC_APPLY) call. Only Windows has one.</summary>
    public interface IDisplayTopology
    {
        int ActiveScreenCount { get; }

        /// <summary>Switches to clone (true) or extend (false); returns the Win32 error code, 0 on success.</summary>
        int Apply(bool clone);
    }

    /// <summary>
    /// The state rules of WPF ScreenMirrorService (Services/Video/ScreenMirrorService.cs): mirror the primary
    /// screen onto every monitor for fullscreen video, and put extend back afterwards and on dispose.
    /// </summary>
    public sealed class DisplayMirror : IDisposable
    {
        private readonly IDisplayTopology _topology;
        private bool _disposed;

        public DisplayMirror(IDisplayTopology topology) => _topology = topology;

        public bool IsMirroring { get; private set; }

        /// <summary>WPF EnableMirror (:37-87). Clone only from 2+ active screens (#157): with Windows projection on
        /// "PC screen only"/"second screen only" the hard-coded extend restore would undo the user's mode.</summary>
        public bool EnableMirror()
        {
            if (IsMirroring) return true;
            try
            {
                var count = _topology.ActiveScreenCount;
                if (count < 2)
                {
                    Log.Information("ScreenMirror: skipping clone — only {Count} active screen(s); leaving display topology untouched", count);
                    return false;
                }

                var result = _topology.Apply(clone: true);
                if (result == 0)
                {
                    IsMirroring = true;
                    Log.Information("ScreenMirror: Enabled clone/duplicate display mode");
                    return true;
                }
                Log.Warning("ScreenMirror: Failed to enable clone mode - {Error} (code: {Code})", Describe(result), result);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ScreenMirror: Exception enabling clone mode");
                return false;
            }
        }

        /// <summary>WPF DisableMirror (:93-118): a failed restore keeps the mirroring flag so a later call retries.</summary>
        public bool DisableMirror()
        {
            if (!IsMirroring) return true;
            try
            {
                var result = _topology.Apply(clone: false);
                if (result == 0)
                {
                    IsMirroring = false;
                    Log.Information("ScreenMirror: Restored extend display mode");
                    return true;
                }
                Log.Warning("ScreenMirror: Failed to restore extend mode, error code: {Code}", result);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ScreenMirror: Exception restoring extend mode");
                return false;
            }
        }

        /// <summary>WPF Dispose (:120-130): always restore extend.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (IsMirroring) DisableMirror();
        }

        internal static string Describe(int code) => code switch
        {
            50 => "NOT_SUPPORTED - Clone mode not available (3+ monitors or mixed GPUs?)",
            5 => "ACCESS_DENIED - Insufficient permissions",
            31 => "GEN_FAILURE - Display configuration incompatible",
            87 => "INVALID_PARAMETER - Invalid display topology",
            _ => "Unknown error",
        };
    }
}
