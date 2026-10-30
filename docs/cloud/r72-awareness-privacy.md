# r72 awareness privacy, part A

Base: 74ac6ce3d5e0ae082c2871c00e864943e15aa2ad.
The initial cloud HEAD was aa9918833722f057079e455167cb3d1b14462340.
Fetched origin avalonia-port/port-docs-html and checked out the required base on work.
Checkout was clean. No requester local changes were present.

## Base audit

References below are relative to the repository root. RuntimeVm means
ConditioningControlPanel/Views/Controls/Companion/Runtime/AwarenessPrivacyRuntimeVm.cs;
AvaloniaVm means CCP.Avalonia/Views/Controls/Companion/AwarenessPrivacyViewModel.cs.

- Ledger, live holder and projection already exist in Core:
  CCP.Core/Services/Awareness/ActivityLedger.cs:237/:251,
  AwarenessLive.cs:26 and AwarenessProjection.cs:45 in the same directory.
  This supersedes the older duplicate rows' claims that they are absent.
- Startup retention sweep, awareness lifecycle and exit disposal already exist:
  CCP.Avalonia/App.axaml.cs:36/:41/:49. WPF App.xaml.cs:2928/:3004.
  This is ledger lifecycle, not a v2 observer or a source of real frames.
- Known-app chips and forget already exist: AvaloniaVm:318, WPF RuntimeVm:570.
- Retention already prunes memory and disk: AvaloniaVm:237.
  WPF RuntimeVm:261 only writes the setting. Existing privacy-side deviation retained.
- Wipe already calls shared full erasure: AvaloniaVm:466, WPF RuntimeVm:790.
- Live line, JSON and live cursor state are still missing: AvaloniaVm:170/:174/:275;
  WPF RuntimeVm:342/:405/:442. Core projection alone does not supply frames.
- The cursor mechanism is already ported, including visibility and live-state changes:
  CCP.Avalonia/Views/Controls/Companion/AwarenessPrivacyView.axaml.cs:36/:138.
  WPF Views/Controls/Companion/AwarenessPrivacyView.xaml.cs:135/:156.
  It cannot blink live without a real observer frame pipeline.
- Legacy band hiding and review action remain unavailable: AvaloniaVm:164/:270;
  WPF RuntimeVm:323/:736. No false switch-on promise added.
- Seen chips only offer the current foreground: AvaloniaVm:308.
  WPF RuntimeVm:525/:534 merges foreground and keyword-trigger ring, not ledger
  transitions. WPF ledger transitions belong to the picker and known-app row.
- Picker candidates previously used only foreground: base AvaloniaVm:391.
  This was stale even though the ledger was already available on the base.

## Port and real user path

Awareness tab adopts the real privacy card at
CCP.Avalonia/Views/Tabs/AwarenessTabView.Companion.cs:19.
The card's Add deny and allow-per-app commands reach AvaloniaVm:420/:427,
then PickAsync:412 and Candidates:393. Candidates now merges foreground,
ledger transitions, then persisted ids, sanitizes both exclusions and candidates,
de-duplicates without case sensitivity and caps at 40. A failed source leaves
other sources usable. Candidates are unchecked until selected.

Spec: ConditioningControlPanel/Services/Awareness/AwarenessAppCandidates.cs:34/:38/:41/:46/:53/:64/:79;
WPF RuntimeVm:671/:697 calls it for both lists. The implementation preserves
WPF's actual transition enumeration order, even though its comment says newest first.
No new localization key, Core API, platform capture or WPF change.

Tests: Tests/CCP.Avalonia.Tests/AwarenessPrivacyCandidatesTests.cs:25/:81.
The theory seeds and reopens a persisted ledger with no transitions, disables
keyword triggers, opens each real picker, selects a retained app and saves it.
The unit test covers transition order, canonicalized exclusions, deduplication
across ring and known ids and the 40-item limit. Tests use the shared dispatcher
and assembly isolation hook. The UI setup uses Program.BuildAvaloniaApp fonts.

## Part B

The row remains stub. WPF reaches this feature: App.xaml.cs:3046 constructs
AwarenessObserver and :3004 attaches its Start/Stop lifecycle. It is not n/a.

- Port observer, arbiter and platform probes behind the existing platform seams:
  WPF Services/Awareness/AwarenessObserver.cs:298 and AwarenessProbes.cs:1;
  App.xaml.cs:3036/:3046. Observer and probes alone total 1,515 lines.
