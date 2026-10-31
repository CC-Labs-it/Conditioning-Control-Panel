// PORTED from ConditioningControlPanel/Services/Glassroad/GlassroadHostService.cs (feat/glassroad-a 5121933ee):
// GLASSROAD, the 3D race (Assets/web/glassroad/index.html), private preview behind CCP_GLASSROAD=1. With the switch
// on, the Racing Thoughts door opens this page instead (GameWindow.Launch); without it nothing changes for anyone.
// INIT GOES OUT BEFORE READY: the page awaits init at boot (3 s, then defaults with no net) but says ready only once
// shaders are warm, so init is posted at the page's load (NavigationCompleted, this engine's nearest seam to WPF's
// DOMContentLoaded), on the first message and on ready (a repeat is harmless). The relay signs as the player, so it
// forwards ONLY /v2/race/, judged on the built Uri (HostNetPathRule). Escape pauses (GameWindow.Escape.cs), a second
// Escape inside 2 s panics, and panic closes it with every other game (PanicSurfaces "games").
// Not here yet: MergedAccountRecovery on a merged-account answer (the board host lacks it too).
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.PieceByPiece;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        internal const string GlassroadId = "glassroad";

        /// <summary>The door switch's raw value; tests swap it rather than the process env.</summary>
        internal static Func<string?> GlassroadDoorEnv { get; set; } =
            () => Environment.GetEnvironmentVariable(GlassroadHostRules.DoorEnv);

        /// <summary>The live read of the door switch. Never throws.</summary>
        internal static bool GlassroadDoorOpen() { try { return GlassroadHostRules.DoorOpen(GlassroadDoorEnv()); } catch { return false; } }

        /// <summary>WPF LauncherCatalogue "race": with the switch on, every Racing Thoughts door opens Glassroad.</summary>
        internal static string ResolveDoor(string id) =>
            string.Equals(id, "race", StringComparison.OrdinalIgnoreCase) && GlassroadDoorOpen() ? GlassroadId : id;

        /// <summary>A direct launch of the page needs the switch too.</summary>
        internal static bool GlassroadGate()
        {
            if (GlassroadDoorOpen()) return true;
            Log.Information("GlassroadHost: launch refused, {Env} is not 1", GlassroadHostRules.DoorEnv);
            return false;
        }

        private bool IsGlassroad => Spec.Id == GlassroadId;
        private GlassroadSession? _glassroad;
        private DispatcherTimer? _grWatch;
        internal Task GlassroadPending => _glassroad?.GlassroadPending ?? Task.CompletedTask;
        private void OpenGlassroad()
        {
            _glassroad = new GlassroadSession(Post, Close, () => Web.Focus());
            Web.NavigationCompleted += url => { if (PageUrl != null && SameOrigin(url, PageUrl)) PostGlassroadInit(); };
            _grWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _grWatch.Tick += (_, _) => CheckGlassroadWatch(DateTime.UtcNow);
            _grWatch.Start();
            Closed += (_, _) => { _grWatch.Stop(); _glassroad.Dispose(); };
        }
        internal void PostGlassroadInit() => _glassroad?.PostGlassroadInit();
        private bool HandleGlassroad(JObject o)
        {
            var handled = _glassroad?.HandleGlassroad(o) == true;
            IsReady = _glassroad?.Ready == true;
            return handled;
        }
        internal string CheckGlassroadWatch(DateTime nowUtc) => _glassroad?.CheckGlassroadWatch(nowUtc) ?? "idle";
    }
}
