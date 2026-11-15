using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using Serilog;

namespace ConditioningControlPanel.Services.Launcher;

/// <summary>
/// One tile on the launcher: a game the CC Labs client can start without going through the panel.
///
/// Same shape as <c>EmiTarget</c>: a bag of delegates, so the launcher never learns what a game IS,
/// only whether it exists in this build (<see cref="IsAvailable"/>), whether the tier gate would
/// refuse it (<see cref="IsLocked"/>), how to start it (<see cref="Launch"/>) and whether its window
/// is up right now (<see cref="IsActive"/>). Every host service already exposes exactly those four
/// facts, so a tile is four lambdas and nothing in the window changes when a game is added.
/// </summary>
/// <param name="Id">Stable id. It is the <c>--game &lt;id&gt;</c> argument and the shortcut
/// argument, so it must never be renamed once shipped.</param>
/// <param name="TitleKey">Loc key of the visible name.</param>
/// <param name="BlurbKey">Loc key of the one-line description under the name.</param>
/// <param name="ArtPath">Resource-relative art (<c>features/dtrh.png</c>), resolved through
/// <c>ModResourceResolver</c> so a .ccpmod can reskin it. Null paints the hue plate with a glyph.</param>
/// <param name="Glyph">One character drawn on the hue plate when there is no art, or over it.</param>
/// <param name="Hue">Tile colour behind the art and the tint of the tile's glow.</param>
/// <param name="IsAvailable">False hides the tile. Build flags and "not ready for release".</param>
/// <param name="IsLocked">True paints the padlock; the click still goes to <see cref="Launch"/>,
/// which owns the refusal toast.</param>
/// <param name="Launch">Starts the game. Idempotent on every host: a live window is re-focused.</param>
/// <param name="IsActive">True while the game's window exists. The launcher polls it to know
/// when to come back.</param>
/// <param name="IsRevealed">Null means the tile always shows its face. False draws the mystery
/// card in its place: no title, no art, a "?" and a Play button that goes to the Back Room, where
/// the reveal is bought. The entry stays Available so <c>--game</c> and a shortcut still reach
/// <see cref="Launch"/>, which owns its own refusal.</param>
/// <param name="RequiresAccount">False only for entries that explicitly allow signed-out play.</param>
/// <param name="IsNew">Shows the existing NEW badge on the card.</param>
/// <param name="Door">Which door's page the card sits on (<see cref="LauncherDoorRules"/>).</param>
/// <param name="Listed">False keeps the card off every page; <c>Find</c>, <c>--game</c> and a
/// shortcut still reach it.</param>
public sealed record LauncherEntry(
    string Id,
    string TitleKey,
    string BlurbKey,
    string? ArtPath,
    string Glyph,
    Color Hue,
    Func<bool> IsAvailable,
    Func<bool> IsLocked,
    Action Launch,
    Func<bool> IsActive,
    Func<bool>? IsRevealed = null,
    bool RequiresAccount = true,
    bool IsNew = false,
    LauncherDoor Door = LauncherDoor.Games,
    bool Listed = true)
{
    /// <summary>The entry as the door rules see it: no delegates left, no UI types.</summary>
    public LauncherDoorCard Card => new(Id, Door, Listed, Available, IsNew, Locked);

    public string Title
    {
        get { try { return Loc.Get(TitleKey); } catch { return Id; } }
    }

    public string Blurb
    {
        get { try { return Loc.Get(BlurbKey); } catch { return ""; } }
    }

    /// <summary><see cref="IsAvailable"/> wrapped: a probe that throws hides the tile.</summary>
    public bool Available
    {
        get
        {
            try { return IsAvailable(); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] availability probe threw for {Id}", Id); return false; }
        }
    }

    /// <summary><see cref="IsLocked"/> wrapped: a probe that throws reads as locked, never as free.</summary>
    public bool Locked
    {
        get
        {
            try { return IsLocked(); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] lock probe threw for {Id}", Id); return true; }
        }
    }

    /// <summary>Account requirement for this entry. The free Breakout demo also opens signed out.</summary>
    public bool NeedsAccount => RequiresAccount && LauncherCatalogue.NeedsAccount;

    /// <summary><see cref="IsActive"/> wrapped: a probe that throws reads as closed.</summary>
    public bool Active
    {
        get
        {
            try { return IsActive(); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] active probe threw for {Id}", Id); return false; }
        }
    }

    /// <summary><see cref="IsRevealed"/> wrapped: null is revealed, a probe that throws is not.
    /// The mystery card is the safe face; the real tile would launch straight into a refusal.</summary>
    public bool Revealed
    {
        get
        {
            if (IsRevealed == null) return true;
            try { return IsRevealed(); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] reveal probe threw for {Id}", Id); return false; }
        }
    }
}