- Publish actual frames and render the wire/JSON, then unlock the live cursor,
  band hiding and consent review: WPF RuntimeVm:323/:342/:405/:442/:736;
  AvaloniaVm:164/:170/:174/:270. Do not synthesize a frame from legacy titles.
- Supply the keyword-trigger foreground ring to seen chips and picker:
  WPF RuntimeVm:534, Services/Awareness/AwarenessAppCandidates.cs:50;
  AvaloniaVm:308/:393. The current keyword head has no such ring.

Only the latest of three matching ledger rows was edited. Historical duplicates
and docs/avalonia-progress.html are unchanged.

## Verification

Installed .NET via dotnet-install --channel 10.0, SDK 10.0.403 in /tmp/ccp-dotnet.
Commands use that dotnet executable. Existing compiler warnings were retained.

- dotnet build CCP.Core: 0 warnings, 0 errors.
- dotnet build CCP.Avalonia: 0 warnings, 0 errors (incremental).
- dotnet build Tests/CCP.Avalonia.Tests: 0 errors.
- dotnet test Tests/CCP.Core.Tests: 4635 passed, 6 skipped, 0 failed.
- dotnet test Tests/CCP.Avalonia.Tests --no-build --filter FullyQualifiedName~AwarenessPrivacyCandidatesTests: 3 passed.
- dotnet test Tests/CCP.Avalonia.Tests --no-build --filter FullyQualifiedName~TestIsolationTests: 12 passed.
- Combined affected run (AwarenessPrivacyCandidatesTests, AwarenessPrivacyCardTests,
  AwarenessLedgerPrivacyTests and TestIsolationTests) encountered the existing
  glyphTypeface creation failure for Consolas (compositefont:Consolas, Courier New),
  Bold, in AwarenessLedgerPrivacyTests at line 75. Existing affected UI tests
  compiled but could not be verified by that run. New UI tests ran separately.
- bash .github/scripts/core-guards.sh: All Core guards passed.
- python3 .github/scripts/avalonia_docs.py: avalonia_docs self-check OK.

Fail-proof: temporarily removed both ledger-source Add calls from Candidates,
leaving foreground and the filtering logic unchanged. The focused test command
failed all three cases (0 passed): both picker theories could not find the retained
row, and the transition test received an empty candidate collection. Restored
both calls from the saved good source before the final build and rerun. No
existing test was changed, skipped or removed.

Final restored run: dotnet test Tests/CCP.Avalonia.Tests --filter
'FullyQualifiedName~AwarenessPrivacyCandidatesTests|FullyQualifiedName~TestIsolationTests':
15 passed, 0 failed, 0 skipped. Build completed with 0 errors.

## CI follow-up: deterministic headless font setup

The GitHub API reports PR run 41086306721 as cancelled, with no failed job;
logs for its two started Avalonia shards end with "The operation was canceled."
There is no assertion failure in those logs to attribute to this patch.

A concrete failure was reproduced locally in a fresh testhost:
`dotnet test Tests/CCP.Avalonia.Tests --no-build --filter FullyQualifiedName~AwarenessLedgerPrivacyTests`
failed at AwarenessLedgerPrivacyTests.cs:75 with "Could not create glyphTypeface",
bold Consolas (compositefont:Consolas, Courier New). The mixed privacy/isolation
run passed 19 tests before the fix. This discrepancy is initialization order:
the platform and FontManager live for the testhost, and the first test's bare
AppBuilder omits Program.BuildAvaloniaApp's production font mappings.

The assembly's existing IsolateProcessStateAttribute.Before hook now initializes
the production app with Skia/headless drawing on the shared UI dispatcher before
snapshotting state. Every test therefore gets the shipped font registrations,
regardless of which test runs first. Product behavior is unchanged.

New regression TestIsolationTests.EveryTestStartsWithProductionFonts asserts the
app and Inter default exist before test work and measures bold monospace text.
It failed alone before the fix (1 failed, 0 passed); no existing test changed.
After the fix the privacy/candidate/ledger/font/isolation group passed all 22
tests, with no skips. The previously crashing ledger test passed alone (1/1).
Core guards passed; avalonia_docs self-check passed; git diff --check passed.
Shuffled-order validation (`CCP_TEST_ORDER_SEED=17`) expanded that filter with
ChasterRaffleCardLayout, BlinkTrainerSession and EmiBookDemo: 39 passed,
0 failed, 0 skipped. The test-project rebuild completed with 0 errors.
