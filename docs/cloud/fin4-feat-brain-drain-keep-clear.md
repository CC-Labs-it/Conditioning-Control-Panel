# fin4-feat-brain-drain-keep-clear

- Dependency-only NOOP: no application, test, or ledger change. The row remains `stub`;
  saving the preference is already wired, but does not keep any pictures clear yet.
- Required base verified: `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`.
  The initial clean snapshot was at `f81ea01fb079b8b0ab750757ca23309cb78fdf30`;
  the specified fetch and checkout succeeded before analysis.
- Read-only import source inspected: `parity/trunk` at
  `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No code imported.
  Candidate capture implementation credit: `11c20f4ef3d96b06386a69e655d4307406a5f7fe`;
  latest overlay-path history: `72462bcef309c9fc5e810b05281bae195dfdb837`.

## WPF comparison at the pinned base

The row cites a historical Notifications/BrainDrainKeepClear.cs location. Its pure
rule now lives in CCP.Core/Services/Notifications/BrainDrainKeepClear.cs, shared
unchanged with WPF. The live WPF caller is
ConditioningControlPanel/Services/Notifications/OverlayService.cs.

Already present in Avalonia: Studio > Brain Drain > ChkKeepClear, default off,
load/attach and settings replacement repaint, preference write/save, and the Core
anchor rule. Existing BrainDrainKeepClearRowTests cover load/write and replacement;
Core BrainDrainKeepClearTests cover JSON persistence and ordering edge cases.

Every missing behavior for this row depends on the absent visual host:

1. Render a real, capture-excluded desktop blur beneath CCP's own pictures while
   leaving other applications below the blur. The rule alone cannot render this.
2. Apply the preference immediately: on sinks the blur; off forces the blur back
   to the topmost band (WPF property handler at lines 3811-3815).
3. Repair drift every 500 ms (timer at line 514, ReassertZOrder caller at 3251).
   The sink method at 3264-3302 scans at most 1024 handles, stops at the first
   non-topmost window, ignores invisible windows, and distinguishes process-owned
   picture windows from drain windows and other applications.
4. Collect every legacy blur HWND and visible capture-excluded compositor host.
   Insert all drains below the lowest visible owned non-drain window, preserving
   their relative order and position/size without activation. Do nothing when no
   drain, no owned picture, or ordering is already correct; do not sink when off
   or no drain is showing.

WPF ChkKeepClear_Changed (Studio code-behind lines 326-335) saves the preference;
OverlayService owns the effect. BrainDrainWithheld is false (line 2278), the
reconciler calls the sink, and the settings handler calls ReassertZOrder. These
are live callers, so P122/dead-code promotion does not apply.

## Dependency and line budget

Dependency: the unported Brain Drain desktop-capture/blur host, tracked under
`views-studio-brain-drain-feature`. Base searches found no Avalonia capture pump,
BrainDrain overlay, or App.BrainDrain service. The welcome-show approximation is
a tint, not desktop capture, and existing overlay shims provide window flags,
not a blurred desktop image. Reordering those surfaces would implement a
different effect.

WPF's capture pump is 771 lines and its layer 434 lines, excluding OverlayService
integration. The smaller trunk adaptation has 458 lines of BrainDrainOverlay,
459 of BrainDrainCapturePump, and 100 of Core Services/BrainDrainVisual: 1017
source lines (including the already-shared policy/anchor rules, which must be
reused rather than copied) before shell/session start-stop/pause hooks, panic registration, settings
replacement handling, card activation, render registration, documentation, or
focused tests (trunk's overlay tests alone are 372 lines). It also references
trunk-only host integration such as EmiDrainWatch. Even after reusing those existing rules, the capture, drawing, and new visual
math leave almost no budget for integration and behavior tests. These counts are a scope
estimate, not a claim that every source line must be copied. Compressing or
stripping the capture/lifetime behavior to fit would sacrifice the required
WPF behavior and tests. A complete adapted host plus this row's behavior cannot
responsibly fit this unit's 1000 added-line limit.

Trunk explicitly supports capture only on Windows: an X11 composited root grab
would capture the haze itself. A Linux implementation needs a separate capture
strategy; an opaque tint or a preference-only caller cannot close this gap.
Port the host as a prerequisite unit, then connect the existing Core rule and
exercise immediate toggle, periodic repair, multi-window ordering, and panic
teardown through the real user path. No new surface exists in this branch.

## Verification limits

- .NET SDK 10.0.402 installed using the requested channel installer (exit 0).
  The installer's preliminary ci.dot.net lookup returned 403; the SDK download
  from builds.dotnet.microsoft.com succeeded.
- `dotnet build -c Release`: CCP.Core, CCP.Avalonia, and every Linux-compatible
  test project in build.yml succeeded (exit 0, zero errors). Test projects:
  CCP.Avalonia.CatalogueStartup.Tests, Consent.Tests, GeneralSettings.Tests,
  Language.Tests, Migration.Tests, Report.Tests, Scheduler.Tests, Snapshot.Tests,
  CCP.Avalonia.Tests; CCP.Core.Content.Tests, Settings.Tests, and CCP.Core.Tests
  (the shortened names retain their respective CCP.Avalonia/CCP.Core prefixes).
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`:
  exit 0; 3953 passed, 5 skipped, 0 failed. The five existing Vosk tests skipped
  because their model is unavailable; no test was edited or disabled.
- Focused Core run with `--no-build --filter FullyQualifiedName~BrainDrainKeepClearTests`:
  exit 0; 7 passed, 0 failed or skipped.
- Avalonia BrainDrainKeepClearRowTests compiled with the whole test assembly
  (zero errors) but were not executed under the supplied font-creation limitation;
  execution remains for GitHub rcheck. No render-all execution was attempted.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. An initial
  sandboxed attempt could not create temporary files; rerun with filesystem
  access succeeded. `.github/scripts/test-plan-coverage.sh` is absent on this
  base; no ledger status changed and no new surface/test-plan step was added.
- `git diff --check`: exit 0.
- WPF test project `Tests/ConditioningControlPanel.Tests` built with
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`:
  exit 0; 554 warnings, 0 errors. Windows behavior was compiled but not run on Linux.

## Assumptions

- The unit's explicit pure-dependency NOOP exception takes precedence over
  implementing a partial host or promoting a preference-only row to wired.
- The row concerns keep-clear behavior; unrelated audio, art, and library gaps
  in the broader Studio card are outside this row.