/// <summary>
/// The games the launcher can start. Pure: <see cref="Games"/> only captures lambdas, it never runs
/// them, so tests can walk the list without opening a window or evaluating a tier gate.
///
/// What is NOT here, on purpose: Remote, Companion and sessions. Those are panel features
/// (Sep 18 2026 owner decision: the split is games vs CCP, not 2D vs 3D). The Graded Intake is
/// the one panel tab with a tile, because it is the best first thing a new account can do; its
/// Launch opens the panel on the tab instead of a window. Breakout has separate demo and full
/// entries that open its standalone host.
/// </summary>
public static class LauncherCatalogue
{
    /// <summary>The id the panel itself answers to in <c>--game</c> and in shortcuts.</summary>
    public const string PanelId = "panel";

    /// <summary>
    /// Piece by Piece was hidden from 6.9.5 ("not ready for release"). Back on 2026-09-27 (owner),
    /// free for everyone, on the launcher and on Play. One switch for both surfaces.
    /// </summary>
    public static readonly bool PieceByPieceAvailable = true;

    /// <summary>
    /// Whether an account is signed in. Entries may explicitly allow signed-out play. Settable so a
    /// test can walk the refusal without an App.
    /// </summary>
    public static Func<bool> SignedIn { get; set; } = () => App.IsLoggedIn;

