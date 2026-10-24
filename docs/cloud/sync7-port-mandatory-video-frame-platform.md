# sync7-port-mandatory-video-frame-platform

PR-body draft; no PR opened. Base: `39260f198fad67d90bc988c63a00480830e5fefc`.

- Audited WPF commits `b6ff63fbd0f298a818f64ff225d5dec316ee4a5a`,
  `6aa9117c220f4a579050223bbd48eb4807433ab9`, and
  `25f8ffecd0cb4c06c6439cb524bcb3534136402a` with `git show` and `git show --stat`.
  These are the source credits for this unit; no trunk code was imported.
- Recorded the missing Avalonia host dependencies below. No production behavior was changed.
  Shared presentation rules alone do not supply a floating player.
- Left the sync ledgers and parity document unchanged. No new surface or panic registration,
  ledger promotion, or tester step is needed for this documentation-only audit.

## Per-commit disposition

| WPF commit | Status | Evidence and missing host |
| --- | --- | --- |
| `b6ff63fbd` | blocked-by-host | `CCP.Core/Services/Video/MandatoryVideoScheduler.cs:334` stops flashes before pre-roll, correctly for the only implemented presentation. `CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs:302` unconditionally sets full screen. There is no floating host or resolved per-clip floating state to drive `VideoFlashRule`. Applying the setting alone would leave flashes running during a full-screen video. |
| `6aa9117c2` | blocked-by-host | `CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs:152` creates one shared LibVLC player, rather than floating instances with independent audio focus. There is no veiled instance, reveal watcher, reveal animation/detach lifecycle, or `DecidePresentation` result to recheck. The floating cleanup presence correction also depends on flashes having remained running. |
| `25f8ffecd` | blocked-by-host | `CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs:301` shows the full-screen player directly. There is no browser floating window, page ready/playing/first-frame protocol, window-region reveal, or floating native frame seam to cloak before first show and uncloak safely on detach. Adding an unused DWM wrapper or first-frame rule would not port this behavior. |

The existing interface at `CCP.Core/Services/Video/MandatoryVideoScheduler.cs:13`
only exposes full-screen show, close-all, and verdict-message operations. The shared
`CCP.Core/Services/Video/MandatoryVideoPresentation.cs:19` rule can choose floating
in isolation, but neither the scheduler nor the Avalonia host calls it. A search
of both trees found no `FloatingFirstFrame`, `FloatingVideoFrame`, `VideoFlashRule`,
or `FloatingWanted` implementation. None of the three commits is already-covered.

## Deferred acceptance when the host lands

- `b6ff63fbd`: full-screen triggers (including Test) stop flashes; floating triggers
  (including the LibVLC route) preserve both existing flashes and their scheduler.
- `6aa9117c2`: veiled instances stay muted and outside audio rotation until reveal;
  a playing clip waits rather than skips for a late frame; grace pause holds its
  reveal clock; detach during reveal never exposes the full window. Recheck the
  actual presentation after pre-roll and restore flash presence after floating cleanup.
- `25f8ffecd`: cloak before first show; fall back to the one-pixel region on refusal;
  empty the window before uncloaking on detach; reveal a playing clip at the 4 s
  ready budget even without frame proof, while a non-playing clip is skipped.
  Retain the source's existing grace/progress gates and include cloak diagnostics.
- Prove each port with focused behavior tests and a failing production-line reversal.
  No such regression proof is claimed here because there is no production change.
  Register any future floating surface in `PanicSurfaces` as part of its host lane.

## Verification limits

- `dotnet build <project> -c Release --nologo -v:q`: exit 0, zero errors for
  `CCP.Core/CCP.Core.csproj`, `CCP.Avalonia/CCP.Avalonia.csproj`, and every Linux
  test project referenced in `.github/workflows/build.yml`:
  `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`,
  `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`,
  `CCP.Avalonia.Migration.Tests`, `CCP.Avalonia.Report.Tests`,
  `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests`,
  `CCP.Avalonia.Tests`, `CCP.Core.Content.Tests`, `CCP.Core.Settings.Tests`,
  and `CCP.Core.Tests` (each under `Tests/<name>/<name>.csproj`).
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release --nologo -v:q -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 warnings,
  zero errors. Compiled on Linux, not run (Windows/WPF runtime required).
- SDK: .NET `10.0.402`. The requested channel installer initially hit HTTP 403 on
  its CI version lookup; installing the release version from the release feed succeeded.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed (3 existing
  baselined exceptions). The first read-only sandbox attempt could not create its
  scratch directory; rerunning with write access passed.
- `dotnet test Tests/CCP.Core.Tests -c Release --nologo -v:q` with
  `DiffEngine_Disabled=true`: exit 0; 3953 passed, 0 failed, 5 skipped, 3958 total.
  No tests were changed, removed, or newly skipped by this branch. The Vosk model
  was not downloaded; existing environment-dependent skips remain.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable on this base (exit 127).
- `git diff --check`: exit 0.
- Avalonia runtime tests were not run under the supplied font-creation environment
  limitation (`cloud-test-fonts` is absent). The supervisor must run the existing
  `MandatoryVideoOverlayTests` and related suites on GitHub; no new behavior tests
  were added to this documentation-only branch.
- Windows desktop behavior, DWM cloaking, browser reveal, and floating audio focus
  were not run. No new Windows code was added.

## Assumptions

- The instruction to report missing hosts takes precedence over constructing sibling
  floating/browser lanes. Do not import those siblings to make this unit appear wired.
- Existing full-screen flash stop/resume behavior should remain intact until the host
  can report the actual presentation for each clip.

## Blockers

All three source commits are blocked by the absent Avalonia floating mandatory-video
host. The reveal fixes additionally require browser instance/reveal/audio-focus
lifecycle support; the cloak fix requires its native floating-frame platform seam.
