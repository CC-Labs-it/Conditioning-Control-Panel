// MOVED from ConditioningControlPanel/Services/Account/DiscordRichPresenceService.cs (WPF 7.1.5): one copy
// serves both heads. Head-specific bits are two seams: App.Logger -> Serilog's static Log (WPF's App.Logger
// writes through it), and the WPF DispatcherTimer/Dispatcher.Invoke -> a thread timer whose ticks hop to the
// head's UI thread through PostToUi (each head sets it at startup, before the service is built).
// DiscordRichPresence 1.6.1.70 speaks a named pipe discord-ipc-N on Windows, the unix
// socket under $XDG_RUNTIME_DIR (and the flatpak / snap paths the library probes) on Linux. No Discord
// running = a logged connection failure, nothing else.
//
// PRESENCE IS OPT-IN and only ever describes THIS user: fixed strings, the user's own level when they
// asked for it, never a friend's or an opponent's name or Discord id. Arming is refused without a linked
// Discord at every switch (ProfilePrivacyPanel, the Home quick toggle) and at startup (App).
using System;
using System.Threading;
using Serilog;
using DiscordRPC;
using DiscordRPC.Logging;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Service for Discord Rich Presence integration.
/// Shows user's activity status in Discord.
/// </summary>
public class DiscordRichPresenceService : IDisposable
{
    // Discord Application ID - Create at https://discord.com/developers/applications
    private const string ApplicationId = "1461012135982403696";

    private DiscordRpcClient? _client;
    private bool _disposed;
    private bool _isEnabled;
    private DateTime _sessionStartTime;
    private string _currentState = "Idle";
    private string _currentDetails = "In the app";
    private readonly Timer _updateTimer;
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(15);

    /// <summary>Runs an action on the head's UI thread (WPF Dispatcher.BeginInvoke, Avalonia Dispatcher.UIThread.Post).
    /// Default runs inline, which is what a headless test wants.</summary>
    public static Action<Action> PostToUi { get; set; } = a => a();
    private int _currentLevel = 0;

    // ---- Goon Game presence (docs/GOON_DISCORD_CONTRACT.md §5) ----------------------
    // The duel OVERRIDES the generic presence while it runs and puts it back afterwards.
    // Three pieces of state, because there are two different "afterwards": if GG connected
    // the client itself (global presence off) the correct exit is ClearPresence+Disconnect,
    // and if the global presence was already running the correct exit is to restore the exact
    // activity it was showing before the duel took over.
    private bool _goonActive;            // a duel currently owns the presence text
    private bool _goonOwnsConnection;    // ...and GG is the only reason the RPC client is up
    private string? _goonPriorState;     // the generic activity to put back on `off`
    private string? _goonPriorDetails;

    public bool IsConnected => _client?.IsInitialized == true;

    /// <summary>The activity line the next refresh sends (test seam: which feature owns the presence).</summary>
    internal string CurrentState => _currentState;
    internal string CurrentDetails => _currentDetails;

