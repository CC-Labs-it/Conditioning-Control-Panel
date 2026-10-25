# Avalonia port: progress log

One entry per stack branch, oldest first. Line counts are added/removed against the
branch's predecessor. "Not run" lists anything that was compiled but not executed.

## avalonia-port/main-20260928 (#1762)
- Merge of WPF main 0ff4583 (upstream sync, exempt from the line cap). Conflict resolutions only.
- Not run: WPF tests (Windows-only).

## avalonia-port/main-20260928-compat (#1763): +20/-20
- Ported main's new Chaster/asset-folder code onto the Core split; fixed new WPF tests' paths.
- Evidence: 9 Linux test projects, LinuxSmoke, --smoke, core-guards green.
- Not run: WPF tests.

## avalonia-port/net10 (#1764): +43/-48
- Every project on net10.0; CI on the 10.0.x SDK only.
- Evidence: all builds; 9 Linux test projects green natively on .NET 10.0.12.
- Not run: WPF tests, self-contained WPF publish, installer.

## avalonia-port/keincheck-mcp (#1765): +17/-1
- Keincheck MCP server embedded in Debug builds.
- Evidence: live app answers MCP; list_windows returns the shell window.

## avalonia-port/parity-ledger
- Adds docs/avalonia-parity.md (362 rows from five scout passes over the WPF head), plus this
  progress log and docs/avalonia-decisions.md.
- Starting totals: 59 missing, 247 stub, 56 wired, 0 verified.
- Evidence: statuses read from source only; nothing verified yet. Reviewer spot-check: 20/20
  statuses correct, 2 uncovered theme files added, stub/wired rule tightened.

## avalonia-port/core-pure-moves-1: +2/-2
- Moves 6 portable WPF-head files into CCP.Core: PresetFileService, QuizSessionGenerator
  (App.Mods -> CoreMods, same fallback), TimelineSession, SkillTree, DescentBarkPolicy,
  DescentStageCopy. No ledger row changes status yet; this unblocks Avalonia wiring for
  shell-preset-io, win-quiz, win-session-editor, views-tab-enhancements and the Descent windows.
- Skipped (need head-only types first, next branch): SettingsPaletteIndex,
  EnhancementImportRules, MandatoryVideoEnhancementScanner, DescentCeremonyCopy, DescentFuseCopy.
- Evidence: Core/Avalonia/WPF-tests builds, core-guards, Core + 6 Avalonia test projects,
  LinuxSmoke, --smoke all green. Reviewer: ACCEPT (no P0-P2).
- Not run: WPF tests (Windows-only).

## avalonia-port/core-pure-moves-2: +165
- Moves the five files the previous branch skipped: SettingsPaletteIndex, EnhancementImportRules,
  MandatoryVideoEnhancementScanner (with EnhancementResolver and Diag), DescentCeremonyCopy,
  DescentFuseCopy (with DescentMigrationChoices, DescentCycleXp and DescentFusePhase).
- Two fail-safe Core hooks seeded by the WPF App static ctor: EnhancementResolver.LibraryMatchProvider
  (unset = no library match) and SettingsPaletteIndex.JustDropDoorAvailableProvider (unset/throwing = false).
- Evidence: CoreHeadProviderTests (unset/live/fail-closed, plus provider arguments); Core 62/62,
  6 Avalonia test projects, core-guards, LinuxSmoke, --smoke green. Reviewer: ACCEPT (P3 notes only).
- Follow-up (next branch): Avalonia still uses local stand-ins for these types
  (SettingsPaletteWindow, DescentFuse/CeremonyWindow, SpiralTabView, EnhancementPlayerWindow).
- Not run: WPF tests (Windows-only).

## avalonia-port/wire-core-standins-1: +332
- Deletes the Avalonia stand-ins for types #1768/#1769 moved to Core: DescentFuseWindow and
  SpiralTabView read DescentFuseCopy; DescentCeremonyWindow reads DescentCeremonyCopy and
  DescentMigrationChoices (only the Offer stand-in stays: DescentMigrationOffer is head-only);
  SettingsPaletteWindow lists SettingsPaletteIndex.Search and navigates (ShowTab, FocusSection,
  2s glow), and the rail's search pill opens it; EnhancementPlayerWindow calls EnhancementResolver.
- Ledger: win-settings-palette stub -> wired. shell-nav-rail, win-descent-fuse,
  win-descent-ceremony, views-tab-spiral, views-deeper-enhancement-player stay stub (notes updated).
- Evidence: Tests/CCP.Avalonia.Tests/CoreStandInTests.cs (5 cases); Keincheck palette open,
  "volume" query and Enter -> Settings > Audio, spiral fog text, under
  ~/ccp-port/evidence/wire-core-standins-1/.
- Not run: WPF tests (Windows-only); Descent windows and enhancement auto-load have no user path
  Keincheck can drive.

## avalonia-port/wire-core-standins-2: +593
- Wires the second batch of #1768 Core types: PresetsTabView lists Core presets and exports /
  drop-imports through PresetFileService; EnhancementsTabView draws SkillDefinition.All (read-only,
  SkillTreeService is head-only); SessionEditorWindow drops its EditorSession stand-in for
  TimelineSession and imports/exports via SessionFileService; QuizWindow builds its session with
  QuizSessionGenerator (fallback content; QuizService AI is head-only).
- Ledger: shell-preset-io, views-tab-presets, views-tab-enhancements, win-session-editor, win-quiz
  stay stub (notes updated: share/CRUD, purchasing, and no opener for the two windows).
- Evidence: Tests/CCP.Avalonia.Tests/CoreStandInTests.cs (+4 cases, each checked to fail when its
  surface is broken); Keincheck Presets rail and Enhancements tree under
  ~/ccp-port/evidence/wire-core-standins-2/.
- Not run: WPF tests (Windows-only); preset export/drop and chip clicks live (Keincheck click_at
  hit-tests fall through to the window backdrop Panel); SessionEditorWindow and QuizWindow have no
  user path.

## avalonia-port/audio-libvlc: +403
- Real one-shot audio: LibVLCSharp 3.10.1 (VideoLAN.LibVLC.Windows 3.0.24 on Windows only; Linux
  uses system libvlc). `Platform/LibVlcAudio.cs` seeds `CoreAudio` in App.axaml.cs only if LibVLC
  initialises. Duck/Unduck on Linux via `pactl` on other apps' sink-inputs (ref count + generation
  as WPF); Windows ducking left unseeded (WASAPI port later).
- Start latency measured 8-22 ms (dummy and PipeWire), so no warm pool.
- New `--audio-probe` (real output, pactl view, duck/unduck check); CI installs libvlc for `--smoke`.
- Ledger: shell-audio-playback-devices stays stub (notes updated). UI sound callers remain silent:
  the head does not link `Assets/sounds` yet (blocker, separate branch); no reachable UI calls Duck.
- Evidence: ~/ccp-port/evidence/audio-libvlc/.
- Review fixes: volume mapped linear->cubic (0.5 plays at ~-6 dB like WPF); exit force-unducks
  other apps; a vanished stream no longer aborts a duck sweep; streams named CCP in pactl; no
  duck at master volume 0. `CoreAudioTests` now cover ref count, generation, force-unduck and
  Shutdown with pactl stubbed. ModCreator's audio preview and AwarenessPresetDetailDialog's
  keyword preview now play user-picked files.

## avalonia-port/head-sounds: +139
- `CCP.Avalonia.csproj` links `Assets/sounds/**` as Content at WPF's logical path `Resources/sounds`
  with WPF's `$(ContentPackSoundsExclude)` set verbatim (flashes_audio audio and the three builtin-*
  mod payloads ship as content packs). Replaces the lone `assets/sounds/faucet_charge_drop.wav`
  link; `--smoke` and `--audio-probe` now read the clip from `Resources/sounds`.
- Output: 145 files, 10,997,611 bytes: the set WPF's Include/Exclude globs select from `Assets/sounds`.
  WPF's own output is larger (254 files) because its temporary `$(CirceNeutralInBoxOverride)` re-adds
  builtin-locked clips in-box; that is deliberately not mirrored (no pack downloader or bark path here).
  bin/Release/net10.0 677,483,550 -> 688,460,831 bytes.
- `SharedSoundsPackagingTests`: every file a PlayOneShot caller names exists under BaseDirectory, and
  no content-pack audio ships while the mod manifests do. Proven to fail (excluded `chaos/ui_click.mp3`;
  planted an mp3 under builtin-locked; removed builtin-locked/bark_rules.json).
- Ledger: nothing goes stub->wired. No sound caller is reachable: PopQuizWindow, QuizWindow,
  SessionCompleteWindow, ChaosOverlayWindow and BubbleCountWindow.ShowOnAllMonitors have no
  production opener, and AvatarTube's GigglePriority is only called from the render ctor, with playSound:false. Notes updated.
- Evidence: ~/ccp-port/evidence/head-sounds/ (live `--audio-probe` with pactl capture).
- Review fixes: the exclude list moved to repo-root `ContentPackSounds.props`, imported by both heads
  (WPF `Resources/sounds` output file list diffed before/after: identical, 254 files); sounds Content
  gets `ExcludeFromSingleFile`. Live UI-sound Keincheck run: not run (no reachable caller); the
  `--audio-probe` run stands in for playback only.

## avalonia-port/core-asset-import: +218
- Phase 2 units 2+3: AssetImportService, AssetExtensionRepair, AssetPresetService, SessionLogService and
  Models/SessionLog move to CCP.Core (renames). App.Logger -> Serilog Log.
- Seams: App.EnsureCustomAssetsDirectories became CorePaths.EnsureCustomAssetsDirectories(customPath)
  (same body; WPF startup and the import both call it, #391). SessionLogService no longer subscribes to
  App.Flash/App.Video; FlashService and VideoService (LibVLC + browser) call RecordImages/RecordVideo
  right after raising FlashDisplayed/VideoStarted, which is where the old handlers ran. Both no-op
  without an active session, as the unsubscribed handlers did.
- Evidence: Tests/CCP.Core.Tests/AssetImportAndSessionLogCoreTests.cs (custom dirs before fallback,
  JSON round-trip against Fixtures/session_log_premove.json generated from the pre-move model,
  RecordMedia append rules); each failed once when its behaviour was broken.
- Not run: WPF tests (Windows-only); no Avalonia wiring, so no Keincheck run.

## avalonia-port/wire-session-surfaces: +396
- Session editor opens from Presets Create New and row ✎ and saves through Core SessionManager
  (built-in -> new custom, custom -> overwrite; save failures logged and shown in an error dialog).
- Recent Sessions lists Core SessionLogService.LoadRecentLogs() instead of fabricated rows and reopens
  SessionCompleteWindow, which now takes Core SessionLog (Recap/MediaKind stand-ins deleted).
- Gap: no Avalonia session engine, so the end-of-session recap open (WPF Presets.cs:1788) stays unwired.
- Evidence: SessionSurfacesTests (4, each fail-proofed); CCP.Avalonia.Tests now runs in a temp
  CCP_USERDATA_DIR; Keincheck text dumps under evidence/avalonia-port/wire-session-surfaces/.
- Ledger: win-session-editor, win-session-log-history, win-session-complete stub -> wired;
  shell-session-io stays stub with an updated note.
- Not run: file-picker save live; WPF tests.

## avalonia-port/wire-programs-roadmap-awareness: +507
- Programs intro opens on the first Programs visit (CoreSettings flags, Core built-in programs and
  CoreMods; ProgramService is never constructed). Enrol stays disabled: no run panel or session runner here.
- RoadmapStepPopup and the track/badge messages open from RoadmapService.StepCompleted on the shell's
  single roadmap instance (subscription covered by a Language test).
- Awareness preset grid ported; built-in presets linked from Assets/AwarenessPresets like WPF. Cards,
  "+ New Preset" and the advanced link open AwarenessPresetDetailDialog (seen live). Tour hook gated on the
  Awareness tour via new CoreTutorial.CurrentTourName; it never fires until this head has a tutorial runner.
  The Awareness nav button now shows its tab.
- Evidence: ProgramsRoadmapAwarenessOpenersTests (4) + LanguageSelectorTests roadmap assertion, each
  fail-proofed; evidence/avalonia-port/wire-programs-roadmap-awareness/. Two review rounds.
- Not run: WPF tests; Chaster confirm on activate (not ported; lock time is declined, the safe direction).

## avalonia-port/x11-override-redirect: +203
- X11Overlay.SetOverrideRedirect(window, bounds): XChangeWindowAttributes(CWOverrideRedirect) + XSync on the
  overlay's own connection, applied before Show() (Avalonia 12.1.2 creates the XID in the Window ctor and maps in
  Show; measured), THEN Position/Width/Height - a configure issued before override-redirect is redirected to the
  WM, which can replay a stale 537x358 after map (was 14/25 FAIL on non-primary screens, now 25/25 PASS).
  Returns false on non-XID windows, 32-bit processes, or an X error from the server.
- X11 backend pinned on Linux (UseX11 after UsePlatformDetect); "X11 or Wayland" comment corrected.
- --overlay-check: per screen, a transparent click-through override-redirect overlay read back from the X
  server (map_state, override_redirect, depth 32, empty input shape, geometry == Screens).
- Evidence: 25 consecutive PASS live on this box (XDG_SESSION_TYPE=wayland via XWayland, 3 screens at 1.79
  scaling; overlay-check-loop-25.txt, before-fix baseline 11/25) and in a nested kwin; exit 1 with
  override-redirect skipped (overlay-check-failproof-no-override-redirect.txt). Existing x11-overlay-probe.sh still passes.
- Ledger: shell-clickthrough-overlays notes updated; stays stub until a feature uses the mechanism.
- Follow-up: X11Overlay's process-wide XSetErrorHandler replaces Avalonia's own handler (pre-existing).
- Not run: in CI (needs a display); Windows (no Win32 shim yet).

## avalonia-port/layered-audio: +395
- LayeredAudio (head-side port of WPF LayeredAudioService on LibVLC): start/restart/stop rules, ignore-master
  path for audio-only sessions, live track/master volume, ducked by other CCP sounds, true infinite loop
  (restart on end-of-file), synchronous bounded shutdown on exit.
- Audio Layers window opens from Settings > Audio and Home, single instance, open failure shown like WPF.
- Volume sticks per stream even when PipeWire restores by app name (bounded re-apply after Playing).
- Evidence: LayeredAudioTests (fail-proofed); --layers-probe with exact per-stream values (15/15 PASS in a
  supervisor re-run); live Keincheck run with a temp profile; 0 binding errors. Two review rounds.
- Ledger: win-layered-audio verified; shell-home-audio, shell-audio-playback-devices, views-settings-audio updated.
- Gaps: output-device selection (this head has no device picker yet); audio-only session start (no session engine).

## avalonia-port/overlay-flashes: +660/-275
- Image flashes as click-through override-redirect overlays, one window per image, WPF placement
  (FlashPlacement moved to Core; the WPF FlashService now calls it, logic unchanged), WPF timing
  (1 s delay, 300 ms stagger, lifetime FlashDuration + 1 s, linear 1/32 fade at WPF's rate), images decoded
  at display size, compositor-side fade via _NET_WM_WINDOW_OPACITY, busy guard like WPF.
- Manual trigger: head-only "▶ Test" on the Flash card (decision logged); the session scheduler that fires
  flashes is the next unit.
- Evidence: FlashOverlayPlacementTests (fail-proofed); X readback of 5 overlays (override-redirect, depth 32,
  empty input shape), timeline 0.28-0.29 s stagger, 6.40 s lifetime; KWin opacity ramp. Two review rounds.
- Not run: a physical click through an overlay (no xdotool), a desktop screenshot over another app (session
  locked), non-compositing X11 WMs (no fade there), Windows (no Win32 shim yet).

## avalonia-port/session-scheduler-core: +154
- CoreFlash: WPF's ambient flash rhythm in Core (3600/FlashFrequency s ±30%, min 3 s; skip when busy or a
  display change is settling). WPF FlashService now calls CoreFlash.NextIntervalSeconds (bit-identical).
- Avalonia: the Flash card's Enable toggle arms/disarms the schedule; the Per Hour slider re-rolls like
  WPF RefreshSchedule. Differences (engine Start/Stop not ported, DND guard, launch arming, Stop leaving
  bursts to finish) recorded in the decisions log and the feat-flash row.
- Evidence: CoreFlashScheduleTests (11, fail-proofed); live with a temp profile at 180/h: 4 automatic bursts
  at 24.2/20.0/21.7 s gaps, none in 75 s after disabling; 0 binding errors. Review: ACCEPT.

## avalonia-port/win32-overlay-shim: +260
- Win32 click-through topmost overlays behind the same overlay entry points (dispatch by platform handle:
  XID -> X11, HWND -> Win32): WPF's style bits (LAYERED|TOOLWINDOW, TRANSPARENT+NOACTIVATE only while
  click-through), HWND_TOPMOST via SetWindowPos, opacity via SetLayeredWindowAttributes, bits kept across
  Avalonia style rebuilds via Win32Properties.AddWindowStylesCallback.
- --overlay-check gains a Windows readback (style bits, visibility, rect, and a CAPTUREBLT pixel read proving
  the overlay draws); new advisory CI step on the windows job (continue-on-error until its first green run).
- Evidence: OverlayBackendDispatchTests (fail-proofed); Linux --overlay-check still PASS. Two review rounds.
- Not run: anything on Windows locally; the CI step is the only proof. DPI risk on mixed-scale monitors noted.

## avalonia-port/video-miniplayer: +444
- LibVLC video on Avalonia 12 via video callbacks into a WriteableBitmap (LibVLCSharp.Avalonia 3.10.1 only
  supports Avalonia 11; decision logged). One shared LibVLC for audio and video; audio media opt out of video.
- MiniPlayerWindow plays real local video with WPF's controls (silent preview, loop, play/pause/Space, seek,
  ±5 s, Esc, drag, full teardown); slider seek bug fixed.
- --video-check <file>: opens the real window, asserts frames change, seek lands, pause freezes, close frees.
- Evidence: MiniPlayerVideoTests + --video-check (fail-proofed: skipped Play -> exit 1; no teardown -> crash);
  screenshot on the real desktop. Review: ACCEPT.
- Gaps: no production opener (Assets tree held back on content packs), animated GIFs show one frame.

## avalonia-port/overlay-text-families: +746
- Subliminals and bouncing text as click-through overlays on the X11/Win32 mechanism, driven by the existing
  Core schedules/engine, with WPF's look and timing (subliminal envelope from the first painted frame;
  bouncing text per-frame loop with WPF's dt clamp, only the screens near the logo repainted).
- Card toggles arm the features (same decision as flashes). Closing the shell now stops every overlay
  and schedule (was: a UI-less process kept overlays up).
- Evidence: TextOverlayTests (4, fail-proofed); live timelines and X content grabs; shell-close proof with a
  negative control; frame stats 145-155 fps after the per-screen repaint fix. Two review rounds.
- Gaps: audio/haptics/XP hooks, engine gating, video pause, topmost re-assert on Windows.

## avalonia-port/achievement-read-rules: +553
- oracle-deep achievements unit 1: Core AchievementRules (single copy of WPF's thresholds), Achievement and
  AchievementMeter moved to Core, AchievementProgress split (data + pure statics in Core; App-dependent
  streak methods as head-side extension methods, call sites unchanged). Persisted shape unchanged.
- Decisions logged: achievements.json path, Avalonia local-only until auth, streak read-only until skill-tree seams.
- Evidence: AchievementProgressShapeTests pins every persisted property (name, type, order, loads back, no
  converter, double.MaxValue default), fail-proofed. Review: ACCEPT after the test hardening.
- Not run: WPF tests (Windows CI). No Avalonia surface yet.

## avalonia-port/notifications: +254
- In-app corner toasts ported from WPF NotificationService (look, 5 s default, 180/220 ms fades, stacking,
  replay before the host exists, action button, dismiss) into the shell's existing NotificationHost.
- Seeds CoreProgram.NotifyProvider and CoreEntitlement.ShowDeniedHandler like WPF: tier refusals show a toast
  whose "See tiers" opens Settings > Account; the browser's offline block toast is wired too.
- Evidence: NotificationServiceTests (fail-proofed three ways); live: Play > Gaze Minigame Open shows the
  refusal toast, "See tiers" opens Account, x and expiry dismiss; 0 binding errors. Review: FIX -> fixed.
- Gaps: tray balloons (OS notifications, later unit), sticky toasts, Patreon reconnect branch.

## avalonia-port/achievement-store: +392
- Core AchievementStore: WPF's achievements.json load/save lifted verbatim (System.Text.Json indented, UTF-8
  BOM, .tmp -> File.Replace with .bak, .bak fallback, one save lock); WPF AchievementService delegates to it.
- Golden fixture written by the pre-change serializer; tests: byte-identical round trip, fixture covers exactly
  the persisted properties, truncated file recovers from .bak, parsed equality, time-zone behaviour pinned.
  Each fail-proofed. Tests (a)/(e) need TZ pinning and run on the Linux CI job. Review: ACCEPT.

## avalonia-port/tray-icon: +305
- Tray icon (Avalonia TrayIcon; SNI on Linux) with WPF's menu (Show, Wake, separator, Exit) plus Stop everything,
  the no-hotkey panic control: stops flash/subliminal/bouncing text, unticks their Enable flags, keeps the app.
- X hides to tray like WPF (always), hides/restores the avatar tube; with no tray host (no StatusNotifierWatcher
  on the session bus) X really exits so overlays can never outlive a reachable UI. Exit uses Shutdown().
- Decision logged: the icon is always visible on this head (it is the panic control).
- Evidence: ShellTrayTests (2, fail-proofed three ways); live D-Bus: SNI registered, menu layout, Stop everything
  3 overlays -> 0, hide/show/exit; no-watcher dbus-run-session -> X exits. Review: FIX -> fixed.
- Gaps: OS balloons (org.freedesktop.Notifications), Lockdown refusal/exit bill on Exit, off-screen repair.

## avalonia-port/achievement-engine-core: +368
- oracle-deep achievements unit 3: Core AchievementEngine owns progress, the store, TryUnlock (WPF order: unlock ->
  save -> log -> suppress gate -> event once), dirty/autosave/retry, Reset, SuppressPopups and exclusive gating via
  CoreEntitlement. WPF AchievementService delegates; timers, Track* counters, forwarding and UI marshalling stay.
- The current progress is resolved inside the store's save lock (a queued autosave can't resurrect a logged-out
  account's progress after Reset).
- Evidence: AchievementEngineTests (9, each fail-proofed, incl. the Reset race). Review: ACCEPT + P2 race fixed.
- Not run: WPF tests (Windows CI). No Avalonia surface yet (unit 5).

## avalonia-port/achievements-wire: +238
- The Avalonia head creates the Core AchievementEngine (~/.config achievements.json), seeds
  CoreProgram.UnlockAchievementProvider and CoreProgression.TrackBubbleCountResultProvider like WPF, shows
  AchievementPopup per unlock on the UI thread, and saves on exit only when dirty. Free/patron counts and the
  bubble-count streak moved into the engine (WPF delegates). The Achievements tab reads live counts.
- Local-only (decisions log): no sync, no streak writes, no ResetProgress. Test apps use a sandbox path, so no
  test can touch a real achievements.json (proven: the real file is still absent after both desktop suites).
- Fixed on the way: a closed shell kept answering language changes (flaky SpiralHelpPopoverTests).
- Evidence: tab renders 3/50 from a sandbox fixture; sha256 unchanged after a 60 s idle session; tests
  fail-proofed (idle exit writes nothing, dirty exit persists, premium/free counts with literal numbers).
- Gaps: card grid, ItemUnlockedPopup (WardrobeCatalog is WPF-only), no user path reaches an unlock yet.

