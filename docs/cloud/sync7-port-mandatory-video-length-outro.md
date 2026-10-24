# sync7: mandatory video length and outro

- Audited WPF commits `9e98566706eca8ddbc9adca1431266bf70f4532a`,
  `6215fe7b10cf0ab0565c100a516d2ef14762e1e7`, and
  `d5fefac0cb0436aac445b2bc6a69f221ff15d640` against base
  `39260f198fad67d90bc988c63a00480830e5fefc`.
- Recorded the missing hosts below; no production behavior or sync ledger status changed.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`.
  No trunk code imported: helpers without a live caller would violate the unit's scope.

## Per-commit disposition

### `9e9856670`: blocked-by-host

WPF arms the configured cap once at the first playback of an app-owned browser
session (Autonomy, Chaos, Remote), independently of reported duration. User and
AvatarLink sessions are exempt. The delayed stop checks session generation and
whether the session is still a takeover. Floating instances close separately on
played time, excluding grace pauses.

Missing surfaces: an Avalonia BrowserMediaService takeover session with ownership,
first-playback notification, generation and force-end handling; and the floating
mandatory-video instance host with per-instance playback accounting and close.
`CCP.Avalonia/Views/Windows/MainShellWindow.Browser.cs:190` logs that
`autoPlayFullscreen` is ignored. The generic WebHost has script and message
transport (`CCP.Avalonia/Views/Controls/WebHost.axaml.cs:95`, `:148`), but that
transport does not implement the missing media session lifecycle.
`CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs:127` creates the existing
full-screen LibVLC surfaces; it has no floating instance collection.

Existing full-screen cap behavior is already covered, but does not fulfill this
commit's new surfaces: `CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs:450`
calls `CCP.Core/Services/Video/MandatoryVideoScheduler.cs:246`, which uses
`CCP.Core/Services/Video/VideoMaxCap.cs:18`. Existing tests:
`VideoMaxCapTests.CapLeftContinuesTheClipBudget`,
`VideoMaxCapTests.MandatoryGuardCountsCapSpentBeforeAReplay`,
`MandatoryVideoSchedulerTests.ClipGuards`, and
`MandatoryVideoOverlayTests` (grace pause, heal budget and max-length cap).
No additional helper was introduced without its host.

### `6215fe7b1`: blocked-by-host (page portion already covered)

The page portion already exists at `Assets/web/player/player.js:565` (early
outro command), `:600` (only the remaining card time after media ends), and
`:955` (message dispatch). No page changes needed.

Missing surfaces: the mandatory browser-video engine and floating instances,
credit metadata/payload and card presentation, file-position versus played-time
reports, and per-clip once-only outro dispatch. The existing routing seam explicitly
allows heads without a browser engine
(`CCP.Core/Services/Video/Browser/BrowserVideoGate.cs:15`). Avalonia's shipped
mandatory host uses LibVLC (`CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs:152`),
not this page. No Avalonia credit-card host exists to consume CreditOutroTiming.

The required rule is to start 2.5 seconds before the earlier of file end and played
cap; short clips keep their first half clear, random starts do not spend the cap,
and an unknown end without a cap retains the post-end card. Adding the pure rule
alone would leave dead code, rather than port that behavior.

### `d5fefac0c`: blocked-by-host

Requires the preceding credit-card hosts and their queued-credit-link state.
The fix suppresses early outro dispatch for paused reports and opens a queued link
before cap cleanup, checking strict/Lockdown/Chaos both before queueing the open
and when opening. The Avalonia LibVLC host has a grace-pause guard at
`CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs:452`, but no credit card or
queued link to which this fix can apply. No new surface was created, so no new
PanicSurfaces registration is needed.

## Verification limits

- `dotnet build <project> -c Release --nologo`: exit 0 and `0 Error(s)`
  for CCP.Core, CCP.Avalonia and all twelve Linux test projects listed in
  `.github/workflows/build.yml` (Core, Core.Settings, Core.Content;
  Avalonia.Tests, CatalogueStartup, Consent, GeneralSettings, Language,
  Migration, Report, Scheduler, Snapshot). Existing compiler/analyzer warnings remain.
- WPF test project build with `-p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0,
  557 warnings, 0 errors. Compiled only; no Windows tests or desktop run.
- .NET SDK: 10.0.100 (channel discovery returned HTTP 403; explicit stable
  version installation succeeded).
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (429 XAML
  references checked). Initial read-only invocation could not create temporary
  files; rerun with write access passed.
- `dotnet test Tests/CCP.Core.Tests -c Release`, with
  `DiffEngine_Disabled=true`: exit 0; 3,953 passed, 5 skipped, 0 failed.
  The five existing speech tests skip without the unavailable Vosk model.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter
  FullyQualifiedName~MandatoryVideoOverlayTests`, with `DiffEngine_Disabled=true`:
  exit 0; 1 passed, 1 skipped, 0 failed. The native playback test skipped its
  dependency check; system libvlc is unavailable. This does not verify rendering,
  pause, panic or playback on this worker.
- `test-plan-coverage.sh` is absent on this base; no ledger rows changed.
- `git diff --check`: exit 0.

No production change means no new behavior test or production-line mutation is
applicable. The existing tests above are evidence only for the existing LibVLC cap,
not for the blocked browser/floating behavior. Windows desktop behavior cannot be
run on this Linux worker. Vosk model download is unavailable in this environment.

## Assumptions

- Treat the explicit blocked-by-host exception as controlling: do not build browser
  takeover, floating-player or credit-card hosts in this unit, or re-port siblings.
- Existing JavaScript and full-screen cap coverage are partial coverage only; none
  of the three commits is claimed fully ported.
- Do not promote ledger rows or alter the test plan for this documentation-only audit.
