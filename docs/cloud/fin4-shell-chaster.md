# fin4-shell-chaster

- Finish `shell-chaster` as wired (never verified). Base audited at
  `91022bb7049fd6634c0bffc3d632927798bdd998`.
- The ledger's two named gaps were already implemented on the base: BookedAt opens
  ChasterBookedPop at the cause/cursor with the rail fallback; CapRefused uses the
  localized CapNotice plan and successful-placement-only two-second throttle.
  Existing code also covers stacking, coalescing, net-zero dismissal, source names,
  motion levels, expiry and shell shutdown. No replacement of those implementations.
- Remaining WPF differences found: cursor fallback was X11-only, and the first-frame
  topmost reassertion in WPF OnContentRendered was absent. Reuse Win32Overlay's
  GetCursorPos and the existing cross-platform X11Overlay.Raise adapter on Opened.
- Register ChasterBookedPop.CloseAll in PanicSurfaces, with focused tests for cursor
  fallback/panic, shell cleanup/unsubscription, and topmost reassertion after show.
- Add numbered manual WPF comparison steps and remove private evidence paths from
  the edited ledger row.
- Read-only trunk fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`;
  no trunk code imported. WPF reference on the pinned base remains the specification:
  MainWindow/MainWindow.Chaster.cs and Controls/ChasterBookedPop.cs.

## Verification limits

- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed.
- `dotnet test Tests/CCP.Core.Tests -c Release` (`DiffEngine_Disabled=true`):
  exit 1, 3337 passed / 2 failed / 5 skipped. Failures were the unrelated
  DtrhRemoteMediaTests cold-cache Loops/Clips assertion and BoardTilesTests frame
  budget (29.77 ms). A no-build focused recheck of DtrhRemoteMediaTests plus the
  frame-budget test passed all 10 (exit 0). No Core or existing test changes.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter
  FullyQualifiedName~Chaster`: exit 0, 98 passed.
- `dotnet build <project> -c Release`: exit 0 / zero errors for CCP.Core,
  CCP.Avalonia, and every Linux workflow test project: CCP.Avalonia.Tests,
  CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler,
  Snapshot, CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests.
- Final `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter ...`
  (motion timings + four non-window PanicSurfaces tests): exit 0, 5 passed.
  Newly added window tests compile but were not rerun after the known font failure;
  before/after behavioural validation remains for GitHub.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release
  -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors.
  WPF app and test assembly compiled; neither can run on this Linux host.
- `git diff --check`: exit 0. Added lines versus the pinned base: 140 (cap 1000).
- The pre-fix focused run compiled successfully, but six shell tests failed before
  their assertions: `Could not create glyphTypeface` for Segoe UI Black in
  PremiumSpark.BuildLabel. The pure motion test passed (1 passed, 6 failed).
  This is the documented cloud font limitation; no valid regression failure proof
  can be claimed here. Tests remain enabled for GitHub validation.
- Windows cursor/native topmost code is compiled on Linux but cannot be exercised
  on a Windows desktop here. No live Chaster account or desktop overlays tested.
- Native Wayland retains the base's rail fallback: global pointer/click-through
  placement is unavailable. This is an intentional platform fallback, not a new host.
- No test-plan-coverage.sh exists on this pinned base; numbered coverage was added
  in the requested format for future checks.

## Assumptions

- Preserve the already-wired Core booking hooks and popup design; the stale shell
  ledger does not justify replacing working base code.
- Panic cleanup follows the unit's explicit safety requirement even though the WPF
  reference only closes these transient windows on expiry and shell shutdown.
- Opened is the Avalonia counterpart for reasserting z-order once Show maps the pop;
  the shared adapter already selects HWND_TOPMOST on Windows and XRaiseWindow on X11.