## avalonia-port/panic-hotkey: +873
- Panic key on Linux. PanicPolicy moved to Core (100% rename). X11/XWayland focus: XInput2 raw key presses on
  the root window (non-consuming, modifier-blind = WPF's low-level hook; layout changes re-resolve the keycode).
  WPF's press handling: lock card and Ctrl+K palette rungs first, then tray Stop everything + lock cards; a second
  press within 2 s exits. Devices rebind wired (an abandoned rebind cancels when the window loses focus).
- Wayland-native focus: GlobalShortcuts portal bound only while effects run (decision C, oracle-deep); fallback
  with a one-time notice when the portal is missing/refused/unanswered (proven live: KDE dialog unanswered -> fallback).
  D-Bus bug found and fixed (MessageWriter struct passed by value -> invalid body -> bus disconnect).
- Discovery: KWin routes Xwayland's XTest through libei, so synthetic presses can't prove anything on the live
  desktop; scripts/panic-check.sh proves the X path in a throwaway kwin --virtual + standalone Xwayland (fail-proven).
- Evidence: PanicKeyTests (5, fail-proofed), --panic-check, --portal-check, --tray-probe; two review rounds.
- Not run: an accepted portal bind + Activated, a physical key press on the live desktop, Windows.

## avalonia-port/quests-core: +456
- Core-extraction unit 4: Quest, QuestProgress and QuestDefinitionService moved to Core (App.Logger -> Log,
  AppVersion via CoreReleaseContent, mod-art resolution via CoreModArt). Reroll methods stay head-side as
  extension methods. One guard allowlist entry: the pack:// root that the saved cache format still requires.
- quests.json and quest_definitions_cache.json stay byte-identical (golden fixtures from the old code, shape +
  round-trip tests, fail-proofed); built-in quest fallback unchanged. Review: ACCEPT.

## avalonia-port/os-notifications: +182
- OS notifications on Linux via org.freedesktop.Notifications (own D-Bus connection; app name + per-user PNG
  icon; transient like a balloon; click restores the window). Wired to WPF's one-time "minimized to tray" hint.
  No notification server -> in-app toast if the window is visible, else drop + log once; Windows falls back.
- Evidence: OsNotificationsTests (fail-proofed); --notify-check live; dbus-monitor capture of the in-app Notify
  with the icon path. Review: ACCEPT + fixes. Other WPF balloon callers await their features on this head.

## avalonia-port/skilltree-service: +137
- Skill-tree ownership rules (HasSkill, CanPurchaseSkill, IsSecretSkillAvailable, sparkle tier, free rerolls,
  reroll bonus) moved from WPF SkillTreeService to Core SkillTreeRules (logic identical, reviewed); WPF delegates.
- Avalonia Enhancements reads owned state through it. Purchasing stays unavailable (auth not ported); no streak
  or skill writes on this head.
- Evidence: SkillTreeRulesTests (thresholds at the boundary), EnhancementsTabDrawsOwnedSkillAsOwned, fail-proofed;
  Keincheck with a sandbox profile owning 2 skills; 0 binding errors. Review: ACCEPT.

## avalonia-port/mods-core-prereqs: +247
- One app version: root Version.props (6.10.3) imported by WPF, Core and Avalonia; WPF stamp unchanged; Avalonia
  no longer reports 1.0.0 (blocked mod MinAppVersion checks), Core no longer 6.9.0. Decision logged.
- ModService unit 1 (oracle-deep plan): PersonaSwitchRules, CompanionContentResolver, ModCompanionContent,
  CountMediaFiles and the Hypnotube default links moved to Core with identical behaviour; seven CoreModsHooks and
  the CoreSession phrase-pool delegates added and seeded lazily in WPF (callers switch over in unit 3).
- Evidence: companion-content tests now run on Linux; new seam test fail-proofed; version before/after. Review: ACCEPT.

## avalonia-port/mods-golden-fixtures: +625
- ModService unit 2: golden fixtures pin the mod persistence formats BEFORE ModService moves to Core. Core tests
  round-trip every mod key in settings.json through SettingsService and a full mod.json; the WPF suite pins pack.json
  and a behaviour golden for Initialize(bambi) -> Activate(sissy) -> Activate(bambi), incl. pool order.
- The WPF suite now always runs in its own temp profile (never a developer's exported one). Each test fail-proofed.
- Not run: the behaviour golden against the real WPF build (Windows CI is its first real run). Review: ACCEPT + P2 fixed.

## avalonia-port/modservice-to-core: +59
- ModService unit 3: commit A swaps every head-only dependency for Core seams (one hook per call site, same order
  and try/catch; reviewed call site by call site). The pool backup reads the session through ONE snapshot delegate,
  so a StopSession between reads can't bring back #906. Commit B: pure git mv of ModService.cs and
  CirceNeutralPackPatch.cs (a dependency the plan missed) into CCP.Core.
- Evidence: the mod behaviour golden (#1799, green on Windows CI against the real WPF build) is reproduced by the
  moved Core ModService in a Linux harness; broken once (mismatch) and restored. Review: ACCEPT.

## avalonia-port/modservice-core-tests: +285
- ModService unit 4: the activation golden runs in CCP.Core.Tests on Linux (WPF copy removed; nothing seam-specific
  lost). New Core tests install a .ccpmod built in the test (backslash entries, traversal, reserved id, MinAppVersion
  above/below) and an uninstall that deletes only mods/<id>; LinuxSmoke gains an Initialize + ActivateMod check.
- Real Linux defect found and fixed: Windows-zipped .ccpmod entries with backslashes installed as flat files. The
  custom extraction runs off Windows only; WPF keeps .NET's ExtractToDirectory exactly. Each test fail-proofed.
  Same defect remains in ContentPackService, ReleaseContentService and the Avalonia ModCreatorWindow (follow-up).

## avalonia-port/mod-art-core-resolution: +199
- ModService unit 6: mod-art path resolution (event skin -> active mod, '..' and rooted paths rejected, .wav/.mp3
  twin) moved from WPF ModResourceResolver into CoreModArt; WPF calls it with caches and public API unchanged.
  Off Windows, lookups ignore case and find backslash-named files from .ccpmod extraction; Windows keeps File.Exists.
- Avalonia seeds CoreModArt in the desktop lifetime (effective once unit 5 creates ModService).
- Evidence: CoreModArtResolutionTests (case, backslash files, traversal incl. rooted skin paths, order), fail-proofed;
  test sandbox asserted. Review: ACCEPT. Follow-ups: cache the Linux probe when mods go live; case-variant folder clashes.

## avalonia-port/ccpmod-backslash-extraction: +54
- One Core helper CcpmodArchive.Extract for every .ccpmod/zip extraction of mod content (ModService, ContentPackService x2,
  ReleaseContentService, both ModCreatorWindow Load paths). Windows: passthrough to ZipFile.ExtractToDirectory with the
  same arguments (identical at every caller, reviewed). Elsewhere: Windows-zipped backslash entries land in folders;
  traversal still rejected; the target folder is created like the framework does. Test fail-proofed. Review: ACCEPT.

## avalonia-port/avalonia-seed-mods: +274
- ModService unit 5: the Avalonia head runs one ModService (desktop lifetime only, after the version seed),
  Initialize(settings.ActiveModId) like WPF, and seeds CoreMods through a new CoreMods.Attach that WPF now also uses
  (same 16 providers, reviewed). Mod-dependent text follows the active mod (live: "Welcome, Good Girl.").
- The Hypnotube known-title table moved to Core, so a legacy-link migration saves canonical titles on either head.
- Evidence: StartModsTests (golden round trip through the head path, fail-proofed); legacy-link migration test with and
  without the WPF provider; live sandbox launch. Review: ACCEPT + P2 fixed. No real profile touched.

## avalonia-port/dialogs-mod-manager-wired: +110
- ModService unit 7: the mod manager activates (+ saves ActiveModId), uninstalls (resets the active mod), installs a
  .ccpmod (picker -> InstallModAsync, WPF messages) and exports; refreshes on ModAvailabilityChanged; opened from the
  shell's Manage Mods. Share hidden until the catalogue client exists.
- Evidence: activate-persists test (fail-proofed); live sandbox: activate -> relaunch still active -> uninstall.
- Not run live: install/export through the native file picker. No live re-theme after a switch (next launch).

## avalonia-port/dialogs-mod-picker-wired: +148
- ModService unit 8: ModPickerDialog.ShowIfNeeded runs WPF's show rules in WPF's order and is called at shell startup on
  the upgrade path (never alongside the first-run wizard). This head has no content-pack service yet (oracle §5), so the
  picker never opens here, like WPF without ReleaseContent (decision logged; it waits for the pack service). The Avalonia
  ModPacks copy is deleted in favour of Core ModService.PackIdForMod (identical mapping).
- Evidence: ShowIfNeeded rule tests (fail-proofed); live sandbox upgrader profile: no picker, settings untouched.

## avalonia-port/engine-start-stop: +402
- Session runner U1: Core CoreEngine mirrors WPF StartEngine/StopEngine (flash always; subliminal, lock-card schedule and
  bouncing text per saved flag; all four skipped under audio-only; head stop hook closes overlays and lock cards). The
  shell Start/Stop drives it; card and wall toggles save while stopped and apply live while running. Tray Stop, panic
  and closing the shell stop the engine and keep saved flags. The session feature lock reads the new
  CoreSession.IsSessionRunning, so a plain Start never greys cards. Decision row replaces arm-from-toggle and tray rows.
- Evidence: CoreEngineTests (arming matrix incl. audio-only), tray and panic tests (fail-proofed); live sandbox: 0 overlays
  on relaunch, Start shows all three, Stop and tray Stop clear them in about 0.1 s with flags On; panic-check PASS, FAIL
  when broken. Review: FIX -> round 1 applied (audio-only, shell close, test cleanup).
- Not run live: a physical panic key on KWin; closing the shell with lock cards up. Stubbed: session stop dialog, Jump
  right in, audio-only audio bed, ramp and scheduler.

## avalonia-port/session-core-pure: +985
- Session runner U2: Core ports (not moves) WPF SessionEngine's pure parts: SessionClock, SessionXp, phase index,
  deferred starts, SessionSettingsSnapshot (WPF's 36 fields), PhrasePoolCustody (FoldUserPoolEdit verbatim). D1/D1a fix
  WPF mod-switch-mid-session pool leaks (D1a in Core ModService, so WPF too, only while a session runs). 50 Core tests
  (fail-proofed); --smoke checks the pool delegates are seeded. Review: FIX -> D1a (oracle-deep) -> ACCEPT. No user path yet.

## avalonia-port/session-runner-core: +425
- Session runner U3a: Core SessionRunner ports WPF SessionEngine Start/Stop/Tick for flash, subliminal (frames, whispers
  flag), bouncing text and lock cards: deferred starts (lock cards get the remaining window, #736), phase index, settings
  snapshot + pool custody restore, engine start/stop, WPF-format session log (XPEarned = banked XP, 0 until progression is
  ported). The session-start ledger is saved before the overrides apply, so a crash never persists session values.
  SubliminalDuration no longer leaks after a session (WPF bug, logged). No user path yet (U3b).
- Evidence: SessionRunnerTests (1-minute run, settings + log goldens, early stop, <30 s no file, deferred starts, ledger
  on disk before overrides), each fail-proofed. Review: ACCEPT, follow-ups applied.

## avalonia-port/secrets-linux: +348
- Auth unit 1: CoreSecrets provider for the Avalonia head: libsecret (*v_sync P/Invoke) on Linux, WPF's exact DPAPI files and
  entropy on Windows, memory-only + a one-time "not remembered" flag without a Secret Service (never plaintext). Seeded
  before SettingsService. Nothing reads secrets yet (login is a later unit).
- Evidence: scripts/secrets-roundtrip.sh runs a throwaway gnome-keyring in Docker: round trip + no-bus/no-file, both
  fail-proofed (and an empty test filter fails). DPAPI WPF-bytes test runs on Windows CI. The real KWallet was never touched.

## avalonia-port/session-runner-wire: OPEN perf finding
- Subliminal cards stay up 1.1-3.1 s instead of 0.23 s when flash and bouncing text run together. It is intermittent. It is not
  dispatcher priority: Render priority on the overlay RunOnce calls was measured and reverted. The suspect is the card's first
  frame arriving late (SubliminalOverlayWindow.Run starts its clock in RequestAnimationFrame). This fails "at least as fast";
  a perf branch follows. Evidence ~/ccp-port/evidence/avalonia-port/session-runner-wire/p2-*.txt.

## avalonia-port/session-runner-wire: +295
- Session runner U3b: the head owns one Core SessionRunner (App.Sessions), started from the Presets session rack with
  WPF's confirm. WPF clock labels on the Start and session buttons, WPF's stop-session confirms, the recap opens by itself
  (Completed / Ended Early), each flash shown is logged, the feature lock greys cards during a session. Tray Stop and
  panic end a session early (pause is U4). SessionClockLabel moved to Core unchanged. Decision: runs only the ported subset.
- Evidence: SessionRunWireTests (fail-proofed); live sandbox 2-minute session: overlays on time, bouncing text at 1:00,
  recap "Completed, 02:00, 5 images" == 5 flash windows and the log on disk, settings.json unchanged except TotalSessions
  and the ledger; early Stop -> Ended Early. Review: ACCEPT.

## avalonia-port/v2-client-core: +731
- Auth unit 2: the V2 HTTP client and its data types moved to Core (CCP.Core/Services/Account/V2AuthService.cs) and the
  XP-curve maths to Core (XpCurve); WPF calls into both (ProgressionService wrappers, ApplyUserDataToSettings stays in WPF
  until unit 6, MergedRecovery hook). No server contract change. Decision: the V2 client and OAuth live in Core.
- Evidence: XpCurveGoldenTests (113 values from the pre-move code), V2AuthServiceWireTests (fake handler: method, URL,
  headers, body transcribed from the WPF source; error paths; tokens never logged), each fail-proofed. Review: ACCEPT.

## avalonia-port/overlay-first-frame-perf: +55
- Root cause of subliminal cards lingering 1-3 s: overlays sat at _NET_WM_WINDOW_OPACITY 0; KWin sends a fully transparent
  XWayland window no frame callbacks, so the vsync'd swap waited on XWayland's 1 s fallback and stalled every overlay and
  the UI thread. X11Overlay.OpacityCardinal floors opacity at 1/(2^32-1) (invisible); a backstop closes each card by
  hold + 200 ms after Show().
- Live 150 s flash + subliminal + bouncing text: card p95 3.06 -> 0.23 s; bouncing-text worst frame gap 2097 -> 62 ms,
  69-152 fps; flash 6.37-6.45 s (WPF 6.45 s). 2 fail-proofed tests. Review: ACCEPT. One unexplained all-overlays vanish at
  131.7 s in one run (log overwritten; suspected stray panic key; not reproduced).

## avalonia-port/loopback-oauth-core: +563
- Auth unit 3: one Core LoopbackOAuth helper (WPF's exact localhost:4783x/callback/, CSRF state, 5-minute timeout, browser
  pages, /…/token exchange); WPF Patreon/Discord/SubscribeStar delegate to it. Windows keeps HttpListener; elsewhere it
  binds 127.0.0.1 and ::1 (managed HttpListener bound only ::1 here). Stray requests (favicon, wrong Host) don't end a
  sign-in; a busy port fails before the browser opens. Decision: OAuth redirect on Linux = loopback.
- Evidence: 19 LoopbackOAuthTests (IPv4/IPv6, bad state, error=, timeout, busy port, stray requests, port reuse, page
  SHA-256 pinned to the WPF originals), each fail-proofed. No real provider sign-in (not provable here). Review: ACCEPT.
## avalonia-port/providers-patreon-substar: +1000 (auth 4a; Patreon/SubscribeStar lifecycle in Core, WPF delegates, Avalonia seeds CoreAccount fail-closed; SecretStore memory-only under CCP_USERDATA_DIR;
review FIX->r1 applied; one earlier live run was unsandboxed: real settings.json not written (mtime before run), no keyring items, two empty mods dirs created and left)

## avalonia-port/session-pause-panic-ramps: +389
- Session runner U4: Core SessionRunner Pause/Resume (clock frozen, 100 XP per pause in the award, Resume restarts only
  features past their start minute), the flash ramp (never reaches settings.json), pink tint (±3 min random start, ramp,
  hidden while paused). Head: pause button with WPF's confirm, panic and tray pause a running session then stop the
  engine (replaces the U3b interim), stop confirm shows the pause penalty.
- The head had no log sink: now stderr + a rolling file under UserData/logs (WPF's package/version), and every engine-stop
  path logs its trigger.
- Evidence: 4 Core tests + SessionRunWireTests (fail-proofed); panic-check.sh session phase PASS, FAIL when broken; live
  sandbox: tray Stop froze the clock, Resume brought back only started features, recap XP +0 (banking not ported; log
  line shows "paused 2x, penalty -200"). Review: ACCEPT, file sink added.

## avalonia-port/art-debt: +372
- Look parity from the ponytail-debt ledger (7 cheap + 23 stale items): shell door medallions, mod-aware hero/side plates
  for five feature cards, Blink Trainer, Graded Intake (side plate bound to the hero like WPF) and the AI permissions hero
  (one helper, ModArt.BindFeaturePlates, painted on attach, decoded once at 800 px), vault backdrop, dashboard card icons,
  first-run pass cards, achievements images (embedded like WPF), EmiBook placement via Core, memory-folder link.
- Evidence: renders of every touched view (all viewed), live sandbox shots of doors and a feature card, 0 binding errors;
  startup-to-shell 2.39-2.44 s before vs 2.42-2.47 s after (noise). FeaturePlateArtTests (fail-proofed).
- Skipped with notes: Remote Control grid art, rail chips, intake pass card, exclusives rim (hue shift not in Core),
  programs sigil (ProgramArt naming not in Core); doors don't follow the mod yet. Review: FIX -> r1 applied.

## avalonia-port/provider-discord: +648
- Auth unit 4b: Discord's token lifecycle (exchange, refresh, validate/identity, auth-token heal, whitelist grant, 24 h
  cache, logout) moved to Core DiscordAccount; WPF DiscordService delegates; token files/entropy/JSON unchanged. The
  Avalonia head seeds it fail-closed beside Patreon/SubscribeStar, all three started in parallel like WPF.
- Evidence: 14 Core tests + seed tests (fake proxy), each fail-proofed; sandboxed live check: Settings > Account signed
  out, 0 binding errors. No real Discord account (not provable here). Review: ACCEPT, follow-up tests added.
## avalonia-port/quiz-core-pure: +997 (quiz unit 1: WPF QuizService's offline half -> Core QuizStore, same files/JSON; WPF delegates; AI quiz stubbed this wave (decision);
goldens from pre-move code, fail-proofed 5 ways; review ACCEPT; stale quiz ponytail notes are reworded in quiz unit 5)

## avalonia-port/login-ui: +801 (auth unit 5)
- OAuth browser flow (authorize URL, PKCE, state) moved to Core; WPF delegates with identical URLs. Avalonia LoginDialog
  wired (providers, register/invite, password, same-email confirm, name check), restore-session check (Core), logout
  (tokens + identity; a failed clear is reported), one-time "sign-in not remembered" notice. Review FIX -> r1 -> ACCEPT.
- Live sandbox: LoginDialog opens with all options, 0 binding errors. Real consent not provable here.

## avalonia-port/popquiz-scheduler-core: +594 (quiz unit 2)
- Core PopQuizScheduler (WPF's 60/rate ±30% tick, disabled -> nothing, no stacking, lock-card defer once then drop,
  busy-queue defer) + question bank/mod resolver behind a 7-member IPopQuizHost; WPF PopQuizService is the host
  (behaviour unchanged). 5 fake-clock tests, each fail-proofed. Review: ACCEPT. No Avalonia host yet (unit 3).

## avalonia-port/popquiz-avalonia-open: +186 (quiz unit 3)
- Pop quizzes are live on the Avalonia head: PopQuizHost opens the topmost PopQuizWindow with the mod-resolved pool,
  chimes via CoreAudio, a one-slot replay after the last lock card closes; a lock card also waits behind an open quiz
  (WPF #763). CoreEngine arms the scheduler on Start and Stop closes everything. Graded Intake "Test pop quiz" works.
- Evidence: PopQuizHostTests (fail-proofed 6 ways); live sandbox: quiz on Start, answering closes it, Stop closes an open
  one in <43 ms, a lock card defers it, Test opens one, broken arming shows none. XP not banked (no provider on this head).
  Review: FIX -> r1 applied. Open: one quiz closed by itself in one run (close reasons now logged).

## avalonia-port/main-20260928b: +90661 (main sync #2, merge only)
- One --no-ff merge of origin/release/6.11.3 (383 commits; decision logged: release/6.11.3 is where the user's work lands,
  GitHub main is unchanged). 6 content + 179 location/delete conflicts resolved toward the stack's layout (Assets/, Core
  delegation); Version.props 6.11.3. Compile fixes in the merge: PresetNaming, ModAudioPolicy, CompanionExperience,
  PersonalitySamples moved to Core. Review: ACCEPT.

## avalonia-port/main-20260928b-compat: +240
- Release behaviour carried into Core: bounce-wall overhang, the CoreAccount identity event, AiEffectControlGate (Avalonia
  grid shows the effective value), CCP Default neutral session words in PhrasePoolCustody. WPF tests read the moved Assets
  and language files through SourceRoots (a Linux test asserts each path exists). 12 parity rows added as missing, plus a
  blocked row for the content-pack script (phase 5). Review: FIX -> r1 applied.

## avalonia-port/nav-rail-hover-parity: +294 (user-reported: side menu looks off, does not open)
- Root cause: the rail's setup hung off OnAttachedToVisualTree, which never fires on a Window, so no hover hook or label
  fade ran live (the clipped "Da"). Setup moves to the constructor; hover reads IsPointerOver (hit-test aware like WPF).
  WPF's 56->236 px 190/150 ms open/close, linear label/pill fade, 44->50 tiles, 40->46 icons, staggered door names.
- Evidence: NavRailHoverTests (fail-proofed 4 ways); live sandbox open/closed screenshots; nav-check and click-through
  pass. Still missing: door glow/shimmer, hue glow, mod door art, watchdog, reduced motion. Review: ACCEPT.

## avalonia-port/lockcard-typed-parity: +221 (user-reported: lock card typing doesn't look/work right)
- Root causes: the success pulse threw (animation on the transform), so a correct repeat never counted; KWin left the
  card a clipped 300x200 box (non-resizable windows aren't maximised). LockCardText moved to Core (WPF matching: ellipsis,
  curly quotes, nbsp); input judged as it lands; the session phrase is unbound from its loc key; focus reclaim; the card
  covers its screen (primary card on the primary screen); 52 px glyph.
- Evidence: LockCardTypingTests (fail-proofed); live sandbox typed card: 3 repeats in 5.1 s, 0 errors. Voice solve
  follows (speech units). Review: FIX -> r1 applied.

## avalonia-port/speech-core-engine: +890 (voice unlock unit 1; user-reported: speak-to-unlock doesn't work)
- The Vosk engine moved into Core (SpeechEngine) behind a mic-source seam; WPF SpeechService derives from it with its NAudio
  mic (API unchanged). Same model, grammar and scoring as WPF (decisions D1-D4, oracle-deep).
- Evidence: SpeechEngineVoskTests with the real model and Vosk's test.wav replayed through the real session path: match,
  reject a wrong phrase (heard text, not a timeout), silence times out; CI fetches the model and requires it. Review: ACCEPT.

## avalonia-port/speech-linux-capture: +249 (voice unlock unit 2)
- PulseMicSource: parec capture (pw-record fallback), pactl source list without monitors, device choice like WPF; seeds
  CoreSpeech on the Avalonia head; Vosk referenced by the head. Recorder stderr/exit logged; the list is cached 5 s.
- Evidence: scripts/speech-capture-check.sh (null sink -> test WAV -> parec on the monitor, never a real mic): match exit
  0, wrong phrase exit 1, no stray recorder (evidence/speech-linux-capture/speech-check-*.txt). Review: ACCEPT.

## avalonia-port/firstrun-folder-picker: +235 (user-reported x2)
- First-run "Choose a content folder" did nothing visible: the click only queued the picker until the wizard closed (a
  WPF modal-on-modal workaround). It now opens the portal folder picker at once, owned by the wizard, through the shell's
  single PickAssetsFolder (same #1053 guard, settings write, images/videos folders); the button shows the chosen folder;
  the wizard can't close under an open picker. Decision logged (deliberate, user-requested).
- The top-right release button read "v6.9.1 IS OUT" on 6.11.3: it now names WPF's btn_v6_11_3_is_out; ReleaseButtonKeyTests
  keeps both shells on the same key (fails on the stale key).
- Evidence: FirstRunFolderPickerTests; live: portal FileChooser.OpenFile with the "First run" window as parent. Review: ACCEPT.

## avalonia-port/cloud-read-only: +475 (auth unit 6, part 1 — high risk)
- Core ProfileAdopt owns WPF's watermark, take-higher, season-forward, curve-epoch and login-adopt rules; EntitlementTierRule
  moved to Core (+ApplyRise). WPF ProfileSyncService / V2AuthServiceHead / SeasonRecapService / EntitlementTierSync /
  App.ValidateRestoredSessionAsync delegate to Core (restore check now Core, per the earlier decision row).
- Avalonia: restore-session, then a READ-ONLY profile load (GET /v2/user/profile); login applies WPF's full adopt. No write
  endpoint anywhere on this head (tested on the startup and login paths).
- Evidence: CloudReadOnlyTests (8 goldens incl. ceremony pending, hand-transcribed from WPF rules; 3 verified by the reviewer
  by hand), no-write assertions fail-proofed. Part 2 (leaderboard, profile card, heartbeat) is a separate branch. Review: ACCEPT.

## avalonia-port/lockcard-voice: +379 (voice unlock unit 3; user-reported)
- Speak-to-unlock on the Avalonia head: voice only when requested + CoreSpeech available + mic consent (WPF :338-342);
  WPF's listen loop on the Core engine (10 s listens, retry, typing after 6 unavailable, states/level/partial on the UI
  thread, stop on close/panic/fallback); DisableVoiceForAll. The Lock Card card's hint names the Linux model folder when no
  Vosk model is installed (none ships yet: ledger row linux-package-vosk-model).
- Evidence: LockCardVoiceTests (WAV-fed: spoken phrase completes, wrong phrase stays open, held mic falls back, no consent
  -> mic never opens, closing mid-listen releases the mic, the hint), each fail-proofed; panel-state frames; capture check.
  No live voice run (would risk the real mic). Review: ACCEPT, follow-ups applied.

## avalonia-port/linux-media-default: +112 (user request)
- On Linux the default media folder (no custom folder picked) is ~/ccp media, created with images/videos/audio/wallpapers
  on first use; both pickers open there. A profile with files already in UserData/assets keeps it (logged, nothing moved);
  sandboxed runs (CCP_USERDATA_DIR) and Windows keep UserData/assets. Decision logged (advisor: user).
- Evidence: LinuxMediaDefaultTests (7, fail-proofed); live run with a temp HOME created <tmp>/ccp media. Review: FIX -> r1.

## avalonia-port/firstrun-gate-dead-clicks: +364 (first-5-minutes fixes)
- 18+ gate ported from WPF: the wizard's consent checkbox (Enter disabled until ticked; closing unaccepted aborts the launch
  like WPF AbortUngatedLaunch) and WPF's app-level "Age Verification" dialog for installs already welcomed but never accepted
  (a crash mid-wizard or a copied WPF settings file). Decision logged for its OK wording.
- Dead clicks fixed: Library rows (Web App via the launcher with WPF's copy-link fallback, Exclusives, Media Log window,
  Phrases editor), dashboard mosaic/split tiles (left-click opens the module, right-click toggles), Vault, companion strip,
  Scheduler/Ramp and Catalogue pills; ramp link labels use WPF's keys.
- Evidence: FirstRunGateDeadClickTests (fail-proofed); live sandbox: gate shown, accept writes the setting, decline exits,
  dashboard and Library routes work, 0 binding errors. Review: FIX -> r1 applied.

## avalonia-port/cloud-read-only-2: +921 (auth unit 6 part 2, read-only)
- Core LeaderboardClient (GET /v3/leaderboard, GET /user/lookup, WPF's ranking) with the models and avatar hue moved into
  Core; WPF LeaderboardService goes through it. The Avalonia leaderboard's sample roster is gone: a real fetch on every
  visit/refresh/mode switch (one at a time, like WPF), your-rank badge, you-bar, honest "Failed to load" offline state.
- The heartbeat is a presence POST, so it waits for unit 7 (ledger row). The profile-card read is in Core; its Trainer Card
  UI is the next layer.
- Evidence: CloudLeaderboardTests (no-write, board, card fields, in-flight guard; fail-proofed); live sandbox: the real public
  board and the offline state. Review: ACCEPT, follow-ups applied.

## avalonia-port/avatar-tube-windowing: +518 (user-reported: tube didn't move on Detach, wasn't on the left when attached)
- WPF's windowing ported: one ToggleDetached for the tube menu and the hero Detach button (detached = topmost + drag from her
  visible parts; attached = docks on the shell's left edge with WPF's formula, TubeDockPlacement moved to Core); follows the
  shell's move/size/state (minimise hides, maximise detaches, restore re-attaches); Attach brings the shell back; mode and
  position saved in WPF DIPs, restored on the right screen after the real scaling arrives (1 s fallback). While attached,
  the tube's input region stops at the seam so it never steals the nav rail's clicks.
- Evidence: TubeDockMathTests, AvatarTubeWindowingTests (fail-proofed); live KWin/XWayland: docks at 767,746 / follows to
  967,946, detach topmost, restore after restart, input region ends at the shell edge. A real WM drag and a real click
  through the margin weren't possible (Keincheck can't drag; the screen was locked). Review: FIX -> r1 applied.

## avalonia-port/play-spiral-bleed: +218 (user-reported: Play's spiral kept showing on other pages)
- Root cause: Avalonia's IsVisible covers only the control itself, so Play's tier rims (drawn in the window's adorner layer),
  ember canvas and badges never saw their tab hide; the rims bled over other tabs and the loops kept burning CPU. New
  EffectiveVisibility.Watch parks/resumes them with their tab; the same fix for Vat glass, the takeover orb and the spiral glyph.
- Evidence: PlaySpiralBleedTests (fail-proofed); live Play -> Home clean x3 (before/after screenshots). Review: ACCEPT.

## avalonia-port/shell-resize-maximize: +251 (user-reported: maximized looks bad; can't resize by the sides)
- The frameless shell gets 5 px edges and 12 px corners (WPF's resize border) that hand the drag to the window manager
  with the right cursor, off while maximized. Min size clamps to the screen's work area (this screen is narrower than it).
- Viewbox: Fill like WPF while the window's shape is within ±15% of the 1585:901 canvas, otherwise Uniform, top-aligned
  (oracle-deep; decision logged). The portrait maximize that stretched everything 2.2x now keeps the art's shape.
- Evidence: ShellResizeMaximizeTests (15, fail-proofed); live maximize/restore screenshots. Not proven live: a real window-manager
  edge drag and the hover cursor. Review: ACCEPT + the oracle's policy.

## avalonia-port/trainer-card: +536 (auth unit 6 part 3, read-only)
- The Trainer Card shows real data: your card (settings, achievements, board rank with WPF's name fallback), My Profile,
  search (exact then partial), leaderboard row double-click; another trainer's card from the board row then GET /user/lookup
  (presence, staff/whitelist pills, achievement tiles). Unknown/offline shows the search plate (offline searches the last
  board). The sample card is gone; the card's text rules moved to Core TrainerCardText (WPF calls them). Board cached 60 s.
- Evidence: TrainerCardTests (fail-proofed); live sandbox: own card real values, a public trainer looked up without signing
  in, offline states, 0 binding errors. Missing: avatar picture, Patreon art, cosmetics, writes (unit 7). Review: ACCEPT.

## avalonia-port/session-runner-popquiz: +74 (quiz unit 4)
- Core SessionRunner drives pop quiz like WPF SessionEngine: starts with the session when the user toggle is on (else stops
  an engine-armed one), stops and closes on pause/panic, restarts on Resume, stops at the end, toggle and rate restored.
  The per-session PopQuiz* minute range is dead in WPF too (BuiltInPrograms.cs:430), so it isn't ported (decision via
  supervisor: WPF parity).
- Evidence: SessionRunnerTests (fail-proofed 4 ways); live 3-minute session: quiz at +46 s, tray panic closed it and paused,
  none while paused, back 43 s after Resume, closed at the end. Review: ACCEPT.

## avalonia-port/quiz-window-static: +303 (quiz unit 5)
- QuizWindow runs on Core QuizStore: categories, WPF's fallback question/profile path, scoring, quiz_history.json in WPF's
  shape, trend. The Graded Intake start button, its AI login gate and the past-quizzes list follow WPF's hidden state
  (pending removal in WPF; decision logged), so no user path reaches the quiz on either head. Fixed the score pulse /
  surrender shake animating the transform (it threw on the first question).
- Evidence: QuizWindowStaticTests (gate, full run with a golden history shape, trend, report), fail-proofed. Review: ACCEPT.

## avalonia-port/sync-body-core: +281 (auth unit 7a)
- Core SyncBody: WPF's sync body in WPF's order, one Known flag per field (unknown fields are omitted, never sent as null);
  WPF sets every flag, so its bytes are identical. The HMAC Signature/SignRequest and WPF's four-field heartbeat body are in
  Core too; Core's heartbeat now sends the four fields. Decision rows for the body builder, unknown fields and heartbeat.
- Evidence: SyncBodyGoldenTests (full, fresh-with-nulls, migration graft, omit-unknown, HMAC, heartbeat; goldens from the
  pre-switch code), fail-proofed. Nothing new is reachable on Avalonia yet (7c). Review: ACCEPT.

## avalonia-port/logout-clear-core: +141 (auth unit 7b)
- Core ProgressionClear.Apply = the settings half of WPF ClearProgressionData (same 28 fields, same order, XP watermark
  cleared, UnifiedId kept); WPF calls it, then saves and resets quests/achievements/skills as before.
- Evidence: ProgressionClearTests (every field), fail-proofed. Avalonia uses it with the push in 7c. Review: ACCEPT.

## avalonia-port/quiz-category-editor: +146 (quiz unit 6)
- The quiz category editor is ported: WPF's validation order and messages (incl. the built-in name clash), template copy,
  save (Id kept on edit) and delete through Core QuizStore; QuizWindow's Create/Edit save through it. Reachable only behind
  WPF's hidden quiz button, as in WPF.
- Evidence: QuizCategoryEditorTests (validation, template, create/edit/delete round trip vs the golden JSON keys),
  fail-proofed 4 ways. Review: ACCEPT.

## avalonia-port/avalonia-sync-push: +785 (auth unit 7c — HIGH RISK; server contract unconfirmed)
- Avalonia pushes /v2/user/sync with only unified_id/xp/level/descent_epoch/achievements (server ∪ local; omitted if the
  profile lacked them); gates: loaded this session + signed in + 30 s cooldown (429/409) + XP watermark. Triggers: after load,
  level-up, XP nudge, session stop, pre-logout, exit (2 s cap). WPF's response rules (level_reset + Descent refusal, adopt,
  watermark) moved to Core. Heartbeat (WPF's four fields) every 120 s after a load. Core ProgressionBank banks XP (no
  multipliers; passive sources skipped until an idle tracker exists). Logout: pre-sync -> heartbeat stop -> identity ->
  ProgressionClear.Apply -> reset; a different account clears progression first; replies after a reset are dropped.
- Evidence: SyncPushTests (13) + SessionRunner XP test, each fail-proofed; sandbox run with no account. Review: FIX -> r1 ->
  ACCEPT. Ledger row server-sync-contract BLOCKED: confirm the server treats absent keys as no change before merging.

## avalonia-port/release-content-core: +669 (content packs L1 — WPF's download path)
- ReleaseContentService moved to Core (pure git mv, then seam edits: Log, CoreSettings, CoreReleaseContent.AppVersion,
  CorePaths.UserData, new UiInvoke seam); WPF delegates with the identical dispatcher rule. Linux: free space measured on the
  mount holding content/; case-sensitive paths off Windows. PendingModChoice (settings half) in Core. CCP_CONTENT_BASE_URL
  honoured only for literal loopback/localhost without userinfo (dev fake server).
- Evidence: new Tests/CCP.Core.Content.Tests (20; loopback fake server that refuses other hosts: happy path, fallback cycle,
  ranged resume, range ignored, hash retry/failure, offline, space, target escape), fail-proofed 12 ways; added to CI.
  Nothing on the Avalonia head uses it yet (L2-L4). Review: ACCEPT.

## avalonia-port/content-pack-script: +94 (content packs L5)
- build-content-packs.ps1 reads pack sources from <repo>/Assets; zip entries and installer deletions use an install-relative
  path (no ../Assets entries). New content-packs workflow (path-filtered): -DeletionsOnly + git diff --exit-code on every
  relevant PR; the cross-commit zip-equality proof runs on demand.
- Evidence: locally with pwsh + a path shim, the deletion list is byte-identical (fail-proofed) and all seven zips and the
  manifest match the pre-move build. The Windows job hasn't run yet. Review: ACCEPT, CI cost follow-ups applied.

## avalonia-port/packs-mod-manager: +400 (content packs L2)
- The Avalonia head builds the Core pack service at desktop startup (WPF's providers and baseline rule; sandboxed runs never
  fetch from GitHub unless pointed at a loopback server) and the Mod Manager's pack rows download, show progress, end
  Installed and refresh the list, as in WPF. The fake content server is shared by both test projects.
- Evidence: ModManagerPackDownloadTests + the sandbox-fetch guard test (fail-proofed); live sandbox against a local fake
  server: not downloaded -> 42% -> "Media downloaded". Review: ACCEPT, follow-ups applied.

## avalonia-port/packs-mod-choice: +709 (content packs L3)
- The header mod combo switches mods live like WPF (activate, save, pending clear, audio-base fetch, palette repaint, selector
  rebuilt outside its own handler); the saved palette applies at startup; Mod Manager switches repaint on close. A chosen mod
  activates when its pack lands or on the next launch. The first-run mod step activates or records + downloads like WPF.
- Evidence: ModChoiceTests (5, fail-proofed); live sandbox against a local fake server: CCP Default -> Dronification re-themes
  with the chip named, a pending Bambi choice auto-activated after its download. Not repainted yet (noted): logo/feature art,
  achievements, skill tree, BambiCloud radio, per-mod presets. Review: FIX -> r1 applied.

## avalonia-port/packs-mod-picker: +227 (content packs L4)
- The mod picker dialog is ported with the live pack service (WPF's show rules, sequential downloads with per-card progress,
  PendingModChoice). The release/6.11.3 WPF removed the returning-user auto-open, so neither head opens it (decision row 31
  superseded); mods are offered via the first-run wizard and Mod Manager. No unprompted manifest fetch in sandboxed runs.
- Evidence: ModChoiceTests (dialog download -> activate; returning-user startup opens nothing and fetches nothing), fail-proofed.
  Review: FIX -> r1 applied.

## avalonia-port/publish-profiles: +265 (installer/updater U2)
- Publish profiles: win-x64 with WPF's settings (self-contained, single-file, native libs bundled, R2R, no trimming) and linux-x64
  (self-contained, loose). Assets/Models copied like WPF. WPF's single-instance mutex name with a named-pipe show/ack (sandboxed
  runs use suffixed names). CI publishes both and runs --smoke from the publish folder. Decision rows: exe name, upgrade in
  place, Linux channels (user brief), updater home, profiles, signal transport.
- Also fixed a test leak: ModChoiceTests left CoreMods pointing at its mod service, so shuffled test order made later tests
  see drone-mode active (10 consecutive green runs after the fix, no retries). Review: pending.

## avalonia-port/release-feed-core: +730 (installer/updater U3)
- Core ReleaseFeed holds the WPF updater's logic, moved verbatim: tag -> version, Setup asset pattern order + size,
  DidUpdateSucceed, update_skip/attempt/result markers (same names, 24 h / 7 day rules), the helper .cmd text (byte goldens
  with CRLF). WPF UpdateService forwards to it and keeps network, elevation, WebView2 cleanup and exit; its AppVersion stays
  until U8 (it seeds CoreReleaseContent).
- Evidence: ReleaseFeedTests (20; a v6.11.3-shaped fixture; helper goldens recorded from the pre-move code), fail-proofed.
  Review: ACCEPT.

## avalonia-port/avalonia-updater: +663 (installer/updater U4)
- The Avalonia head's updater on Core ReleaseFeed: WPF's pending-outcome check at startup, update check (offline skip,
  installed copies only on Windows, 24 h skip), the header pill and a one-time startup dialog after the first-run modals, the
  manual check. Windows downloads and runs the Core helper like WPF (compile-only here); Linux/macOS only notify (open the
  releases page). One Core LoopbackUrl rule guards CCP_CONTENT_BASE_URL and CCP_UPDATE_API_URL (fixed: "loopback" host).
- Evidence: AppUpdaterTests (Linux never downloads, pill, pending outcome, startup dialog + 24 h skip, loopback rule),
  fail-proofed; live sandbox against a loopback fake feed: pill "UPDATE TO v99.1.0", click opens the releases URL.
  Not run: the Windows download/install path. Review: FIX -> r1 applied.

## avalonia-port/linux-package: +201 (installer/updater U7)
- packaging/linux/build-tarball.sh: self-contained linux-x64 tarball with the Vosk small model (sha256-pinned), .desktop +
  128/256 px icons + appstream metainfo, LICENSE; app id and X11 WM_CLASS io.github.CodeBambi.ConditioningControlPanel. AUR PKGBUILD
  (-bin, /opt, MIT) and a Flatpak manifest (GNOME 50, bundled libVLC) consume it. CI builds + smokes the tarball (artifact only).
- Evidence: tarball smoke with the bundled model (fail-proofed by removing it); makepkg -si + --smoke in archlinux:latest;
  WM_CLASS read live. Flatpak never built here (row linux-package-flatpak blocked: first build + WPE question). Review: FIX -> r1.

## avalonia-port/linux-inapp-deps: +222 (user Linux brief, in-app)
- Distro-aware missing-package message (dlopen probe + /etc/os-release -> pacman/apt/dnf command, library list fallback) as a startup
  notice and in the web view panel, with Copy command; portal Registry.Register with the app id before the panic-key bind.
- Evidence: LinuxDependenciesTests (fail-proofed); dbus-monitor: KDE files the shortcut under our id once the .desktop is
  installed. Not live: a really missing library. Review: ACCEPT.

## avalonia-port/catalogue-client-core: +694 (catalogue U1)
- WPF CatalogueService logic moved verbatim to Core CatalogueClient (HTTP, token exchange + per-instance cache, SubmissionResult,
  status mapping incl. 429 Retry-After); WPF is a thin subclass; write gate stays WPF's token + unified id (decision logged).
- Evidence: CatalogueClientTests (15, fake handler, no network), fail-proofed 4 ways. Avalonia wiring is U3. Review: ACCEPT.

## avalonia-port/catalogue-lookup-core: +542 (catalogue U2)
- WPF CatalogueLookupService logic moved to Core CatalogueLookup (lookup by HT URL, download, validate, save with the collision
  suffix; the UI-thread opener is a per-head callback); WPF keeps its Dispatcher behaviour. Avalonia uses the Core CatalogueEntry.
- Evidence: CatalogueLookupTests + the SubmissionSucceeded once/never test, fake handler, no network, fail-proofed. Review: ACCEPT.

## avalonia-port/catalogue-submit-wire: +296 (catalogue U3)
- Deeper library 📤 -> CatalogueSubmitDialog -> Core CatalogueClient -> saved record -> WPF result toast; App.Catalogue built like
  WPF; CCP_CATALOGUE_BASE_URL loopback-only, sandbox without it sends nothing (WPF unaffected).
- Evidence: CatalogueSubmitWireTests (signed in/out) + base-URL theory, fail-proofed; live signed-out: 0 requests, 0 binding errors.
  Signed-in submit not live (no sandbox token). Review: ACCEPT.

## avalonia-port/catalogue-picker-pill: +549 (catalogue U4)
- WPF HT lookup chain on Core CatalogueLookup (toast -> picker or direct download -> opens in the player, UI-thread opener),
  triggered at WPF's point with the requested URL (WebHost has no live-URL hook, so no user path yet: row stub). /mine share-status
  polls at startup, Presets/Deeper tab open (90 s throttle) and Manage Mods; pills on the preset pane, session rack and Mod Manager.
- Evidence: CatalogueShellTests (fake handler, fail-proofed); live: Mod Manager pill "Rejected", 0 binding errors. Review: ACCEPT.

## avalonia-port/login-dialog-fixes: +53 (user live test 2026-09-29)
- Login: an error or late success after the dialog closed crashed the app (closed owner) or signed in silently; now logged and dropped.
- Shell: screen/scaling work-area fit waits 300 ms for the move to settle (it bounced the window between monitors mid-drag).
- Sandbox SecretStore no longer shows the "no secret store" notice. SIGTERM now shuts down through the lifetime (was ignored).
- Evidence: user Discord OAuth live in a sandbox (tokens received); SIGTERM live (exited in <6 s); SecretStoreTests; full gate. Review: ACCEPT.

## avalonia-port/companion-room-refresh: +232
- Companion hero card shows real state (WPF CompanionHeroRuntimeVm): name (from CoreMods), mod, flavour, level/XP, mute/show,
  AI and awareness pills, AI-access plate; mute/show/wake through the shell, Chat opens the tube input, Switch/Engine/Awareness
  scroll the room, the plate opens Settings · Account (audit-fix-ui: it was a no-op until ShowTab("patreon") redirected); the room re-reads the hero on tab return. Rows stay stub (Tutorial chip placeholder).
- Evidence: CompanionHeroSyncTests (fail-proven; order-independent after the StartModsTests reset); live mute saved, 0 binding errors.
  Review: FIX -> fixed.

## avalonia-port/studio-presets-wire: +367
- Presets New/Save-over/Load/Delete (WPF Presets.cs:2202-2381) with Core CoreEngine.Reconcile (#872: stops what the preset turned
  off, never starts); Studio rack right-click -> ToggleWallFeature; dashboard tile rings follow the saved flags; tile breathing
  pauses on hidden tabs (improvement over WPF, also fixed a headless double-click flake).
- Evidence: StudioPresetsWireTests (2), CoreEngineTests.Reconcile_*, fail-proven; live: presets saved/loaded/deleted, loading
  "Quiet" stopped Subliminal and cleared its ring, 0 binding errors. Rows stay stub. Review: ACCEPT.

## avalonia-port/quest-service-core: +294
- QuestService, QuestHardwareGate and QuestProgressRerolls moved into Core (git mv, 91/94/83% similar); head-only needs behind
  CoreQuests (provider verifying, streak shield, perfect-week bonus, program verifier, completion sound/haptics, camera/mic probes);
  timers post to the UI thread; WPF seeds the seam in App's static ctor and behaves the same. Avalonia tab wiring is next.
- Evidence: QuestServiceTests (roll, reroll, progress+completion; 4 fail-proofs); full gate; WPF tests compile-only here. Review: ACCEPT.

## avalonia-port/session-io: +508
- Sessions: per-row export, delete for custom sessions (Delete/Cancel confirm like WPF), export strip enabled on selection,
  .session.json/.preset.json drop import with WPF validation. SessionLock.Owned ported: 47 of 53 WPF markers greyed during a
  session incl. Haptics (row stays stub: ribbon pulse, program reason line, 6 markers). SessionSummaryPresentation moved to Core
  (git mv); the recap opens passively while a lock card/quiz/bubble count is up.
- Evidence: SessionIoTests (4, fail-proven); live: custom-row delete, lock during Morning Drift, 0 binding errors. Review: FIX -> fixed.

## avalonia-port/quests-tab-live: +486
- Avalonia builds and seeds the Core QuestService: definitions, provider-verifying hooks, completion chime through CoreAudio, real
  camera (/dev/video*) and Pulse mic probes (fail-open like WPF); SkillTree/Programs hooks unseeded. Quests tab and daily cards
  show the live board, weekly card, stats, streak and month calendar; rerolls spend the budget; completion banner, popup and
  sync push. Payout maths is one Core method (QuestService.ScaledQuestXp). Progress sources: flash images, XP awards.
- Evidence: QuestsTabLiveTests (fail-proven); live board and both rerolls. Rows stay stub (FX burst, stamps, art, punch card,
  streak fix, animations). Review: FIX -> fixed.

## avalonia-port/achievements-tab-live: +493
- Achievements tab card grid on Core (WPF order, hidden skipped, blurred/???/requirement, progress bars, Patreon chip,
  All/Unlocked/Locked); built once, one card redrawn per unlock (bursts combined), in-place refresh on show, cached 150 px badges.
  CheckLevelAchievements moved into Core AchievementEngine (WPF delegates); the head runs it on LevelUp plus a silent startup pass.
- Evidence: AchievementsTabGridTests + LevelUp->unlock test, fail-proven; live: session levelled 9->10, "Plastic Initiation" popup
  and card. Rows stay stub (rewards need WardrobeCatalog, tile animations, sound, passive toast). Review: FIX -> fixed.

## avalonia-port/header-xp-live: +270
- Header level chip, LVL label, XP readout and XP bar live from Core progression (CoreSettings + XpCurve); repaint on XP awards,
  level-ups, sign-in, profile load, logout and menu open. Bar fill slides 0.6 s and flashes on level-up (WPF FillXpBarTo /
  CelebrateLevelUp), off at MotionLevel Off. Profile menu Level/XP rail live.
- Evidence: HeaderLevelLiveTests (fail-proven, incl. sign-in/logout repaint and unsubscribe); live Lvl 10, 300/994 XP, 0 binding
  errors. Rows stay stub (odometer, glow dot, chip pop, burst, bank hold, rank title). Review: FIX -> fixed.

## avalonia-port/wardrobe-catalog-core: +370
- WardrobeCatalog moved to Core (git mv); WPF keeps its image half as WardrobeArt and seeds ProgressProvider; the achievement ->
  item map is Core AchievementRewards(). Avalonia achievement cards show reward art/silhouette and name, the Has-a-reward chip,
  the wardrobe counter and real item names; the item-unlocked toast fires 900 ms after a gated unlock (max 3) with real art.
- Evidence: Core WardrobeCatalogTests (3) + AchievementsTabGridTests, fail-proven; live band, silhouettes, counter 1/63, filter,
  0 binding errors. Toast not seen live. Rows stay stub (tile FX; toast inbox/no-activate). Review: ACCEPT. Follow-up: stale
  "WardrobeCatalog is WPF-only" notes in ProfileCustomizeDialog / WardrobeEditorDialog / ProfileWardrobe (profile unit).

## avalonia-port/lab-awareness-live: +701
- FIX (cross-cutting): Avalonia AccountSeed now seeds CoreEntitlement.HasPremiumProvider/HasLabProvider like WPF; paying users had
  no premium anywhere on Avalonia. AccountSeedTests fail-proven.
- Blink Trainer camera-free half: settings, demo loop (art moved to /Assets, WPF path unchanged), premium gate, Core
  BlinkTrainerState status row (WPF delegates), folder library, editors; consent manage/revoke; camera buttons disabled with a
  tooltip. Play card opens it. Rows stay stub (camera, session, calibration). Review: FIX -> fixed.

## avalonia-port/profile-wardrobe-live: +716
- Trainer Card wears the real decoration and charms (own card, board row, lookup), re-placed on resize; Customize opens from the
  hero, its wardrobe section and the Wardrobe editor use Core art; WardrobeStageGeometry moved to Core (git mv).
- Saving pushes the loadout like WPF PersistOwnCosmetics (SyncPush.PushCosmeticsAsync: cosmetics only on this push; empty = WPF's
  clear; a save skipped by the 30 s cooldown rides the next push instead of being dropped - deviation, logged).
- Evidence: ProfileWardrobeTests + ProfileCosmeticsSyncTests, fail-proven (6 breaks); live card/dialogs/editor screenshots. Rows
  stay stub (HoverPop, banners/presets/pins need CosmeticsCatalog, no WPF side-by-side). Review: FIX -> fixed.

## avalonia-port/hover-pop-behavior: +303
- WPF HoverPop ported (Controls/HoverPop.cs: 1.06 back-ease, 4-key wobble, leave ride-home, MotionLevel Off snap, rig on first
  hover) and attached on 16 of 17 WPF sites (Lockdown art absent here); FeatureCard drives it from ApplyHover like WPF.
- Evidence: HoverPopTests (Full/Off, real pointer enter/leave), fail-proven. Live hover not shown: Keincheck hit-test lands on the
  window template Panel in the content area (investigating separately). Rows wired: behavior-hover-pop, shell-profile-wardrobe,
  views-adorned-avatar. Review: ACCEPT.

## avalonia-port/content-hit-test: +82
- Question: Keincheck hit_test in tab content returned the window template Panel. Answer: not an app bug. Keincheck answers
  from the last drawn frame; the live shell was Minimized (first-run window active), so no new frames. Real input reaches the
  controls: ContentHitTestTests (Studio/Presets/Awareness/Quests, hit test + headless press on every visible button), fail-proven.

## avalonia-port/premium-gates-audit: +404
- Premium gates audited against WPF now that entitlement is seeded (#1913). Fixed: the six tab veils were always hidden (free
  users saw premium tabs unlocked) and the eight Play lockbands always shown (patrons saw them locked); unlock buttons were dead;
  the daily free feature and program premium check were never seeded; the Awareness master toggle had no gate. Veils repaint on
  tab switch, sign-in/out, tier change and day rollover. WPF EnforceEntitlementLapse flag half moved to Core EntitlementLapse
  (WPF delegates); startup clears in memory, events save (decision logged).
- Evidence: PremiumGatesTests, EntitlementLapseTests, lapse write test, all fail-proven; live Awareness veil for a free user.
  Rows stay stub (veil FX, Intake band, FREE TODAY stamps). Review: ACCEPT + P2s fixed.

## avalonia-port/exclusives-tab-live: +454
- ExclusiveFeature moved to Core (gates via CoreEntitlement + IntakePassAvailableProvider; WPF seeds the same sources). Vault from
  the real roster with live gates, art and FREE TODAY; cards route to their tabs (For You/Just Drop/Back Room inert with an honest
  tooltip); rail stars read Core; vault and stars repaint on tier/day change. Programs lock from entitlement; enrol disabled.
- Fixes: DailyFree never fetches the real override endpoint from a sandbox (since #1917). HoverPopTests leaked
  CoreSettings.ServiceProvider (#1915), which flaked ShellTray/Quests/Awareness tests in full runs; restored in finally.
- Evidence: ExclusivesVaultTests, rail stars, DailyFreeSandboxTests, all fail-proven; full Avalonia suite 5x green; live vault.
  Rows stay stub (motion, sheens, tier plates). Review: ACCEPT + notes fixed.

## avalonia-port/inbox-friends-core: +93
- Friends wire (FriendsApi), models and notice/landing/poll/sfx rules git-mv'd into CCP.Core/Services/Friends; identity, base URL
  and the merged-account reply hook are passed in (WPF passes the same BackRoomApi/MergedAccountRecovery values). FriendsService
  (DispatcherTimer + App.Settings) and all friends/inbox UI stay in WPF for now.
- Evidence: FriendsApiCoreTests (6, fake HTTP), fail-proven. No UI change. Review: ACCEPT.

## main sync #3: avalonia-port/main-20260929 + -compat (+231)
- Merge of origin/release/6.11.5 (185 commits, 6.11.4 + 6.11.5 "Locktober"); hand edits vs git's automatic merge are exactly the
  14 declared files (conflicts + compile fixes: FriendEvent/FriendReceipts/FriendsSentBook moved to Core, one logger swap, two
  WPF test paths, the update-button key). Every release line added to a file the stack had moved is present at its new path.
- Compat: ServerClock/SyncFailureBackoff to Core; signed requests use the server clock; SyncPush learns the clock, re-signs once on
  clock skew and backs off after failures (not on shutdown cancellations, as WPF); refused Patreon/Discord refresh tokens are not
  resent (as the release). 15 new ledger rows (12 missing, 3 wired). Evidence: new tests fail-proven; full gate green.

## avalonia-port/emidesk-live: +518
- Head EmiDeskService (WPF Toggle/Summon/Dismiss/EnsureWindow/mute prompt) owns whether EMI is out; dock chip, hover x and the
  settings switch route through it; she closes with the shell. Mute silences the tube (WPF Speech.cs:461). EmiState and the
  mute rule (EmiMuteRule) moved to Core, both heads use them; WPF behaviour identical, exit flush kept.
- Evidence: EmiDeskSummonTests (5, fail-proven); live summon/mute/dismiss. Rows stay stub (ring, codex, book, options, chord
  hotkey, face/voice, RefreshOutfit, StopPresentation). Review: FIX -> fixed, ACCEPT.

## avalonia-port/chaster-core: +53
- ChasterClient (App.Logger -> Serilog.Log), 17 pure Chaster rule files (incl. release 6.11.5 CirceLines/CircesMood/TabPriceEdit),
  StakeRules and LockSnapshot/LockLookup moved to CCP.Core with git mv; ChasterClientTests (27, fake handler) now run on Linux.
  ChasterService stays in WPF (App partial + System.Windows.Point); next slice splits CreateForApp and adds a Core point type.
- Evidence: 27 tests, fail-proven; full gate. No UI; ledger notes only. Review: ACCEPT.

## avalonia-port/spiral-descent-live: +373
- DescentCountdownService moved to Core (git mv; CoreSettings/CoreDispatch; WPF speech through a Speaker seam; phase marked before
  speaking like WPF; no ticks after exit). Core SyncPush arms/kills it from descent_countdown (absent = kill switch, as WPF).
  Avalonia shell: spark, T-minus tooltip, corner clock (gold at Terminal), presence per phase; at zero the Live fuse show opens
  unless the account already answered; witnessed flag saved.
- Evidence: DescentFuseLiveTests (5, fail-proven); live T-8m sandbox: gold corner, spark 1.25, zero opened the show, surfaces
  cleared. Rows stay stub (chrome dim, phase lines, rail chip, stage visual, ceremony/migration). Review: ACCEPT + P3s fixed.

## avalonia-port/chaster-service-core: +239
- ChasterService moved to Core (git mv; Core ScreenPoint replaces the WPF Point in NoteAt/BookedAt, converted at the WPF call
  sites; CreateForApp + DEBUG demo in head-side ChasterServiceApp; ChasterLadderApi in Core with injected identity/URL;
  FixedTimeEquals for the state check). Avalonia SecretChasterTokenStore reads/writes WPF's chaster_auth.dat shape via SecretStore.
- WPF source-scan tests (ChasterHooks, Circe, Natasha, ChasterTab*) read all product roots / moved languages and web assets.
- Evidence: ChasterServiceTests (63) on Linux + token-store tests, fail-proven; scan paths proven by script. No UI yet (no
  door: ChasterRailChip unported). Review: FIX -> fixed.

## avalonia-port/bubbles-live: +762
- Plain ambient Bubble Pop runs on this head: Core AmbientBubbles (WPF numbers: speed, wobble, pop, 40/3 cap, cadence, lucky 5%
  x20, 300 XP/day bucket; WPF BubbleService delegates the bucket), CoreEngine start/stop in WPF order. Avalonia BubbleOverlay per
  screen, click-through except on bubbles (X11 input region; Win32 toggles WS_EX_TRANSPARENT under the cursor), pop sound, XP,
  quests; idle loop parks when empty. Card toggles live, N/300 line; header XP tooltip filled on open.
- Evidence: AmbientBubblesTests (0 alloc/step at 40 bubbles), BubbleOverlayTests, fail-proven; live 3 overlays, pops paid XP;
  0.013 ms/step. Windows popping not run. Rows stay stub (trigger/v2 bubbles, lucky FX, Bubble Count). Review: ACCEPT + notes.

## avalonia-port/video-playback-core: +894
- Mandatory Video slice 1: Core MandatoryVideoScheduler (WPF interval x0.8-1.2, 60 s floor, shuffled local pick, 1.3 s pre-roll,
  re-arm on end/Esc, strict keys, secondary-monitor rule, volume, watch credit; WPF delegates). CoreEngine arms it. Avalonia
  MandatoryVideoOverlay: one LibVLC decoder feeding full-screen topmost windows (shared VlcFrameSink). Safety: a strict video
  falls open on the panic key/Esc when no global panic listener is live (as Lock Card #875). Empty library keeps re-arming
  (deviation, logged).
- Evidence: MandatoryVideoSchedulerTests + overlay tests, fail-proven; live first frame 20-45 ms, 21% of one core at 720p30,
  Stop clears in 0.3 s. Rows stay stub (blurred background, attention checks, grace pause, watchdogs, ducking). Review: FIX -> fixed.
## avalonia-port/chaster-tab-live: +1000
- Head builds the Core ChasterService (WPF options, SecretChasterTokenStore; a sandbox reaches only a loopback CCP_CHASTER_API_URL, fail closed otherwise; consent and site links obey it). Rail padlock (ChasterRailChip) opens a read-only tab: account chip, live lock clock, ends line, pills, lock pick, loopback PKCE link, facts, mood line.
- Evidence: ChasterTabLiveTests (fail-proven, real pointer click); live against a loopback fake, 0 binding errors. Rows missing->stub. Review: ACCEPT.

## avalonia-port/chaster-tab-2: +607
- Chaster slice 2: Unlink as WPF (confirm, revoke, SecretStore token cleared), the Run-the-tab switch with the one-time consent
  card, Pause; StartSettle at startup (sandbox loopback-only; outside a sandbox only when on + linked + not paused, as WPF) and the
  hooks this head can raise (quests, dailies board, level-ups; attach-once with a detach); rail chip hover peek and ring-coloured
  glow.
- Evidence: ChasterTab2Tests (4, fail-proven x7); live against a loopback fake (consent, pause, unlink + revoke, 0 binding errors).
  Rows stay stub (FX, peek spring/idle/pulse, program/escape hooks). Review: ACCEPT + P3 fixed.

## avalonia-port/video-attention: +821
- Mandatory Video slice 2: attention checks (Core rules; WPF VideoService/AttentionTargets/AchievementService delegate: target
  count/timing, verdict, +15 XP per catch, (replays+1)*50+200 pass, troll, third-replay mercy, stop/new clip cancels a pending
  replay), blurred fill when the clip letterboxes (64-px downscale, 0.6-0.8 ms/frame, 0 dropped), audio ducking, bouncing-text
  pause, no-videos dialog once per launch, Chaster attention notes (twice per fail, as WPF).
- Evidence: Core + Avalonia tests incl. attention trackers, fail-proven; live replays -> mercy, 3/3 catch, blurred bars, duck
  100->20->100%. Rows stay stub (grace pause, watchdogs, duration filter, bubble pause). Review: ACCEPT + P3s fixed.

## avalonia-port/bubble-count-live: +785
- Bubble Count is live: Core BubbleCountScheduler (WPF schedule 1-10/h +-20% min 60 s, 800 ms lead-in, XP 100 x clip length with a
  3-min cooldown, strict retry + mercy after 3, ambient-bubble pause; WPF delegates interval, XP scaling, target count), armed by
  CoreEngine; the game plays on the shared LibVLC across screens, the answer window pays 250 x clip length; panic closes all;
  strict falls open without a panic listener.
- Evidence: BubbleCountSchedulerTests + BubbleCountGameTests (committed 3 KB clip, runs on CI), fail-proven; live 3 screens,
  correct answer, strict retry. Rows: win-bubble-count-result wired; feat-bubble-count stub (Bambi Freeze lead-in); win-bubble-count
  stub (click raises the game over its bubbles). Review: FIX -> fixed.

## avalonia-port/video-grace-watchdog: +462
- Mandatory Video slice 3: Esc grace pause (WPF #735; 60 s card in the video window, auto-resume; the global panic key grace-pauses
  only when panic-overrides-all is off, else panic wins), lost/dead output replays the clip once then ends (WPF vout heal; deviations
  logged), overrun/max-length guards from the first frame, Test-button stuck/force-reset prompt, ambient bubbles held for the whole
  run, Chaster "video" note, Trainer -25 XP (CompanionPerks moved to Core).
- Evidence: scheduler + overlay + shell panic tests, fail-proven; live grace pause/resume/auto-resume, stuck prompt, panic stop, max
  length. Rows stay stub (UI-freeze watchdog, pack clips, min-duration filter, interaction queue). Review: FIX -> fixed.

## avalonia-port/bambi-freeze: +578
- Bambi Freeze lead-in (WPF BubbleCountService.cs:353) and subliminal whisper audio on Avalonia: Core SubliminalWhisper (90% reset
  roll, 4-8 s / 1-2 s gaps, 50+250 ms whisper-to-card, 500 ms unduck, (sub x master)^1.5 volume, clip lookup mod -> shared ->
  neutral), WPF SubliminalService/BackRoomVoice delegate; ContentLocator folds backslashes off Windows (fixes Windows-style
  paths on Linux).
- Evidence: Core + Avalonia tests (Windows-safe path asserts), fail-proven; live whisper at 40%, freeze card on 3 screens,
  game at +0.8 s, reset at +5.2 s. Rows stay stub (freeze haptic; subliminal StealsFocus, shared host, whisper stop). Review: FIX -> fixed.

## avalonia-port/haptics-core: +864
- HapticService + device manager, mixer, patterns, Buttplug (Intiface) and Lovense providers, ButtplugUrl, FunScript, ToyInput moved to
  Core (git mv; WPF delegates; three head seams). CoreHaptics seeded by both heads (shared auto-connect; exit stop first). Avalonia
  call sites as WPF: bubble pop, bounce, subliminal/freeze timing, video hit/background vibe/FunScript. Panic key and tray stop
  zero the toys first (WPF PanicStopEverySurface). Haptics page: real settings (save only on change), premium-gated enable/connect,
  providers, addresses (#1310), panic/test/status; wizard connect/test. core-guards now rejects using System.Windows in Core.
- Evidence: HapticsCoreTests, HapticsHeadTests (incl. panic zeroes the toy), fail-proven; mock toy only, no real hardware; live gates
  shown without premium. Rows stay stub (toy cards/routing, pattern editors, premium live connect); feat-intiface-url wired.
  Review: FIX -> fixed.

## avalonia-port/lockdown-core: +650
- LockdownService / LockdownStrictHold / LockdownPauseRule moved to Core (git mv; WPF delegates; EscapeKinds + CostsChaster in Core);
  time left now runs on a UTC clock (a DST fall-back or clock set back no longer lengthens a lockdown; affects WPF too, logged).
  Avalonia runs a real lockdown: premium gate, consent, Activate, clock, 5-tap secret phrase; Emergency Exit slab per the oracle-deep
  rule (notice with exact steps and real time left, tripwire, Chaster hold; never ends/restarts). WPF refusals: global panic ignored
  (window text input still works), Stop/tray Stop everything, Exit, user window close, pause, settings palette; minimize trips the
  escape counter; crash recovery at startup; preset/strict-toggle hold.
- Evidence: LockdownTests (7: phrase under StrictLock, global-only key ignore, recovery, preset hold, slab, UTC clock, expiry),
  fail-proven; live sandbox lockdown and unlock. Row stays stub (badge, theme, Possession, Dose, exit games, syskey hook). Reviews:
  FIX -> fixed; final safety check ACCEPT.

## avalonia-port/remote-control-core: +966
- Core RemoteRelay (WPF relay protocol: endpoints, headers, PIN/pairing URL, 5 s poll, backoff, idle/expiry; sandbox loopback-only)
  and RemoteCommands; WPF delegates constants/PIN/URL/labels. Avalonia Remote Control tab pairs: premium gate, login, double
  waiver, code/PIN/QR, status, tiers, Stop, command log; share-avatar pushes at once.
- Escape integrity (oracle-deep, logged): disable_panic always refused; under Lockdown only enable_strict_lock refused; the waiver
  says a controller can never turn the panic key off.
- Evidence: RemoteRelayTests (fake relay) + RemoteControlTabTests, fail-proven; live gate/login dialog/tiers, 0 binding errors. No real
  relay pairing tested. Rows stay stub (controller overlay, session/feature commands, toasts, 409 recovery). Reviews: FIX -> ACCEPT.

## avalonia-port/awareness-observer: +730
- Legacy Awareness Mode title observer (WindowAwarenessService + AppClusterMap) moved to Core (git mv; WPF builds it with its user32
  reader and no filter, unchanged). Linux reads _NET_ACTIVE_WINDOW/_NET_WM_NAME (XWayland windows only). Avalonia applies
  AwarenessPrivacyRules before anything reads the title, logs included (decision logged); entitlement re-checked every tick, lapse
  stops it; consent-gated start; the tube speaks the preset line only (no AI here, nothing leaves the machine). Fixed null
  AppClusterMap tables without an override file.
- Evidence: WindowAwarenessServiceTests (9) + tube reaction test, fail-proven; live test windows only. Rows stay stub (OCR/keyword
  Awareness tab, v2 observer, privacy view). Review: ACCEPT + notes fixed.

## avalonia-port/she-listening: +360
- Core VoiceInputRules (armed rule, modes to run with entitlement/consent/availability, wake-phrase parse + variants, sensitivity
  maths); WPF AutonomyService.Voice and MainWindow.SheListening delegate. She's Listening tab wired over real settings behind the voice
  gate (Start/Stop, consent, revoke, mantras, calibrate notice, device/models folders); Stop/revoke cut voice lock-card capture.
  The mic never opens on this head (no voice-command consumer yet); armed shows an honest "not on this build" status (decision logged).
- Evidence: VoiceInputRulesTests + Vosk WAV test + SheListeningTests (fakes/WAV only, never the real mic), fail-proven. Rows stay stub
  (blocker: AutonomyService.VoiceCommands). Review: ACCEPT.

## avalonia-port/autonomy-core: +928
- Takeover decisions moved to Core AutonomyScheduler (gate with takeover free day re-checked before every action, cooldown, busy retry,
  jitter x time-of-day, idle trigger, mood/intensity weighted pick, announce roll + delay, stop generation); WPF AutonomyService
  delegates (mantra check order kept: no speech probe without mic consent). Avalonia runs Takeover (switch/Start-Stop, Lockdown hold
  with WPF message only, resume opt-in, state hero) with Flash/Subliminal/LockCard/Video/Bubbles/BouncingText. Panic stops Takeover:
  confirmed as WPF's real behaviour (oracle-deep, logged).
- Evidence: AutonomySchedulerTests (fake clock: entitlement, stop generation, busy, weighted pick, idle) + TakeoverTests, fail-proven;
  live Start -> three actions -> Stop. Rows stay stub (other actions, banner/countdown/audio, voice commands). Review: ACCEPT + notes.

## avalonia-port/companion-ai-core: +603
- AiService, BambiSprite prompt assembly, PersonalityService (+ModPersonalityPicks, CompanionQuota, CompanionProxyContract, AiTextHygiene,
  CompanionLinkIndex) moved to Core (git mv; WPF seeds the Core seams in App's static ctor). Avalonia tube chat sends through Core
  AiService on WPF's brain-off path: moderation on input and output, sign-in + AI access gate, loopback-only CCP_AI_BASE_URL (sandbox
  fails closed), one ModerationCounter with cooldown, no chat text logged. WPF prompt tests seed CoreMods.Service again.
- Evidence: AiServiceProxyTests (5), TubeChatAiTests, BambiSpriteCoreModsTests, fail-proven; live signed out: 0 proxy requests.
  Rows stay stub (CompanionBrain default route, thinking animation, warning dialog, local providers). Review: FIX -> fixed.

## avalonia-port/companion-brain-core: +380
- CompanionBrain slice 1: CompanionBrain, PromptAssembler, MemoryStore, CompanionSessionStore, ChatSession, CompanionTurn,
  ConversationRecall, ExplicitMemory, CompanionMemoryMaintenance, ConversationDelivery, ConversationRelationship moved to Core (git mv;
  head-only deps are static seams WPF seeds before building the brain; no-settings reads keep WPF's false default). Avalonia App.Brain;
  tube chat routes through it while UseCompanionBrain is on (WPF ChatInput.cs:772); ask cards off in this slice (on since companion-brain-tube, only when a card can follow).
- Evidence: TubeChatBrainTests (loopback fake, protocol 2) + ConversationDeliveryCardTests, fail-proven; live signed out: 0 requests,
  no chat text logged or stored. Row feat-companion-brain stub. Plan: ~/ccp-port/briefs/companion-brain-plan.md. Review: FIX -> fixed.

## avalonia-port/voice-commands + voice-commands-safety: +974 / +162
- She's Listening gets its consumer: WPF VoiceCommands moved to Core (git mv; WPF delegates via VoiceCommandHost). Avalonia Vosk wake
  loop + push-to-talk over PulseMicSource only with consent + armed + entitled; Stop/revoke/lapse/shutdown close the mic and cancel any
  command in flight (no re-prompt can reopen it); a cancelled session never starts parec. Panic aborts capture, loop stays armed; spoken
  safe word works under Lockdown (oracle-deep). Help and "What you can say" list only runnable commands.
- Evidence: VoiceCommandsTests (Avalonia WAV-fed + Core), SpeechEngine cancelled-token test, fail-proven; never the real mic. Rows stub.
  Split at the 1000-line cap. Review: FIX -> fixed.

## avalonia-port/webcam-core: +494
- Webcam slice 1: WebcamCalibrationData moved to Core (git mv; same file and format); new Core WebcamPipeline holds the frame-free logic
  verbatim (BlinkDetector thresholds + 2 s hold + cooldown, GazeSideClassifier hysteresis + fallback, OneEuroFilter); WPF
  WebcamTrackingService delegates (-330 lines, same capture thread, no new allocations). Capture stack decided (oracle): OpenCvSharp4
  + OnnxRuntime on both heads.
- Evidence: WebcamPipelineTests (9, synthetic streams, fail-proven); no camera opened. Row shell-webcam stub. Review: ACCEPT.

## avalonia-port/test-flakes: +69
- Three order/timing flakes fixed at the root: {loc:Str} bindings no longer overwrite text set from code on a language switch (a real
  product bug; WPF semantics), HoverPop's test steps an injectable clock instead of sampling wall time, and tests flush their private
  settings service so a queued save cannot land in the next test.
- Evidence: full CCP.Avalonia.Tests 5 runs in a row green; each fix fail-proven (language switch keeps code text, untouched labels
  still re-localize).

## avalonia-port/webcam-capture + webcam-capture-safety: +921 / +168
- Detectors (BlazeFace/FaceMesh/Iris) moved to Core FaceModels.cs (WPF delegates). Avalonia WebcamTracker: IFrameSource seam, OpenCV V4L2
  source (OpenCvSharp4 4.13 + OnnxRuntime 1.20.1; Linux natives only on Linux), frames only in memory, consent at start and every frame,
  each session owns and always releases its camera, an abandoned loop blocks a new Start until it exits. Blink Trainer tracker toggle.
- Evidence: WebcamTrackerTests (fake source), fail-proven; ldd clean; tarball +54.1 MB; live with the device unavailable. Never a real camera.

## avalonia-port/companion-brain-tube: +692
- CompanionBrain slice 2: WPF thinking bubble (Core ThinkingPhrases) and double bounce; ask cards after a reply and unprompted
  (CompanionAskService + Ask* moved to Core behind 7 seams WPF seeds); a card is promised only when one can be built; busy = session,
  video, lock card, bubble count, pop quiz; the ask timer stops on exit on both heads.
- Evidence: TubeChatParityTests (loopback fake, protocol 2, fail-proven x9); live ask cards reached the tube. Rows stay stub. Review: FIX -> fixed.

## avalonia-port/companion-brain-signals: +222
- CompanionBrain slice 3: MemorySignalWriter moved to Core (head sources via SourcesHook/DeferredSourcesHook, WPF wiring unchanged);
  Core MandatoryVideoScheduler.VideoStarted; Avalonia seeds SignalMirrorFactory (profile, level-ups, feature use, video starts, chat turns).
- Evidence: MemorySignalSeedTests + VideoStarted test, fail-proven x3; live memory.json profile. Row stays stub (bark echo, mantra/drain/wipe).

## avalonia-port/inbox-flyout: +612
- InboxItem/StartupQueueCore moved to Core; new Core StartupInbox (WPF StartupPresenter delegates). Avalonia StartupLadder parks passive
  cards (programs intro, wardrobe toasts) through the Core quiet rule; shell badge (99+, hidden at 0) opens InboxFlyout.
- Evidence: InboxFlyoutTests (3, fail-proven); render-all. ctrl-inbox-flyout wired, shell-inbox stub.

## avalonia-port/chaos-hub-state: +272
- Chaos slice 1: ChaosMetaStore + ChaosRanks moved to Core (WPF ChaosMeta delegates). Avalonia slot picker shows real saves/ranks/locks,
  erase with WPF confirm, persists the chosen slot; hub balances/rank/stats read the active save; overlay uses the Core ranks.
- Evidence: ChaosMetaStoreTests (incl. legacy migration), ChaosHubStateTests, fail-proven. Rows stay stub (no opener, shelves, ChaosModeService, waves).

## avalonia-port/blink-trainer-session: +777
- Webcam slice 3 part 1: Blink Trainer session on Avalonia (click-through overlay per screen, swap per blink, looped video, auto-stop,
  haptic, quest credit), tab Start/Stop/countdown/preview, CoreWebcam seeded (Devices Start tracking/Privacy/Revoke). Merge with part 2.

## avalonia-port/blink-trainer-session-safety: +338
- Blink Trainer review fixes (revoke deletes calibration, close-to-tray stop, panic during start, cached preview pool, haptic off-thread) and
  the oracle-deep decision: every panic and the tray Stop everything also stop the camera (consent kept, notice shown). Fail-proven.

## avalonia-port/chaster-bill: +918
- Chaster exit bill (tray/Settings Exit; panic, 18+ refusal and ungated first run skip it), import confirm on both preset paths, account badge,
  booked "+time" flash on the rail padlock (Core BookedFlashPlan). Faked-write secret tests now read an empty store, never the keyring.
- Evidence: ChasterBillTests (7, fail-proven), headless frames. Rows wired x4, receipt stub. No Keincheck run.

## avalonia-port/mantra-service: +499
- MantraService moved to Core (WPF seeds XP/quest/Chaster seams); Avalonia MantraWindow on App.Mantra with synthesised tones and a LibVLC
  drone at WPF's volumes; exit cleanup; mantra favourite in memory signals.
- Evidence: MantraServiceTests, MantraWindowSessionTests, fail-proven. win-mantra stays stub (no opener, animations).

## avalonia-port/webhost-live-url: +240
- WebHost live URL (CurrentUrl, NavigationCompleted, always-navigate Navigate); site buttons/Reload navigate; the HT catalogue lookup fires on
  completion at WPF's point; CatalogueLookup obeys the sandbox/loopback rule. Fail-proven tests; completion not seen live (no web engine here).

## avalonia-port/emidesk-ring: +310
- Core EmiCodexChapters (WPF EmiCodex delegates); the Avalonia Codex reads the shipped chapters and keeps its bookmark; EmiDesk summon count
  and placement through EmiState. Fail-proven tests. Ring/Book/Options and the Codex opener stay stub.

## avalonia-port/launcher-core: +999
- Launcher slice 1: Core LauncherCards + rules (WPF builds tiles from them); Avalonia LauncherWindow with the openable cards, the CC Labs
  door and WPF's close rule. Fail-proven tests; Keincheck live. Rows missing -> stub.

## avalonia-port/takeover-actions: +480
- Takeover performs Pink Filter pulse (handed back on every stop path incl. app exit), Bubble Count, Comment (AI or WPF-style Giggle preset)
  and seeded Mind Wipe; unavailable actions are never picked. Wallpaper picker and voice hint. TakeoverTests (7), fail-proven.

## avalonia-port/graded-intake: +397
- IntakePassService in Core (each head passes its entitlement hooks; Avalonia hooks them in AccountSeed.Seed); the Graded Intake tab paints
  WPF's four pass gates and Begin Intake's checks. GradedIntakeGateTests (4), fail-proven. The intake run itself isn't ported yet.

## avalonia-port/friends-drawer: +1002
- Core FriendsService (IUiTimer seam; WPF FriendsServiceApp); Avalonia friends rail chip + drawer (presence, requests, block/remove with
  confirm, report, add by code), loopback-only in a sandbox, disposed on exit. FriendsDrawerTests (10 methods, 15 cases; asserted, not fail-proven. audit-fix-ui adds AClickInsideTheDrawerKeepsItOpen, fail-proven in ~/ccp-port/evidence/audit-fix-ui/fail-proofs.txt). Rows stub.

## avalonia-port/chaos-shelves: +660
- Chaos slice 2: ChaosMeta/Upgrades/Boons/Lessons/Reveal in Core, ChaosBench; WPF run effects keyed by id; the Avalonia hub's shelves spend
  and save the active slot (one atomic save per buy); rendering never writes. Fail-proven tests. Rows stay stub.

## avalonia-port/webcam-gaze: +988
- Webcam slice 4 part 1: Core GazeEngine (WPF delegates); tracker gaze/face/head-pose feed with the WPF-profile calibration; Tracker Test
  and Quick Recal ported. Synthetic-frame tests, fail-proven. Merge with part 2.

## avalonia-port/sandbox-net-guard: +615
- Core SandboxNet: a sandbox sends every non-loopback HTTP/websocket request to http://127.0.0.1:0/ (refused, no DNS); ExternalOpener is the
  only link/file opener and refuses remote and UNC targets in a sandbox; SafeHyperlinkButton; scan tests block new bypasses. Fail-proven.

## avalonia-port/friends-drawer-fixes: +78
- Friends review fixes: dispose on exit, sandbox URL tests, presence marks the ask, remove/report tests, manual-tick timer test, ledger stubs.

## avalonia-port/intake-host: +574
- Graded Intake host slice 1: Core IntakeRun (latches, heartbeat, grading, caps, drafting, pass only on a parsed result; WPF delegates);
  Avalonia IntakeHostWindow + WebHost message bridge, not opened yet (page serving is slice 2). Fail-proven tests.

## avalonia-port/companion-brain-effects: +701
- CompanionBrain slice 4 part 1: AI command gate + 11 effect commands in Core (head Surface hooks); Avalonia executor and activities.

## avalonia-port/launcher-boot: +355
- Launcher slice 2: WPF's boot decision (Lockdown/18+/fresh install → panel), skip box, tray Back to CC Labs, second-instance handoff;
  the tube stays down during a hidden boot. Fail-proven tests; Keincheck live boot + handoffs.

## avalonia-port/web-assets-host: +282
- WebAssetServer: token-gated loopback HTTP over Resources/web (WebKitGTK has no custom scheme in Avalonia 12.1); Assets/web shipped with
  WPF's subset (+~158 MB). Hardened (constant-time token, per-port cookie, nosniff/no-referrer, symlink confinement). Fail-proven tests.

## avalonia-port/webcam-calibration: +993
- Webcam slice 5 part 1: Core WebcamCalibrationFit (WPF delegates); the Avalonia 16-point calibration window; Calibrate enabled. Merge with part 2.

## avalonia-port/webcam-calibration-fixes: +69
- A revoke during calibration stands; atomic calibration save (both heads); failed save keeps the old calibration. Row back to stub.

## avalonia-port/companion-brain-effects-safety: +319
- getbacktome follow-ups re-gated at fire time (3 per follow-up), every panic route on both heads cancels pending AI follow-ups and drops a late reply.

## avalonia-port/emidesk-ring-2: +822
- EmiSuggester + EmiDoors in Core; the Avalonia desk opens the ring with real openers (surface-less doors hidden); Options click-away via
  an X11 poll that keeps no key state; Book TAKE ME THERE. Fail-proven tests. Rows stay stub.

## avalonia-port/companion-awareness-brain: +229
- Awareness reactions take WPF's legacy AI-first route only with v2 off + consent + chat + AI; v2 on stays preset-only and sends nothing.
  Privacy tests (loopback), fail-proven. Arbiter chain stays WPF-only (decision logged).

## avalonia-port/chaster-ladder: +835
- Chaster tab ladder: clock, raffle card, top-ten scrap with opt-ins off by default (nothing posted until opted in); loopback-only in a sandbox.

## avalonia-port/intake-page: +655
- Graded Intake slice 2: Begin Intake opens the served intake page (origin-fenced, same-document); media served media-only and never from
  the profile; completion signals; pass only on a parsed result. Fail-proven tests; the page itself not run live (no web engine here).

## avalonia-port/calib-flake: +46
- Calibration window timing behind a TimeProvider; the test steps a manual clock (no sleeps). Full suite 3x green under load.

## avalonia-port/spoken-mantras: +577
- Spoken mantras: Core SpokenMantra + MantraVoiceService (WPF delegates); Takeover Spoken Mantra and the wake/PTT fallback; the mic opens only
  after her clip ends; panic/revoke close it. WAV + fake-mic tests, fail-proven.

## avalonia-port/ci-sweep: +126
- CI: Avalonia tests sharded (5 processes, completeness guard) to stay under the runner's memory; root-cause fixes for the Windows/Linux CI test failures on #1962–#1990. Native leak logged for follow-up.

## avalonia-port/her-room-chat: +716
- Her Room chat and memory diary live over the brain (Core CompanionRoomLogic + CompanionMemoryViewModel, WPF delegates); forget/wipe/switch-off
  really delete, incl. temp/backup siblings. Fail-proven tests.

## avalonia-port/flake-minepoll: +17
- Test-order flakes fixed at the source: a leaked auth token (MinePoll) and a shared awareness cooldown (AWindowChange). OpensPlays unconfirmed.

## avalonia-port/chaster-receipt: +652
- Chaster tab: own receipt panel with language re-render, calendar sheet, 2x2 preset keys (mood + limits refresh), 30 s hero tick; ticks only while visible.

## avalonia-port/chaos-waves: +448
- Chaos run engine (clock, end/Relapse, waves/acts, payout scalars) in Core as ChaosRunEngine; ChaosModels/ChaosRunEffects moved to Core; WPF RunTick delegates, behaviour unchanged.
- Avalonia ChaosHudWindow binds the Core ChaosRunState; runs still cannot start on Avalonia (no bubble field/draft/overlays); chaos-hud-window stays stub.

## avalonia-port/test-isolation: +214
- Assembly-wide per-test snapshot/restore of process-wide statics, CoreSettings and test-profile json (order leaks impossible by construction); optional seeded test order.

## avalonia-port/flake-tubechat: +12
- TubeChatBrain flake: HerRoom test leaked ChatMemoryEnabled=false through the shared settings.json; restored at the source.

## avalonia-port/main-20261005 (main sync #4): +23400
- Imports WPF main 4629b1f4a (7.0.3–7.0.5). Delta ledger docs/avalonia-main-sync-20261005.md: 137 commits = 47 ported-by-merge, 79 needs-port (31 lanes), 11 n/a.

## avalonia-port/main-20261005-compat: +32
- Test paths through SourceRoots, release button key 7.0.5, two tests updated to main's new catalogue.

## avalonia-port/port-shellcopy: +435
- Rail Back arrow + mouse/Alt+arrow tab history (TabHistory in Core; refused under Lockdown/tutorial); bug report on top; startup cards one at a time (stepped clock); "Monthly Recap".
- shell-layout n/a (Win32-only); side-art folded into the shared feat-adaptive-side-art row.

## avalonia-port/local-providers: +774
- Local AI providers (Ollama / OpenAI-compatible) moved to Core as brain transports; Avalonia Engine Room drawer chooses a provider, tests the connection and clears the conversation (confirmed); App.Ai disposed on exit; logged URLs redacted.

## avalonia-port/port-fx: +576
- Flash picks walk the whole folder before repeating (ShuffleBag in Core); Random image count + Fewest slider.
- Video card: monitor picker and Mercy "after N fails" in a keyboard-reachable More options fold; both Core schedulers use MercyAfterFails.
- Folded: drift speed, no-videos reason, spiral opacity curve, gaze camera at boot, Lock Card and Brain Drain folds.

## avalonia-port/port-chaster7: +352
- Circe's tab: setup line covers "adds off"/"nothing can count", raffle rows in a SplitRowPanel, ladder note; demo picks never save.
- Lock Card: Reset on typo inside More options. Leash, booked-pop tag and keys glow folded into their parity rows.

## avalonia-port/port-social: +1023
- Invites: rules, wire and reward ladder in Core; Exclusives invites card, header ticket (wired at startup), drawer link; EveryShellInitializerIsCalled scan test.
- Lobby folded into views-tab-available-subjects and win-launcher.

## avalonia-port/launcher-account: +567
- Launcher slice 3: account chip, tier badge, SP, mod pill (Core LauncherModMenu), panel card status/Stop/stats; Lockdown refusals stand in for the missing veil.

## avalonia-port/port-vault: +971
- Vault gate card on every padlock and the "See tiers" toast; Reconnect via Core PatreonReconnectRule; doors refuse under Lockdown; VaultOffer/VaultSale to Core.
- Exclusives: Prime-first shelf (Core ShelfOrder), IsShown hides Arcademy/Just Drop, width-fit columns.

## avalonia-port/port-brain7: +251
- Companion/awareness 7.0.3: pages widen, pop-out wakes the companion, listening bubble while the mic is open, chat stays open, cooldowns to an hour (Core AwarenessCooldownScale), Brain Drain in the preset editor.

## avalonia-port/launcher-games: +109
- Launcher slice 4: no game host on Avalonia yet, so tiles/mystery/return-poll wait for the first host; every launcher door refuses under Lockdown.

## avalonia-port/lockdown-veil: +509
- Launcher Lockdown veil (WPF look, keys too, breath only while visible); shell door table test (19 rows) closed 5 gaps; shell has no veil by WPF design.

## avalonia-port/package-verify: +71
- Tarball, AUR (host makepkg + bwrap) and GNOME 50 runtime smoke-verified for 7.0.5; stale PKGBUILD version fixed and enforced in smoke-tarball.sh; Flatpak build still blocked.

## avalonia-port/window-memory: +181
- Shell and views release their handlers on close/unload so MainShellWindow instances are collectable (user fix; ShellMemoryTests).

## avalonia-port/chaos-overlays: +422
- ChaosOverlayWindow on Core run types; ChaosRunHost runs HUD + countdown -> 4 Hz clock -> recap (no payout); panic/tray/exit end it without arming double-press exit. FALL IN not wired; rows stay stub.

## avalonia-port/port-misc: +425
- Quests: Takeover time credited while minimised; cards and streak header quote what the completion pays (Core StreakPaidOn, shield-aware).
- Presets drop sniffing, programs active-mod-first, Deeper opacity % labels; 7 rows folded.

## avalonia-port/port-remote: +505
- Controller leave hands back the panic key and the controller's own strict lock; remote haptics play and stop on panic (both heads) or leave; late commands refused after panic/leave.
- disable_panic refused with WPF wording; faster polling while busy; HUD pill and live preview folded into shell-remote-control.

## avalonia-port/rows-subliminal: +112
- feat-subliminal whisper: new whisper cuts the last; Stop/panic silence it and drop pending card/Reset (Core CoreAudio.PlayStoppable).

## avalonia-port/rows-emi-codex-book: +357
- EmiBook deck/demos/pixel canvas to Core (WPF blits via EmiPixelBitmap); Avalonia book: real deck, demo clock only while open, reduced-motion still, Complete Guide; codex door opens at the bookmark.

## avalonia-port/rows-audio-devices: +536
- Settings Audio + dashboard card share one binder (dials, ducking with force-unduck, live volumes, sync pair, output picker routed to one-shots/layers/video, Test Audio, help card); rows wired.

## avalonia-port/rows-achievements-tab: +606
- Achievement tiles: stagger, hover tilt/lift/pop, unlock reveal + gated burst; FX timer only while moving and shown. Season Recap re-view (Core SeasonRecapStore). Rows wired.

## avalonia-port/rows-tutorial-help: +1085
- HelpLoops kit + FlashImages/PinkFilter scenes hosted in the ? popover (visible-only frames, motion levels); 21 scenes and the tour (TutorialService) remain.

## avalonia-port/main-20261008: +513
- Main sync #5: WPF main 0d9e83383 merged (breakout web fixes, descent ceremony retirement, descent_auto); delta ledger docs/avalonia-main-sync-20261008.md.

## avalonia-port/main-20261008-compat: +25
- Retire the Avalonia descent ceremony; descent_auto golden tests; WPF scan test adapted to the typed SyncBody.

## avalonia-port/spiral-overlay: +599
- Spiral overlay on Avalonia: per-monitor click-through windows, background decode, opacity curve shared with WPF via Core SpiralFrames, engine/session/panic wiring.

## avalonia-port/rows-pink-mindwipe: +592
- Mind Wipe plays on Linux (LibVLC, WPF 120 ms overlap loop measured gapless); Core CoreMindWipe run rule shared with WPF; pink tint follows the dashboard tile.

## avalonia-port/rows-popups-unlock: +301
- Achievement and item-unlocked passive toasts (X11 override-redirect, device-pixel placement), chime, Discord share in Core; rows wired.

## avalonia-port/rows-floating-text: +357
- FloatingWord overlay (WPF keyframes) thrown by friends drawer actions; bouncing text corner_hit achievement and real-time topmost re-assert (measured 0.4-0.7 s).

## avalonia-port/launcher-fx: +1025
- Launcher FX on one visibility/focus-gated frame clock: spirals and ambient, hover lift/tilt/glow, exit beat with guard, edge glint, fade, sounds and melody (Core).

## avalonia-port/rows-splash-startup: +222
- Splash fronts real launches with WPF steps/percentages; failed startup logged and exits non-zero; timing within noise of no-splash.

## avalonia-port/rows-feat-bubble-count: +200
- Bubble Count waits behind video / lock card / quiz like WPF TriggerGame; panic and stop drop the waiting game.

## avalonia-port/rows-lockdown-tab: +325
- Lockdown title-bar badge wired (clock, hide mask, click/Enter -> tab); Emergency Exit breath (off under Photosafe, motion Off, minimised); quest hint + Hide-clock toggle.
- 3 shell tests, 9 itemised fail-proofs; live evidence rows-lockdown-tab/; row stays stub (theme, Possession, Dose, Strict greying, EE games, system keys).

## avalonia-port/help-loops-kit: +988
- Help loops kit finished (Back blur, rounded corners fixed) + BouncingText, BlinkTrainer, KeywordTriggers, Audio, Scheduler scenes (7/23).

## avalonia-port/rows-presets-tab: +448
- Presets session detail matches WPF SelectSession (feature line, minutes, XP multiplier + colour, summary, spoiler lines).
- Reveal Details behind WPF's three warnings (9 languages); Community Catalogue chip opens the catalogue (click/keys, focus-visible).

## avalonia-port/rows-descent-migration: +325
- Main sync #5 fc9640ff7 ported: the +10% XP bonus only for migrated accounts (Core XpBonusFor, applied by ProgressionBank); the server's migration confirmation shared in Core (DescentMigrationAck, called by SyncPush and WPF); own-card badge + (+N%) readout (DescentReceipt moved to Core).
- abac4fa02 needs-port (lane descent-migration-offer): offer acceptance and ApplyChoice stay WPF-only, decision B (docs/avalonia-decisions.md).

## avalonia-port/rows-takeover-ui: +165
- Takeover tab: WPF copy restored, Open-models-folder button, Configure-in-Settings -> Devices, no-voiced-mantras chant hint; shell-takeover-ui -> wired.
- TakeoverTabUiTests (6 fail-proofs) + Keincheck live run; still stub: Mantra Chant playback, wallpaper hooks.

## avalonia-port/rows-feat-video: +141
- Length filter (min/max) at selection via Core MandatoryVideoScheduler.KeepByLength; VideoMetadataCache + NoVideosReason git-mv'd to Core (managed LibVLCSharp 3.8.5).
- Folded 854ac954a: the no-videos dialog names the length filter and range when it emptied the library; tests fail-proven, live evidence in rows-feat-video/.

## avalonia-port/panic-surfaces: +265
- One PanicSurfaces registry: panic key, tray and safe word call StopAll once (WPF order); id/order test and source-scan guard so new starters register.

## avalonia-port/rows-quests-tab: +444
- Quests: QuestComplete haptic post + on-tab completion burst (shared EventBurstLayer, Achievements reuses it); season title shimmer + gradient-panel particle drift on a 24 fps clock that runs only while shown. Rows stay stub (voice line, off-tab burst, stamps, art/punch card missing).

## avalonia-port/rows-gate-fx: +1010
- PremiumGateFx (fog/padlock glow/sheen on 8 gates), row sweep, perimeter comet, card sheen adorners; effects park off-screen; host gaps listed.

## avalonia-port/rows-split-card: +305
- feat-split-card (stays stub): breath, glow, sweep, title and hover now follow the MotionFx/PerformanceProfile/window-focus gates; off halves dim; each half has its own "?" (HelpPopover); rings are rounded. `SplitFeatureCardFxTests` fail-proven per gate.
- Still missing: DashboardCardDepth, greyscale HalfMute, live re-gate on a settings change (shell-dashboard-fx caller).

## avalonia-port/rows-feat-flash: +411
- Flash Drift & Bounce plus the speed slider (#1265): picker on the card, windows moved on one shared tick. Core gets FlashMotion, FlashDrag and FlashPendulumRig by `git mv`. FlashDriftTests: 4 tests, each seen to fail when broken.
- Ownership seam PrizeOwnership: unowned in Release, CCP_PRIZE_GRANTS override in Debug only. New row svc-prize-ownership. feat-flash stays stub; the missing list is in the row.

## avalonia-port/rows-spiral-tab: +285
- SpiralRoom moved to Core; the Avalonia Spiral tab and rail row now read the real fuse and settings (live fog T-minus, phase pulse and flare, gold-ellipsis rail row). SpiralRoomShellTests is fail-proven 3 ways and checked live in Keincheck.
- Both rows stay stub: the block and withhold services (DescentService, DescentMigrationService) are still WPF-only, so the Spiral/embed state, FIRST LIGHT, SFX and the bark are still missing.

## avalonia-port/rows-haptics-tab: +713
- Haptics VMs and their builders moved to Core and are shared with WPF. The Avalonia Haptics page now uses real chips, routing rows and toy cards, plus every Phase F/DSP/DtRH/sync dial and the pattern lab.
- views-tab-haptics and shell-haptics move from stub to wired. One fail-proven headless test (mock toys only) and a sandbox live check.

## avalonia-port/rows-settings-general: +803
- Settings General: display card (monitor picker, countdown), XDG autostart run-on-startup with start-hidden warning, Deeper switch hides the rail button; views-settings-general stub->wired.
- Settings Performance: Back Room FX combo; changing motion level updates loops already showing (5 controls); DND picker/guard still missing, row stays stub.

## avalonia-port/rows-lock-card: +303
- feat-lock-card wired: repeat-shape rows (random / match by length) on the panel; Core `LockCardScheduler.ResolveRepeats` (moved from WPF) sizes the Avalonia card.
- Test fail-proven 3 ways; live: length-mode card shows 29 repeats and ESC closes it.

## avalonia-port/intake-bridge: +747
- Graded Intake slice 3: loom-save, save-image, need-remote, speech bridge, audio-web pack request on Avalonia (8 files to Core); panic closes the intake and its mic.

## avalonia-port/help-loops-b: +744
- Help-loop scenes SpiralOverlay, GazeMinigame, Video, BubblePop, BrainDrain, MindWipe and BubbleCount ported line-for-line from WPF (14 of 23 done). Headless frames are saved per step, and the live BubblePop `?` shows the loop.
- Six of these scenes still have no Avalonia entry point: the SplitFeatureCard help ids and the GazeMinigame help attach.

## avalonia-port/lockdown-hidden-fix: +176
- Dialogs no longer crash with a hidden shell owner (ShowDialogSafe, ownerless fallback like WPF); Lockdown activate from tray works.
- Headless test hides the shell and activates via the real button; fail-proven.

## avalonia-port/rows-name-dialogs: +342
- Trainer Card rename/delete wired through DisplayNameDialog -> CoreAccount -> Core V2AuthService; delete signs out without a push.
- UsernamePickerDialog documented as unreachable on WPF too (dead BtnQuick*Login handlers); both rows stay stub with exact gaps.

## avalonia-port/rows-mini-player: +198
- win-mini-player: GIFs now loop (SkiaSharp decode + frame timer) and load failures show a MessageDialog notice before closing; the row stays stub because there is no opener.
- MiniPlayerPreviewTests: 2 tests, each fail-proven.

## avalonia-port/flake-layout: +24
- Root-caused the Blink/ChasterRaffle/EmiBook shard flakes to one test setting up headless drawing without Skia (first setup wins per testhost); fixed it.
- Added a source-scan guard that every Avalonia test platform setup uses Skia drawing; fail-proven with a fixed order seed.

## avalonia-port/rows-media-history: +547
- win-media-history: the Media Log reads the shared MediaHistoryService, now in Core and fed by both heads. Added audio filter, thumbnails, remote Copy link / Go to Source, LibVLC video and SkiaSharp GIF preview, and Clear behind a confirm. Row stays stub: Load preview, Brain Drain producer and the MediaFailed card are still missing.
- Tests fail-proven (window, service, scheduler LastVideoPath); live evidence in evidence/rows-media-history.

## avalonia-port/rows-blink-trainer: +208
- Blink Trainer stage plays video picks (VLC), status dot breathes while running, help popover attached, hidden page parks all work.
- Duplicate parity rows merged; both rows stay stub with the remaining list.

## avalonia-port/rows-quiz-report: +162
- win-quiz-report: past-quizzes list (trends + runs -> QuizReportWindow, one at a time) ported; still stub because both heads hide BtnStartQuiz.
- QuizPastRunsReportTests, fail-proven x3.

## avalonia-port/rows-season-recap: +482
- Season recap card follows the active mod accent (Core RecapPalette, shared with WPF) and the foil shimmer is back on WPF's curve; it runs only while visible. The recap window's backdrop and pills follow the mod too.
- Copy card and Share on X put the PNG on the clipboard (checked live on Wayland). All 3 recap rows are now wired.

## avalonia-port/help-loops-c: +812
- Help-loop scenes LockCard, FocusGaze, IntensityRamp, Presets, Subliminals and WebcamCalibration ported (20 of 23). Each renders every step and lights its chips in order on the stepped clock.
- New WebcamCalibration and IntensityRamp pixel tests, both fail-proven. Live: Settings Subliminals `?` plays the loop.

## avalonia-port/rows-feature-intro: +219
- win-feature-intro: cards now yield to a startup modal or update dialog; tour callers wired on 10 tab cases plus the Dashboard launch (daily-free / one-account) via StartupLadder; 4 fail-proven tests; row stays stub with the missing callers listed.
- Test profile marks every intro key as spent, so a card no longer leaks across shell tests and holds passive surfaces in later ones.

## avalonia-port/rows-content-dialogs: +332
- Tube Personality submenu with the CCBill explicit-content acknowledgement gate; content-policy warning on the moderation counter; both rows stub -> wired.
- TubeContentGatesTests (fail-proven x4); other acknowledgement callers still head-blocked.

## avalonia-port/rows-settings-data: +296
- Settings · Data offline mode stops the heartbeat and greys the login and browser controls like WPF, including at startup; views-settings-data stub→wired.
- Phrase import uses WPF's Import/Cancel confirm, with Enter on Cancel (supervisor decision).

## avalonia-port/rows-grace-pause: +117
- win-grace-pause-overlay stub -> wired: Resume is a keyboard-reachable Button pill (0.85/0.7), the countdown/auto-resume run on a TimeProvider, and the panic key is tunnel-routed so it beats a focused Resume (supervisor-approved)
- Headless test covers Esc -> card -> Tab+Enter, mouse hover/press/click, 60 s auto-resume, and panic key Enter beating Resume, each check fail-proven

## avalonia-port/rows-emi-windows: +325
- EmiDesk ring: Core EmiRingLayout (git mv, tests now on Linux), card art plus icon plates, shipped pixel font, ring sounds on open/close; click/hover pat sound and pat count.
- Options/ring live Keincheck run recorded; rows still stub with exact remaining lists.

## avalonia-port/rows-haptics-setup: +70
- win-haptics-setup stub -> wired: a headless test drives the wizard from the Studio rack (Mock: gate refusal, connect, device list, Done, Test buzz, page re-read), fail-proven on 5 breaks; no product code changed.
- Live sandbox run: premium veil matches WPF; with the veil lifted the gated connect is shown, and settings.json records Provider=Mock (evidence/rows-haptics-setup).

## avalonia-port/help-loops-d: +686
- Help-loop scenes ScreenOcr, RemoteControl and SessionEditor ported (17 of 23). HelpVideoWindow now shows the loop like WPF TryShowLoop, and the Session Editor ? opens it.
- Rows stay stub: the help-loops-c scenes are pending, and the ScreenOcr/RemoteControl ? buttons are not attached (SetHelpContent not ported).

## avalonia-port/rows-corner-gif: +502
- win-corner-gif wired: standalone corner-GIF overlays (CornerGifOverlay, seeded CoreCornerGif) with stagger, crash sentinel, panic surface `corner-gif`; CornerGifOverlayTests fail-proven 4 ways; live X readback in evidence/avalonia-port/rows-corner-gif/.
- Still missing: session-scoped corner GIF in SessionRunner, so the Presets corner-GIF option stays hidden; live panic not reproduced (headless only).

## avalonia-port/rows-local-ai-wizard: +641
- Local AI setup wizard wired: OllamaSetupService moved to Core; detect, start, pull, smoke test and save work like WPF; Linux has no auto-install, so the user installs Ollama and Continue re-detects (decision logged).
- The head seeds EnsureServerRunning and stops only the `ollama serve` it spawned on exit; 5 faked tests, all fail-proven except the PATH lookup.

## avalonia-port/rows-mantra-window: +277
- win-mantra: five storyboards stepped on a clock, panic surface `mantra`, Programs mantra-task door; row stays stub (TodayTaskList unbuilt, failure MessageBox dropped).

## avalonia-port/rows-webcam-windows: +230
- Webcam loading splash wired to tracker startup progress (shell init, error/close paths); calibration Verify live gaze cursor; quick recal on calibrated monitor.
- 4 fail-proven headless tests; live sandbox splash check (no camera opened); bubble test, mouth checks, quick-recal hotkey still stub.

## avalonia-port/rows-profile-dialogs: +360
- Customize dialog: 12 scene banners + 12 preset avatars from Core CosmeticsPool, pin tiles draw achievement art (artless pins dropped, as WPF), tiles take Enter/Space; titles use Core Achievement.TitleName.
- Wardrobe editor stage paints the chosen banner (top-aligned like the hero); both rows stub -> wired.

## avalonia-port/rows-pink-rush: +341
- win-pink-rush stub -> wired: engine- or session-armed 10-min roll, 60 s 3x XP (Core PinkRushRules shared with WPF), popup + wash; panic/engine stop end it.
- PinkRushTests, 8 fail-proofs (logged); no live rush (random 10-min trigger).

## avalonia-port/rows-attention-check: +60
- ctrl-attention-check: WPF never starts AttentionCheckService, so the ring stays unshown on both heads. A parity scan test (fail-proven both ways) pins this. Row stays stub with corrected notes.

## avalonia-port/help-attach: +224
- Every WPF tab ? (33 SetHelpContent buttons + Intake Pass) is now attached from shell startup through an x:Name table; all 23 help loops can be opened (ctrl-help-popover, feat-help-loops: stub -> wired).
- New HelpButtonsTests: a WPF-source table scan, plus a shell walk that clicks every ?; fail-proven.

## avalonia-port/reset-close: +105
- The test isolation hook closes app windows a test left open and resets StartupLadder, so passive startup cards no longer leak between tests (P52).
- Proven by a fixed-order leaky pair (LeakedWindowIsolationTests). B fails with the close commented out.

## avalonia-port/rows-settings-account: +659
- Settings · Account: provider cards, login/logout/link, Patreon reconnect, Chaster card and presence switch are wired. Backup/restore/export are still hidden (row stays stub).
- 4 fail-proven shell tests on fake providers; ProviderLinkResponseRules moved to Core.

## avalonia-port/rows-settings-devices: +196
- Settings → Devices camera picker and ↻ live over V4L2 sysfs (metadata nodes skipped, WPF log lines verbatim); row stays stub
- WebcamDevicePickerTests (fake sysfs, fail-proven); live props in evidence/rows-settings-devices

## avalonia-port/rows-companion-editors: +478
- Prompt editor: Save keeps only edited boxes (`ActivePersonaPromptText` moved to Core), unticking clears the community prompt, and the X asks Save/Discard/Cancel (`btn_discard` added in 9 languages).
- Attention style editor: Test spawns a bouncing topmost test target that panic closes (`attention-test` surface), and the preview shadows are back. attention-target-editor is now wired; the prompt and phrase editors stay stub.

## avalonia-port/rows-mod-picker: +43
- dialogs-mod-picker re-audited: unreachable on WPF too, parity holds; row stays stub with that note (x:Names match plus port-only TitleBar/TxtDownload).
- Stale WPF cite MainWindow.xaml.cs:610-611 -> 587-588 (ledger + 3 comments); stale WPF first-run comment corrected.
- Cherry-picked CornerGif frame-stepping + SettingsService leaked-save disarm flake fixes (from rows-companion-editors).

## avalonia-port/rows-friends-drawer: +452
- Friends drawer card: Invite / Poke / Send a watch actions with their inline pickers over Core FriendsService (Goon/chess tiles shut: hosts WPF-only).
- Results after fold fly over the chip's window; folded drawer unsubscribes; poke hover; shut-tile string in 9 languages.
- 2 headless tests over a fake wire (fail-proven x5 and x3; the second opens via the rail-chip click and checks the folded-drawer word); rows stay stub (feed, trails, tables, leash, juice, rail-chip extras remain).

## avalonia-port/rows-program-enroll: +135
- ProgramEnrollDialog headless test (contract gate, Strict unavailable, clock results, Escape, Cancel), each fail-proven; row stays stub (caller, run panel, session runner missing).
- Decision logged: Enroll caller lands only together with the run panel (no lapsing enrollments).

## avalonia-port/rows-asset-submit: +289
- dialogs-asset-submit wired: preset Share + session rack ☁ open AssetSubmitDialog -> Core CatalogueClient submit/record/badge/toast; auth + affirmation gates as WPF (AssetSubmitWireTests, 4 fail-proofs).
- Live (sandbox) limited to listing/disabled state; mod (MEGA) branch still waits on shell-mod-catalogue.

## avalonia-port/rows-companion-workshop: +465
- Workshop Library cell: the per-mod video link editor now works on Avalonia (add/preview/delete/save), and its rules live in Core `VideoLinkPool`, which WPF also uses.
- Roster cards show each companion's level, name, tooltip and an accent ring on the active one; switching companion and assigning a personality are still missing (stub).

## avalonia-port/rows-deeper-editor: +583
- Deeper editor plays local audio through LibVLC (clock, seek, end, real length, panic surface). Editor and player previews use Core's host allowlist on every hop.
- Player: Create new and Open in editor work (re-read, one editor per file, recent). The dialog picker remembers the last folder.

## avalonia-port/audit-fix-media: +382
- Audit fixes for #1778/#1843/#1933/#1932/#1935: overlap layer loop, media-folder fallback, panic-safe grace pause, bubble count LibVLC skip, hermetic Buttplug test.
- Each fix has a real-path test proven red by a break (evidence/audit-fix-media/fail-proofs.txt); gate GATE-OK.

## avalonia-port/rows-companion-cards: +668
- Her Room: live attention gauge (AttentionCopy/ConstellationMath shared from Core), runtime dormant constellation with shimmer, chat History transcript window, SyncBrain on provider pick; "patreon" key opens Settings/Account like WPF.
- Rows: attention-gauge and constellation are wired; chat-threshold (link chip, wheel relay) and hero-card (tutorial chip) stay stub.

## avalonia-port/rows-inbox: +317
- shell-inbox: server announcement routed via the startup Inbox (Core V2AuthService fetch + account dismissal), row/popup dismiss record both halves; 3 tests, fail-proven x4.
- Row stays stub (modal ladder, intake nudge, other posters).

## avalonia-port/audit-fix-safety: +257
- Closes 7 still-open P1/P2 audit findings (#1790, #1987 x2, #1982, #1956, #1833, #1937): rebind double-capture, a CRLF scan test, a late follow-up reply's effects after a panic (both heads), a refused pink filter overwriting settings, Lockdown counted as busy, a held lock card replaying on stop, and remote session-end stops moved to the UI thread.
- Each fix has a fail-proven headless or Core test.

## avalonia-port/audit-fix-misc: +273
- Closes seven open P0-P2 audit findings (#2096 #1763 #1908 #1456 #1318 #1313 #1289): the update tooltip key, the passive recap no longer staying on top, the rack toolbar layout, locked sessions shown as "Coming Soon", `**bold**` in help text, and the freeze pre-tick in the bug report.
- Each Avalonia fix has a test that was seen to fail when the fix was broken; #1763 was checked with a string script.

## avalonia-port/flake-followup: +12
- Fixes the intermittent Core test `AiCommandGateTests.AFollowUpThatIsNotCancelledStillRunsItsReplysCommands`, which failed in the rows-roadmap and rows-takeaway push gates. The test now waits for the reply's flash instead of asserting the moment it releases the AI. Test-only change.

## avalonia-port/rows-roadmap: +149
- shell-roadmap stub -> wired: completion chime (WPF Exclamation) and keyboard-reachable nodes with a focus border; RoadmapNodeTests (2, fail-proven); live nodes screenshot in evidence/avalonia-port/rows-roadmap.
- Mod-gated Roadmap sub-tab visibility recorded as missing under shell-ui-updates.

## avalonia-port/rows-takeaway: +70
- shell-takeaway: sample Takeaway orders replaced with WPF's empty state (no drawer/door on this head); headless test fail-proven, live sandbox check, binding errors 0.
- Row stays stub: drawer, door, chips, tray and actions still missing.

## avalonia-port/audit-fix-ui: +194
- Closed the open P1/P2 audit items from #1974 (hint relocalises), #1976 (drawer click-inside), #1914 (cosmetics ride every sync; empty only as the explicit clear), #1899 (patreon -> Settings · Account), #1847 (right-dock input cut), plus 2 doc corrections (#1976, #1837).
- Each fix has a real-path test proven to fail when the fix is broken (evidence/audit-fix-ui/fail-proofs.txt); gate OK.

## avalonia-port/rows-settings-perf-emidesk: +999
- EMI Desk summon chord: Core rules (WPF delegates), X11 XGrabKey arm at startup/rebind, live rebind from Settings; row stays stub (Offers/Glass/Spice unread).
- Performance: DND app picker (X11 window owners) + one Core DndGuard for both heads; TierBadge follows live motion change.

## avalonia-port/rows-mod-creator: +759
- Mod Creator Pools & Triggers + Personalities panels ported (manifest round-trip as WPF); achievement slots from Core ModAchievementSlots (registry, not 16 hand-listed).
- Re-export keeps Advanced manifest sections; row still stub (Advanced/UiArt/ArtFraming/Barks/Mantras/EventAudio/Portraits/Emotes remain).

## avalonia-port/rows-emi-dock-picker: +193
- EmiRingPicker on Core EmiSuggester/EmiState pins + EmiTargets catalogue + ring card art (views-emi-ring-picker -> wired).
- EmiDock mini face mirrors the widget via EmiDeskWindow.FaceChanged; knock (TryKnock/EmiKnockMachine) still missing (ctrl-emi-dock stays stub).

## avalonia-port/rows-gaze-minigame: +329
- Gaze minigame now plays image rounds on WebcamTracker. Settings and pack library moved to Core (git mv). Flash is paused during the game, vibration goes to the haptics service, and panic closes the game.
- Row stays stub: video rounds, animated GIFs, 4 reward effects and the reward audio are still missing.

## avalonia-port/rows-chaster-tab: +679
- Circe's mood meter, CirceSays bubbles (tab page + rail) and the shell's CirceLines picking; rail chip pulse/pip pop/peek spring-in/idle swing+breath.
- Ledger: ctrl-chaster-rail-chip and views-chaster-circe-mood wired; duplicate views-tab-chaster row merged.

## avalonia-port/rows-studio-lab: +195
- Studio rack: 120ms panel crossfade and 260ms state-dot pop ported from WPF, tested on a stepped clock (fail-proven).
- Lab, Lab-tab and Brain Drain rows re-audited with exact missing lists (still stub).

## avalonia-port/panic-order-test: +60
- PanicSurfacesTests checks WPF-cited ordering pairs, unique ids and a safety-critical set instead of one exact list, so branches that add a surface stop conflicting on it (P53).

## avalonia-port/rows-exclusives: +400
- Vault: tier plates read the access gates, 3px/4px tier livery, two coming-soon teasers, and one visibility-gated 24fps clock for padlock breath, FREE TODAY pulse, Ken Burns and sheens.
- Duplicate shell-exclusives ledger rows merged; both rows stay stub (re-tint, hover lift, launches and hero art still missing).

## avalonia-port/rows-profile-cards: +663
- Profile bubble paints identity, tier and account doors and reacts to XP/level/achievements; the public-profile door is live (row stays stub).
- Profile FX wired: card stagger, search glow, OG border loop gated per P01; spiral/vat/faucet blocked on DescentService, privacy panel on a sync payload change (P44).

## avalonia-port/rows-play-tab: +487
- Play wall matches WPF's 2026-09-18 layout: GAMES zone with Breakout covers, Graded Intake states and FREE TODAY stamps; Devices and Loom buttons open the right section. Game buttons stay disabled because this head has no host for them.
- Start caret "Jump right in" (RandomizeAndStart) ported, refused while a session holds the lock; tests fail-proven and checked live.

## avalonia-port/rows-session-io: +629
- Window-wide file drop (session/preset/mod/assets/zip/folder/unrecognised + overlay) over a new Core `DropRules` that WPF also calls; a single media file offers Add to Library only (decision logged).
- Session-lock ribbon pulses on a refused change; `WindowDropTests` (8, incl. session/preset/mod) + `SessionIoTests.RefusedChangePulsesTheLockRibbon`, all fail-proven.

## avalonia-port/rows-programs-tab: +362
- Programs browse cards + intro popup show WPF's banner/sigil/plate art via new Core ProgramArtPaths (shared with WPF ProgramArt).
- Enroll/run panel still unwired (decision A); slice plan in briefs/programs-run-plan.md.

## avalonia-port/rows-she-listening: +132
- She's Listening: title-bar mic privacy pill (click/Enter = disarm), armed-disc pulse, open-models button; 5 fail-proven assertions in SheListeningTests.
- Rows shell-she-listening / views-tab-she-listening stay stub (sherpa KWS, bark lines, grammar gaps).

## avalonia-port/rows-keyword-triggers: +606
- Awareness custom trigger list (add/import/9 row editors), OCR combos persisted, OCR access gate + panel resync; 9-language strings; 3 fail-proven tests.
- Rows stay stub: KeywordTriggerService/ScreenOcr/KeywordHighlight runtime (no Linux keystroke source) not ported.

## avalonia-port/rows-patreon-subscribestar: +234
- Premium celebration is back: a Patreon or SubscribeStar tier change, or the launch re-check, shows one card per tier (none for an invite week), via the presenter or Inbox. 2 fail-proven tests; live sandbox run showed the card.
- shell-patreon and shell-subscribestar stay stub, with an exact missing list (live tier rise, unlockables/programs/profile-bubble repaint).

## avalonia-port/rows-descent-fuse: +933
- Fuse rail chip (tiers, flash-out at zero, Spiral Room door) and the zero-show stage visual are ported; both rows are now wired.
- 2 fail-proven headless tests and a sandbox Keincheck run. win/shell descent-fuse rows stay stub (openers, sounds, dimming).

## avalonia-port/rows-awareness-privacy: +661
- Z5 privacy card on real settings: consent-gated dial, deny/allow chips, pause, wipe; WPF older-pipeline band whenever awareness is on (v2 observer still missing; decision logged).
- "Set up local" opens the local-AI wizard (untested); tests can no longer read the real desktop (X11ActiveWindow.Disabled); duplicate awareness ledger rows merged.

## avalonia-port/programs-run-1: +346
- Avalonia builds ProgramService at startup in load-only, read-only mode (CHECKPOINT A). It never writes programs.json, does no rollover and runs no timers, and it is disposed on exit. WPF's ctor path is unchanged.
- 7 tests, all fail-proven: a byte round-trip of a WPF-shape fixture under a pinned time zone, plus no-write, temp-recovery, corrupt-file, no-lapse, startup-audit-skipped and startup-scan checks.

## avalonia-port/rows-deeper-hub: +883
- Deeper hub: open/play/context menu/keyboard selection/two-step delete with undo + trash, submission badge, welcome Demo/Tour/Dismiss, row hover lift; shell-deeper-fx now wired.
- Still stub: import, watcher, duration probe, demo seeding, tutorial, webcam card, sticky accepted toast.

## avalonia-port/rows-enhancements: +846
- Enhancements tab draws WPF node states, header, secret cards, owned-glow breath, gradient drift and hover pop. XP multiplier and time math moved to Core SkillTreeRules (WPF delegates).
- shell-enhancements-fx is wired. Still missing: purchase click, the PRO panels, the lucky toast.

## avalonia-port/programs-run-2: +867
- Read-only Programs run view of a saved enrollment; stale days read LAST SAVED, not TODAY (CHECKPOINT A); no lifecycle/session controls, no writes.
- ProgramsRunViewTests (shell path, 7 breaks fail-proven) + Keincheck temp-profile screenshot with programs.json byte-identical.

## avalonia-port/rows-dashboard: +987
- Dashboard FX funnel wired (RefreshFx on motion/performance/window changes, vault CTA breath, FeatureCard parks at peak); browser fold and billboard ported (BrowserFoldRule and DashboardBillboard moved to Core); billboard and fold rows now wired.
- Still stubs: dashboard-fx (? box, logo, rail, browser sweep), hero-fx (untouched).

## avalonia-port/rows-login-phone: +684
- Link phone dialog is real: Core mobile-link code, QRCoder QR, countdown and expiry. Sign in via Web polls Core V2DeviceCodeService, which moved from the WPF head with `git mv`. dialogs-login and dialogs-link-phone are now wired.
- Unopenable sign-in links are copied with WPF's prompt. The home Discord button opens the invite. 5 headless tests on a stepped clock, all fail-proven.

## avalonia-port/rows-cloud-remember: +267
- shell-remember stub -> wired (Core RememberedConfig, refusal mid-session, 9-language tooltips, fail-proven headless test, live ☆->★).
- shell-cloud-backup kept as a stub by decision (P44): server contract pending, cross-OS path translation not designed; missing list recorded.

## avalonia-port/programs-run-3-0: +220
- Core schema-skew seam for programs.json: `Extra` at three levels, a `SchemaVersion` stamp that forces read-only, `IsReadOnly`, and a stepped `Now` clock. WPF's bytes are unchanged.
- Logs checkpoint-B decision (c); Avalonia still calls `CreateReadOnly()`.

## avalonia-port/rows-shell-rails: +592
- Dashboard FAVORITES + RECENT rail ported: art chips, pin menus, RECENT from ShowTab, lock repaint; Core FavoritesRailRule/Art moved with git mv; premium-rail stand-in deleted.
- Banner One Account link is a real link and retires its beat; Just Drop and the rest of the marquee remain stubs, with the missing pieces listed in the ledger.

## avalonia-port/programs-signals: +479
- Quest signals now wired: LockCard, Pink Filter/Spiral minutes (1 s tick only while the overlay shows) and Lockdown completed, all as on WPF.
- `CanEnroll` refuses programs with required tasks this head never raises (Core provider, unseeded on WPF, plus head `ProgramCapabilities` table); refused cards name what is missing.

## avalonia-port/rows-companion-tab: +222
- Companion tab visibility hook: drawer state persisted on leave/exit and restored on show, upgrader v2 consent offered on show, mod switch repaints the roster.
- Rows stay stub; what remains needs CompanionService + CommunityPromptService in Core.

## avalonia-port/rows-launcher: +83
- Launcher speaker mute button (WPF Sound.cs), real-click test fail-proven, live sandbox checked.
- win-launcher ledger: 5 duplicate rows merged into one (P21); still stub.

## avalonia-port/programs-run-3a: +738
- Linux now writes programs.json: full ProgramService (timers, startup rollover, Dispose flush), Enroll (first_week only), Withdraw and today's session through a SessionRunner bridge; a newer-schema file is read-only on both heads.
- A panic ends a program session; Lockdown refuses each lifecycle control. Pause/Restart/Dismiss/Ritual/Mantras are left for 3b.

## avalonia-port/rows-companion-room: +400
- Make-her-yours (Z4) now uses the real personality data: preset chips, the Slut Mode toggle (both behind the explicit-content gate), Reset, and the prompt-editor links; it re-reads when the tab is shown.
- New test `CompanionPersonalityZoneTests`, fail-proven by 6 breaks; v2 hides the interview card and the spice toggle snaps back after Cancel; the three rows stay stub, each with its missing list.

## avalonia-port/main-20261009: +47588
- Main sync #6: WPF main 11d7c3afc merged (releases 7.1.0-7.1.5); delta ledger docs/avalonia-main-sync-20261009.md (8 ported-by-merge, 132 needs-port in 15 sync6-* lanes, 8 n/a).

## avalonia-port/main-20261009-compat: +199
- Compat for main sync #6: BillboardContract/NavSections into Core, QuestsOpen/GameAvailable head seams, LegacyBillboard keeps the Avalonia slideshow, WPF scan tests via SourceRoots/Assets. GATE-OK.

## avalonia-port/rows-possession-lockdown: +278
- views-tab-lockdown: blood-red theme + 600 ms activation flash + Video/BubbleCount Strict greys wired, restored on exit (10 fail-proofs; theme live; Bubble Count hold decided at Lockdown event like WPF, not on rack re-attach). Row still stub: Possession readout, Dose keeper.
- shell-possession untouched: ~12.1k-line subsystem needs a slice plan (P33).

## avalonia-port/rows-graded-intake-tab: +312
- Graded Intake window lifecycle ported: `fullscreen-set` with the mode remembered, the control panel minimised for the run and restored on close, and a heartbeat watchdog that relaunches once (WPF IntakeHostService). Tests use the real `OpenIntake` path on a stepped clock and were proven to fail when broken.
- First-ever-run rule moved to Core `IntakePunchCardState`; duplicate parity row removed. The row stays `stub`.

## avalonia-port/rows-remote-control: +611
- Remote emotes (tab + overlay big picker, edit popup) via Core RemoteRelay.SendEmoteAsync; controller overlay, 2 s command toast, joined notice and Start lock wired from startup.
- Rows stay stub: directory opt-in chain, listing pill, session verbs, HUD/preview folds, taskbar flash, avatar emote bubble.

## avalonia-port/rows-possession-lockdown: +278
- views-tab-lockdown: blood-red theme + 600 ms activation flash + Video/BubbleCount Strict greys wired, restored on exit (10 fail-proofs; theme live; Bubble Count hold decided at Lockdown event like WPF, not on rack re-attach). Row still stub: Possession readout, Dose keeper.
- shell-possession untouched: ~12.1k-line subsystem needs a slice plan (P33).

## avalonia-port/rows-tab-navigation: +136
- shell-tab-navigation: door accordion is gated on the rail like WPF (a shut rail opens no door; hover restores the chosen one; MotionLevel Off snaps the tween). NavDoorRailGateTests is fail-proven x4 (door switch touches only the new and previous door, like WPF :878), plus a live Keincheck check.
- shell-window-chrome / theme-main-window / shell-main-window untouched (left for later lanes).

## avalonia-port/sync6-nav-polish: +364
- Polish 8 readability pass on Avalonia: type scale + section ink tokens, token greys sweep, Play/Companion on the scale (rows 72-74, 76).
- Rail/strip/depth/edge-fog rows (28) left needs-port for sync6-nav-polish-b, blocked on sync6-nav-rail.

## avalonia-port/rows-assets-tab: +910
- Assets tab: real asset tree, checks, thumbnails, preview, counts and preset CRUD (03af6e8bb online preset fold + OnlineChannelsReset seed).
- Review fixes: untick/preset/repair drops the video + bubble queues (#130); Refresh keeps the open folder; repair refresh skips an unloaded tab. Rows stay stub (remote picker, packs, video thumbs); FX in the next layer.

## avalonia-port/sync6-nav-rail: +755
- Section tab strip + breadcrumb from Core NavSections/NavStripTable (WPF NavStripRules delegates), zone keys, launchers, last-tab memory, Ctrl+K from every page; test SectionTabStripShellTests (fail-proven), Keincheck evidence/sync6-nav-rail.
- 3 of 25 rows ported; 22 deferred to sync6-nav-rail-b (section rail, badges, glow, pin, help, wash, zone scroll, Monitors/Account & Plans).

## avalonia-port/rows-assets-fx: +161
- Assets FX: tree-row hover nudge (3 px / 130 ms, instant when motion is Off) and the Media Log pulse (stops on hide or open). Row stays stub (pack-card sheen).

## avalonia-port/sync6-tonight-board: +891
- The Tonight Board deck (Core BillboardDeck, house + tip providers, dots as the hold clock, snooze + toast, push, no sound) replaces LegacyBillboard (deleted); HouseProvider and TipCards moved to Core.
- 4 of 24 ledger rows ticked; the rest (drawn art, other providers, animation polish) moved to sync6-tonight-board-b. Parity row is stub.

## avalonia-port/sync6-social-pages: +403
- Friends page (drawer in page mode) and Leash page (WPF empty state) behind temporary You-door entries; Leaderboard opens on All-Time with no season countdown.
- Chip tooltip carries the full name, HT box takes a pasted link; Lobby badge lease, Social medallion and Leash "?" stay needs-port (blockers named).

## avalonia-port/sync6-home-layout: +797
- Home: favorites drawer at the right edge, fold arrow pill (companion strip removed), one LVL chip, window opens at 1661 x 1002 and shrinks evenly (WindowFitRule moved to Core); 6 of 13 rows ported.
- Still needs porting: hover bubbles, banner FX, marquee drum, rail icons/EMI label, Sparkle pill, animated logo.

## avalonia-port/sync6-media-picker: +969
- Assets tab: the remote media picker is ported (awaited consent, segmented source, flavour cards, Fine-tune list with niche rows, your own communities and a probed add). FlavourPresets and RemoteSubAddMessages moved to Core; 5 main-sync rows ticked.
- Open gap: flash/video on this head do not use online media yet (only intake does).

## avalonia-port/roadmap-atomic-save: +169
- Core RoadmapService: flushed temp+rename save, and a roadmap.json.corrupt-<ts> backup before falling back to defaults; saving is turned off if that backup fails (both heads).
- 4 fail-proven Core tests on temp dirs via an internal ctor.

## avalonia-port/sync6-premium-page: +199
- Premium page (main a1d8a7ff1): "premium" key on Home, silent "exclusives" redirect, shelf grouped Basic/Prime/Free under headers (Core PremiumShelfOrder git-mv), reserved seats dropped; fail-proven tests.
- Left needs-port: header Premium spark (97/99) and vault flair (100).

## avalonia-port/programs-run-3b: +619
- Pause/Resume, Restart, Dismiss, mantra door and ritual photos live on the run panel; roadmap provider seeded lazily; presentation + the_takeover enrollable.
- Lockdown refuses Pause/Resume/Restart/ritual/mantra door; Restart refused for unfinishable programs; strings x9; 22 fail-proofs, GATE-OK.

## avalonia-port/sync6-715-fixes: +151
- 7.1.5 fixes: the pop quiz "Turn these off" link, a 20-minute default for Lockdown, and a session no longer restores the pop quiz switch (ledger rows 144/146/147 ported).
- Shatter grey-out and perk-row opacity were folded into missing parity rows because those surfaces are not on this head.

## avalonia-port/sync6-companion-ai-page: +251
- Companion > AI page: the AI pill opens it, and it shows the room's live Connection drawer, Behaviour and Triggers cells and memory diary, giving them back on hide. Rows 138 and 140 are ported; 136 is n/a (no ConversationPage on this head).
- CompanionAiPageTests (fail-proven) plus a live Keincheck run.

## avalonia-port/sync6-tonight-board-b: +651
- Tonight Board: Waiting/Resume/Event providers on the Avalonia Home deck. Pure cards (git mv) and adapters are now in Core behind a `BillboardHead` seam both heads fill; tests are fail-proven.
- Live/Board/Showcase, drawn art and animation polish are left for sync6-tonight-board-c (all 20 rows moved there).

## avalonia-port/sync6-home-layout-b: +774
- Home account strip + quick pills -> one line of hover bubbles (HoverBubbleBar, WPF 41288a884/acd51fcf9); drawer handle gains the Embers layer.
- Rail icons left for nav-rail-b; Sparkle pill blocked on the wallet; banner FX/drum not started.

## avalonia-port/sync6-nav-search: +864
- Ctrl+K palette brought up to the 7.1 nav rework: recent picks, no-hits Try/All, "(was X)" hints, each row opens through its own action, right-click pin, click-away re-activate; GameAvailableProvider set at startup. 10 tests, each proven to fail when broken.
- What moved card (plan moved to Core): offered once on upgrade through StartupLadder, Help replay, Show me; glow and Games zone (rows 38/46 partial) wait on sync6-nav-rail.

## avalonia-port/sync6-drone-mod: +2
- Ledger row 110 (drone-mode.ccpmod emotes + voice) marked n/a: content-only, ships via the shared mod-drone pack.
- Drone outfit emotes folded into avatar-tube-window (this head has no emote mode).

## avalonia-port/sync6-nav-rail-b: +857
- Section rail replaces the flyout (Social door with Friends/Leash pills, lit row, last tab, Ctrl+1..7, fixed Back row); pills pin on right-click; Play zone pills scroll.
- Ctrl+K held toggles once; 7 rows ported/n/a, 15 left needs-port for sync6-nav-rail-c.

## avalonia-port/sync6-gate-pay: +74
- Vault gate: card/PayPal checkout link under every pay button (main 750e76812), via ExternalOpener; fake-launcher test, fail-proven.

## avalonia-port/sync6-tube-avatar: +369
- Tube cel title plate (floating plate, raised coin arrows) and Embers motes in the glass (AmbientFxCanvas Embers/Retint/RunWhileInactive); sync6 rows 2889ddb23, bd95424c8 ported.
- TubeMotesTests (fail-proven x3); render evidence only, no live check.

## avalonia-port/sync6-premium-page-b: +830
- Header Premium spark ported (rows 97/99): PremiumSparkRules/Field moved to Core, Avalonia `PremiumSpark` draws it, clock gated by visibility/motion and stopped on detach; `PremiumSparkTests` fail-proven, live Free check.
- Row 100 (vault flair, no header row on Premium, Home ring) left needs-port for sync6-premium-page-c.

## avalonia-port/sync6-launcher-badges: +106
- Launcher whole-card launch + once-per-window grow to seat every tile row (WPF 7.1.5 d0001dd5e); 2 fail-proven tests.
- Badge hue/seen-dim folded to shell-nav-rail and win-launcher Lobby fold (no surface yet).

## avalonia-port/sync6-companion-pages: +134
- Companion Personality/Permissions/Links pills open the Companion tab scrolled to their zone (zone pills until the v2 page lands; decision B+C).
- Rows 5681c132a/f3a00a9dc stay needs-port, folded into views-companion-v2-conversation; CompanionZonePillsTests fail-proven.

## avalonia-port/reference-render-ci: +430
- reference-render.yml: WPF (Windows) vs Avalonia renders paired per parity row into side-by-side PNGs; first run 173 WPF / 213 Avalonia / 280 rows paired.
- Opt-in WpfReferenceRender test + pair-renders.py; fetch with ~/ccp-port/bin/fetch-renders.sh <branch>.

## avalonia-port/snapshot-lock: +197
- Verify.Avalonia SSIM (0.995) snapshots lock the 2 verified parity rows in their own testhost (RenderProof setup + pinned Noto Emoji fallback). They pass on Arch and on ubuntu-latest (run 38055802311).
- `ledger-snapshot-check.sh` fails any verified/improved row without a baseline. The parity doc now has a "Locking a row" rule.

## avalonia-port/icons-infra: +1000
- Fluent UI System Icons infrastructure (L0a): fx:IconGlyph/IconLabel/IconMap/IconText, {loc:StrBare}, Theme/Icons.axaml brushes and ramp; PremiumGateFx and HoverBubbleBar now go through IconGlyph.
- Tests: render (non-blank, not .notdef, inherited Foreground, Regular != Filled), map/split, contrast; 16 fail-proofs; --scale 2 renders.

## avalonia-port/no-desktop-notify-in-tests: +73
- OsNotifications no longer talks to D-Bus in a CCP_USERDATA_DIR sandbox (only `--notify-check` opts in); Sink/BusAttempts seams.
- RemoteControlShellTests asserts the join notification via the Sink; sandbox no-bus test; both fail-proven.

## avalonia-port/sync6-tonight-board-c: +431
- Premium showcase on the Tonight Board: Core showcase provider (git mv), head art registry and lifecycle, muted ClipArtView. The art is released on shell close (P68).
- Ledger row 104 is ported. Board stays silent, Live is blocked on Lobby, and drawn art needs a slice plan.

## avalonia-port/distro-check: +292
- Clean-distro Docker check of the linux-x64 tarball (6 distros, real Xvfb, offline run, screenshots + result.json) and a distro-check.yml CI matrix (run 38061705873 green).
- Findings F1-F6 (tumbleweed no-font abort, WPE not packaged, libvlc P/Invoke, emoji tofu, HiDPI wizard, pactl) in evidence/distro-check/findings.md.

## avalonia-port/sync6-nav-polish-b: +839
- Section strip in its hue (tray, per-tab tints, bevelled plates, sliding glowing lit fill), page wash + ink, window edge line/band/lift and HUD band per section; SectionPaintShellTests, Keincheck strip-*.png.
- Strip colour math in Core NavStripPaint (both heads wrap it); ledger rows 47/49/70/75/77 ported, 48/50/54/80 partial; the rest -> sync6-nav-polish-c (depth, fx, fog/embers, rail coins).

## avalonia-port/ci-green: +85
- `build` is green on GitHub Actions (run 38063614481). Changes: PKGBUILD 7.1.5; WPF tests follow the Core/Assets moves; the board budget test runs alone and fully warmed up (12 ms budget unchanged).
- Fixed the Windows-only Avalonia test failures: a locator scope leak (8 WindowDropTests), the intake token check on runners with WebView2, and the webcam splash wall-clock flake. `build.yml` gains `workflow_dispatch`.

## avalonia-port/sync6-nav-rail-c: +608
- Rail paint: spur, art tint, label glow, 160 ms halo fade, hover nudge, NavBadges pills + rich tooltip (NavBadges moved to Core).
- Landing glow (GlowNavTarget + NavGlow on pill, rail row, Settings pill, Play zone header) and the window-title crumb; ledger rows 2/23/46 ported, 33 partial, the rest -> sync6-nav-rail-d.

## avalonia-port/roadmap-tests: +340
- Core tests for RoadmapService steps, photo diary, notes, track unlocks and persistence; mutation score 21.9% -> 70.8%, and every remaining survivor is logging, an equivalent mutant, or crash-durability that cannot be tested in-process.

## avalonia-port/linux-runtime-deps: +309
- Bundled fonts (Inter default, Fredoka, Noto Sans Mono, Noto Emoji fallback): no crash on a font-less system, WPF display face on Linux.
- libvlc soname resolver + Fedora VLC plugin deps; pactl probe no longer spawns when absent; install doc updated.
- Review fixes: audio packages named when LibVLC fails to start, Noto Sans Mono license shipped.

## avalonia-port/lockdown-badge-flake: +7
- LockdownVeilTests: the setup marks every startup intro and the What moved card as seen, so an intro held by the ladder (real 1 s timer, 3 s settle) can no longer open inside the headless pre-key pump and take the badge's Enter. Fail-proven by breaking the keyboard route.

## avalonia-port/server-sync-unblock: +14
- Server sync contract confirmed by the owner (#1867): `server-sync-contract` blocked -> resolved, decision logged; 7c and the #1914 cosmetics clear stand as built (no code change).
- Evidence: fake-wire SyncPush/cosmetics tests fail-proven with 4 mutants (evidence/server-sync-unblock/fail-proofs.log); GATE-OK.

## avalonia-port/sync6-nav-polish-c: +897
- Strip pills: Fluent glyphs, depth (sunken tray, hue drop bands, pressed-in lit pill, press spring), and motion (hover lift/grow, glyph wiggle, spark burst, 9 s sheen gated by motion and visibility).
- Core DepthLaw + NavStripMotion (WPF forwards); Avalonia Theme/Depth.xaml; SectionStripDepthFxShellTests, fail-proven.

## avalonia-port/startup-ladder-clock: +101
- The StartupLadder held-card re-check now runs on the ladder's steppable clock where it used to run on a real DispatcherTimer. Tests default to a stand-still TestClock, so no intro card can open on its own in the middle of a test's input (root fix for P76).

## avalonia-port/programs-today-card: +502
- Dashboard Programs Today card + banner art/fog/sheen/sparkles/parallax (shell-program-banner -> wired); loops gated on visibility+motion, stopped on detach.

## avalonia-port/programs-ignition-fx: +833
- Programs run view ignition rig, built on Core ProgramHeat: heat-tiered ambient loops, particles registered with RegisterTabFx("programs"), and the day/chapter/graduation moments. Loops follow Motion, tab visibility and minimise (like WPF), and stop on detach. Brush and transform loops run on a per-frame driver, because Avalonia 12 gives them no clock.
- 3 headless tests, 10 mutations each turned them red; headless frame-timing CSV plus a per-loop motion CSV (angle, shimmer, comet); live run (lit but still: the window was inactive). Bursts, pip pulse and session sheen still missing.

## avalonia-port/wpf-behaviour-ci: +598
- FlaUI drives the offline, firewall-blocked WPF reference on Windows CI (UIA tree + default/hover/pressed/opened PNGs for doors and dialogs); Avalonia `--uia-dump` writes the same schema; `uia-parity.py` diffs them per view into parity-diff.md/json (run 38179660532 green: wpf-behaviour 16 views, 0 step failures; firewall Domain/Private/Public on).
- First diff: 5 findings in evidence/wpf-behaviour/findings.md (#1 Bug Report withdrawn: harness click was covered by the help overlay; offline does not disable logins, type-name accessible names on 35 buttons).

## avalonia-port/sync6-nav-rail-d: +732
- Settings > Monitors (System bubble scrolls to it, crumb follows; SystemFeatureControl retired), Library Folders zone + Data logs row, ? badge beside every strip pill, mod door art on the rail.
- Ledger rows 1/25/33 ported, 6/11/17/21/96 partial; Account & Plans, Games cards, Just Drop ask, Moved note -> sync6-nav-rail-e.

## avalonia-port/wizard-hidpi-wpe: +219
- First-run wizard capped to the screen working area (pixels divided by scaling): at 200% on 1080p, Enter stays on screen and the 18+ box is reachable by scrolling (F5; distro-check hidpi wizard_done true on ubuntu-24.04).
- Missing-WPE message: Arch keeps WPE in pacman (extra ships the trio; PKGBUILD unchanged). Other distros get no failing command, and a 9-language line says those pages are unavailable. No unbuilt Flatpak is offered (F2).

## avalonia-port/win-panic: +827
- Windows panic key (WH_KEYBOARD_LL hook), Lockdown system-key block and unfocused Esc door, all from trunk. Core PanicKeyRules with tests. The Windows code compiles but has never run.
- SingleInstance sandbox suffix was already on base; this adds the fail-proven test for it.

## avalonia-port/windows-ci-flakes: +94
- Windows CI flakes fixed at the root: the NavGlow glow now reads a steppable TimeProvider instead of a real animation clock, the hit test no longer races ToolTipService, and the Windows Avalonia suite runs sharded (it ran out of memory in one testhost).

## avalonia-port/juice-clock: +621
- Trunk U1 infra: FrameGate (Core), FrameClock (30 fps shared beat), BeatLoop; TransformTween/BreathClock/Easings/Keyframes/MotionGate/MotionTimings deferred to their first user.
- Profile OG border spin and vault CTA breath were inert (Animation.RunAsync on a Transform threw); now BeatLoops on the 30 fps beat, angle/scale asserted, red run saved.

## avalonia-port/text-maxlines: +130
- WPF pixel MaxHeight line caps on 17 TextBlocks (11 files) -> MaxLines with the WPF line count; long localized copy no longer drops/clips lines.
- TextLineCapTests: headless line-count check (fail-proven on the Assets creator card) + axaml guard against TextBlock MaxHeight.

## avalonia-port/icons-guard: +355
- IconGuardTests: font-codepoint hard 0 (1 temporary entry), emoji/loc/Path ratchets (971/234/26) failing both ways, FluentIcons isolation, allowlist liveness on comment-stripped code, with per-line counts for Path entries and reasoned whole-file art entries; every guard mutation-proven.
- `--render-all` icon census line (1040 icon-led texts, 158 unnamed icon-only buttons); evidence in ~/ccp-port/evidence/icons/icons-guard/.

## avalonia-port/sandbox-full-access: +158
- Debug+sandbox runs with CCP_SANDBOX_WHITELIST=1 (set by kc start) get whitelist/premium/Lab; Release compiles it out.
- Tier card shows "Whitelisted (sandbox)"; tests + Release-IL check + live evidence in evidence/sandbox-full-access.

## avalonia-port/mine-safety: +592
- P0 safety: panic silences the tube (`tube-speech`), the 6-blink stop gesture behind BlinkStopGate (moved to Core), and the camera pill now stops the camera.
- The explicit-content acknowledgement uses ShowDialogSafe, so a hidden tube no longer throws. Gate OK, no live check.

## avalonia-port/mine-launcher-account: +236
- The launcher now checks a leash gate before play (the seam exists but nothing sets it yet). Header stat pills are ported and read the last leaderboard page. The bubble sprite is re-read from the active mod on each start.
- P2 (account/data + devices) is skipped because it overlaps the login-fix lane; face473bd (pause-key capture) has no overlap and can be its own lane.

## avalonia-port/content-packs-store: +960
- ContentPackStore + PackManifestModels + PackFolderMover on Core (trunk d6a8343db, 1b387ad80); WPF manifest models moved to Core.
- PackFolderMover is copy -> verify -> rename -> remove source; failure keeps the source (fail-proven tests).

## avalonia-port/login-fix: +347
- User report "login does not work". The window was a sandboxed live-check app. An unreachable sign-in server now ends the attempt at once with WPF's "Login failed" message instead of a 5-minute spinner. Closing the dialog cancels the OAuth, so a retry works again.
- Title-bar "Sign in" chip lands on Settings · Account on the first open. Offline Mode greys the Home Login button.
- Sandboxed runs show "[SANDBOX] " in every window title and a SANDBOX badge in the shell title bar. A Settings Link waiting on the browser ends when the Account section leaves the tree. LoginRetryTests: 9 tests; the new one is fail-proven, the rest are asserted.

## avalonia-port/icons-home-settings: +292
- Fluent icons for Home + Settings (L1): 17 product files, emoji ratchet 971→905, loc ratchet 234→214, census unnamed icon buttons 158→142.
- Tests now check IconGlyph/IconLabel Kind (fold, drawer, panic key, link labels), each made to fail once on purpose; parity rows marked divergent by decision.

## avalonia-port/icons-studio-features: +434
- Fluent icons L2: Studio rack, feature pages, feature/intro/settings popups, PinkRush, CornerGif, LayeredAudio, MiniPlayer (play/pause by Kind); emoji ratchet 971→895, loc-prefix 234→198.
- win-layered-audio snapshot re-baselined (icons only, by decision); snapshot tests can no longer launch a diff GUI.

## avalonia-port/icons-studio-features: +434
- Fluent icons L2: Studio rack, feature pages, feature/intro/settings popups, PinkRush, CornerGif, LayeredAudio, MiniPlayer (play/pause by Kind); emoji ratchet 971→895, loc-prefix 234→198.
- win-layered-audio snapshot re-baselined (icons only, by decision); snapshot tests can no longer launch a diff GUI.

## avalonia-port/icons-premium-tabs: +133
- L3 Fluent icons: 9 premium tabs + Circe faces draw fx:IconGlyph; gate padlocks keep PremiumGateFx glow (LockClosed + tier brush); Chaster Key/LockClosed per D6.
- Ratchets lowered: emoji 971→920, prefixed-loc 234→224, path 26→19; Exclusives feature-emoji data slots and Takeover state strings left (L10/follow-up).

## avalonia-port/icons-voice: +143
- Fluent icons L4: She's Listening (21 voice-command rows -> IconLabel, gate padlock), lock card voice states, webcam calibration/gaze windows; icon-emoji 971->919, icon-prefixed-loc 234->233. Review P3s: hearts Filled + PinkBrush; shield, sparkle, pause Filled per design ref.
- Left for L10: the Start/Stop listening button (text swapped in MainShellWindow.SheListening.cs).

## avalonia-port/icons-progress: +506
- Fluent icons on the quest, achievement, skill-tree and program surfaces and their 7 popups and dialogs (L5). Icon-led loc keys go through IconLabel/StrBare; gold/success/tier brushes follow the D tables.
- Ratchets: icon-emoji 971→880, icon-prefixed-loc 234→210; census 1040→820. IconLabel.Args now re-renders, so a reroll count that changed under the same key no longer stays stale.

## avalonia-port/icons-deeper-media: +385
- Fluent icons L6: Deeper tab/editor/player/new dialog, Media Log, Assets (+MediaPicker), WebHost converted to IconGlyph/IconLabel/StrBare; ratchets 971→838 emoji, 234→215 prefixed loc.
- Deeper header-wave drift now finds the IconGlyph (fail-proven test); Keincheck before/after in evidence/icons-deeper-media/.

## avalonia-port/icons-companion-social: +327
- Fluent icons lane L7: Companion room, Profile/Discord (tick Paths too), Friends drawer (WPF MDL2 action glyphs restored), InvitePanel, ProfileCustomize; ratchets emoji 971→905, loc 234→224, path 26→23.
- Fail-proven asserts in WorkshopCells/ProfileWardrobe/FriendsDrawer tests; Keincheck shots of Companion and Profile tabs.

## avalonia-port/icons-chaos-play: +470
- Fluent icons lane L8: Chaos hub/HUD/overlay/slot picker, Play tab, Gaze minigame, AvatarTube, BubbleCount and EasterEgg glyphs become IconGlyph/IconLabel with an arcade look (filled marks, tier/gold brushes, pip counts kept); icon-emoji ratchet 971 -> 846.
- Tests: new ChaosPlayIconsTests; ChaosRunHostTests and ChaosHubStateTests assert icon Kinds; all fail-proven; Keincheck Play tab, headless Chaos hub render.

## avalonia-port/icons-dialogs-windows: +497
- Fluent icons lane L9: dialogs, PresetsTabView, ModCreator/Quiz/SessionEditor/HelpVideo/WhatMoved/Announcement/EmiOptions/EmiRing; BuildActionChips returns icons.
- Ratchets 971→832 emoji, 234→185 loc, 26→25 path; mod-manager snapshot re-baselined by decision.

## avalonia-port/icons-dialogs-windows: +497
- Fluent icons lane L9: dialogs, PresetsTabView, ModCreator/Quiz/SessionEditor/HelpVideo/WhatMoved/Announcement/EmiOptions/EmiRing; BuildActionChips returns icons.
- Ratchets 971→832 emoji, 234→185 loc, 26→25 path; mod-manager snapshot re-baselined by decision.

## avalonia-port/render-budget-loc: +175
- Skia GPU cache raised to 256 MB (trunk RenderBudget) plus Win32 presentation env switches; idle Home CPU (Linux only) 0.67 -> 0.30 cores (dotnet-counters, 2x3 samples each side).
- Trunk loc fix 6be091d12 already in base, no json in diff (0 of 18 keys differ); FrameClock fps counter not imported (no FrameClock in this head).

## avalonia-port/wpf-findings-a11y: +157
- Buttons whose content is a panel or TextBlock are named by their text, and the name follows content changes; this fixes 55 type-name accessible names. Offline mode greys Home's unified login as WPF does.
- WPF UIA findings 2 and 4 reproduced on the real startup path with the CI seed: the shell matches WPF, and the `--uia-dump` harness never seeds settings or services.

## avalonia-port/flash-core: +547
- Core flash rules from trunk U9 (FlashExit/StayRule/Corners/Shatter moved to Core, tests on Linux).
- Avalonia flashes honour Click: a press pops with haptic and the chosen leave; Stay until popped (10 min, cap 40) only for scheduler flashes, not remote/voice ones.

## avalonia-port/flash-wire: +677
- Flash: lucky roll + chime, glow card, XP/achievement/quest per flash, hydra on pop, TvOff/Glitch/Pop exit layers (FlashFxRules imported with its caller).
- Flash XP still not banked (no idle tracker); Shatter, lucky pulse/toast, FlashCorners, voice pool, remote source left.

## avalonia-port/leash-core-move: +289
- Leash services, rules and LeashHoldToCut moved to CCP.Core/Services/Leash (git mv, trunk shape). WPF keeps thin head files (LeashServiceApp, LeashCutSafetyApp, ChasterLeashTab).
- 134 leash tests now run on Linux in CCP.Core.Tests. GATE-OK.

## avalonia-port/leash-wire: +372
- The Core LeashService now runs on the Avalonia friends poll: seams are seeded, it starts after friends and is dropped on exit, and the launcher leash gate reads it, but holds a tile only once a gate card can present it (LeashGateCard unit). Cut target: `RemoteRelay.EndSessionNow`.
- The +372 is counted against the stack-tip merge 8d147364d (icons-progress), not 2a4a55570. Tests: LeashHeadTests and a RemoteRelay test, all fail-proven. GATE-OK.

## avalonia-port/content-packs-service: +1000
- Core ContentPackService network half (trunk U8): manifest, Patreon signed-URL install with Range resume, encrypt, rate limit/auth, V2 link, status, local zip; 13 fake-handler tests incl. bad signed URL rejected with user packs kept.
- Startup seed, previews and pool wiring follow in content-packs-wire.

## avalonia-port/content-packs-wire: +654
- Content packs seeded at startup; flash, scheduled video and bubble count draw active pack media through Core PackMediaPool (WPF-weighted).
- Pack previews (installed picks + URL cache, sandbox-safe) in Core ContentPackService; svc-content-packs stays stub (no packs UI).
- Review fixes: startup sweep of crash-left ccp_temp_* decrypts; video/bubble weigh pack clips left this cycle like WPF.

## avalonia-port/lag-fixes-a: +670
- Shell ambient loops (FeatureCard/SplitFeatureCard, Chaster chip, status pulses, fuse, Deeper, TierBadge, SpiralGlyph, roadmap, Lockdown) now run on the 30 fps BeatLoop, and a hidden or minimised loop unsubscribes its clock. Tile and tier-badge glows are sibling BoxShadows instead of Effects. Three motions that never ran before (chip swing, Deeper drift, TierBadge sway) now animate.
- A ratchet blocks new infinite Animations (10 files left for lag-fixes-b). There are 16 recorded fail-proofs. Measured live: Play tab CPU 49 -> 32% of a core. Idle Home still composes at about 55 fps in both builds (cause outside this lane).

## avalonia-port/lag-fixes-b: +759
- Spiral, Quests, Exclusives, Enhancements and Home loops ride the shared 30 fps beat, glows are sibling BoxShadow layers, and FX timers moved onto FrameClock. Idle renders/s: Home 56->37, Play 45->32, Enhancements 59->37, Premium 59->35.
- Glows under loops are baked once (GlowBake: Spiral digits and waiting line, padlocks) or use sibling BoxShadows, with headless render proofs. Effect ratchets cover code and .axaml. The ~115 comp updates/s are idle 144 Hz ticks (evidence/lag-fixes-b/live-fps.txt).

## avalonia-port/feature-day-log-core: +210
- FeatureDayLogService + model moved to Core (same feature_day_log.json); seeded/disposed on the Avalonia head; `ConditioningMinutesOn` accessor for the leash day report.
- Follow-ups: LeashHead.DayInputs minutes (leash-wire), Chaster MinutesOn, Note/sync hooks on Avalonia.

## avalonia-port/first-show: +380
- FirstShow rules (deck, presets, media loader, layout, script) are now in Core and WPF reads them; Core tests moved or ported and fail-proven.
- The Avalonia stage, EMI presentation, panic and trigger are left to first-show-b; the Guide to first-show-c.

## avalonia-port/lang-italian: +8864 (39 non-data)
- Italian selectable in both heads: verbatim trunk it.json + one AvailableLanguages entry; missing keys fall back to English.
- Language test: every file parses strictly and is offered; WPF count asserts 9→10.

## avalonia-port/lang-italian: +8864 (39 non-data)
- Italian selectable in both heads: verbatim trunk it.json + one AvailableLanguages entry; missing keys fall back to English.
- Language test: every file parses strictly and is offered; WPF count asserts 9→10.

## avalonia-port/render-all-segv: +188
- Root-caused the intermittent `--render-all` exit 139 on CI. Avalonia's `SkiaTypeface.TryGetStream` reads an SKStreamAsset it doesn't keep alive, so a GC during the copy finalizes it and the read hits freed memory (use-after-free). It was hit through synthetic bold of the 10.8 MB system emoji font.
- The system emoji font now loads through `SystemEmojiFontCollection`, which refuses synthetic bold/italic. CI keeps a crash dump and report as an artifact when render-all fails.

## avalonia-port/possession-core-a: +253
- Pure Possession rules (deck, off-limits name rule, toast/rewrite pools, enums, bark triggers) moved from WPF to CCP.Core via git mv; WPF keeps the FrameworkElement contracts and the tree walk.
- Deck tests moved to CCP.Core.Tests; new CoreMods mod-routing test (fail-proven).

## avalonia-port/games-host: +710
- Launcher opens games: shared GameWindow host (init, media-request deal from the library, exit, panic surface "games"), backroom bridge.js on the WebKitGTK string carrier; Breakout demo wired (demo=1, leash check first).
- Tests: GameWindowTests (fail-proven via WebHost carrier, origin guard, demo=1, media deal, leash, panic), node bridge-carrier.test.js (string carrier = hosted for in-window fx, pause Exit and the F2 dev-panel gate; red run saved); GameWindow acks fx (WPF BackRoomBridge.OnFx), media deal walks images/ once per window, single-flight; live: tile Play opened the GameWindow (page boot not visible in capture).

## avalonia-port/chaster-minutes: +61
- Chaster MinutesOn stays null on purpose until the conditioning-time tracker feeds the day log; WPF wiring would charge every day as idle. Decision, ledger note and guard test added.
- SeasonRecap TrackFeature and BackRoom NoteEvent have no Avalonia counterpart yet, so nothing was routed.

## avalonia-port/win-panic-b: +602
- #919b panic watchdog, Pause key capture (all platforms) and press (Windows), and Escape claim for the friends drawer, all ported from trunk c072be180/face473bd.
- The windows CI job now runs `--win-panic-check` (F24 passes, F23 must fail); green in run 38195413447.

## avalonia-port/first-show-b: +997
- EMI's welcome show runs on Avalonia: a click-through stage (overlay set up before Show) with partial effects and cues, and the real desk EMI presenting. It opens once after the first-run wizard; panic, Esc and closing the shell stop it. Downloads are capped and the temp dir is swept.
- The fail-proven FirstShowStageTests pass (7 cases), the gate passed, and Keincheck evidence is in evidence/first-show-b{,/fix1}. Sparks, subliminal, drain, spiral and words are carried to first-show-c along with the Guide.

## avalonia-port/conditioning-time-tracker: +300
- Conditioning-time tracker ported: Core ConditioningTimeTracker, driven by shell engine start, session start and engine stop; the Pink Hours pill counts live.
- Not ported: server sync and the season mirror.

## avalonia-port/leash-gate-card: +645
- Leash gate card on the panel (WPF LeashGateCard + MainWindow.Leash.cs): PresentLeashGateProvider seeded, pending punishment holds tiles, Panic and Cut leash (LeashCutSafety) always work; 4 fail-proven headless tests + live sandbox evidence.
- Punish window + task runner left for leash-punish.

## avalonia-port/first-show-c: +514
- Welcome guide ported (WelcomeSteps on the real shell, highlight, FeatureCard tutorial preview, follows the app's monitor); live run reaches Presets.
- Show effects carried from first-show-b (sparks, subliminal, spiral, floating words, drain veil); feat-first-show stays stub (bleepese, replay doors, bubble pops).

## avalonia-port/game-race-a: +263
- Race rules (10 services + TrackChart) git-mv to CCP.Core; WPF seeds PrizeGrants + NAudio decode seams.
- Race tests move to Core tests, plus fail-proven seam tests; ledger row game-race (missing).

## avalonia-port/awareness-core-a: +26
- Moved 10 awareness files (frame, policy, scorer, projection, angle cards, prompt builder, tally, cooldown ledger, interfaces) to CCP.Core, together with the angle-deck embedded resource; 5 pure test classes moved to CCP.Core.Tests.
- ActivityLedger and AwarenessObserver (each over 1000 lines) are left for their own units; see evidence/awareness-core-a/map.md.

## avalonia-port/game-race-b: +333
- Racing Thoughts host shell on GameWindow: the purchase door, a launcher tile behind the leash gate, init + manifest, one payout per run, the heartbeat watchdog; panic closes it.
- Next: BambiCloud levels, own tracks, the track player, smaller host frames.

## avalonia-port/game-race-c: +507
- Racing Thoughts own tracks: track-pick/play/pause/stop/cancel, Core charting worker (authored/cache/generated, partial then full), 250 ms track-clock, teardown on panic (GameWindow.RaceTracks.cs).
- Player behind IRaceTrackPlayer (none yet -> track-error); next: LibVLC player + decoder seed, then BambiCloud.

## avalonia-port/game-pbp: +888
- Piece by Piece opens from the launcher in GameWindow: Core PbpHostRules, net lane, settings/identity, own-library media, watchdogs, panic; page bridge on the WebKitGTK string carrier.
- Online pictures, whispers, stakes and friend intents are queued as game-pbp-b.
- Review fixes: loopback override, a cached library walk off the UI thread, a signal-based test wait; WPF routing filed as a follow-up.

## avalonia-port/lag-fixes-c: +237
- Programs ignition (8 loops) and the dashboard Today banner (fog/sheen/sparkles/breath) now run on the 30 fps BeatLoop with WPF curves; the rail and boss glows are BoxShadows; the banner width is read every frame; the hover nudge works and no longer leaks a CTS; NavGlow uses the shared FrameClock.
- Programs idle went from 141 to 44 renders/s (CPU 45% to 23%); the ratchet lists went down by 4 entries.

## avalonia-port/showcase-renderer: +123
- Cloud session (Opus), local review + gate.

## avalonia-port/ci-parallel: +145
- CI: CCP.Avalonia.Tests shards now run as parallel matrix jobs (avalonia-linux/avalonia-windows x5) with a per-OS avalonia-coverage sum check; build.yml wall time went from 50m47s to ~17.5-24 min (runs 38306259237/38312941874).
- Gate-branch-only concurrency cancel. avalonia-tests-sharded.sh gains --shard N / --expected; with no argument it behaves as before.

## avalonia-port/emi-line-engine: +215
- EmiLineEngine (1360, verbatim plus ask probes) and the EmiKnock machine moved to Core with git mv. WPF seeds the probes and keeps EmiKnockWorld; the rotation and knock tests now run on Linux in Core.
- Next: EmiVox/EmiOffers/EmiChains seams, then the Avalonia moment bus and desk chains UI.

## avalonia-port/tutorials-import: +3779
- Trunk TutorialService.cs imported verbatim (Stack rule 4, 557d9aac3), with 554 lines of wiring: seam seeded, `tour` stop in the PanicSurfaces list, a refused start keeps the running tour's name, Fluent step icons. Tours now start, step and close, and panic ends them.
- Follow-ups: tutorials-icons, tutorials-live-overlay (Live.cs triggers/stage), EmiTourNarrator, KeywordTriggerHead, ? panel StartGuide, Mod Creator nav.

## avalonia-port/game-justdrop: +603
- Just Drop door: Core JustDropService/CreditedOrders (git mv) + JustDropHandoff; ShowTab(justdrop) opens the live shop in a GameWindow (signed-in handoff header, shared chrome.webview carrier, panic closes).
- Row shell-just-drop stays stub: replay/fullscreen strip/orders shelf/EMI door missing; not live-checked against the real site.

## avalonia-port/mine-launcher-links: +995
- Launcher Media pill + dialog (preset via the panel), shortcut link + tile shortcut button writing .desktop entries (Linux) / .lnk (Windows, compile-only); Core LauncherShortcuts rules, LauncherMediaSettings moved to Core.
- Rest: launcher friends chip and What's New link; WPF cyan chip styling.

## avalonia-port/possession-core-b: +742
- PossessionDirector + PossessionRemember git-mv'd to CCP.Core behind PossessionDirectorHead/static seams; WPF-free contracts in Core, WPF keeps typed reads via IWpfPossessionHost + C# 14 extension properties.
- Core tests: director bookkeeping (moved), tripwire/TargetFor seams, Remember arm rule + once/20 s flow, each fail-proven.
- Review fixes: WPF head hook-mapping source pin, scene files renamed to their contents, doc/comment fixes.

## avalonia-port/goon-core-a: +104
- Cloud session (Opus), local review + gate.

## avalonia-port/companion-core-head: +820
- Core BarkEngine (TimeProvider clock) + CompanionCore; WPF CompanionService XP modifier/level loop delegate to Core. Cloud session, local review fix + gate.

## avalonia-port/companion-heads: +289
- Avalonia BarkHead/CompanionHead seeded at startup, ladder panic bark, head tests with full teardown; feat-companion-barks missing -> stub.

## avalonia-port/mine-dnd-tube: +589
- Tube right-click menu acts through the shell (engine #479, takeover #514/#978, mutes, trigger); Shrink/Grow/Dismiss with saved scale; panic stops what it starts.
- Chat box closes on outside press; closing the tube stops her voice and unhooks ask cards. DND was already ported.

## avalonia-port/ci-hybrid-gate: +21
- build.yml `scope=windows` dispatch input: GitHub runs only Windows jobs, Linux half runs locally (bin/rgate hybrid). Hybrid rgate GATE-OK (run 38397970327).

## avalonia-port/platform-heads: +440
- Discord Rich Presence: the service moved to Core, one copy for both heads (PostToUi seam). Armed only when enabled, a Discord is linked and the app is online, on both switches and at startup. Bubbles and Mind Wipe now set the presence text. A sandboxed run never connects.
- MPRIS/SMTC media watcher deferred to platform-heads-b (needs the AwarenessObserver Core move first).

## avalonia-port/mine-tube-speech: +404
- Tube speech queue as in WPF: presets queue in order with the earned gap. Priority lines, panic and the listening bubble drop the queue, and each new line cuts the previous clip. 4 tests, each fail-proven.
- Talk clips and CompanionPhraseAudio are deferred: this tip has no emote system and nothing that speaks lines by phrase category.

## avalonia-port/chaster-leash-minutes: +146
- Chaster idle-day row now reads the day log (WPF MinutesFromDayLog), fed by the conditioning-time tracker; end-to-end test engine run -> day log -> idle charge 0 today / charged otherwise.
- Upgrade edge guarded: a day before the day log's first conditioning day reads null (no charge), so a pre-tracker day is never billed (FeatureDayLogService.FirstConditioningDay).
- Follow-up: LeashHead day minutes once leash-wire is in the base.

## avalonia-port/notifications-core-a: +70
- Cloud session (Opus), local review + gate.

## avalonia-port/deeper-core-a: +274
- Cloud session (Opus), local review + gate.

## avalonia-port/descent-core-a: +119
- Cloud session (Opus), local review + gate.

## avalonia-port/home-marquee: +982
- Cloud session (Opus), local review + remote gate.
- Server update banner, URL-first pill, scrolling marquee (parks while Home is hidden, tested), Home actions, browser card.
- Next PR: home-marquee-b, the update pill Tag skins (urgent red / available pink / hover), split out for the size cap.

## avalonia-port/home-marquee-b: +37
- Update pill Tag skins (urgent red, available pink, hover; WPF MainWindow.xaml:1353-1373) + UrgentUpdatePillIsRed, split from home-marquee for the size cap.

## avalonia-port/tracking-core-a: +230
- Gaze target scoring + refine ambiguity/choice rules moved to Core GazeTargetScoring; WPF GazeFocusService routes to it (one copy). Core tests fail-proven per reachable clause; rgate GATE-OK.

## avalonia-port/subliminal-core-a: +123
- Cloud session (Opus), local review + gate.

## avalonia-port/billboard-core-a: +428
- Cloud session (Opus), local review + gate.

## avalonia-port/session-core-a: +82
- Cloud session (Opus), local review + gate.

## avalonia-port/audio-core-a: +291
- Cloud session (Opus), local review + gate.
- Review P3 fixes: loopback listener claims its own port with retry; all 12 cases fail-proven.

## avalonia-port/webcam-core-a: +304
- Cloud session (Opus), local review + gate.
- Review fix: LongStareDetectorTests split out, all 8 tests fail-proven, tracker ponytail note refreshed; rgate GATE-OK incl. WPF suite on windows.

## avalonia-port/media-core-a: +173
- Cloud session (Opus), local review + gate.

## avalonia-port/video-core-a: +345
- Cloud session (Opus), local review + gate. Review P3 fix: unsafe-video cache save/reload test on a stepped clock (TimeProvider seam).

## avalonia-port/compositor-core-a: +170
- Cloud session (Opus), local review + gate.
- Review fixes: FillGate test settings isolation (P02), PrimaryOnlyUnlessDualMonitor test; both fail-proven; rgate GATE-OK.

## avalonia-port/haptics-core-a: +159
- Cloud session (Opus), local review + gate; review follow-up adds a fail-proven device-backed stop test.

## avalonia-port/haptics-core-a: +159
- Cloud session (Opus), local review + gate; review follow-up adds a fail-proven device-backed stop test.

## avalonia-port/flake-loc-order: +21
- Test-only fix for the order-dependent BillboardShowcase flake (strings loaded in the module initializer; Assert.NotNull instead of `!`). rgate GATE-OK.
## avalonia-port/remote-core-a: +25
- Cloud session (Opus), local review + gate.

## avalonia-port/remote-core-a: +25
- Cloud session (Opus), local review + gate.

## avalonia-port/speech-core-a: +78
- Cloud session (Opus), local review + gate.

## avalonia-port/speech-core-a: +78
- Cloud session (Opus), local review + gate.
## avalonia-port/friends-core-a: +57
- Cloud session (Opus), local review + gate; review fix routes Avalonia drawer presence toggle through Core.

## avalonia-port/friends-core-a: +57
- Cloud session (Opus), local review + gate; review fix routes Avalonia drawer presence toggle through Core.

## avalonia-port/account-core-a: +257
- Cloud session (Opus), local review + gate.
- Review fix: separate trys in WPF StopSync seam; stale contract-D comments refreshed. rgate GATE-OK.

## avalonia-port/chaos-core-a: +98
- Cloud session (Opus), local review + gate.

## avalonia-port/ui-core-a: +36
- Cloud session (Opus), local review + gate.
- Review fix: LayoutDrift move made a pure 100% rename; rgate GATE-OK (windows + core-linux + core-guards).

## avalonia-port/update-core-a: +429
- Cloud session (Opus), local review + gate.
- Review fix: MainModule read back inside a try (pre-move swallow); read-only elevation test fail-proven.

## avalonia-port/chaster-core-a: +54
- Cloud session (Opus), local review + gate.
- Review follow-up: zero-size monitor test; BookedPopLayout/CapNotice fail-proven; rgate GATE-OK (run 38335803915).

## avalonia-port/game-fyp: +988
- For You feed on the shared GameWindow behind OpenFypFeed's premium gate (Play hero, Exclusives spotlight, EMI door; panic closes it). Its rules are in Core `FypRules`, which WPF now calls too (one copy). A shared web carrier lets `fyp/main.js` talk to WebKitGTK.
- Not done here (left for game-fyp-b): eye control, ghost mode and the file menu. The page gets WPF's own refusal frames instead. The live round trip is unproven: the window opens, but no ready frame showed up in the logs.
- Review fixes: truthful eye-control status, EMI door opens the feed directly, WPF build no longer ships `shared/tests`. Byte-range seeking and the carrier JS test come in the next PR, game-fyp-range.

## avalonia-port/game-fyp-range: +113
- FYP follow-up: the asset server now answers byte ranges, so the For You feed can jump into a clip window and loop it instead of playing each video from the start. Adds the carrier JS test split off game-fyp.
- WebAssetServerTests covers 206, suffix/open ranges, 416 (with `bytes */len`, and for `bytes=-0`), ignored backward/multi ranges, HEAD and `Accept-Ranges`. Remote gate: Linux green; windows red on 2 unrelated WPF timing flakes (run 38337309579). carrier.test.js runs locally only.

## avalonia-port/webview-probe-fix: +213
- WebHost's probe now follows Avalonia.Controls.WebView 12.1 on Linux (WPE = OffscreenRenderer, WebKitGTK = NativeDialog). Linux web views stay off by default (fallback panel). `CCP_LINUX_WEBVIEW=wpe|gtk` opts in; gtk sets PreferWebKitGtkInstead.
- Live runs 4-5 (unlocked, intake window active): WPE froze after one frame and GTK drew only the background. Neither sent a rAF heartbeat, so the default stays off (evidence/webview-probe-fix/live-run4-5.md).

## avalonia-port/suite-memory: +112
- CCP.Avalonia.Tests shard peak RSS 8.4-12.1 GB -> 1.6-2.9 GB: BeatLoop stops on window close (even parked), Opened-time static subscriptions no longer re-added on each Hide/Show, GC after window tests.
- Regression tests: ClosedShellsReleaseMemory with a tray round trip; AnAmbientLoopDoesNotKeepItsClosedWindow (ticking + parked).

## avalonia-port/possession-head-a: +770
- Possession head A: transform lease, off-limits tree and 4 transform effects (nudge/drift/breathe/wobble) on the Core deck, with a 45 s cooldown and a panic surface that restores every touched control.
- 5 headless tests (hidden-parent and stepped-clock cooldown fail-proven). Controller and Exclude tags come in head-b; row stays stub.

## avalonia-port/companion-core-b: +81
- Cloud session (Opus), local review + gate.

## avalonia-port/chaos-intro: +393
- ChaosIntroWindow ported: the hub shows it on its first open, sets and saves SeenIntroGuide, the `?` button shows it again, and panic closes it (fail-first tests, render-all).
- Ledger row chaos-intro-window goes from missing to stub, because nothing a user can reach opens the hub yet.

## avalonia-port/flake-stackcapture: +173
- Root-caused 4 flakes: parked-thread stack snapshot; SettingsService disarm waits for in-flight debounce writes + TestIsolation late-write guard; hit test renders the live tree before each probe; voice baseline read before panic.
- Each reproduced or mechanism-proven fail-before/pass-after (evidence/flake-stackcapture/proof.md); no assertion loosened.

## avalonia-port/game-race-d: +487
- Racing Thoughts plays and charts local tracks through LibVLC (RaceTrackPlayer + TrackDecoder seed), like WPF TrackPlayer/NAudioTrackDecode.
- Load is non-blocking, and a Brake right after track-play is held until playback starts (fail-proven test).

## avalonia-port/backroom-core-a: +231
- Back Room rules (bridge, API relay, fx plan/queue, tab ledger, overlay math) moved into CCP.Core with two small WPF seams; the 10 test classes now run on Linux CI.
- Fix round: stale comments are fixed, and the two bridge/ledger tests call SetResult off the test thread before asserting. Remote gate GATE-OK.

## avalonia-port/emidesk-core-b: +291
- Cloud session (Opus), local review + gate. Merges avalonia-port/emi-line-engine (P19): EmiLineEngine/EmiKnock
  moves, ask probes and ask test are that branch's; this one moves EmiNames, EmiBarkBridge, EmiBubblePlacement,
  EmiTopmostRule, TubeDeskVisibility to Core with three head seams. +291 includes emi-line-engine's two commits.
  rgate GATE-OK (run 38344989624). Land emi-line-engine first.

## avalonia-port/possession-core-b2: +291
- PossessionAudio (ember tick + rung/tripwire dip) git mv'd to CCP.Core behind CoreAudio (+IsOutputSuppressed seam), stacked on possession-core-b; Install takes the Core director directly (App.WirePossessionAudio gone).
- Tests/CCP.Core.Tests/PossessionAudioTests.cs: 7 facts, each fail-proven (evidence/review-possession-core-b2/failproof.log); no Avalonia caller yet (no director on this head).

## avalonia-port/friend-toast-knock: +856
- Friend invites now show a WPF knock card (countdown ring, tuck, stack, Not now / Later -> Inbox; Join only for Ramp, which opens the panel on Studio), driven by the Core landing router from startup; panic folds the cards into the Inbox.
- win-friend-toast set to n/a (WPF removed FriendToast in 17213d9bf); win-knock-card and feat-friends-landing set to stub; feat-friends-open-tables names the folded notices.

## avalonia-port/monitor-picker: +56
- Overlays honour the global "Show content on" pick like WPF ResolveScreens (pink/spiral Default, unplugged fallback to primary); fail-proven resolver tests.
- feat-monitor-target-picker ledger row missing -> wired (picker already existed).

## avalonia-port/arcademy-core-a: +249
- Cloud session (Opus), local review + gate.
- Fix round: the meta-store debounce runs on a TimeProvider and its timer is disposed; the test uses a stepped clock and cleans up its temp folders; every test store runs on a fake clock.

## avalonia-port/quiz-core-a: +178
- AI quiz (`QuizService`) moved into CCP.Core (git mv, Core seams); WPF now honours a loopback `CCP_AI_BASE_URL` like Core `AiService`.
- 8 Core tests against a loopback fake proxy; cloud session, local review + remote gate.

## avalonia-port/chaster-trailer-pop: +519
- ctrl-chaster-booked-pop missing -> wired: the "+N:NN" pops at its cause/cursor in an X11 click-through window on a FrameClock (rail flash fallback on Wayland), and the day/tab-full CapRefused tag; BookedPopLayout + CapNotice git-mv to Core.
- ctrl-chaster-trailer-view stays missing: it needs the views-chaster-help-price-edit menu rows (its only WPF host), so it should be ported with them.

## avalonia-port/mine-settings-rows: +72
- Settings > Notifications banner pool switch (`ChkBannerPool`, WPF parity) with a fail-proven paint/write test.
- Parity row views-settings-notifications -> stub: banner pool and intake nudge readers are still missing (shell-marquee).

## avalonia-port/leash-punish: +999
- Leash task runner host + caged punish video window: the gate action starts the task, panic/Cut park it and close a locked window.
- A palette-closing Escape is not a panic (WPF MainWindow.Leash.cs:382).

## avalonia-port/welcome-back-logo: +832
- Welcome-back sheet + its WPF trigger (backup-less; row stub). WelcomeBackDecision moved to Core.
- The dashboard logo animates on the 30 fps beat, drawn off-thread and parked when hidden (row wired).

## avalonia-port/webview-render: +477
- Root cause of "no Linux web view keeps drawing": the web process aborts on a page's first WebAudio sound when GStreamer's autoaudiosink (gst-plugins-good) is missing. The engines are fine: WebKitGTK draws at ~62 fps. In addition, wpewebkit 2.54 dropped the libwpe API, so 12.1's WPE adapter crashed the app (upstream #88).
- WebKitGTK is now the Linux default (`CCP_LINUX_WEBVIEW=wpe` only where the export exists, `=off` for the panel). A missing autoaudiosink shows the panel with the distro install command. PKGBUILD depends on gst-plugins-good, and WPE moved to optdepends. Evidence: evidence/webview-render/findings.md.
- oracle-deep review adopted: the gate stays, with no fake sink. An explicit `CCP_LINUX_WEBVIEW=gtk` overrides the gate. The probe follows the GStreamer path rules: GST_PLUGIN_SYSTEM_PATH replaces the distro dirs, and the user dir counts. The panel and the startup toast now tell the user to restart CCP after installing.

## avalonia-port/goongame-core-c: +208
- Cloud session unit (goongame-core-c); reviewed and gated locally.

## avalonia-port/compositor-core-b: +33
- Cloud session unit (compositor-core-b); reviewed and gated locally. GlyphFallback font tests skip visibly without a covering font.

## avalonia-port/chaos-effect-banner: +556
- Chaos effect-banner strip ported: Core `ChaosEffectBannerStrip` (order, de-dup, fade/throb timing) and a click-through Avalonia window, created and closed by ChaosRunHost at the WPF lifecycle points.
- No banner without a compositor, or when the platform refuses the window. The refusal is remembered per run (one Warning, no rebuild on each Show).
- Row chaos-effect-banner changed from missing to stub: no effect calls Show on this head yet.

## avalonia-port/companion-transcript: +81
- Her Room History transcript: panic closes it (PanicSurfaces "companion-transcript"), stored-dialogue + panic tests (fail-proven), ledger row missing -> wired.

## avalonia-port/main-20261015: +69256
- Main sync #7: WPF main + release 7.1.14 (f7f0f612b) merged (releases 7.1.6-7.1.14); delta ledger docs/avalonia-main-sync-20261015.md (80 ported-by-merge, 195 needs-port in 18 sync7-* lanes, 34 n/a). Red by design; gated in -compat.

## avalonia-port/main-20261015-compat: +317
- Compat for main sync #7: Scrolller cluster + 13 helpers into Core (licence switch now reaches the Avalonia online-media path), App.* in Core as seams, head hooks (sweeper, idle voice, funnel), Avalonia vault card/update button follow 7.1.14, WPF scan tests via SourceRoots/Assets. rgate green except the listed RaceTrackPlayer flake (run 39709240603).

## avalonia-port/possession-core-c: +319 vs base
- Cloud session unit (possession-core-c); reviewed and gated locally.

## avalonia-port/deeper-core-b: +337
- Cloud session unit (deeper-core-b); reviewed and gated locally. Review follow-up: 2 tests (effect/haptic lines, cancelled recording dispatch).

## avalonia-port/deeper-core-b: +342
- Cloud session unit (deeper-core-b); reviewed and gated locally. Review follow-up: 2 tests (effect/haptic lines, cancelled recording dispatch).
## avalonia-port/video-core-b: +334
- Cloud session unit (video-core-b); reviewed and gated locally.

## avalonia-port/video-core-b: +334
- Cloud session unit (video-core-b); reviewed and gated locally.

## avalonia-port/emidesk-core-c: +47
- Cloud session unit (emidesk-core-c); reviewed and gated locally.

## avalonia-port/emidesk-core-c: +47
- Cloud session unit (emidesk-core-c); reviewed and gated locally.
## avalonia-port/dtrh-feel-sync: merge of 46e9e908d
- Imports the head dev's DTRH feel rework (dtrh-feel/01..17, 27 commits). It had one conflict, in DtrhAssetManifest (resolved by keeping both the loop field and CorePaths.UserData). The 24 new web files are under Assets/web/dtrh, KeptEscapePage.Dtrh is in Core PanicPolicy, and the node tests pass 81/81.

## avalonia-port/dtrh-feel-port: +77
- DTRH feel rework delta ledger (27 rows: 22 ported-by-merge, 5 needs-port -> lane game-dtrh-host), parity row game-dtrh (missing), DtrhMotionWire git mv to Core with fail-proven Core tests. Local gate GATE-OK (P97 fallback; Windows runs in PR CI).

## avalonia-port/sparkle-wallet: +688
- Header Sparkle wallet + invite/help popups on BeatLoops; balance from SkillPoints, award cue via SparklePointRewards (moved to Core).
- ctrl-sparkle-wallet -> stub (EmiFace), shell-sparkle-wallet -> stub (no Avalonia award producer yet).

## avalonia-port/chaos-bubble-field: +458
- Chaos bubble variants and payloads moved to CCP.Core (uint tints; Fire goes through the head-seeded EffectPayload.Firer). WPF fire bodies are now in EffectPayloadFiring.
- New ChaosFieldMath (spawn cadence, pop points) is used by WPF ChaosModeService and has tests proven to fail when broken. Avalonia seeds Firer with a logging no-op; the bubble field is the next lane.

## avalonia-port/chaos-pop-text: +440
- Chaos pop text ported: pooled click-through combat-text overlay with WPF timing/tint, pool closed on run teardown.
- Row stays stub until bubble field/toys/payout callers are ported; live overlay steps queued.

## avalonia-port/overlay-speak-cue: +499
- Speak cue overlay + Core SpeakCueState ported (fade, anchor, panic, render-all); row stays stub until Deeper runtime gives it a caller.

## avalonia-port/ci-skip-cloud: +19
- Push-triggered port workflows (distro-check, reference-render, snapshot-lock) skip avalonia-port/cloud-** and gate-** branches; workflow_dispatch kept.
- Decision entry reserves the cloud-/gate- prefixes for throwaway branches and notes that gate pushes already carry [skip ci].

## avalonia-port/flake-clocks: +52
- ChasterFlash coalescing and MatchClock scheduled fire run on a TimeProvider seam; ChasterBill coalescing test and the P108 GoonClock dispatch test are deterministic (stepped clocks), both fail-proven; rgate 39687660015 green.

## avalonia-port/panic-key-sweep: +185
- Panic key matched by Key value (alias-aware, Enter/Return) on MandatoryVideoOverlay, BubbleCount game/result, FirstShowStage via one helper `Platform/HotkeyNames`.
- Panic bound to Enter now beats the focused control; headless tests fail-proven.

## avalonia-port/chaos-fx-window: +498
- Chaos FX window (vignette, edge glow, heat tint) ported with Core envelope maths; heat tint wired into ChaosRunHost; run-end close tested (fail-proven).
- Row chaos-fx-window: missing -> stub (no user path to StartRun yet).

## avalonia-port/launcher-edge: +134
- Rounded launcher (DWM round on Windows, transparent + 8 px clip elsewhere, square when maximised); glint turns in the unit box like WPF RelativeTransform.
- Edge timing (flare/breath/glint/idle 14 s/focus 4 s/park) covered by 4 fail-proven tests (focus gap on a stepped clock); win-launcher-edge -> wired.

## avalonia-port/v2-get-it-row: +290
- V2GetItRow ported (Core V2PurchaseRule via git mv); Flashes v2 box shows the Get it row: owned hides it, signed out offers sign-in, signed in reads "Not available" until feat-backroom-counter-buy.
- ctrl-v2-get-it-row missing -> stub; new row feat-backroom-counter-buy.

## avalonia-port/inline-loop-video: +362
- InlineLoopVideo (muted looping LibVLC surface via VlcFrameSink) is now in the HelpPopover clip poster; ctrl-inline-loop-video -> wired, lifecycle and leak tests fail-proven; review fixes drop wall-clock asserts.

## avalonia-port/overlay-takeover-announcer: +617
- Takeover cue overlay ported: Core TakeoverCueQueue (queue + WPF timings), Skia bloom/sparks + shake on a click-through X11 window, shown on the announce roll.
- Panic and shell close clear the queue and close it; ledger overlay-takeover-announcer -> wired.
- Review fixes: the topmost re-raise runs on real elapsed time and the alpha is written only when it changes; the test drives performable actions.

## avalonia-port/overlay-takeover-announcer: +617
- Takeover cue overlay ported: Core TakeoverCueQueue (queue + WPF timings), Skia bloom/sparks + shake on a click-through X11 window, shown on the announce roll.
- Panic and shell close clear the queue and close it; ledger overlay-takeover-announcer -> wired.
- Review fixes: the topmost re-raise runs on real elapsed time and the alpha is written only when it changes; the test drives performable actions.
## avalonia-port/companion-barks: +1016
- BarkEngine extracted into Core; Avalonia BarkHost plays default-voice barks (bouncing-text, doorbell triggers, mute/panic).
- Review fixes: ordinary barks via Giggle (no chat log), honest test claims, EMI mirror listed; rgate GATE-OK (run 38460174466), committed 5f2c3ddfb.

## avalonia-port/companion-barks: +1016
- BarkEngine extracted into Core; Avalonia BarkHost plays default-voice barks (bouncing-text, doorbell triggers, mute/panic).
- Review fixes: ordinary barks via Giggle (no chat log), honest test claims, EMI mirror listed; rgate GATE-OK (run 38460174466), committed 5f2c3ddfb.

## avalonia-port/adaptive-side-art: +278
- AdaptiveSideArt.CollapseBelow ported to Avalonia and applied to 21 side-art views with WPF thresholds; row feat-adaptive-side-art updated (not verified).
- Headless resize test fail-proven; source-scan test pins all 21 thresholds to WPF; rgate GATE-OK.

## avalonia-port/lock-title: +635
- LockTitle ported (padlock letters, candy/ice, entry/idle/jolt/rattle with WPF timings on one FrameClock parked when hidden/detached) and wired into the Chaster hero; ctrl-lock-title -> wired.
- LockTitleTests (curves + stepped-clock lifecycle, fail-proven, incl. the 4 Chaster tab hooks); no shadow (P82 ratchet), no booking sparks.

## avalonia-port/banner-link-overrun: +40
- Banner web link stays inside its line at the narrower header (lead + link in one embedded DockPanel; link ellipsizes like WPF); headless test narrows HeaderBannerHost explicitly.

## avalonia-port/chaos-gif-cascade: +798
- Chaos gif rain ported: Core GifCascade maths (WPF timing/caps/motion) + click-through FrameClock overlay ticking every frame; panic surface gif-cascade. Row stub (no caller yet).
- Review P3s fixed: spawn band uses the window's DesktopScaling (mixed DPI).

## avalonia-port/chaos-boon-bar: +420
- Chaos run-pick ribbon (ChaosBoonBarOverlay) ported: opened at BeginRun, rebuilt live in pick order with newcomer pop-in (Core ChaosBoonBarRules), closed at run end/panic.
- Row chaos-boon-bar missing -> stub (no user path starts a run yet); live steps queued in live-queue/overlays.md.

## avalonia-port/chaos-cursor-glow: +410
- Rabbit Caller cursor halo ported: Core maths (size/breath/follow), click-through topmost Avalonia overlay with Arm/Disarm/MoveToPx/CloseActive seam, closed by ChaosRunHost teardown and panic (PanicSurfaces).
- Row chaos-cursor-glow missing -> stub (nothing arms it until the Rabbit Caller skill/run loop exist).

## avalonia-port/emi-desk-extras: +518
- Emi desk toss on Avalonia: drag lift cue, fling detection, landing squash and the land face, using the new Core `EmiToss` (EmiFlingDetector + EmiTossRules.LandingScale), which WPF now calls too.
- Row win-emi-desk-extras missing -> stub (land body frame and dizzy chain missing; Tube has no user path).

## avalonia-port/chaos-bubble-field-b: +418
- Chaos bubble field: SpawnTick cadence, treat/prism pops and score on BubbleOverlay, cleared by panic/stop (Core ApplyTreatPop/ApplyPrismPop); manual pause holds the field and swallows clicks.
- Still no live bubbles, FALL IN or payout (next: chaos-payout-fallin).

## avalonia-port/game-dtrh-host: +897
- Down the Rabbit Hole opens on Avalonia behind the Lab door. Start-up sends init with motionLevel and the media manifest; the run lifecycle and payout use Core `DtrhRunWire`, which the WPF host now calls too; a window closed mid-descent banks its last snapshot.
- Escape with DtRH in front pauses it (kept-escape); a second Escape inside 2 s closes it. Slice B covers meta, online media, the art/audio origins, watchdog and barks.

## avalonia-port/chaos-announcer: +618
- Chaos announcer: Core queue/timing shared by both heads; Avalonia click-through overlay on the shared beat, T-10 s/Relapse announces, panic closes it (row stub).
- Review fix: ChaosOverlayWindow calls the real announcer (stand-ins gone); line centred on the work area incl. left-docked panels.

## avalonia-port/companion-picker-card: +689
- CompanionPickerCard ported (live + preview faces, PerkPicker, Core `CompanionPerks.Active/TrySelect` shared with WPF SetPerk); left unhosted per ruling B, rendered by render-all. Row views-companion-picker-card: missing -> stub (hosting -> lane companion-picker-host, needs a WPF reference render).
- Test CompanionPickerCardTests (selection -> settings fail-proven); local gate GATE-OK, Windows not gated (P97).

## avalonia-port/chaos-vibe-trail: +482
- VibePopping pointer glow + fading trail overlay ported (Core `ChaosVibeTrail` maths, click-through Avalonia window, panic surface); row stays stub until the vibe window starts it.

## avalonia-port/chaster-help-price-edit: +693
- Chaster tab: menu boards (rows write ChasterPrices, red flash switch), how-it-works card, menu hint, Reset times and a per-row price editor on Core TabPriceEdit. Esc belongs to the box (Windows only; X11 has no claim yet), and a starting lock closes and freezes it.
- `ChasterMenuTests` (2 tests, 6 breaks each turned a test red); the render frame shows the menu states; views-chaster-help-price-edit -> wired.

## avalonia-port/marquee-reads: +490
- Home marquee Barnum interludes are wired: cadence, split, picker and banner pool are in Core; only one act (NeonWarmUp, fade path) is ported. Row shell-marquee-reads missing -> stub.
- The loop clock stops while Home is hidden (as in WPF). Rest of the acts and FX: marquee-reads-b.

## avalonia-port/chaos-flash-overlay: +483
- Chaos braindrain wash on Avalonia: a faint desktop-wide click-through image or GIF wash per stage screen, on the WPF fade curve (new Core `ChaosFlashWash`), refused without a compositor, closed by panic, run end and exit.
- Row chaos-flash-overlay missing -> stub (no caller on this head yet).

## avalonia-port/chaos-field-fx: +641
- chaos-field-fx missing -> stub: Core ChaosFieldFxModel (fail-proven tests) + click-through Avalonia overlay on the shared FrameClock, hidden/paused when idle, refuses without compositor.
- Panic and run teardown close it (PanicSurfaces chaos-field-fx); nothing draws on it until the bubble field lands.

## avalonia-port/chaos-unlock-card: +588
- Unlock-card rules moved to Core (WPF delegates); hub purchases show WPF's queued top-center unlock card on Avalonia.
- Interactive mid-run overlay + lesson pause deferred to chaos-unlock-card-b (row stays stub).

## avalonia-port/chaos-wave-timer: +418
- Pocket Watch wave countdown overlay ported (Core readout rules shared with WPF), hooked to ChaosRunHost ticks; panic/run end close it; it refuses to show without click-through or transparency.
- Row chaos-wave-timer missing -> stub; live steps queued in live-queue/overlays.md.

## avalonia-port/game-dtrh-host-b: +774
- DtRH online half moved to Core `DtrhRemoteMedia` (online media copied locally, manifest re-posted as copies land, loops lane, 30 min loops-dry back-off); WPF now calls it; WebAssetServer serves only `.temp/ccp_temp_remote_*` copies.
- DtRH manifest uses root-relative asset urls so hostMedia.js counts the library as local (it never did on Avalonia); delta rows 16/22/24 -> ported-here.

## avalonia-port/friends-landing: +389
- FriendsLanding on Avalonia: the poke-received word plus Emi line, sent beat, and Tell over the game/panel/launcher, routed by the Core router; LandingRules.SentWord/PokeThrowsWord (fail-proven).
- feat-friends-landing -> wired; knock/inbox/flavour folded to win-knock-card, toasts to feat-friends-open-tables.

## avalonia-port/chaos-payout-fallin: +215
- The Chaos hub's FALL IN starts a real run. Its end banks the WPF payout: XP, Sparks, reveals, the rank card and the loop-tip gold. The maths is in Core `ChaosPayout`, which WPF also uses. Panic pays nothing.
- Fail-proven: `ChaosPayoutTests` (Core) and the headless test that clicks FALL IN, steps ticks to the end and checks the payout. Live panic steps are queued in live-queue/safety.md. The hub has no startup opener yet (its only opener is the recap), so no user path reaches FALL IN.

## avalonia-port/chaos-estim-overlay: +576
- Chaos E-Stim lightning (click-through, refuses without a compositor) and the X11Pointer cursor halo are ported visual-only, with Core `ChaosEStimMath`. Run end and panic close both. Rows are stub because nothing calls them on this head yet.

## avalonia-port/brain-drain-keep-clear: +150
- Keep-pictures-clear rule moved to Core (tests now run on Linux); the Avalonia Brain Drain card has the option row, saved to the same setting and repainted on Reset/cloud restore.
- No blur overlay on this head yet, so the setting has no effect there; ledger row is stub.

## avalonia-port/chaos-skia-fx: +723
- chaos-skia-fx: Avalonia click-through sparkle/bloom overlay. The WPF `ChaosFxLayer` sim and painter moved to Core and is shared by both heads. The frame loop runs only while effects are live; refused without a compositor; panic closes it (`PanicSurfaces` `chaos-skia-fx`, ChaosRunHost).
- Row stays stub until FALL IN or the bubble field emits effects. Tests are fail-proven (15 deliberate breaks, each caught). rgate GATE-OK, Windows included.

## avalonia-port/leash-receipts-hold: +836
- Hold-to-cut: Core LeashHoldFlow; the ring and the "Cut the leash?" window run from the panic key (X11 key-up added). Early release never cuts, and the panic still fires on the first press.
- Holder receipts list (LeashSentList) ported but not hosted yet; the row is stub until the holder card lands.

## avalonia-port/chaos-dvd-overlay: +674
- Chaos DVD bounce: Core ChaosDvdFlock (fail-proven maths) + one click-through host window on the existing frame source; panic/run-end close it; refuses without a compositor.
- Rows chaos-dvd-overlay + chaos-dvd-host missing -> stub (no caller until the ChaosModeService toy layer lands).

## avalonia-port/theme-converters: +1
- theme-converters row missing -> wired: every WPF shared converter maps to an Avalonia built-in (IsVisible bool, `!` negation) or is made unnecessary by native colour emoji/IconGlyph; no new converter.

## avalonia-port/chaos-toy-button: +610
- Chaos toy hero buttons: per-toy topmost no-activate window, click + key trigger (after panic), Core cooldown/charge rules, panic closes them.
- Row chaos-toy-button missing -> stub (no run start path, effects absent); live steps in live-queue/safety.md.

## avalonia-port/chaos-bubble-host: +1
- chaos-bubble-host row missing -> stub: ambient half mapped onto BubbleOverlay; chaos bubbles, flash/subliminal hosting, z fixer and the shared-host toggle listed as missing (pending lane chaos-bubble-field-b).

## avalonia-port/goongame-core-d: +194
- Cloud session unit (goongame-core-d); reviewed and gated locally.

## avalonia-port/deeper-core-c: +519
- Cloud session unit (deeper-core-c); reviewed and gated locally.

## avalonia-port/bureau-core-a: +490
- Cloud session unit (bureau-core-a); reviewed and gated locally.

## avalonia-port/compositor-core-c: +90
- Cloud session unit (compositor-core-c); reviewed and gated locally.

## avalonia-port/launcher-media-dialog: +805
- Cloud session unit (launcher-media-dialog); reviewed and gated locally.
- Review fixes: Done/link hover paint, Yes on consent, video volume follows master, stale veil comment.

## avalonia-port/prizes-core-a: +171
- Cloud session unit (prizes-core-a); reviewed and gated locally.

## avalonia-port/remix-core-a: +158
- Cloud session unit (remix-core-a); reviewed and gated locally.

## avalonia-port/emidesk-core-d: +185
- Cloud session unit (emidesk-core-d); reviewed and gated locally.

## avalonia-port/video-core-c: +257
- Cloud session unit (video-core-c); reviewed and gated locally.

## avalonia-port/marquee-reads-b: +752
- Home marquee interludes: all five WPF read acts, NeonWarmUp flicker + tag breath, baked strip glow, return sheen on the shell's support banner (WPF SweepBannerSheen); Photosafe drops flicker/jitter.
- shell-marquee-reads -> wired; MarqueeGlyphRot moved to Core, CanPlay rule in Core.

## avalonia-port/friends-feed: +557
- Friends drawer "What happened" feed, per-friend send trails, chip unread badge, tray unread count, accept-after-fold honesty word (feat-friends-feed -> partial).
- No Core changes (rules already in Core); 4 fail-proven tests; Windows not gated (P97 local fallback).

## avalonia-port/friends-open-tables: +259
- Friends drawer: open Goon tables (hosting row first, pink wash, Join/lock, 30 s poll while open); LobbyWire seeded on this head.
- Join, the launcher badge and the corner notices are still stub/missing (no Goon host or Goon tile on this head; notices go to the -b lane).

## avalonia-port/friends-open-tables: +259
- Friends drawer: open Goon tables (hosting row first, pink wash, Join/lock, 30 s poll while open); LobbyWire seeded on this head.
- Join, the launcher badge and the corner notices are still stub/missing (no Goon host or Goon tile on this head; notices go to the -b lane).

## avalonia-port/friends-open-tables: +259
- Friends drawer: open Goon tables (hosting row first, pink wash, Join/lock, 30 s poll while open); LobbyWire seeded on this head.
- Join, the launcher badge and the corner notices are still stub/missing (no Goon host or Goon tile on this head; notices go to the -b lane).
## avalonia-port/companion-v2-conversation: +989
- Companion V2 conversation page ported (mounted above the room; collapse -> companion-v2-pages-b); name editor + recap on Companion > AI; pure rules in Core ConversationRules shared with WPF.
- Review fixes (style scope, hover/disabled brushes, WPF name/flavor sources); row views-companion-v2-conversation missing -> stub.

## avalonia-port/companion-v2-conversation: +989
- Companion V2 conversation page ported (mounted above the room; collapse -> companion-v2-pages-b); name editor + recap on Companion > AI; pure rules in Core ConversationRules shared with WPF.
- Review fixes (style scope, hover/disabled brushes, WPF name/flavor sources); row views-companion-v2-conversation missing -> stub.

## avalonia-port/companion-v2-conversation: +989
- Companion V2 conversation page ported (mounted above the room; collapse -> companion-v2-pages-b); name editor + recap on Companion > AI; pure rules in Core ConversationRules shared with WPF.
- Review fixes (style scope, hover/disabled brushes, WPF name/flavor sources); row views-companion-v2-conversation missing -> stub.

## avalonia-port/companion-v2-conversation: +989
- Companion V2 conversation page ported (mounted above the room; collapse -> companion-v2-pages-b); name editor + recap on Companion > AI; pure rules in Core ConversationRules shared with WPF.
- Review fixes (style scope, hover/disabled brushes, WPF name/flavor sources); row views-companion-v2-conversation missing -> stub.
## avalonia-port/post-push-nits: +6
- Lands two review nits that were committed after their branches were pushed: the BillboardShowcase test tidy (w161) and the hybrid-gate decision wording (w124).

## avalonia-port/post-push-nits: +6
- Lands two review nits that were committed after their branches were pushed: the BillboardShowcase test tidy (w161) and the hybrid-gate decision wording (w124).

## avalonia-port/post-push-nits: +6
- Lands two review nits that were committed after their branches were pushed: the BillboardShowcase test tidy (w161) and the hybrid-gate decision wording (w124).

## avalonia-port/post-push-nits: +6
- Lands two review nits that were committed after their branches were pushed: the BillboardShowcase test tidy (w161) and the hybrid-gate decision wording (w124).

## avalonia-port/decision-tester-verification: +37
- decisions log entry: tester verification + proposed goal text

## avalonia-port/ledger-dedupe: +54
- Parity ledger: 64 duplicate row ids merged (honest status, newer text first, union of paths/notes), section row counts fixed.

## avalonia-port/emidesk-core-e: +344

EmiChains data/rules and EmiNudges moved to CCP.Core (git mv); WPF keeps the DispatcherTimer player as EmiChainPlayer and seeds EmiNudgeWorld.QuietProbe at startup. Avalonia reads EmiChains.RestFace. Core tests: moved EmiNudgeMachineTests + new QuietProbe/EmiChains tests. No UI change; no parity rows moved.

## avalonia-port/deeper-core-d: +194
- Cloud session, local review (ACCEPT) + gate.
- AudioWaveformCache (DPK2 cache + bucket rule) moved to Core behind a Decoder seam; WPF NAudio decoder registered at startup; 10 Core tests.
- Review fixes: withdrew unbacked pass counts from Verification limits; WPF sample provider built lazily again (pre-move parity). rgate GATE-OK.

## avalonia-port/stakes-core: +112
- Stake wire, bridge and settlement maths (plus V2WalletAdoption) moved into CCP.Core with git mv. The WPF App gates are now in StakeAppHooks, seeded at startup. StakeTests (25) run in CCP.Core.Tests and are fail-proven.
- feat-stakes missing -> stub. Next lane is stakes-host: route the Avalonia GameWindow `stake-*` frames to StakeBridge.ForApp and seed the head gates.

## avalonia-port/goongame-core-e: +402
Goon match engine (GoonMatchService, MatchClock, GoonSuddenDeath, GoonTransportBase/GoonDispatch, GoonLoopbackTransport, Rounds/*) git-mv to CCP.Core; host/join/paint-stall rules extracted to GoonHostRules (one copy, WPF calls it); DispatcherTimers -> TimeProvider; GoonDispatch.ShutdownStarted seeded in WPF App(). Tests: GoonPaintStallRuleTests moved, GoonHostGateTests, GoonMatchEngineTests (one-thread loopback match, fail-proven). Gate: local Linux GATE-OK; Windows red only on known flakes.

## avalonia-port/tester-test-plan: +675
- docs/avalonia-test-plan.md: tester steps per ledger area (open/click/expect, WPF file:line, row ids), the live queue rewritten in plain words, safety steps flagged for an agent live check before the shipping switch.
- Every ledger row id is listed with its WPF reference. Only tester reports move a row to verified.

## avalonia-port/video-core-f: +113
- Moved six video memory-render helpers (`DisplayAspectFrom`, `FitToAspect`, `StagingBytesFor`, `ComputeSnapshotDims`,
  `IsSnapshotFrame`, `IsRebuildCurrent`) from WPF `VideoService` into `CCP.Core/Services/Video/VideoRenderMath.cs`, unchanged.
  WPF calls them through `using static`.
- `BlurSurfaceFrameTests` (24 cases) moved to `Tests/CCP.Core.Tests`, so they now run on Linux CI.
- Review accepted (side-by-side equal). The two P3s were left as they are, with reasons given in the body.

## avalonia-port/wire-win-quiz-report: +21
- win-quiz-report: run-row hover (#15FFFFFF) and a real pointer click on a report row are now tested through an open gate. Stays stub because both heads hide BtnStartQuiz; whether to show it or delete it is an open product decision.
- QuizPastRunsReportTests fail-proven (failproof-5-no-hover.log).

## avalonia-port/video-max-cap-links: +192
- BUG-7NAKK79CZ2: the user's max video length now ends app-started web-video takeovers (Core `VideoMaxCap.TakeoverStopAfter`, wired into the WPF BrowserMediaService), and a heal or fallback replay continues the clip's cap on both heads.
- Tests: Core VideoMaxCapTests plus the headless MandatoryVideoOverlay heal-at-cap test, both fail-proven. Linux gate green; one known Windows flake (PhrasePoolCustody).

## avalonia-port/wire-views-settings-performance: +368
- Settings > Performance: AutoPerformanceMode escalates via Core PerformanceTierRule (shared with WPF); GPU decode is read by the mandatory-video player; a motion change repaints the shell chrome.
- Row stays stub: ChkUnifiedOverlay and chrome FX refresh -> wire-views-settings-performance-b.

## avalonia-port/wire-win-mini-player: +103
- win-mini-player: stub -> wired. Library thumbnail double-click and context-menu Preview open MiniPlayerWindow owned by the shell; closing it re-activates the shell only while we own the foreground (or none, as WPF).
- AssetsTabTests: 2 tests, fail-proven (evidence/wire-win-mini-player/fail-proofs.txt).

## avalonia-port/wire-win-knock-card: +120
- Knock card: the sender's proxy picture fades in over the initials (WPF LandingChrome.Avatar). Join, Not now and the x lift 1.02 over 150 ms quad-out and snap at Motion Off (WPF MotionFx.HoverLift).
- Tests: KnockCardTests cover the picture fetch (no network) and the hover lift with motion Off and Full. Each was fail-proven.
- Ledger: win-knock-card stub -> wired. rgate GATE-OK.

## avalonia-port/possession-core-d: +211
- Warden bark rules (`WardenRules`) and reactive consent/Start-Stop rules (`PossessionReactiveRules`) moved into CCP.Core; WPF head calls them, no private copies.
- PossessionBarkRulesTests git-mv'd to CCP.Core.Tests (runs on Linux); 6 new pure-rule tests.
- Review ACCEPT (54); P3 fix: run labels use WardenRules.Knock/Stare. rgate GATE-OK.

## avalonia-port/wire-views-daily-quest-card: +239

- Daily quest card gets WPF MotionFx motion: bar fill tween (0.6 s QuadOut, resize snaps), hover lift 1.02/150 ms, gold BoxShadow glow (blur 18, 0.5) fading out; MotionLevel.Off snaps and skips the glow.
- Test: DailyQuestCardMotionTests (Full + Off, stepped clock), fail-proven. Ledger views-daily-quest-card: stub -> wired.
- Not run: real-pointer desktop feel.

## avalonia-port/settings-migration-test: +616
- Settings migration test over a real WPF profile (local only; CI runs a synthetic variant): 768 keys and 9568 files survive startup+shutdown.
- It found and fixed 3 losses: Companion drawers reset on exit, built-in preset action ids re-minted each load, Circe-neutral voice lines reverted (LockedMod/overrides now ship).

## avalonia-port/linux-package-wired: +28
- linux-package -> wired: the installed tarball reaches the First run window under kwin --virtual (splash 0.46 s, wizard 5.80 s, 749 MiB installed); AUR test-local passes.
- Hand-install steps for the tarball added to docs/avalonia-linux-install.md (the .desktop Exec needs a PATH launcher).

## avalonia-port/wire-shell-ambient-fx: +107
Premium room canvas enrolled in the shell ambient registry under `premium` (WPF MainWindow.Exclusives.cs:379; `exclusives` redirects). Settings/Programs already enrolled; WPF Play has no canvas. Ledger shell-ambient-fx names the unenrolled WPF `chaster` and `appsettings` canvases and their owning rows. Test TabAmbientRegistryTests.PremiumRoomRegistersOnceAndParksThroughRealNavigation (fail-proven). rgate: local Linux green, Windows run 38586062437 green.

## avalonia-port/wire-views-tab-deeper: +201
- Deeper webcam setup card: consent manage/revoke, calibrate, quick recal and tracker start/stop delegate to the Blink Trainer handlers as WPF does (MainWindow.BlinkTrainer.cs:1410-1488); consent colours/text and calibration line follow live state; Devices link opens Settings → Devices. Row stays stub; follow-up wire-views-tab-deeper-b (Import, Tutorial/Tour, status-chip telemetry).

## avalonia-port/wire-dialogs-username-picker: +93
- Headless tests pin the already-ported UsernamePickerDialog (length rules, colours, Cancel); not wired: WPF has no live caller.
- Remote gate green (run 38714794266); row stays `stub`.

## avalonia-port/wire-views-tab-haptics: +315
- views-tab-haptics: stub/wired duplicate rows merged into one row, now wired. The Tier-1 side-art column is back (WPF HapticsTabView.xaml:901), using a head port of AdaptiveSideArt and the #1321 rule moved to Core SideArtCollapse, which WPF now delegates to.
- Trunk HC10 haptics part: page Test/Play stop on rack module switch (unlike WPF) and HapticService.PanicStop also cancels a per-toy test; tests fail-proven, no wide render of the plate yet.

## avalonia-port/wire-win-mantra: +71
- win-mantra stub -> wired: Programs Mantra door click-through test; StartMantraSession failure dialog ported (WPF text).
- Review fixes: ledger names the kept-enrollment blocker (shell-programs-tab); test waits bounded by wall time (P73). rgate 39688519974 green except known RaceTrackPlayer flake.

## avalonia-port/wire-views-tab-presets: +870

Presets tab row views-tab-presets goes from stub to wired: the corner-GIF option panel, the session corner GIF in Core SessionRunner with a CornerGifOverlay session slot, the card sheen, and the rack hover reveal (out of the Tab order at rest). Fix round: a Fluent Folder icon on Select GIF, and gate fixes in ContentHitTest and ShellMemory. Fail-proofs: ~/ccp-port/evidence/review-wire-views-tab-presets/failproof.log.
- Round 2: ContentHitTest skip narrowed to `.rackActions` (fail-proven).

## avalonia-port/wire-views-tab-remote-control: +762
Remote tab wired as WPF: directory opt-in chain (5-tag cap, n/80, Remember), title-bar pill, share-media-sources row, tube emote bubble, hero art; Core RemoteRelay opt-in + re-publish (in-flight opt-in across a stop dropped). Row views-tab-remote-control: wired. Tests RemoteDirectoryTabTests, RemoteDirectoryOptInTests (fail-proven). Gate: rgate run 38563034446 GATE-OK; the fix round (run 39689830908) has Linux green and only unrelated Windows failures (P94).

## avalonia-port/wire-shell-roadmap: +116
- shell-roadmap: the photo-confirm/submit and locked-track tests prove the ported WPF roadmap flow, and the stale duplicate stub row is gone.
- The photo-submit test now resets the static roadmap and removes its diary photo (review P02, fail-proven). Photo test stubs the chime; ledger no longer over-claims fail-proof.

## avalonia-port/wire-dialogs-companion-phrase-editor: +353
- The Phrase Manager shows real voice-line and bark rows from Core `CompanionVoiceLines`, which WPF shares. Its bulk enable/disable persists to settings.json.
- Manage Phrases opens from the Companion room's Workshop Triggers cell. Tests cover the disk round-trip and the real navigation path.

## avalonia-port/wire-dialogs-companion-prompt-editor: +179
- Installed community prompt badge wired (purple name / red unknown), shared Core loader with one folder-name constant; WPF forwards to it.

## Cloud merge-fix: wire-shell-favorites-rail + wire-dialogs-companion-prompt-editor
- Keep the favorites Click tips popup, inverted dashboard clicks and pin NavGlow alongside the companion prompt editor's installed-prompt badge and shared Core loader.
- EscapeClaim documents both marked edit surfaces and retains the mini-player foreground rule. Parity rows consolidate evidence and advanced existing statuses without new verification claims; the retired migration-input blocker points to the local-only migration test. All panic surfaces and test assertions are retained.

## avalonia-port/wire-views-companion-room: cloud merge-fix
- Retained the lane's shared BeatLoop thinking dots, Core CompanionThinkingPulse, Enter-to-send/frame/minimise coverage, and room hide/detach parking while merging the incoming conversation page's room wiring and resume/park lifecycle.
- Ambient animation ratchets retain both the ChatThresholdView animation migration and the incoming DailyQuestCard Effect migration; no tests or assertions were removed to merge them.
- Consolidated the parity ledger to one row per id, retaining evidence and the most advanced status up to wired. The incoming prompt-editor progress entry remains above. Existing preview/DataContext composition and community-prompt-browser gaps remain in views-companion-room.
## avalonia-port/cloud-merge-wire-theme-inputs

- Combined wire-theme-inputs (`b144a765b`) with wire-dialogs-companion-prompt-editor (`6948b6de0`): preserved the TextBox chrome/focus/selection changes and tests, installed prompt badge/shared Core loader, and the complete companion-tip panic registry/tests. No textual merge conflicts.
- Retained every existing progress entry. The input lane had no separate progress entry; its implementation/evidence remains in the theme-inputs parity row. Consolidated parity IDs using the most advanced status (capped at wired), retained distinct earlier evidence as historical context, and preserved both lanes' evidence.
## avalonia-port/wire-views-companion-workshop-accordion: cloud merge-fix

- Retain phrase preset select/save/delete and the tab-return refresh alongside the conversation resume/park lifecycle and the shell phrase-manager entry from wire-dialogs-companion-prompt-editor.
- Preserve both lanes' tests and progress history. Consolidate parity IDs, retain prior evidence as context, and use statuses no higher than wired in this merge.
## avalonia-port/wire-dialogs-display-name: +989
- DisplayNameDialog row: stub -> wired. The welcome mode now has its caller: the shell arms WPF CheckPendingRegistrationAsync on Opened, followed by HandlePostAuthAsync and the name loop over the Core LegacyAccountApi (V1).
- The 401 recovery is Core `AuthRecovery`, shared by both heads: rename recovers the session without retrying; delete retries once.
- Review fixes: an unparseable restore-session 200 is no longer a recovery (WPF parity); time-bounded test waits; no armed timer in shell tests; the WPF 401 test now points at Core AuthRecovery.
- Gate: the local gate fallback passed (P97). Windows was not gated before push.

## avalonia-port/wire-win-tutorial-overlay: +394
- Tutorial overlay auto-advance triggers (click, text, selection, slider, CoreTutorialEvents) via shared Core TutorialAdvanceRules; row stays stub.
- Triggers unreachable on this head until Deeper tours host an overlay; remote gate green.

## avalonia-port/wire-ctrl-spiral-embed-view: +509
- Spiral §9 embed bridge shared in Core; Avalonia SpiralEmbedView on WebHost wired into Spiral tab (row stays stub: no descent publisher yet).
- Failed embed -> waiting room with WPF give-up latch; Navigated -> host shown + splash hidden; both tab-level tests fail-proven.

## avalonia-port/wire-shell-catalogue-submissions: +503
- Cloud session (codex), local review FIX, local fix commit 6f6e89466.
- Review fix: share and toast-fade tests await a Task / removal seam instead of 5 Task.Delay polls (P08/P73); Share hidden without InstalledPath (WPF :459); previews always 160 px wide like DecodePixelWidth; ledger row labels fail-proven vs asserted.
- rgate: only failure is Core GoonClockAndRounds, which already fails on the base (fix is in flake-clocks, P108). The other gate steps were run by hand (see body).
- Review fix 2 (dcd62bbe0): Deeper acceptance is a keyed sticky (fail-proven, rcheck); mod-catalogue row wired. rgate 39697111817 red only on P108 + listed RaceTrack flake; skipped steps green via rcheck 39700816833.

## avalonia-port/wire-shell-enhancements: +974
- Enhancements tab: the four owned PRO analytics expanders (Lifetime Dashboard, Season Rewind, Bestie Records, Brain Drain Report), aggregation shared with WPF through Core `EnhancementAnalytics`.
- Season Rewind deltas use Fluent arrows; row stays stub (purchase + lucky toast in wire-shell-enhancements-b).

## avalonia-port/wire-shell-lab: +146
- Lab Lockdown: level-chip glow stays crimson during the run and returns to the live accent on exit; shell close drops the theme/flash, and queued work can't re-theme a closed shell (3 headless tests, fail-proven).
- shell-lab row stays stub: it now lists the possession readout and rules card (shell-possession) and the missing game hosts (wire-shell-lab-b).

## avalonia-port/wire-dialogs-mod-picker: +196
- Mod picker catalogue cards keep their shipped art; Download/close/retry/upgrader paths covered by real-click tests (row stays stub: WPF has no caller).

## avalonia-port/wire-views-tab-awareness: +227
- Awareness app-scope list commits on Enter/blur through the shared Core AwarenessAppScopeRules and persists; WPF delegates to the same rules.
- Editor test is deterministic and fail-proven; ledger numbers corrected with a stored green log.

## avalonia-port/wire-views-tab-available-subjects: +500
- Lobby tab ports the current Open/Playing/Friends frame with Core LobbyPageRules, narrow switcher and gated Remote host; pills get an explicit template; redraws on every tab open.
- Still stub: live discovery, Chess/Goon host and Join flows (follow-up wire-views-tab-available-subjects-b).

## avalonia-port/wire-shell-tab-fx-takeover-lab-status: +261
- Status dots breathe on effect-free sibling BoxShadow layers with motion/perf/visibility gates; Haptics glow on arrival fail-proven.
- Review fixes: redundant seed dropped, ratchet trimmed, padlock/Env gaps moved to owning rows; row stays stub; last rgate 39701865337 red only on listed P108 flake.

## avalonia-port/wire-win-emi-codex: +132
- Core `EmiCodexRules` (content probes + offer brakes with WPF's per-brake reason), WPF delegates; Avalonia caller deferred to wire-win-emi-codex-b.
- Foreign test bootstrap change reverted; remote gate run 39688213091 green.

## avalonia-port/wire-views-tab-graded-intake: +434
- Graded intake on Avalonia serves downloaded pack audio via the loopback origin and sends the bubble sprite plus the enabled subliminal/whisper payload (Core `IntakeSubliminals`, shared with WPF).
- Row `views-tab-graded-intake` stays stub; EmiDesk hold, crash relaunch and punch card remain.

## avalonia-port/wire-shell-lab-tab: +250
- Shared webcam state chips (Devices, Play, Blink Trainer, Deeper) driven by Core WebcamStatus; Play Eyes cards dim while capture is off.
- Review fixes: consent refusal stays Stopped like WPF; live-capture Play cards tested; Skia headless setup and ledger corrected.

## avalonia-port/wire-theme-controls: +312
- Shared rounded Deeper themes app-wide; Action/Secondary/Outline get WPF 80ms press / 150ms BackEase(0.3) release, sampled on a stepped clock in tests.
- theme-controls stays stub; HudPlank and ToggleStyle timings left for wire-theme-controls-b.

## avalonia-port/wire-views-profile-privacy-panel: +512
- Profile privacy dialog: the six audience switches save and push per-field, account-owned consent via Core SyncPush (scoped, unlike WPF's all-six push; decision 2026-10-13, oracle-deep pending). The Discord button runs the real Account settings link flow.
- Row stays stub: link-notice CTA, post-link achievement-sharing offer, own-card DM button and the privacy-sync scope go to lane -b.

## avalonia-port/wire-shell-profile-bubble: +459
- Header bubble shows avatar presets, the WPF tier-badge hover enlargement and 8 s one-shot wobble, a level-up burst on the shared EventBurstLayer, and the copy-link prompt when the browser won't open (skipped in a sandbox).
- Review fixes: no idle frame clock for the wobble, sandbox-refused link never writes the clipboard, menu-restack tested, ledger row no longer claims Core FxBurstAnchorRules. Fail-proven; rgate green apart from two listed flakes.

## avalonia-port/wire-views-companion-ai-permissions-grid: +434
- The Engine Room live-actions feed is fed by the Core command sink (shared AiLiveActionFeed rules). The V2 permissions grid is stacked; Forget/memory-off clear history; the lock CTA opens Settings > Account.
- The lock band repaints on tier change and on re-show, as WPF does. rgate 39698040401: Linux green; the one Windows flake is in flakes.md (P94).

## avalonia-port/wire-views-tab-companion: +205
- Workshop roster card clicks switch the companion through Core `CompanionCore.SwitchCompanion`, refresh the hero and ring, and put the companion's avatar set on the tube (WPF `SwitchToCompanionAvatar`, saved globally and per mod).
- `CompanionRosterSwitchTests` fail-proven for both the Core switch and the tube set. Rows stay stub; remaining work is in wire-views-tab-companion-b.

## avalonia-port/wire-shell-app-startup: +101
- Startup seeds brain mod-switch + BambiSprite prompt-invalidation hooks (WPF App.xaml.cs:317/327).
- Two StartModsTests facts fail-proven via real ActivateMod; GATE-OK; row stays stub.

## avalonia-port/wire-ctrl-attention-check: +156
- Dormant attention ring: pulse on shared BeatLoop, BoxShadow glows, headless tests; row stays stub (WPF never shows it).
- Review fixes: ring glow dark at progress 0 and pulses with the ring (fail-proven); glow assertions fail-proven.

## avalonia-port/wire-shell-nav-rail: +310
- Nav rail hue bevels + mod-aware rail captions (Core NavRailPaintRules/ModAwareLabelRules); row stays stub, follow-up wire-shell-nav-rail-b.
- Review fixes: closed-rail relabel guard proven by test, Back/Web App mod caption test, test-builder churn dropped. Ledger: per-fact fail-proof status, one follow-up lane.

## avalonia-port/wire-win-feature-intro: +75
- Feature-intro cards wait in the Inbox while the first-run wizard, welcome-back offer or an update dialog is up; the card now looks like WPF's and scrolls on small screens.
- Manually opened What's New or a pending manual update check no longer holds cards (as in WPF).

## avalonia-port/wire-views-companion-hero-card: +105
- The Companion hero Tutorial chip starts the Companion tour; a headless click test (fail-proven in rcheck 39688790545) covers it, including panic abandonment.
- Review fixes: overlay accessor instead of reflection, v2-pages precondition and fail-proof run cited in the parity row.

## avalonia-port/wire-views-companion-keyword-triggers: +159
- Keyword drawer reveal: 24px header alignment and a two-cycle pink pulse on the shared beat (Motion Off suppresses it, detach clears it); tests click the real Awareness link, scroll + pulse fail-proven.
- Row stays stub and names follow-up lane -b (OCR interval push, highlight overlay, service lifecycle); shell row left to its lane.

## avalonia-port/wire-win-emi-options: +251
- Emi options panel folds on Windows via non-consuming WH_MOUSE_LL/WH_KEYBOARD_LL hooks (WPF parity); X11 keeps the 30 ms poll; 3 fail-proven tests.
- Duplicate win-emi-options ledger row collapsed (stays stub).

## avalonia-port/wire-feat-subliminal: +207
- Advanced Steals Focus now reaches subliminal cards: passive show, then a focus request that keeps clicks passing through.
- Review fixes: the test drives the real setting, and a refused X11 focus request is retried once after the map, then warned (both fail-proven).

## avalonia-port/wire-shell-programs-fx: +157
- Program reward-rail halo breath and once-per-day node ignite on the shared beat (WPF ProgramsTab.cs:779-867), motion/visibility gated.
- Review fixes: WPF ignite alpha, single breath gate, no test PNG; fail-proof log cited, views-tab-programs cross-ref, stock test harness.

## avalonia-port/wire-views-tab-she-listening: +73
- She's Listening: master Start/Stop uses Fluent Play/Stop; the Video cheat-sheet row shows only the runnable pause/resume phrase.
- Side art is left to lane adaptive-side-art; voice seams, sherpa KWS and calibration go to lane -b.

## avalonia-port/wire-views-settings-performance-b: +215
- Lockdown tab pulse (shared Core LockdownPulse timeline) starts on Lockdown entry, stops on leave / motion below Full, as WPF; start, Off-stop and close-stop each fail-proven.
- Ledger row views-settings-performance stays stub; unified overlay compositor left to lane -c.

## avalonia-port/wire-shell-multi-monitor: +141
- Bubble Count result windows: map before monitor enumeration, place on the reported primary or fallback, read-only mirrors on other screens (MultiMonitorResultTests).
- Review fixes: primary positioned once, Skia test setup; ledger states X11 multi-head not run live; row stays stub.

## avalonia-port/wire-feat-fyp-host: +156
- FYP feed right-click file menu (Open file / Show in folder) on Linux; shared Core FypFileRules for both heads; unsafe/remote/missing ids refused.
- Review fixes: shared test bootstrap, duplicate WPF test removed, menu-open asserted before panic; rgate 39697255997 green except known RaceTrack flake.

## avalonia-port/wire-feat-video: +414
- Mandatory-video toy-button attention checks shared via Core binding; Yes/No reset prompt; shared no-videos guidance with pack counts.
- Review rounds: awaited+logged Test-click run, pack-count test, toy reward haptic asserted, panic leg via scheduler Stop; row stays stub (gaze dwell, interaction queue).

## avalonia-port/wire-shell-spiral-room: +212
- Spiral Room stays in fog for saved, unanswered Descent migration offers. The rule lives in Core `SpiralRoom`, shared with WPF; the Avalonia tab watches the persisted gates.
- The row stays stub; live offer, ceremony and block wiring are owned by -b.

## avalonia-port/wire-shell-she-listening: +1
- Voiced wake/command confirmations read the active bark manifest; mic waits for her clip (or any bark that cut it) plus a cancellable 300 ms echo tail.
- Stop/revoke/lapse/panic end a command prompt in flight without reopening the mic.

## avalonia-port/wire-win-webcam-calibration: +695
- Calibration bubble accuracy test ported to Avalonia; its rules moved to Core (`BubbleAccuracyTest`, `GazeTrimData.Compose`) and WPF now calls them.
- Row stays stub: mouth detector and cursor trail queued as lane `wire-win-webcam-calibration-b`.

## avalonia-port/wire-theme-fonts: +104
- Shared WPF `Font.Mono` restored in App.axaml; UpdateFailedDialog, SpiralTab and the Companion wire chip consume it via DynamicResource; mono chain maps to bundled Noto Sans Mono off Windows.
- theme-fonts stays stub: FontGuard + synthetic-typeface risk -> wire-theme-fonts-b, which also lists the hardcoded mono sites.

## avalonia-port/wire-shell-presets: +197
- WPF dashboard gesture caption + shared Core DashboardToggleHintRule.NoteUse; Spiral wall toggle refreshes the live overlay.
- Review fixes: parity row names the remaining live FX members and cites the fail-proof; spiral test uses the right-click combo path. rgate green but the P115 RaceTrack flake.

## avalonia-port/wire-shell-exclusives: +257
- Premium vault accents (edges, badges, pills, title shadows) follow the active mod, as on Windows.
- New tests drive a real mod switch and check the mounted vault repaints.

## avalonia-port/wire-shell-hero-fx: +372
- XP header hero fx (odometer, meniscus slide/breath, level-chip pop) on shared BeatLoop; row stays stub, rest in wire-shell-hero-fx-b.
- Review fixes: test restores offline settings; Opened no longer stacks the motion-gate hook (fail-proven).

## avalonia-port/wire-svc-media-session-watcher: +457
- Core MediaSessionWatcher owns the 3s SMTC poll lifecycle; WPF delegates (row stays stub, follow-up -b).
- Review fixes: Windows restart test uses Core fakes, SMTC debug logs restored, dangling cloud link dropped.

## avalonia-port/wire-views-deeper-new-enhancement-dialog: +240
- The New enhancement dialog's HypnoTube/TikTok sample now comes from a shared Core selector (`HypnotubeDefaultLinks.SelectTikTokTutorialVideo`), which both heads call. On Avalonia the table is still the static one, so users see no change yet.
- Headless tests click the real button from the shell's Deeper tab and cover native Browse/Create (fail-proven). The row stays stub: the three interactive tutorials are deferred to lane `-b`.

## Cloud merge-fix: wire-shell-favorites-rail + wire-views-deeper-new-enhancement-dialog
- Retain the shipped panic-test bootstrap (including Inter/AppFonts) and every panic surface, assertion and ordering constraint. Keep the favorites Click tips/inversion/pin cue alongside the incoming dashboard gesture caption and shared new-enhancement sample selection.
- Consolidate the parity ledger to one row per ID, preserving both histories' evidence and advanced statuses without verified claims; retain both progress histories.

## avalonia-port/wire-views-tab-friends (cloud merge-fix)
- Retains this lane's one-time presence question: Yes shares, No stays hidden, both persist the Core FriendsPresenceAsk marker; WPF delegates to the same rule.
- Merges wire-views-deeper-new-enhancement-dialog at e9eb9cd94c81796f4ce89d39f4f9d563cdde0946, including friends feed/send trails, unread badge and open tables without removing their behavior or assertions.
- Consolidates parity IDs at the most advanced unverified status and retains distinct evidence/context. The incoming progress entries and panic-surface ordering/assertions are retained.

## avalonia-port/panic-tests-derive: +93
- Panic ordering rules now sit on the PanicSurfaces registry entries (`After`, with WPF cites); the test reads them, so its hand-kept order list is gone.
- SafetyCritical is kept as one sorted id per line so new panic surfaces merge without conflicts; fail-proven for line deletion and pair swap.

## avalonia-port/video-core-d: +505
- Moves VideoService selection, cooldown, strict-lock and wedge-ladder rules to CCP.Core; WPF routed to Core (no second copy).
- Review fixes: pre-move grace-pause log texts restored; stale EvaluateGraceRequest comments updated.

## avalonia-port/chaos-payout-fallin-b: +751
- Live chaos bubbles: fuse, hold-to-defuse snap (hold and Snap Chain on wall time, a lost capture breaks the hold), detonation (shield/collar/Snap Chain) in Core; a detonation costs the doubled clean-loop tip and silk_touch.
- Run-end lessons moved to Core ChaosRunLessons (WPF delegates); barks and SNAP/NO FOCUS labels deferred to -c.

## avalonia-port/goongame-core-f: +485
- Goon media/share-card/avatar/transfer stack moved to CCP.Core behind seams; WPF seeds them.
- Review ACCEPT; fix commit documents GoonCacheBridge.Post dispatch difference (P3). rgate GATE-OK.

## avalonia-port/backroom-counter-buy: +466
- Standalone v2 prize buy: V2PurchaseService/BackRoomApi moved to Core (WPF seeds two hooks); Avalonia Get it row now reads the counter, confirms, buys, toasts and hides.
- feat-backroom-counter-buy missing -> wired; ctrl-v2-get-it-row stays stub (per-run ownership, no Bubble row).

## avalonia-port/wire-views-tab-studio: +301
The Studio rack Haptics row right-click now goes through the page's own box (with the premium/free-day gate), and the haptics dot follows that box live. The Flash and Bubble Pop rows show the v2 pill from prize ownership, as WPF does (StudioTabView.xaml.cs:532/:717/:864/:965/:1505, StudioTabView.Haptics.cs). Tests: StudioRackWireTests (fail-proven). Row views-tab-studio: stub -> wired.

## avalonia-port/deeper-core-e: +742
- EnhancementEngine, IPlaybackTimeSource and the Logging/Recording dispatchers move to CCP.Core behind small seams (IEnhancementWebcam, IEnhancementRunCleanup, PlaybackRect/Point); SpeakPromptPlan holds the speak effect's rules; WPF keeps RealActionDispatcher, SpeakPromptSession UI and a WebcamEnhancementEvents adapter.
- Fix over trunk: RecordingActionDispatcher forwards Stop cleanup (Stop test fail-proven; the other new tests are asserted, not fail-proven).
- Review fix: corrected the "WPF installs no CoreDispatch.PostProvider" claim (App.xaml.cs:266) in the adapter, gaze test and PR body. rgate GATE-OK (Windows run 38569267565 green; two earlier runs hit unrelated flakes / disk-full).

## avalonia-port/wire-win-emi-ring: +205
- Tab/rack opens outside the ring now teach EMI card ranking (Core EmiDoors maps shared with WPF).
- Review fixes: one ledger row, rack hook fail-proven alone.
## avalonia-port/cloud-merge-wire-shell-animations
- Merged wire-views-settings-performance-b at 6095620b4e54a91477fdff126b61425d83f893d6 into the held shell-animations lane at f917157399f89034d14d10aea8e39922b579d81a.
- Kept shell animation lifecycles and Lockdown restart-on-reselect, with the shared Core pulse timeline, sibling glow opacity restoration and live performance/motion stop gates. All parent test cases and assertions retained.
- Consolidated the parity ledger to one row per id, preserving source evidence and capping surface statuses at wired; retained all panic surfaces in incoming-tip order.

## avalonia-port/wire-shell-animations: +127
- Lockdown plate pulse follows ShowTab (start on arrival, restart on re-select, stop/restore on departure and close); glow moved to a sibling BoxShadow Border.
- Season shimmer / skill-tree clocks confirmed already owned by their views; ledger row corrected.

## avalonia-port/wire-shell-assets: +329
- Library browses installed Content Packs (tree, decrypted thumbnails with LRU cache, checks, previews); tree shaping moved to Core `AssetPackTree`.
- Content Packs root shows a Box glyph; full remote gate green.

## avalonia-port/wire-win-webcam-calibration-b: +493
- Calibration mouth prompts fire from real capture (Core MouthOpenDetector, WPF order) and verify/bubble cursors draw the shared Core GazeCursorTrail comet.
- Review fixes: frame-run-tagged mouth events, cached trail paints, all 10 new tests fail-proven remotely.

## avalonia-port/wire-views-companion-workshop-accordion: webcam lane merge-fix

- Merge webcam calibration lane `be09ff13f0ea97c92e72c6dfcad763e387ad1aec` into pinned workshop lane `5252d4f0598e96f01d98bc635dd997e29a3ab726`.
- Retain companion roster identity refresh, phrase-preset select/save/delete and tab-return refresh, conversation resume/park, and prompt-editor entry points.
- Keep all 37 panic surfaces in parent order, their ordering contracts, and both parents’ tests. Consolidate parity IDs with the more advanced status and distinct evidence; retain both progress histories without claiming live verification.

## avalonia-port/main-20261024: +5620
- Main sync #8: WPF main f81ea01fb merged (Oct 22 night bug hunt, 32 commits); delta ledger docs/avalonia-main-sync-20261024.md (2 ported-by-merge, 30 needs-port in 15 sync8-* lanes, 0 n/a). Red by design; gated in -compat.

## avalonia-port/main-20261024-compat: +62
- Compat for main sync #8: App.* in Core as Serilog/CoreSettings, LockdownExitRule/PanicAftermath/VoicePanicReach/TickStep into Core, main's WPF scan tests via SourceRoots and retargeted to where the stack split the code. rgate GATE-OK (run 40109201391).

## avalonia-port/ledger-dedupe-2: +7
- Parity ledger back to one row per id (1815 -> ~540 lines), meaning-preserving; pushq resolver fixed (P132).

## avalonia-port/barks-wpf: +138
- WPF BarkService delegates its bark decisions (conditions, gate, rotation, commit) to Core BarkEngine; about 720 duplicated WPF lines deleted, and the barks-wpf ponytail note is gone.
- Remote gate GATE-OK (WPF windows job green); Core Bark tests 15/15. Review fixes: the WPF log fields are back, throwing probes now fail closed, and the seam wiring is pinned.

## avalonia-port/companion-v2-pages-b: +678
- Companion v2: room collapsed; Personality/Permissions/Links pages adopt the live zones; Awareness tab hosts the privacy card.
- Cell->page map, Takeover<->Permissions links; Personality Advanced row opens the prompt editor; rest -> companion-v2-pages-c.

## avalonia-port/wire-win-settings-palette: +62
- Palette Games row scrolls to the Games zone; panic closes the palette without eating Escape grace (both fail-proven).
- Review fix after reboot: headless test setup restored, minimal panic-list diff, parity evidence path. rgate green; panic test now proves Escape grace survives panic.

## avalonia-port/wire-feat-pbp-host: +215
- Piece by Piece whisper clips: Core `PbpWhisperClips` shared with WPF, authenticated sub-audio/words routes, settings frame sends whisper URLs (row stays stub).
- Review fixes: tests moved not copied, unchecked brain-drain clips no longer offered (fallback works), settings test GETs the offered clip, parity row cites in-tree evidence.

## avalonia-port/wire-views-companion-chat-threshold: +349
- Live assistant watch chips via Core CompanionLinkIndex + CompanionLinkLauncher (remote guard, embedded browser, HTTPS fallback); row stays stub (lane -b: media takeover/autoplay).
- Review fixes: native wheel chaining now proven by a headless test (fail-proven); ask-service links via the launcher. rgate 39697647617: Linux green, Windows known flakes only.
## avalonia-port/wire-views-tab-play: cloud merge-fix
- Retained the free Breakout Demo and Piece by Piece Play-wall launchers, repeat-click focus and games panic coverage alongside the incoming Mantra window factory and failure dialog.
- Merged the companion prompt editor integration tip; parity has one row per id with combined evidence, and no row is assigned verified status by this merge.

## avalonia-port/wire-views-tab-play: +94
- Play wall free games (Breakout Demo, Piece by Piece) launch and focus their hosts; panic closes them.
- Hand cursor on both buttons, asserted in PlayWallTests.

## avalonia-port/wire-ctrl-spiral-first-light-visual: +492
- First-light fog/arm canvas ported onto the Spiral tab fog host, driven by the shared BeatLoop and Core timeline; row stays stub.
- Review fixes: fog clock stepped in test, no PNG writes, 30 fps note; parity rows refreshed (render-all limit stated).

## avalonia-port/wire-ctrl-attention-check-b: +194
- Dormant attention ring glow rasterized from the visible arc at attach density (redrawn on scale change, skipped when progress is unchanged), shared pulse transform.
- Review fixes done; rgate 39694832799 green except known P108 GoonClock core-linux failure.

## avalonia-port/wire-feat-pbp-host-b: +163
- PBP Escape is kept like WPF (pause, second Escape panics). WPF and Avalonia share the Core PbpHostRules, and WPF now builds media URLs only for the kept picks.
- The Racing Thoughts kept-Escape gap is recorded in the game-race row. rgate 39718749188 is all green.
## avalonia-port/cloud-merge-wire-shell-chrome-fx
- Merged wire-win-webcam-calibration-b at be09ff13f0ea97c92e72c6dfcad763e387ad1aec into the held shell-chrome-fx lane at 9aae8d95a4c9fd751254d60ba73c82a5aaa8751e.
- Banner rotation, announcements and Home marquee return share the Core gate/throttle and deterministic BeatLoop clock, lifecycle parking and incoming hide callback, live host width, and incoming running-state/timing test contract. Both parents' tests and assertions are retained.
- Parity ledger consolidated to one row per ID, retaining distinct evidence and the most advanced status without claiming live verification; panic surfaces retain incoming-tip order and all progress entries survive.

## avalonia-port/wire-shell-chrome-fx: +274
- Restored WPF's banner message sheen on the shared BeatLoop. The throttle and motion rule moved to Core `ChromeBannerSheen`. The row stays `stub`, and the rest of the work is handed to wire-shell-chrome-fx-b.
- Review fixes: added a real-layout banner test (fail-proven) and restored the header plan. Remote gate run 39698116986 passed on all jobs.

## avalonia-port/wire-views-tab-chaster: +863
- Chaster tab: menu boards, inline price editing and the two-limit drawer, on Core `TabPriceEdit`/`LimitChange`/`TabLimits`; the menu is visible only while linked, as in WPF.
- Row `views-tab-chaster` stays stub; page Fx, art and trailer remain in wire-views-tab-chaster-b.

## avalonia-port/wire-shell-notifications-toasts: +340
- Avalonia NotificationService gains WPF's ShowSticky/Dismiss(key) (pre-host replay, dedup, persisted × dismissal via Core NotificationDismissals, secondary action row); catalogue/deeper acceptance notices are sticky; Pink Rush reuses passive corner placement.
- Tests are deterministic (no wall-clock waits); row stays stub, remaining lifecycle callers named under wire-shell-notifications-toasts-b.

## avalonia-port/chaos-payout-fallin-c: +341
- Chaos fuse ring flash/brink phases + danger breathe; SNAP/NO FOCUS/effect labels; defuse/detonation cues and HUD flashes; WPF pulse/shake calls behind unseeded hooks.
- Barks deferred to chaos-payout-barks (needs companion-barks BarkEngine); remap/heavy gate/pulse window/shake to chaos-payout-fallin-d.

## avalonia-port/wire-shell-clickthrough-overlays: +318
- Tutorial spotlight/transparent-wait steps set a native input region (X11 XFixes, Win32 GDI) so background clicks pass through; geometry in Core `SpotlightInputRegion`.
- Review fixes x2: real-path tests, layout-size fallback, ledger rows (LayeredCompositorHost, spotlight hole); gate run 39691313720 green except P108 + known RaceTrack flake.

## avalonia-port/wire-shell-quest-stamps: +902
- Live header quest stamps (3 daily + weekly): art, progress, completion checks, hover cards, reroll/mod/language refresh, click to Quests; shared QuestStampRules in Core.
- Review round 2: fail-proof re-run on current code (rcheck 39701823626); P105 quest-art dedupe rule in the ledger; rgate 39702277001 bar P108/P94.

## avalonia-port/wire-shell-awareness-b: +217
- Awareness status glow breathes behind the steady dot through the shell's one pulse registry, with WPF's full motion/glow/focus gate.
- Per-tab duplicate pulse removed; status-dot gate gained AllowAmbientLoops/AllowGlow/MaxGlowBlurRadius for all tabs.

## avalonia-port/wire-shell-window-chrome: +330
- Shell window chrome lifecycle wired: real-exit vs tray, WM maximize glyph, DPI spawn quiescence, attached-avatar minimize custody (Core WindowMinimizeRecovery, both heads).
- Review fixes: tray-hide closes quiz windows like WPF; DisplayChange quiet window reset between tests.

## avalonia-port/wire-views-companion-room: +231
- Her Room Z2 thinking dots: shared BeatLoop (parks when hidden, minimised or detached) with Core CompanionThinkingPulse (WPF stagger); WPF untouched.
- Minimise park fail-proven (rcheck 39711293690 red, 39711782427 green); rgate 39712264692 green except known RaceTrack flake.

## avalonia-port/wire-views-tab-exclusives: +322
- Premium vault accent chrome (untiered rims, spotlight rim/glow/badge/pill, every shelf badge/pill/title glow) follows the active mod palette through Core VaultHue; row stays stub (follow-up wire-views-tab-exclusives-b).
- Accent test drives the real CoreMods.ModChanged path (fail-proven rcheck 39721281655); rgate 39721698896 green except the known RaceTrack flake.

## avalonia-port/wire-feat-friends-blocked-presence: +296
- Per-account block fallback and the once-only presence ask, shared with WPF through Core `FriendsBlockList`. The local list is retired only by the landing tick, as on WPF.
- Fix round: the Core test compiles again, and the WPF `FriendsDrawerKnockTests` regression is fixed (reads stay pure).
## avalonia-port/cloud-merge-wire-theme-inputs: Deeper dialog merge

- Combined the TextBox input chrome/focus/40% selection theme with the shared Deeper themes and button press/release timing; both namespaces and both App.axaml additions remain.
- Retained both progress histories and all tests/assertions. Consolidated the parity ledger to one row per ID, retaining the most advanced status capped at wired and preserving distinct older evidence as historical context. No new verification claim.

## avalonia-port/wire-theme-inputs: +291
- Implicit WPF TextBox chrome + FocusGlowTextBox alias: r8, 8x5 padding, 60% hover border, 45% pink focus glow (200ms), 40% pink selection, 50% disabled.
- Borderless .transparent boxes keep no halo; 4 deterministic headless tests; row stays stub (ComboBox/CheckBox/menus -> wire-theme-inputs-b).

## avalonia-port/wire-views-companion-workshop-accordion: +274
- Workshop Triggers cell: phrase preset select/save/delete restored on shared Core `PhrasePreset` (WPF prompt, confirm, log lines); combo re-reads on companion-tab return. 4 headless tests, each fail-proven.
- Row stays stub; camera/browser/roster/community/phrase-pool work handed to lane `-b`.

## avalonia-port/wire-win-help-video: +143
- HelpVideoWindow plays a topic's tutorial clip like WPF StartClip: it starts on Opened (WPF Loaded), plays muted (`:no-audio`), loops, and goes through the head's LibVLC VlcFrameSink. It fails soft and frees the clip on close. Row win-help-video: stub -> wired, now one row.
- Test: a real 2 s clip with an audio track. Fail-proven by removing the loop, removing `:no-audio`, and starting the clip in the constructor. The wait is event-driven, with no sleep. The panic allowlist gives the WPF-parity reason.

## avalonia-port/wire-dialogs-asset-submit: +237
- dialogs-asset-submit: mod Share in Manage Mods opens AssetSubmitDialog(requireDownloadUrl) -> MEGA link -> ccp-mod/v1 envelope via Core CatalogueClient, record + toast (WPF MainWindow.ModCatalogue.cs:24-178, ModManagerDialog.xaml.cs:459/692).
- Test: AssetSubmitWireTests.ModManagerShareAsksForTheMegaLinkThenSubmitsTheModEnvelope (fake handler, awaits LastShare, restores App.Mods/CoreMods); duplicate stale ledger row removed. Ledger: shell-mod-catalogue -> wired (same WPF file).
- Not run live against the real catalogue/MEGA; --render-all cannot show the Share button.

## avalonia-port/wire-views-tab-blink-trainer: +68
- BlinkTrainer side art rides the shared AdaptiveSideArt (adaptive-side-art lane); duplicate control/Core rule dropped; real-tab resize test fail-proven.
- rgate: all green except one unrelated Windows-only ProgramTodayCard flake (P94).

## avalonia-port/wire-win-emi-book: +600
- EMI book: welcome replay, card tours via CoreTutorial, WPF rail art (shared Core painters), bundled pixel fonts.
- Fix round 2: refused replay restores the book; dead ledger ref removed.

## avalonia-port/wire-win-mod-creator: +602
- Mod Creator Advanced panel (tube layout, bubble scale, skill labels, tooltip overrides) on Avalonia, with rules shared from Core.
- Row stays stub; art/barks/mantras/audio/portraits/emotes panels left for wire-win-mod-creator-b.

## avalonia-port/wire-shell-blink-trainer: +205
- Deeper hub webcam setup buttons relay to the Blink Trainer consent/calibration/Quick Recal/tracker workflows; the card shows live shared state.
- Review fixes: no real-time test wait, single refresh path, complete follow-up list, tracker-label test, honest parity rows (fail-proven; gate 39702277881 green).

## avalonia-port/wire-views-companion-room-preview: +248
- Companion room preview strip swaps chat, diary and attention mocks for all six WPF states; zones without mocks are disabled in the harness so it cannot write real settings.
- Row stays stub; follow-up wire-views-companion-room-preview-b seats hero/personality/awareness/engine/workshop mocks.

## avalonia-port/wire-views-tab-app-settings: +170
- Settings re-open refreshes General (startup registration/video), Devices (mic/camera re-enumeration), Performance and Notifications repaint; AppSettingsRefreshTests (General startup-video repaint fail-proven, other hooks asserted). Row stays stub.
- Remaining lane -b: embed existing ExclusivesTabView in an Account PlansMode and wire vault-motion follow/park.

## avalonia-port/wire-views-tab-spiral: +361
- Spiral room fog-entry/splash cues wired once per visit; cue ladders shared in Core `DescentRoomAudio`.
- rgate green except one unrelated Windows Tube flake (P94); fail-proof rcheck shows the audio tests catch a missing cue.

## avalonia-port/wire-feat-spiral: +420
- The spiral randomizer and the personal-folder refusal now live in Core and are shared by WPF and Avalonia. Each run keeps one pick, never repeats the previous image, and refuses personal folders with a logged warning. The Avalonia head's duplicate check is removed.
- Review fix: a random spiral that can't be decoded is re-picked once, so the run isn't left blank (fail-proven). Gate: rgate 39697469596 failed only on two known flakes, and both passed in rcheck 39701813936.

## avalonia-port/wire-shell-tab-fx-presets-quests-achievements: +481
- Presets/quests focal FX (selected sheen, hover/press, arrival stagger, weekly tween) ported; row stays stub, follow-up lane -b.
- Review fixes: visible-only Loaded stagger, scoped no-replay test (was failing on CI), stale doc reference dropped.

## avalonia-port/wire-shell-webcam: +437
- Calibration Verify gains the WPF four-bubble accuracy check (Core CalibrationBubbleProbe) and GazeLock/LongStare feed in WPF order; headless tests.
- Row stays stub; near-miss auto-trim, mouth MAR and long-stare consumers go to wire-shell-webcam-b.

## avalonia-port/wire-views-tab-leash: +596
- Leash page: own card (holder/day, Cut, tasks/pardons, strictness, DND, video cap, Watch), repaint on show and stop listening when hidden; Core PendingGateCount shared with WPF. Row stays stub; follow-up -b.
- Review fixes: named fail-proofs (x2), renders moved to durable evidence, feat-leash row refreshed.

## avalonia-port/wire-views-tab-settings: +351
- Home Click tips popup + saved left/right gesture inversion on feature cards, DashboardToggleHintRule in Core.
- Review fixes: real-pointer test (fail-proven), ledger rows corrected; P108 core flake only.

## avalonia-port/wire-shell-audio-playback-devices: +208
- WPF's 2.5 s mid-duck rescan ported to the Linux pactl ducker (shared Core rule), timer retired on unduck/panic/shutdown.
- GATE-OK (run 39702243881); row stays stub.

## avalonia-port/wire-win-media-history: +475
- Media Log wired: reveal selects the file (Explorer /select, open -R, FileManager1.ShowItems), Load preview behind consent (redirects by hand, WPF headers), broken-clip plate only for empty/image/audio files.
- Review fixes 7eaf2bfe9; rgate run 39690948829 green except a known Windows flake (P94).

## avalonia-port/wire-ctrl-emi-dock: +411
- EMI rail chip ported (64px layout, EmiFace renderer, Core EmiFaceRules; BoxShadow glow pulse replaces the animated Effect); row stays stub, knock/first-contact flow left to lane -b.
- Review fixes: knock test asserts pulse timelines instead of sleeps; face test locks the fill and lift numbers; rgate green except the listed RaceTrack race.

## avalonia-port/wire-shell-dashboard-fx: +193
- Browser frame 2.4 s sweep / 14 s cycle and loading 0.35-1 status pulse on BeatLoop with motion/focus/hidden-tab gates; row stays stub (mystery tile + logo sheen -> wire-shell-dashboard-fx-b).
- Review fixes: NotEmpty guard (fail-proven), honest fail-proven wording, failed-status gap recorded, loading match via IconText.BareLoc (icon-loc ratchet).; unused Localization using dropped.

## avalonia-port/wire-shell-preset-io: +141
- File dropped on the launcher opens the panel and imports through its existing preset path.
- Headless test covers drag-over effect, drop and import; fail-proven 3 ways with saved logs.

## avalonia-port/wire-shell-quests: +148
- Off-tab quest completion bursts 85 sparks from the centre of the owning Quests nav row. Fallbacks (the track, the nav row) fire from their centre, as in WPF FireBurstAtFirstVisible; only the filled bar keeps RightEdge.
- The shell-quests row stays stub and is reworded to "Added".

## avalonia-port/wire-ctrl-spiral-rail-host: +271
- Native spiral rail host + shared Core rail rules; shell mount kept dormant (WPF retired it), row stays stub.
- Review fixes gated: rgate 39722260221 GATE-OK.

## avalonia-port/wire-theme-main-window: +1
- Review nits: tab themes reindented, help cursor/rest brushes asserted (fail-proven), ledger cites gate not unbacked counts.
- rgate 39691420992: only P108 GoonClock red.

## avalonia-port/wire-feat-companion-ai-cloud-chat: +282
- Avalonia startup seeds one Core moderation session before the AI provider; refusal records carry no chat text.
- Selecting Local restores the WPF Ollama setup offer (Yes/Cancel; Yes opens the wizard, Ready stays silent); tests await the in-flight offer.

## avalonia-port/wire-win-emi-desk: +268
- EMI Desk pat faces (poke ladder moved to Core): pat, wink, glee and chime, annoyed, rage. A live face eats clicks the way WPF does.
- The full chain player, particles and body are deferred to wire-win-emi-desk-b. The row stays stub.

## avalonia-port/stakes-host: +359
- Piece by Piece board routes PvP stake frames to Core StakeBridge and posts replies back; StakeHead seeds the Chaster gate, loss row and wallet adoption (WPF StakeAppHooks).
- feat-stakes stays stub: no Goon host on Avalonia; live /v2/stakes steps queued in live-queue/prizes.md.

## avalonia-port/wire-shell-awareness: +180
- Awareness app-scope list commits on Enter/blur through Core AwarenessAppScopeRules (shared with WPF), logged and guarded.
- Review fixes: parser name aligned with the views-tab lane; ledger citation fixed.

## avalonia-port/wire-shell-mod-catalogue: +365
- Avalonia Manage Mods: Share now submits `ccp-mod/v1` metadata through a Core builder that WPF also uses.
- The second review round added a fail-proven assertion for the auth toast. Gate run 39697561326 is green except a known RaceTrackPlayer flake.

## avalonia-port/wire-views-tab-friends: +161
- Friends page once-only presence question ported (Core FriendsPresenceAsk shared by WPF + Avalonia).
- Review fixes: ledger rows no longer list the ask as missing; feed/trails and tables point at lanes friends-feed / friends-open-tables. Gate run 39699624563 green apart from the known RaceTrack flake.

## avalonia-port/wire-ctrl-card-sheen-adorner: +309
- CardSheenAdorner hosted on the selected preset/session card, the Chaster hero, the Launcher CTA and wandering launcher tiles (WPF radii and timing); the launcher tile glow stays on its DropShadowEffect.
- Assets hover sheen deferred to lane -b; the row stays `stub`.

## avalonia-port/wire-feat-split-card: +659
- feat-split-card: WPF half depth (travel/tilt/spring), dashboard click inversion and cached grey HalfMute via Core DashboardDepthLaw/CardMuteRule; re-gating via the shell's RefreshFx; tilt keeps one transition per transform, grey art scales first (no PNG round trip).
- Gaps logged: no Avalonia DashboardInvertClicks writer; single FeatureCard ignores inversion.

## avalonia-port/wire-shell-programs-tab: +311
- Live Programs session clock/bar/sheen driven by the shell tick; Core ProgramSessionProgress shared with WPF.
- Review fixes: test seam without reflection, parity FX items moved to shell-programs-fx; sheen/hide/panic/percent fail-proven.

## avalonia-port/wire-views-companion-workshop-community-cell: +1
- Community prompt Workshop cell wired on Core CommunityPromptService; rows and Z4 readout stay in sync on Activate/Remove, Z4 Reset/preset pick and tab show, like WPF.
- Gate: rgate 39697210905 green except known flakes (RaceTrack, GoonClock P108); consent gate tested.

## avalonia-port/wire-win-whatmoved-card: +117
- What moved card: parity tests for the Show me landings, read mode, keys and fresh install. Duplicate tests removed.
- Row icons are now Fluent glyphs (IconMap, 📑 -> Tabs), not emoji text.

## avalonia-port/wire-ctrl-section-tab-strip: +251
- Section strip pills: choosing one draws the NavGlow ring (Full/Reduced/Off), and pointer press squishes the pill to 0.97 over 80 ms through Core InteractionPressMotion, which WPF MotionFx shares. Covered by SectionStripChoosePressTests; the row stays stub, with the remaining work in follow-up -b.
- Review fixes: the NavGlow clock is restored per test, siblings share one bare app builder, and the press Transitions are built once so the 80 ms release ease survives a click (fail-proven). Final rgate 39732875251 was green except the lone RaceTrackPlayer flake (P115).

## avalonia-port/wire-shell-tab-navigation: +115
- Shell ShowTab refuses unknown keys before the bark hook; Blink Trainer refreshes on every visit, including a same-page re-visit.
- Review fixes: no double refresh when arriving from another tab; the ledger now names the Presets/Patreon refresh follow-ups. Gates 39681859342/39732888606 were green apart from listed flakes.

## avalonia-port/wire-dialogs-username-picker-b: +202
- UsernamePickerDialog: HttpClient + TimeProvider test seams; 7 headless tests cover debounce, trimmed/escaped query, taken/error responses, stale and post-close replies with no wall-clock waits (fail-proven).
- Ledger row dialogs-username-picker stays stub (WPF has no live caller); host work is lane wire-dialogs-username-picker-c.

## avalonia-port/wire-feat-feature-card: +276
- FeatureCard TeaseTier attaches the shared-clock gold/diamond TierFxBorder; hover lift/rim snap at Off; window focus/minimize and loaded-only motion gates; FeatureCardFxTests (fail-proven).
- Ledger `feat-feature-card` stays stub: DimWhenInactive, DashboardDepth, DashboardInvertClicks, ShowV2Badge left for wire-feat-feature-card-b.

## avalonia-port/wire-shell-launcher: +198
Panel crossfade through launcher doors (WPF 180/260 ms + guard) on the shared FrameClock; hide mid-fade still opens the launcher; repeated press and Lockdown-mid-fade guarded. 5 headless facts (3 fail-proven by rcheck 39749675334); door tests elsewhere step the fade. rgate 39753216263 green except known flake RaceTrackPlayerTests. Row shell-launcher stays stub (cloak/ladder -> wire-shell-launcher-b).

## avalonia-port/wire-shell-leaderboard-fx: +588
- Leaderboard podium entrance + gold glow breath on the shared beat, refresh-only rank washes (cap 12), rank-snapshot engine moved to Core and shared by both heads, you-bar rank delta wired; glow blur tier-capped as WPF.
- Tests: LeaderboardFxTests (2, fail-proven), LeaderboardRankSnapshotTests (2). Ledger: shell-leaderboard-fx wired; shell-leaderboard / views-tab-leaderboard stale delta notes corrected.
- Limits: gate red only on known flakes (GoonClock P108, RaceTrack); WPF tests compiled not run; no live Keincheck run.

## avalonia-port/wire-shell-login: +397

Post-sign-in Discord achievement-sharing offer (WPF Login.cs:214 / Patreon.cs:836-890): owned Yes/No question, Inbox deferral under games/launcher/hidden panel, working Share Achievements checkbox; review fixes for Yes/No labels, level repaint during the question and no settings-file polling. Row shell-login stays stub (follow-up wire-shell-login-b).

## avalonia-port/wire-shell-marquee: +327
- Codex lane, local review ACCEPT (58). BannerPoolService moved to Core; Avalonia shell pool beat matches WPF timing; row stays stub (rest in wire-shell-marquee-b).
- Review fix: stale NotificationsSettingsSection comment. rgate 39749619553: only P108 GoonClock + RaceTrack flake red; WPF windows job green.

## avalonia-port/wire-shell-profile-cosmetics: +294

Trainer Card cosmetics paint (banner, accent + glow, title, pins) on own/viewed/save/clear paths, per-step guarded like WPF; avatar-slot rule shared from Core. Tests: ProfileCosmeticsPaintTests (viewed path via /user/lookup, glow asserted; fail-proven), ProfileAvatarSlotClaimTests. Ledger shell-profile-cosmetics stays stub (lane -b). rgate GATE-OK.

## avalonia-port/wire-shell-session-feature-lock: +128

Avalonia session feature lock now shows WPF's program-session reason copy (title+day / title / unnamed / ordinary fallback) on the ribbon and the five LockCard dose controls; headless test drives real SessionRunner+ProgramService. Ledger row stays `stub` (BubblePop CmbMotion → follow-up `-b`). Review ACCEPT 62, no fixes.

## avalonia-port/wire-shell-subjects-fx: +296
Roster entrance stagger (WPF StaggerSubjectCards/MotionFx.StaggerIn) on the shared BeatLoop; Taken stays dimmed; Reduced animates, Off never starts. Connect press-squish not ported (WPF OnSubjectConnectPress has no caller). SubjectsFxTests fail-proven (rcheck 39749752722); rgate 39753326525 red only on known flakes.

## avalonia-port/wire-shell-ui-updates: +384
- Signed-in startup leaderboard fetch for the header pills (WPF 500 ms; the test awaits the task, no sleep), mic backstop, XP bonus chips and journal mod gating. Percentile and journal policy are shared in Core.
- Row shell-ui-updates: stub. Missing items are listed in the row. Tests: UiUpdatesTests, AccountSeedTests.SignedInStartupFetchesPillsWithoutOpeningLeaderboard (fail-proven), LeaderboardPercentileTests.

## avalonia-port/wire-views-tab-deeper-b: +550
- Deeper tab: real multi-file import + window drop through Core EnhancementImportBatch (WPF shares it) with 1.8 s row reveal; Tutorial/welcome Tour via TutorialHead (panic closes it); webcam chip follows live tracker state (stale-run publishes dropped). Row views-tab-deeper wired.

## avalonia-port/sync7-scrolller-a: +308
- "via Scrolller" credit: Core ScrolllerCreditRules (WPF forwards), badge on First Show + Media History, "Media from Scrolller" line on Assets + launcher media dialog (rows 166, 193 ported).
- Rows 152/153/170/251/256/268 deferred to sync7-scrolller-a-next (no Scrolller feed on video/flash, no GIF cascade on this head).

## avalonia-port/sync7-scrolller-d: +507
- Core ScrolllerWebRules: the WPF web gate's rules, one copy that WPF forwards to. WebHost sends the honest UA and our Referer to Scrolller, and refuses Scrolller page loads while the licence switch is off. The in-app browser is not gated.
- Rows 194/201/204 stay needs-port (sync7-scrolller-d-native): this engine has no response seam, so embedded sub-resources cannot be refused or cached yet.

## avalonia-port/wire-shell-keyword-triggers: +736
- Keyboard/audio keyword triggers: matching extracted to Core; armed at boot; panic cuts the clip only; tutorial demo stays a gated no-op stub; foreground looked up only when the scope needs it.
- Gate 39707879211: only P115 flakes (RaceTrack decode, GoonClock); row stays stub (follow-up -b).

## avalonia-port/sync7-mandatory-video-a: +361
- Mute + close buttons on every full-screen mandatory video (WPF 5a3e335ff/74dcf0445/51287db40/e3bba7adf); rule moved to Core.
- Headless real-clip test, fail-proven; floating/browser half left to sync7-mandatory-video-g.

## avalonia-port/sync7-leash-a: +320
- Main sync #7 row 6 (a12bb6c53): LeashLevelGuide git-mv to Core; LeashLevelHelp ported to the Avalonia head with Core + headless tests (fail-proven).
- No user path yet: hosts LeashSelfCard/LeashAskCard missing (follow-up feat-leash); row stays needs-port.

## avalonia-port/flake-tube-takeover: +114

Test-only reliability fix for the tube chat, takeover cue, She Listening pill and tube content-gates tests: production callbacks stepped at a controlled clock, awaited menu activation, chunked-safe HttpListener fake with port retry, one window-driven RequestAnimationFrame check kept. rgate GATE-OK; 4 tests x20 green (run 40150070220). No ledger changes.

## avalonia-port/wire-feat-flash-exit-stay: +336
- Flash Shatter dismissal on Avalonia: owned v2 switch, falling/tumbling shard exit, drift parked on breaks, explicit `flash` panic surface.
- Review fixes: shards draw only the image (no black boxes on transparent pictures, fail-proven), exact tray flash-cancel count, no temp PNG from tests, prize-gated RowShatter/BoxFlashV2 theory.

## avalonia-port/wire-shell-settings: +610
- Help tours, Save→preset offer (localized Yes/No/Cancel via MessageDialog.AskYesNoCancelAsync), pack migration through Core PackFolderMover/AssetFolderSafety.
- Review fixes: no sleeps in ShellSettingsWiringTests (fail-proved), redundant preset refreshes removed, parity row limits stated inline.

## avalonia-port/wire-shell-main-window: +217

Header mod selector regains the WPF "Open Mod Manager" footer row, and Home feature-card art follows the active mod via CoreMods.ModChanged. The Mystery/Vault filename fallback (ModTileArt) moves to Core and both heads use it. Ledger row shell-main-window stays stub; follow-up wire-shell-main-window-b.

## avalonia-port/repo-urls: +7 -7 (vs lock-title)
- WIP from the dead worker was backed up (refs/backup/2026-10-22/repo-urls-wip) and committed as `wip: repo-urls (pre-merge)`, then lock-title was merged in with no conflicts. 19 files that main #2447 had already changed now match the tip exactly.
- What remains: AppUpdater.GitHubApi, the 2 Avalonia consent links, PKGBUILD url and the metainfo homepage. The app id and handoff identities are unchanged.
- rcheck: RCHECK-OK. rgate run 39790204255: GATE-OK.
- Review fixes (c3c07cca6, docs/comment only, no gate per P116): ledger row 149 ticked in this lane; AppUpdater comment cites UpdateService.cs:270-271.

## avalonia-port/updater-stall-timeout: +191 -11 (stacked on repo-urls)
- Ported WPF ddc9dd047: installer download stall timeout through Core ReleaseFeed.CopyWithStallTimeoutAsync, with a loopback stalled-stream test that was fail-proven.
- rcheck: RCHECK-OK. rgate 39792261287: green except the known BubbleCount Windows flake (P115). One commit, 47b0a64ec. Ledger row 13 ticked (149 moved to repo-urls).
- Review fixes (a5635b8fe): Core caller-cancel test (fail-proven), DownloadDir seam keeps the stall test out of the real updater folder, WPF second copy recorded as a decision. rgate 39803894556: GATE-OK.

## avalonia-port/wire-svc-content-packs: +49
- Restacked on wire-views-tab-assets (P105 duplicate dropped): one Library test for pack-file name sort, videos-folder check vs the pack video pool, and deactivate hiding the pack.
- Ledger svc-content-packs row points at the Library pack tree; still stub (wire-svc-content-packs-b).

## avalonia-port/wire-svc-discord-rich-presence: +192
- Discord presence follows Flash, Session and Video lifecycles through the Core `CoreDiscord` hook; the attention retry keeps "Watching" as WPF does.
- Brain Drain, Goon Game and Mind Wipe session callers follow in wire-svc-discord-rich-presence-b.

## avalonia-port/wire-shell-takeaway: +567
- Takeaway shelf: Core JustDropOrdersService/TakeawayText (git mv, shared with WPF), three pinned receipts plus the tray, +n toggle, guarded shop door, copy taste link, bare-player replay with panel duck/restore and panic close.
- Review fixes: left-aligned shelf (fail-proven), session-end refresh, reload after a mid-fetch account switch, ServerEnabled restore in tests. rgate GATE-OK.

## avalonia-port/gaze-minigame-rounds: +322
- Gaze minigame plays video rounds (two muted InlineLoopVideo panes, random start in the first half like WPF) and animated GIF rounds (spiral decoder, pane-sized, on the round clock); panic/close frees players and frames; feedback after panic starts no round.
- Tests: GazeMinigameTests (image/video/mixed/GIF, real libvlc player count, panic), fail-proven. Row lab-gaze-minigame stays stub: Flashes/Bubbles/MindWipe/OverlayPulse rewards missing.

## avalonia-port/chaos-unlock-card-b: +599
- Cloud session (Codex), local review ACCEPT + fix commit 971aa945f: WM close releases the lesson pause; expiry timer dismisses directly. rgate run 39879298220 green (tree matches; P100).

## avalonia-port/flake-waveout-teardown: +114
- WaveOutTeardownTests made deterministic: the fake report is raised at the wait boundary through an optional wait delegate on the internal `WaveOutTeardown.Run` test entry (Release unchanged), so thread-pool starvation can no longer turn Disposed into Abandoned; the Release test proves non-blocking with a Stop gate instead of a 500 ms wall-clock budget and no longer sits out the real 2 s deadline.
- Proofs: RCHECK_WIN=1 RCHECK_REPEAT=20 green on windows-latest (run 39882846063, 7 tests x20, ~87 ms each); dispose-before-wait mutation red (run 39882955715). rgate 39887355100: only the known LeashGateCard Windows flake (P94).

## avalonia-port/fin-views-tab-spiral: +380
- Spiral Room fog-entry swell and splash-open chime ported from WPF DescentRoomSfx (gains, audio/master gates, once per entry).
- Shared ambient motion gate now applies WPF's Windows animation-effects and Gentle Visuals caps; OS flag cached, tests pin the probe.
- views-tab-spiral stays stub with an explicit Missing list (descent block state, map adapter, bark, FIRST LIGHT caller dormant in WPF).
- Gate: rgate GATE-OK runs/39903857547.

## avalonia-port/fin-ctrl-sparkle-wallet: +165
- Sparkle wallet visor face: WPF EmiFace flat path (`WalletEmiFace`, Noto Sans Mono, 152x137 glass, 95% fit, 5 px round pink stroke, 2% lift) replaces the text placeholder.
- ctrl-sparkle-wallet stays `wired`; tests `WalletVisorUsesTheEmiGeometryRenderer`, `WalletFacePaintsTheFittedPinkInkAndClears` (fail-proven).

## avalonia-port/fin-feat-leash-receipts-hold: +348
- Leash hold tick sound (WPF LeashFx.Tick gain/suppression/mod cue), WPF hover colours on the cut confirm, and a portal Activated/Deactivated hold on native Wayland.
- Portal repeats are dropped only within RepeatGap (panic never goes dead); a portal hold trusts release only after the compositor has sent Deactivated.
- Row stays stub: holder receipts list and Let go need the feat-leash holder card.

## avalonia-port/fin-views-companion-chat-threshold: +296
Companion chat threshold (Z2) motion matches WPF: staggered thinking dots on the shared BeatLoop (450ms each way, 0/150/300ms delays, own repeat cycles, parked when hidden/minimised, reset on stop), state-triggered dormant shimmer (0.25s wait, 1.4s CubicEaseInOut, hidden if unloaded mid-sweep), VM detach on unload and re-sync on reload. Ledger views-companion-chat-threshold stays stub (BrowserMedia ownership + autoplay owned by wire-shell-browser-b-next); stale thinking-dots gap removed from views-companion-room. Tests: ChatThresholdMotionTests, HerRoomChatMemoryTests (rcheck 39898672572); rgate 39899074901 green except known P108 GoonClock flake.

## avalonia-port/migration-privacy-seed: +141
- Moving from the Windows app keeps your six privacy choices owned by your account, so the first sync sends exactly what Windows always sent.
- A fresh install, a different account or an incomplete settings file sends no privacy values until you change them yourself.

## avalonia-port/wire-views-tab-programs: +371
- Programs live session slot (clock, progress bar, paused caption, heat sheen); 1 s tick repaints clock only.
- Core `SessionClockLabel` program clock/progress shared with WPF; zero-length bar reads full as WPF.

## avalonia-port/flake-language-suite-discovery: +138

Windows "Test process did not return valid JSON (non-object)" discovery flake: xUnit 3.2.2 printed its 1 s shutdown notice into the -assemblyInfo stdout on slow teardowns. A shared module initializer now keeps only the JSON line in that launch (xUnit 4.0's own fix). New AssemblyInfoDiscoveryTests replays the slow exit and was fail-proven. rgate 40189678272 GATE-OK.

## avalonia-port/wire-views-tab-quests-b: +448
- Quests tab depth pass (wells, seats, planks, DONE stamp) on Core DepthLaw with finite BeatLoop travel; shell hue repaints quest bands.
- Fix-day status icons now Fluent (IconLabel.Kind), IconGuard ratchets back at baseline; fail-proven tests.

## avalonia-port/wire-views-companion-workshop-behavior-cell: +184
- Workshop Behavior cell's browser-pause switch drives the Home browser (mute + pause/resume media), live state only.
- Pause survives navigation; resume restores the saved mute preference.

## avalonia-port/wire-shell-assets-fx: +214
- Pack-card hover sheen is wired: one CardSheenAdorner moves between cards and detaches on leave, hide, card removal or unload. The strip stays hidden, as in WPF. The shell-assets-fx row is now `wired`.
- Five AssetsTabTests cover it, including one that uses real headless mouse moves. All were fail-proven (runs 39696872030 red, then 39697331663 green).

## avalonia-port/wire-shell-favorites-rail: +403
- Favorites drawer click-tips popup (hover/pin/Escape/focus return), persisted inverted dashboard clicks via Core DashboardClickRule, lilac glow on a new pin.
- Review fixes: bounded glow wait, seeded intro gates, real Escape key routing, no temp PNG; gate run 39688122404 green; feat-split-card row now cites DashboardClickRule (no duplicate inversion).

## avalonia-port/wire-views-companion-make-her-yours: +209
- Z4 Community link browses and installs through the Workshop cell's single CommunityPromptService. The readout shows the installed name, and Z4 and the Workshop rows refresh together. Row wired.
- Stacked on wire-views-companion-workshop-community-cell (5e10cd18a).

## avalonia-port/wire-feat-mod-personality-picks: +118
- Switching mods now brings back the companion personality picked in that mod, and the live chips update to match.
- Review fixes: the test no longer checks a fence that could not catch a bug; fail-proof plus rgate green except a known Linux flake.

## avalonia-port/sync7-mandatory-video-c: +235
- Video card "Shows as" switch; a floating mandatory video is one small unfocused topmost window (no attention checks; strict/Lockdown/Chaos stay full screen). WPF bd659f9f3.
- Rows 17/21/20 stay needs-port under their owning lanes sync7-mandatory-video-d/-e/-f.

## avalonia-port/sync7-leash-b: +811
- Leash "?" ink slips (main 325115b21): Core LeashHelpCatalog + LeashLevelGuide (pure git mv), Avalonia LeashHelpSlip card and reusable LeashHelpButton; host lands with sync7-leash-c.
- Tests LeashHelpSlipTests (5, fail-proven) + LeashHelpCatalogTests (4); gate red only on the RaceTrack P115 flake; render-all 226/226 incl. LeashHelpSlip.png.

## avalonia-port/flake-mantra-task-door: +72

Mantra Lab completion close timer is now stopped in Cleanup, so an early-closed completed lab can no longer end the next Programs task-door session (shard-2 flake root cause). Regression test fail-proven remotely; gate green.

## avalonia-port/wire-views-companion-awareness-privacy: +200
- Retained awareness history shown/forgotten/pruned/wiped via the Companion privacy card; ActivityLedger/AwarenessLive shared in Core.
- Fix rounds: startup only sweeps retention, startup hook tested via WindowAwarenessService, ledger unpublished on exit; row stays stub.

## avalonia-port/cloud-settings-backup: +747
- Cloud settings Backup now / Restore / status line on Avalonia through Core CloudSettingsBackup (same endpoints as WPF). Absolute-path fields are never uploaded, including the AudioLayers, CornerGifOverlays and KeywordTriggers lists, whose items carry paths, and every restore keeps this machine's values (oracle-deep Q1). SettingsBackupBudget moved to Core; WPF untouched.
- Export, 401 recovery, automatic backup and the startup restore prompt remain as wire-views-settings-account-b-next.

## avalonia-port/fin-avatar-tube-window: +619
- Tube look picker (WPF order, custom labels with blank-label fallback, per-mod restore, 200 ms fade, companion coupling), detached Ctrl+wheel/arrow zoom, Core make-room with user takeover and restore. The emote registry is cached and parse errors are not fatal.
- Row avatar-tube-window stays stub: animation, speech producers, reactions and Fuse are still missing.
- Evidence: ~/ccp-port/evidence/review-fin-avatar-tube-window/ (full.diff, failproofs-fix.md).

## avalonia-port/fin-ctrl-spiral-embed-view: +188
- Cloud session, local review (ACCEPT 55) + fix round.
- Review fix: ledger row cites the PR body, not docs/cloud; SpiralEmbedViewTests 13/13 via rcheck, environment theory fail-proven (devtools=true -> 3 red).
- rgate run 39924588177: green except known P115 flakes (RaceTrack, GoonClock).

## avalonia-port/fin-ctrl-spiral-rail-host: +1

- `ctrl-spiral-rail-host` stub -> wired under P122: WPF no longer mounts `SpiralRailHost` (MainWindow.xaml:746-749 gives the slot to `ChasterRailChip`); Avalonia host stays dormant and collapsed.
- Ledger-only diff (docs/avalonia-parity.md:466). Existing `SpiralRailHostTests` run via rcheck (run 39921170660): 2/2 passed.

## avalonia-port/fin-ctrl-v2-get-it-row: +1

- NOOP lane: dependency analysis; `ctrl-v2-get-it-row` stays stub (blocked by svc-prize-ownership, feat-bubble-pop). Stub reason now names the missing GrantsChanged repaint. rgate GATE-OK (run 39928533953).

## avalonia-port/fin-feat-brain-drain-keep-clear: +1

- Dependency-only NOOP: keep-clear effect needs the unported Brain Drain blur overlay (`views-studio-brain-drain-feature`); row stays `stub`. Corrected the row's WPF line refs (OverlayService.cs:3251,3264,3811). rgate GATE-OK (run 39921269080).

## avalonia-port/fin-feat-bubble-count: +460
- Cloud session, local review + gate.
- Studio > Bubble Count: For You defer (one replay, 500 ms polls, 90 s deadline), Pause other effects editor + per-round flash/text hold, localized empty-library warning. Row stays stub (shared interaction queue, overlay parking, ghost feed).
- Review fix: label ellipsis, ledger deviations restored. rcheck BubbleCountQueueTests 5/5; rgate GATE-OK.

## avalonia-port/wire-shell-haptics-device: +200
- Mock haptics feedback toast in Avalonia matching WPF MockToast, including ALL STOP staying visible after panic.
- Tested with a stepped clock (no sleeps). Fail-proven (rcheck 39697261650 red / 39697752707 green); render-all exemption covers only the toast.

## avalonia-port/fin-shell-tab-fx-presets-quests-achievements: +1
- Dependency-only NOOP audit; ledger row stays stub with explicit Missing list (shell-takeaway, shell-chrome-fx).
- Review ACCEPT; fix: row reference repointed to PR body. Doc-only, no rgate (pushq re-gates).
