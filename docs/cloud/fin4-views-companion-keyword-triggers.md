# fin4-views-companion-keyword-triggers

- Dependency-only analysis; no application, test, or ledger changes. Retain the single
  `views-companion-keyword-triggers` row as `stub`; no new surface needs panic registration.
- Verified clean base `eb1556c910858c7b897391bb219398182efcd793` after fetching the requested
  branch and resetting the snapshot's `work` branch. Work is on
  `avalonia-port/cloud-fin4-views-companion-keyword-triggers`.
- Read both cited WPF panel files, the forwarded handlers in
  `MainWindow.KeywordTriggers.cs`, and their live Awareness/service callers at that base.
- Fetched read-only `parity/trunk`, pinned at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`.
  No trunk code imported; that commit is credited here as feasibility evidence only.

## Comparison with WPF

The existing head implements add defaults, import and its two messages, preset-clone
filtering, delete, enable, keyword commit and linked-audio lookup, audio picker,
visual-effect selection in WPF order, cooldown, volume, haptic and duck edits,
settings persistence and action rebuilding. Buffer timeout, session XP multiplier,
OCR confirmation/highlight mode, duration, access-gated OCR detail, source hints,
24px reveal alignment, the finite 220ms twice-reversing bloom, and outer wheel
forwarding already have implementations. Existing `KeywordTriggersPanelTests` cover
these editors and interactions. Fluent icons and the shared-beat sibling shadow
are established port adaptations; neither is a missing runtime feature.

Remaining WPF behavior absent from this head:

1. The scan-interval slider must retime an active scanner, beyond saving milliseconds
   (`MainWindow.KeywordTriggers.cs:105-119`). There is no scanner to call.
2. OCR scan interval and confirmation selections must affect actual discovery and
   matching: all-screen word rectangles, own-UI exclusion, consecutive scan streaks,
   200ms quick confirmations (maximum three), matching and cooldown/dispatch.
   See `Services/ScreenOcrService.cs` and `KeywordTriggerService.cs:868-1110`.
3. Highlight mode and duration must affect real matched-word overlays, including
   all/random selection, repeated-word suppression, screen/DPI positioning,
   capture visibility, finite removal and cleanup. See `KeywordHighlightService.cs`
   and `KeywordTriggerService.CheckOcrWords`.
4. The enclosing Awareness master/OCR source must start/stop the runtime and keyboard
   source, with entitlement gates (`MainWindow.Awareness.cs:420-468`); access loss/gain
   must stop/restart OCR (`MainWindow.Patreon.cs:186,434`).
5. Capture visibility must refresh existing overlays (`MainWindow.Awareness.cs:743`),
   panic/resume must stop/restart OCR (`MainWindow.xaml.cs:1114,1162`), and shutdown
   must dispose scanner/overlays (`MainWindow.WindowChrome.cs:317-319`).
6. Configured custom triggers must actually fire their audio, visual/haptic actions
   and session XP effects. Persisting the editor values alone cannot provide this.

These are live WPF paths, so P122 (dead WPF caller) does not apply. This is the
unported `shell-keyword-triggers` runtime dependency, not an isolated missing panel
handler. `rg --files CCP.Core CCP.Avalonia` finds no ScreenOcrService,
KeywordHighlightService/Overlay or KeywordTriggerEngine implementation. The head's
`Platform/KeywordTriggerHead.cs` has only an empty `Stub.FireDemo`; the shell partial
and `AwarenessTabView.axaml.cs` explicitly mark their runtime hooks as absent.

## Why the dependency does not fit this unit

At the pinned base the WPF scanner, highlighter and trigger service total 2,928 lines
(336 + 508 + 2,084). This is not a proposed verbatim import: shared rules must move
into Core and platform capture/highlight behavior must remain in the head.

The already-adapted trunk reference still needs 1,473 lines across just five files:
`KeywordTriggerHead.cs` (457), `ScreenOcrService.cs` (196),
`KeywordHighlightOverlay.cs` (164), `KeywordTriggerEngine.cs` (452) and its OCR
partial (204). Its Windows reader adds 183 and X11 listener 173, before startup,
access/panic integration, additional types and focused tests. The base has none of
these services to reuse. Even the five-file subtotal exceeds the 1,000-added-line
cap. A timer-only seam or a no-op call would leave the same user-visible gaps and
would not justify `wired`.

Decision: follow the explicit pure-dependency exception, make no code change and
leave the ledger honest. Port the shell runtime as a separate feature, then wire
and test the panel's interval update against that real service. Linux OCR remains
an explicit platform concern; no claim of Windows capture execution is made here.

## Verification limits

No runtime behavior was changed; no new tests or fail-before-fix claim is applicable.

- Installed .NET SDK 10.0.402 successfully (exit 0). The initial `--channel 10.0`
  discovery failed at the CI feed (403); explicit version installation from the
  builds feed succeeded.
- `dotnet build <project> -c Release --nologo`: exit 0, zero errors for both
  `CCP.Core/CCP.Core.csproj` and `CCP.Avalonia/CCP.Avalonia.csproj`, and every Linux
  test project named in `.github/workflows/build.yml`:
  `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`,
  `CCP.Avalonia.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`,
  `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`,
  `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Report.Tests`,
  `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests`
  (each at `Tests/<name>/<name>.csproj`). Existing warnings remain.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false --nologo`: exit 0,
  zero errors, 512 warnings. WPF app and tests cross-compiled only, not run.
- `bash .github/scripts/core-guards.sh`: exit 0; tail: "All Core guards passed."
  Initial sandbox execution could not create temporary files; rerunning with
  workspace/temporary-file write access passed.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable at this base (exit 127
  when invoked); `docs/avalonia-test-plan.md` is also absent. No row was added or
  promoted.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release
  --no-build --nologo`: exit 0; 2,811 passed, 0 failed, 5 skipped, total 2,816.
  The existing Vosk tests skipped because the model is unavailable; they were not
  altered. No claim of speech-model coverage.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Avalonia.Tests -c Release
  --no-build --nologo --filter FullyQualifiedName~KeywordTriggersPanelTests`:
  exit 1; 0 passed, 9 failed. All fail at headless layout with
  `Could not create glyphTypeface` for Inter, the environment limitation identified
  in the task. The tests compiled, but behavior assertions could not be validated;
  GitHub rcheck is still needed. No tests were weakened or skipped by this change.
- `git diff --check`: exit 0. No render-all or real desktop/notification/capture
  checks were run. Existing test module initialization uses a temporary user-data
  sandbox and disables foreground-window reads.

## Assumptions and remaining dependency

The explicit dependency-only exception takes precedence over implementing a partial
runtime that would exceed the line cap. The existing shell runtime row remains the
single owner of that feature. No imported code, new surface, or ledger promotion.

NOOP: shell-keyword-triggers runtime (trigger engine, OCR capture and highlight overlays).