    /// <summary>A CCP_USERDATA_DIR sandbox (like SecretStore.Sandboxed). Test seam: a test swaps it to reach a FAKE
    /// discord-ipc socket, or to prove the refusal.</summary>
    internal static Func<bool> Sandboxed = () =>
        SandboxNet.Active || Environment.GetEnvironmentVariable("CCP_USERDATA_DIR") != null;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                if (value)
                    Connect();
                else
                    Disconnect();
            }
        }
    }

    public DiscordRichPresenceService()
    {
        _sessionStartTime = DateTime.UtcNow;

        // Update presence every 15 seconds
        _updateTimer = new Timer(_ => PostToUi(UpdatePresence), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Connect to Discord RPC
    /// </summary>
    public void Connect()
    {
        // Don't connect in offline mode
        if (CoreSettings.Current.OfflineMode)
        {
            Log.Debug("Offline mode enabled, Discord Rich Presence disabled");
            return;
        }

        // Not in WPF: a sandboxed run (tests, gate, kc) never shows presence on the user's real Discord.
        if (Sandboxed())
        {
            Log.Debug("Sandboxed run: Discord Rich Presence not connected");
            return;
        }

        if (_client != null && _client.IsInitialized)
            return;

        try
        {
            _client = new DiscordRpcClient(ApplicationId)
            {
                Logger = new ConsoleLogger { Level = LogLevel.Warning }
            };

            _client.OnReady += (sender, e) =>
            {
                Log.Information("Discord RPC connected");
            };

            _client.OnError += (sender, e) =>
            {
                Log.Warning("Discord RPC error: {Message}", e.Message);
            };

            _client.OnConnectionFailed += (sender, e) =>
            {
                Log.Warning("Discord RPC connection failed on pipe {Pipe}. Make sure Discord is running.", e.FailedPipe);
            };

            _client.OnConnectionEstablished += (sender, e) =>
            {
                Log.Information("Discord RPC connection established on pipe {Pipe}", e.ConnectedPipe);
            };

            _client.Initialize();
            _sessionStartTime = DateTime.UtcNow;
            _updateTimer.Change(UpdateInterval, UpdateInterval);

            // Initialize with current level
            _currentLevel = CoreSettings.Current.PlayerLevel;

            // Give Discord a moment to connect, then set initial presence
            System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ =>
            {
                PostToUi(() =>
                {
                    if (_client?.IsInitialized == true)
                    {
                        UpdatePresence();
                        Log.Information("Discord Rich Presence connected and presence set");
                    }
                });
            });

            Log.Information("Discord Rich Presence initializing...");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize Discord RPC");
        }
    }

    /// <summary>
    /// Disconnect from Discord RPC
    /// </summary>
    public void Disconnect()
    {
        _updateTimer.Change(Timeout.Infinite, Timeout.Infinite);

        if (_client != null)
        {
            try
            {
                _client.ClearPresence();
                _client.Dispose();
            }
            catch { }
            _client = null;
        }

        Log.Information("Discord Rich Presence disabled");
    }

    /// <summary>
    /// Update the current activity state
    /// </summary>
    public void SetActivity(string state, string? details = null)
    {
        _currentState = state;
        if (details != null)
            _currentDetails = details;

        UpdatePresence();
    }

    /// <summary>
    /// Set activity for session mode
    /// </summary>
    public void SetSessionActivity(string sessionName)
    {
        _currentState = "In Session";
        _currentDetails = sessionName;
        _sessionStartTime = DateTime.UtcNow;
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for idle/browsing
    /// </summary>
    public void SetIdleActivity()
    {
        _currentState = "Browsing";
        _currentDetails = "Exploring the app";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for watching video
    /// </summary>
    public void SetVideoActivity()
    {
        _currentState = "Watching";
        _currentDetails = "Mandatory viewing";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for flash/conditioning
    /// </summary>
    public void SetFlashActivity(int? imageCount = null)
    {
        _currentState = "Conditioning";
        _currentDetails = imageCount.HasValue ? $"Flash training ({imageCount} images)" : "Flash training";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for popping bubbles
    /// </summary>
    public void SetBubbleActivity()
    {
        _currentState = "Playing";
        _currentDetails = "Popping bubbles";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for bubble count video minigame
    /// </summary>
    public void SetBubbleCountActivity()
    {
        _currentState = "Playing";
        _currentDetails = "Bubble counting challenge";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for subliminal messages
    /// </summary>
    public void SetSubliminalActivity()
    {
        _currentState = "Conditioning";
        _currentDetails = "Subliminal training";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for brain drain overlay
    /// </summary>
    public void SetBrainDrainActivity()
    {
        _currentState = "Deep conditioning";
        _currentDetails = "Brain drain active";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for mind wipe
    /// </summary>
    public void SetMindWipeActivity()
    {
        _currentState = "Deep conditioning";
        _currentDetails = "Mind wipe in progress";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for lock card
    /// </summary>
    public void SetLockCardActivity()
    {
        _currentState = "Locked";
        _currentDetails = "In chastity lock";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for browsing Hypnotube
    /// </summary>
    public void SetHypnotubeActivity()
    {
        _currentState = "Browsing";
        _currentDetails = "Exploring Hypnotube";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for AI companion interaction
    /// </summary>
    public void SetCompanionActivity(string? companionName = null)
    {
        _currentState = "Chatting";
        _currentDetails = companionName != null ? $"Talking with {companionName}" : "Chatting with companion";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity with level info
    /// </summary>
    public void SetLevelActivity(int level)
    {
        _currentState = $"Level {level}";
        _currentDetails = "Progressing";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for viewing achievements
    /// </summary>
    public void SetAchievementsActivity(int unlocked, int total)
    {
        _currentState = "Viewing achievements";
        _currentDetails = $"{unlocked}/{total} unlocked";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for leaderboard
    /// </summary>
    public void SetLeaderboardActivity(int? rank = null)
    {
        _currentState = "Leaderboard";
        _currentDetails = rank.HasValue ? $"Ranked #{rank}" : "Checking rankings";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for settings/configuration
    /// </summary>
    public void SetSettingsActivity()
    {
        _currentState = "Configuring";
        _currentDetails = "Adjusting settings";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for spiral watching
    /// </summary>
    public void SetSpiralActivity()
    {
        _currentState = "Mesmerized";
        _currentDetails = "Watching spirals";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for dual monitor video
    /// </summary>
    public void SetDualMonitorActivity()
    {
        _currentState = "Immersed";
        _currentDetails = "Dual monitor experience";
        UpdatePresence();
    }

    /// <summary>
    /// Goon Game presence — FIXED STRINGS ONLY (contract §5). <paramref name="s"/> is an enum:
    /// lobby | live | recap | off. Never the opponent's name, never free text, never a level
    /// suffix: the whole vocabulary is the three strings below, so there is nothing here that can
    /// leak who the player is duelling.
    ///
    /// CONNECT-ON-DEMAND. Two entry states, two exits:
    ///  * global presence OFF, GoonRichPresence ON — GG connects the RPC client for the duration
    ///    of the duel and, on `off`, clears the presence and disconnects again. The user asked for
    ///    Goon Game presence, not for app presence, and they get exactly that.
    ///  * global presence ON — GG overrides the running activity, and `off` RESTORES the exact
    ///    state/details that were showing before it took over (captured on the way in).
    /// Either way, a duel run with GoonRichPresence OFF never reaches this method at all — the host
    /// drops rp-state before it gets here — so the generic presence is untouched.
    /// </summary>
    public void SetGoonActivity(string s)
    {
        try
        {
            string state;
            switch (s)
            {
                case "lobby": state = "In the lobby"; break;
                case "live": state = "In a duel"; break;
                case "recap": state = "Match over"; break;
                case "off": EndGoonActivity(); return;
                default: return;   // enum only — anything else is not a presence we know how to write
            }

            if (!_goonActive)
            {
                _goonActive = true;
                _goonPriorState = _currentState;
                _goonPriorDetails = _currentDetails;
                if (_client == null || !_client.IsInitialized)
                {
                    // GG is the reason this pipe exists, so GG is the one that closes it.
                    _goonOwnsConnection = !_isEnabled;
                    Connect();
                }
            }

            _currentDetails = "Goon Game";
            _currentState = state;
            UpdatePresence();
        }
        catch (Exception ex)
        {
            Log.Debug("SetGoonActivity({S}) failed: {E}", s, ex.Message);
        }
    }

    /// <summary>The `off` half of <see cref="SetGoonActivity"/>. Idempotent: a teardown funnel calls
    /// it whether or not a duel ever set a presence.</summary>
    private void EndGoonActivity()
    {
        if (!_goonActive) return;
        _goonActive = false;

        var owned = _goonOwnsConnection;
        _goonOwnsConnection = false;
        var priorState = _goonPriorState;
        var priorDetails = _goonPriorDetails;
        _goonPriorState = null;
        _goonPriorDetails = null;

        if (owned)
        {
            // Nothing else wanted a presence; put the pipe back the way we found it.
            try { _client?.ClearPresence(); } catch { }
            Disconnect();
            return;
        }

        // The generic presence was already running: give it its own words back.
        _currentState = priorState ?? "Idle";
        _currentDetails = priorDetails ?? "In the app";
        UpdatePresence();
    }

    /// <summary>
    /// Update the current level for Rich Presence display
    /// </summary>
    public void UpdateLevel(int level)
    {
        _currentLevel = level;
        UpdatePresence();
    }

    private void UpdatePresence()
    {
        // `|| !_goonActive`: a duel may have connected the client on its own while the global
        // presence toggle is off — that connection exists precisely to publish this.
        if (_client == null || !_client.IsInitialized || (!_isEnabled && !_goonActive))
            return;

        try
        {
            // Build state string, optionally including level. NOT for the duel: contract §5 pins
            // the Goon Game presence to fixed strings, and "| Level 47" is not one of them.
            var state = _currentState;
            if (!_goonActive && _currentLevel > 0 && CoreSettings.Current.DiscordShowLevelInPresence)
            {
                state = $"{_currentState} | Level {_currentLevel}";
            }

            var presence = new RichPresence
            {
                Details = _currentDetails,
                State = state,
                Timestamps = new Timestamps
                {
                    Start = _sessionStartTime
                }
            };

            if (_goonActive)
            {
                // Art for the duel only — the app's own generic presence still has no uploaded
                // assets (see the commented block below). DiscordRPC 1.6 accepts an https URL as a
                // key; if Discord rejects it the presence simply shows no image, which is the
                // agreed acceptable failure.
                presence.Assets = new Assets
                {
                    LargeImageKey = "https://cclabs.app/img/goon-game.png",
                    LargeImageText = "Goon Game",
                };
            }

            // Only add assets if images are uploaded to Discord Developer Portal
            // To add images: Discord Developer Portal > Your App > Rich Presence > Art Assets
            // Upload images with keys: "app_icon", "session", "video", "flash", "idle"
            // Uncomment below once images are uploaded:
            // presence.Assets = new Assets
            // {
            //     LargeImageKey = "app_icon",
            //     LargeImageText = "Conditioning Control Panel",
            //     SmallImageKey = <"session"/"video"/"flash"/"idle" by _currentState>,
            //     SmallImageText = _currentState
            // };

            _client.SetPresence(presence);
            Log.Debug("Discord presence updated: {Details} - {State}", _currentDetails, state);
        }
        catch (Exception ex)
        {
            Log.Debug("Failed to update Discord presence: {Error}", ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Disconnect();
        _updateTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}
