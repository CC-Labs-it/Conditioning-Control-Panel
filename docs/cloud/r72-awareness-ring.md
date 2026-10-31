# Awareness privacy foreground ring

Base e8c4d7dff1a9129350e231f39e5809732b7647ea, PR #3347 head.
Initial cloud checkout: aa9918833722f057079e455167cb3d1b14462340, work, clean.
The named remote branch was absent. Fetching the exact SHA succeeded.

## Audit of all missing items

The prior row notes and docs/cloud/r72-awareness-privacy.md describe the base.
WPF RuntimeVm below means ConditioningControlPanel/Views/Controls/Companion/Runtime/AwarenessPrivacyRuntimeVm.cs.
AvaloniaVm means CCP.Avalonia/Views/Controls/Companion/AwarenessPrivacyViewModel.cs.

Already on the base:
- Core Services/Awareness/ActivityLedger.cs:237/:251, AwarenessLive.cs:26,
  AwarenessProjection.cs:45. Lifecycle CCP.Avalonia/App.axaml.cs:36/:41/:49;
  WPF App.xaml.cs:2928/:3004.
- Known-app forget AvaloniaVm:318, WPF RuntimeVm:570.
- Retention prune AvaloniaVm:237, WPF RuntimeVm:261 writes settings only.
- Wipe AvaloniaVm:466, WPF RuntimeVm:790.
- Visible refresh and cursor mechanism AwarenessPrivacyView.axaml.cs:36/:138;
  WPF AwarenessPrivacyView.xaml.cs:135/:156. The cursor still has no real frames.
- Foreground/ledger/persisted picker candidates AvaloniaVm:393/:420/:427;
  WPF Services/Awareness/AwarenessAppCandidates.cs:34 and RuntimeVm:671/:697.

Missing on base: keyword ring (KeywordTriggerHead had no collection; AvaloniaVm:308
used only current foreground and :393 omitted the fourth picker source).
This patch ports WPF Services/KeywordTriggerService.cs:489-521 (eight names,
case-insensitive MRU, snapshot, never persisted), :582 (exclude own/unknown),
:618/:662 (two-second sampling with scoping off). The hook still uses its existing
Win32 foreground PID seam; no platform code was added to Core. Scoped input names
are remembered even when scope suppresses the trigger, as WPF resolves before gating.
Avalonia Platform/KeywordTriggerHead.cs:69/:128/:138/:155 implements this.

WPF RuntimeVm:525-557 supplies seen chips from foreground then ring, distinct,
sanitized, deny-filtered, at most eight, with one-click hide. Port: AvaloniaVm:308.
WPF AwarenessAppCandidates.cs:50 adds the ring after ledger sources; port:
AvaloniaVm:409. Existing picker commands and settings persistence are reused.
No new copy or localization keys. No WPF or Core changes.

## Part B

The row remains stub. WPF is reachable: App.xaml.cs:3046 constructs the observer,
:3004 wires Start/Stop. It is not n/a. Remaining work exceeds this slice:
- Observer, arbiter, platform probes: WPF Services/Awareness/AwarenessObserver.cs:298,
  AwarenessProbes.cs:1, App.xaml.cs:3036/:3046 (observer/probes alone 1,515 lines).
- Real live line/JSON, band hide/review and live cursor state: WPF RuntimeVm:323,
  :342/:405/:442/:736; AvaloniaVm:164/:170/:174/:270 remain unchanged.
- Linux native keyword input capture is absent on the existing head. This patch
  keeps that OS boundary; Linux tests drive the actual runtime input seam and
  real picker/chip commands, not a synthetic observer frame.

Only the latest of three historical matching ledger rows is updated, on one line.
docs/avalonia-progress.html is unchanged.

## Verification

Installed .NET with dotnet-install --channel 10.0. The CI-channel lookup returned
403, then the installer completed with SDK 10.0.403. A parallel explicit fallback
installed 10.0.100; final commands use 10.0.403 from /tmp/ccp-dotnet/dotnet.

- dotnet build CCP.Core: 50 existing warnings, 0 errors.
- dotnet build CCP.Avalonia: build succeeded, 0 warnings, 0 errors (incremental).
- dotnet build Tests/CCP.Avalonia.Tests: build succeeded, 0 warnings, 0 errors
  (incremental; initial full build reported existing warnings).
- dotnet test Tests/CCP.Core.Tests: 4635 passed, 6 existing skips, 0 failed.
- dotnet test Tests/CCP.Avalonia.Tests --no-build --filter
  'FullyQualifiedName~AwarenessPrivacyRingTests|FullyQualifiedName~AwarenessPrivacyCandidatesTests|FullyQualifiedName~AwarenessPrivacyCardTests|FullyQualifiedName~AwarenessLedgerPrivacyTests|FullyQualifiedName~KeywordAudioRuntimeTests|FullyQualifiedName~TestIsolationTests':
  32 passed, 0 failed, 0 skipped on Linux. Production font setup worked.
- bash .github/scripts/core-guards.sh: All Core guards passed. The script needs
  writable temporary files, so the initial read-only sandbox invocation was rerun
  with write access; the read-only invocation's false baseline failures are discarded.
- python3 .github/scripts/avalonia_docs.py: avalonia_docs self-check OK.

Focused tests are Tests/CCP.Avalonia.Tests/AwarenessPrivacyRingTests.cs:24/:37/:66.
They use the shared assembly setup and AvaloniaTestDispatcher. Runtime state is
restored after the real UI tests. Both actual dialogs save a selected ring app;
the seen chip can then hide it and disappears from the row. No existing test
was modified or weakened. Windows native keyboard capture was compiled, not run;
Linux runtime, picker and chip behavior was run, not merely compiled.

Fail-proofs, each an isolated mutation, each restored immediately afterward:
- Omit RememberForeground call: ring assertion failed, 1 failed, 0 passed.
- Change sampling threshold from 2 to 3 seconds: boundary assertion failed,
  1 failed, 0 passed.
- Omit ring from seen chips: recent-app chip assertion failed in both directions,
  2 failed, 0 passed.
- Omit ring from picker candidates: recent-app picker row assertion failed in
  both real dialogs, 2 failed, 0 passed.
All failures were assertions, not build/font/environment failures. Sampling uses
UTC conversion of the existing runtime clock to match WPF across clock offsets.

Final restored build tails: CCP.Core 0 warnings/0 errors; CCP.Avalonia 201
existing warnings/0 errors; test project 144 existing warnings/0 errors.
Final restored affected filter: 32 passed, 0 failed, 0 skipped, 35 s.
Final guards, docs validator and git diff --check passed. No mutation remains.
