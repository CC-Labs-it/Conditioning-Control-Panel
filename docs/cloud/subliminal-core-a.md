# subliminal-core-a: one Core copy of the subliminal card rules

Base: `avalonia-port/feature-day-log-core`. Branch: `avalonia-port/cloud-subliminal-core-a`.

## What `ConditioningControlPanel/Services/Subliminal` already shares with Core

| WPF file | State before this unit |
|---|---|
| `BouncingTextService.cs` (776 lines) | Already routed: bounce/physics/logo state live in Core `Services/Subliminal/BouncingTextEngine.cs` + `BounceOverhang.cs`; what is left is WPF windows, DPI and `Screen` code. Nothing pure to move. |
| `SubliminalService.cs` (1407 lines) | Scheduler state is Core `CoreSubliminal`; whisper rules (Reset roll, gaps, volume curve, linked-audio lookup) are Core `SubliminalWhisper`. The **card** rules (hold, fades, alpha, font size, outline offsets) were still copied in three places: `SubliminalService` (twice: keep-alive window and solid-mode host card), WPF `Compositor/SubliminalLayer` and Avalonia `Views/Overlays/SubliminalOverlay`. |

Neither WPF file is WPF-independent as a whole, so there is no file to `git mv`; this unit
extracts the one duplicated pure rule group instead and routes every renderer to it.

## Changes

- New `CCP.Core/Services/Subliminal/SubliminalCard.cs`: `DefaultFadeMs` (50), `FontDip` (120),
  `OutlineOffsets` (the 8 border copies), `HoldMs(frames, override)` (frames x 17, min 100,
  override wins and keeps the floor), `FadeMs(ms)` (default 50, min 1), `Alpha(percent)` (0..1).
- WPF `SubliminalService`: `ShowSubliminalVisuals`, `BuildSubliminalContent`, the solid-mode
  hosted card and `AnimateSubliminal` defaults read `SubliminalCard`; its private `DefaultFadeMs`
  and both inline offset arrays are gone.
- WPF `Compositor/SubliminalLayer`: fade default, font size and outline offsets read `SubliminalCard`.
- Avalonia `SubliminalOverlay`: `FadeMs`, `HoldMs` and `Offsets` removed; hold, alpha, font size,
  outline and fades read `SubliminalCard`.
- Tests: the pure hold test moved from `Tests/CCP.Avalonia.Tests/TextOverlayTests.cs` to the new
  `Tests/CCP.Core.Tests/SubliminalCardTests.cs`, which also pins the override floor, fade
  defaults, alpha clamp, font size and offsets.
- One behaviour note: WPF used to pass `SubliminalOpacity / 100` unclamped to the keep-alive and
  hosted cards. It is now clamped to 0..1 (WPF already renders opacity outside 0..1 as 0 or 1, and
  the compositor layer and Avalonia overlay already clamped), so nothing on screen changes.
- No trunk (`parity/trunk`) commits imported: trunk has no Core subliminal card rules; its
  `SubliminalStealsFocusTests` is Avalonia UI and belongs to a later unit.
- No embedded resources moved, so no core-guards baseline entry.

## Next units

- **subliminal-core-b**: the linked-audio directory-listing cache (`SubliminalService.ListAudioFiles`,
  60 s, per mod) into Core `SubliminalWhisper`, so the Avalonia whisper path gets the same cache.
- **subliminal-core-c**: the card colour rules (`SubBackgroundColor`/`SubTextColor`/`SubBorderColor`
  fallbacks black / magenta / white, `#`-less hex accepted) as a Core hex parse both heads use.
- **subliminal-core-d**: OCR text-rect geometry (`GetActiveTextScreenRects`, 40 DIP pad) shared by the
  legacy windows and `SubliminalLayer.GetActiveTextRectsPx`.
- **subliminal-avalonia (UI)**: `SubliminalStealsFocus` and the solid-mode host on the Avalonia head
  (trunk `90cdc1120`, `SubliminalStealsFocusTests`).

## Verification limits

SDK: .NET 10.0.112 from Ubuntu apt (the dot.net install host is blocked by this sandbox's egress policy).

- `dotnet test Tests/CCP.Core.Tests -c Release`: 1142 passed, 5 skipped, 2 failed. The 2 failures
  (`ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_KeepsThePackInstalledButInactive`,
  `LoopbackOAuthTests.GoodState_OnEitherLoopback_ServesSuccessPage_AndReturnsCode(host: "[::1]")`)
  fail identically on the unchanged base in this container (root can delete a read-only folder; no
  IPv6 loopback). Not related to this unit. Filter `SubliminalCard|SubliminalWhisper`: 8/8 passed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter TextOverlayTests|SubliminalWhisperShowTests|HapticsHeadTests`
  (headless, `DiffEngine_Disabled=true`): 16/16 passed.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet build -c Release` with 0 errors: CCP.Core, CCP.Avalonia, CCP.Core.Settings.Tests,
  CCP.Core.Content.Tests, CCP.Avalonia.{Consent,Language,CatalogueStartup,Scheduler,Report,
  GeneralSettings,Snapshot}.Tests.
- WPF: `Tests/ConditioningControlPanel.Tests` built with 0 errors using
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`, which
  compiles the edited `SubliminalService.cs` and `Compositor/SubliminalLayer.cs`. Compiled but **not run**:
  the WPF app and its tests are Windows-only, so the WPF cards (keep-alive window, solid-mode
  host, compositor layer) were not exercised on screen.
- Added lines vs base: see the commit (`git diff -M --numstat`), well under the 1000-line cap.