    /// <summary>True when the games must refuse and ask for a sign-in. A probe that throws reads
    /// as signed out, never as signed in.</summary>
    public static bool NeedsAccount
    {
        get
        {
            try { return !SignedIn(); }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] sign-in probe threw"); return true; }
        }
    }

    private static readonly Lazy<IReadOnlyList<LauncherEntry>> _games = new(Build);
    private static readonly Lazy<IReadOnlyList<LauncherEntry>> _tools = new(BuildTools);

    /// <summary>The Games door, in screen order. Holds the unlisted Breakout demo as well.</summary>
    public static IReadOnlyList<LauncherEntry> Games => _games.Value;

    /// <summary>The Tools door: editors and makers. No desktop shortcuts, no panic surface of
    /// their own (each opens the panel or a host that is already registered).</summary>
    public static IReadOnlyList<LauncherEntry> Tools => _tools.Value;

    /// <summary>Every entry behind either door, games first.</summary>
    public static IEnumerable<LauncherEntry> All => Games.Concat(Tools);

    /// <summary>The cards a door's page shows, in order: listed and available.</summary>
    public static IReadOnlyList<LauncherEntry> Page(LauncherDoor door)
    {
        var ids = LauncherDoorRules.Page(All.Select(e => e.Card), door);
        return ids.Select(id => Find(id)!).ToList();
    }

    public static LauncherEntry? Find(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var key = id.Trim();
        return All.FirstOrDefault(g => string.Equals(g.Id, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when any catalogued game currently has a window up.</summary>
    public static bool AnyActive => All.Any(g => g.Active);

    /// <summary>
    /// Starts a game by id. False when the id is unknown, the tile is unavailable or nobody is
    /// signed in; a locked tile still routes to <c>Launch</c> because the host owns the refusal
    /// toast and the "See tiers" action.
    /// </summary>
    public static bool TryLaunch(string? id)
    {
        var entry = Find(id);
        if (entry == null) { Log.Warning("[Launcher] unknown game id {Id}", id); return false; }
        return TryLaunch(entry);
    }

    internal static bool TryLaunch(LauncherEntry entry)
    {
        if (!entry.Available) { Log.Information("[Launcher] {Id} is not available in this build", entry.Id); return false; }
        if (entry.NeedsAccount) { Log.Information("[Launcher] {Id} refused: nobody is signed in", entry.Id); return false; }
        try
        {
            entry.Launch();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Launcher] launch of {Id} threw", entry.Id);
            return false;
        }
    }

    private static Color Tile(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    private static bool Always() => true;
    private static bool Never() => false;

    private static bool PremiumOk(string titleKey, string dailyKey)
    {
        try { return TierGate.RequiresPremium(Loc.Get(titleKey), dailyKey).Allowed; }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] premium probe failed"); return false; }
    }

    private static bool LabOk(string titleKey, string? dailyKey)
    {
        try
        {
            var name = Loc.Get(titleKey);
            return dailyKey == null
                ? TierGate.RequiresLab(name).Allowed
                : TierGate.RequiresLab(name, dailyKey).Allowed;
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] lab probe failed"); return false; }
    }

    private static List<LauncherEntry> Build()
    {
        // The face, the order and the account rule are Core's LauncherCards; this head adds how
        // each one starts. A card this head has no host for is left out.
        var hosts = new Dictionary<string, (Func<bool> available, Func<bool> locked, Action launch,
            Func<bool> active, Func<bool>? revealed)>(StringComparer.OrdinalIgnoreCase)
        {
            // The Back Room: free, no gate, no account needed (the SP relay just goes quiet signed out).
            ["backroom"] = (Always, Never,
                () => BackRoom.BackRoomHostService.Launch(),
                () => BackRoom.BackRoomHostService.IsRoomActive, null),

            // Breakout is ONE card (2026-11-15 declutter): the full game for whoever holds it, the
            // free demo for everyone else, so it never wears a padlock and never asks for an account.
            // The demo keeps its id for --game and old shortcuts, off the page.
            ["breakoutdemo"] = (Always, Never,
                () => BackRoom.BreakoutHostService.LaunchDemo(),
                () => BackRoom.BackRoomHostService.IsBreakoutDemoActive, null),
            ["breakout"] = (Always, Never,
                () =>
                {
                    if (BreakoutFull()) BackRoom.BreakoutHostService.LaunchFull();
                    else BackRoom.BreakoutHostService.LaunchDemo();
                },
                () => BackRoom.BackRoomHostService.IsBreakoutFullActive || BackRoom.BackRoomHostService.IsBreakoutDemoActive, null),

            // Piece by Piece: free for everyone (owner, 2026-09-27), so no lock and no account: the
            // computer plays anyone, and the board's own lobby asks a signed-out player to sign in.
            ["piecebypiece"] = (() => PieceByPieceAvailable, Never,
                () => PieceByPiece.PieceByPieceHostService.Launch(),
                () => PieceByPiece.PieceByPieceHostService.IsActive, null),

            // Racing Thoughts is the 3D race (Glassroad), PRIME ONLY, solo included (owner, 2026-10-29:
            // "THE whole feature is for primes"). Always revealed: below Prime the tile wears the
            // padlock and the click goes to Launch, whose DemandPrime paints the refusal. The 2D race
            // has its own tile since 2026-11-05 (raceclassic, below). Online is tier 2+ on the server too.
            ["race"] = (Always,
                () => !Glassroad.GlassroadHostService.PrimeAllowed,
                () => Glassroad.GlassroadHostService.Launch(),
                () => Glassroad.GlassroadHostService.IsActive, null),

            // Racing Thoughts Classic: the original 2D race, back beside the 3D one (owner, 2026-11-05).
            // Free for everyone with every track (RacingAccess.PurchaseDoorArmed is off), so no lock
            // and no Prime badge. Its own key art: the pre-3D race picture.
            ["raceclassic"] = (Always, Never,
                () => Chaos.CaucusHostService.Launch(),
                () => Chaos.CaucusHostService.IsActive, null),

            // Down the Rabbit Hole: Basic since 2026-10-31 (Racing Thoughts 3D is the Prime game now). The
            // Play handler gates it; Launch does not, so the tile asks the gate itself and lets
            // DemandPremium paint the refusal. A server drop day still opens it for free accounts.
            ["dtrh"] = (Always,
                () => !PremiumOk("launcher_game_dtrh_title", "dtrh"),
                () =>
                {
                    var gate = TierGate.RequiresPremium(Loc.Get("launcher_game_dtrh_title"), "dtrh");
                    if (gate.Allowed) Chaos.DtrhHostService.Launch();
                    else TierGate.DemandPremium(Loc.Get("launcher_game_dtrh_title"), "dtrh");
                },
                () => Chaos.DtrhHostService.IsActive, null),

            // The Arcademy: Launch owns the build flag, the Lab gate and the audio-only refusal.
            // The art is the Arcademy's own entrance-gates plate, copied into Resources/features/
            // because the web tree is Content (copied to disk) and only a WPF Resource resolves
            // through a pack:// uri - pointed at the web path the tile drew the glyph plate instead.
            ["arcademy"] = (() => Arcademy.ArcademyHostService.DoorAvailable,
                () => !LabOk("launcher_game_arcademy_title", null),
                () => Arcademy.ArcademyHostService.Launch(),
                () => Arcademy.ArcademyHostService.IsActive, null),

            // Goon Game: free to join. The launcher has already tucked the panel away, so the game
            // must not duck it a second time.
            ["goon"] = (Always, Never,
                () => GoonGame.GoonHostService.Launch(duckMainWindow: false),
                () => GoonGame.GoonHostService.IsActive, null),

            // Graded Intake: a panel tab, not a window, so Launch opens the panel on it and IsActive
            // never reports a window (the launcher does not wait for the panel). Locked when the
            // weekly free pass is spent and the account is below tier 2; the click still opens the
            // tab, whose gate explains the pass, so the tile is never a dead end.
            ["intake"] = (Always,
                () => !(App.IntakePass?.CanStartIntake ?? false),
                () => LauncherHost.OpenPanelTab("gradedintake"),
                Never, null),
        };

        var list = new List<LauncherEntry>();
        foreach (var c in LauncherCards.All)
        {
            if (!hosts.TryGetValue(c.Id, out var h)) continue;
            list.Add(new LauncherEntry(c.Id, c.TitleKey, c.BlurbKey, c.ArtPath, c.Glyph,
                Color.FromRgb(c.R, c.G, c.B), h.available, h.locked, h.launch, h.active, h.revealed,
                c.RequiresAccount, c.IsNew, LauncherDoor.Games, c.Listed));
        }
        return list;
    }

    // A gate that cannot be read (no account, no tier service) plays the demo, never a refusal.
    private static bool BreakoutFull()
    {
        try { return !NeedsAccount && BackRoom.BreakoutAccess.FullAllowed; }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] breakout gate probe failed"); return false; }
    }

    /// <summary>
    /// The Tools door. Only doors that exist: two panel tabs, the Mod Manager (where a mod is
    /// made), and the two standalone makers. None needs an account here; each surface keeps its
    /// own gate.
    /// </summary>
    private static List<LauncherEntry> BuildTools()
    {
        var list = new List<LauncherEntry>();

        void T(string id, string? art, string glyph, Color hue, Func<bool> available, Action launch, Func<bool> active)
        {
            list.Add(new LauncherEntry(id, "launcher_tool_" + id + "_title", "launcher_tool_" + id + "_blurb",
                art, glyph, hue, available, Never, launch, active, null, false, false, LauncherDoor.Tools, true));
        }

        // The Deeper editor and the Session editor are panel tabs: the panel comes up on them.
        T("deepereditor", "features/deeper_editor.png", "✎", Tile(0x7B, 0x5C, 0xFF), Always,
            () => LauncherHost.OpenPanelTab("deeper"), Never);
        T("sessioneditor", null, "▤", Tile(0xFF, 0x69, 0xB4), Always,
            () => LauncherHost.OpenPanelTab("presets"), Never);
        // Mods are made from the Mod Manager, a modal the panel owns: panel first, then the manager.
        T("modcreator", null, "✂", Tile(0xFF, 0xB3, 0x6B), Always,
            () => LauncherHost.OpenPanelModManager(), Never);
        T("justdrop", "features/justdrop.png", "▼", Tile(0x8C, 0xF5, 0xC8),
            () => JustDrop.JustDropService.DoorAvailable,
            () => JustDrop.JustDropHostService.LaunchShop(),
            () => JustDrop.JustDropHostService.IsActive);
        T("loom", "features/loom.png", "◎", Tile(0x55, 0xB7, 0xFF), Always,
            () => Chaos.LoomHostService.Launch(),
            () => Chaos.LoomHostService.IsActive);

        return list;
    }
}
