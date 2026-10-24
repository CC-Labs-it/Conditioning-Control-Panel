# fin2-ctrl-spiral-first-light-visual

- Fix the first-light block-wait deadline: a cancellable one-shot timer dispatches to the UI independently of the shared animation clock, including while minimised.
- Reject an already queued deadline belonging to an earlier reveal; dispose the timer on finish, navigation and detach.
- Extend the existing shell test with normal/minimised deadline cases that advance time without animation frames. Both fail against the base at the timeout visibility assertion.
- Keep the parity row **stub**, with the remaining dependencies explicit. No Core or WPF source changes; no new surface requiring panic registration.
- Read-only reference fetched: parity/trunk at a4ee52d40cd347d288dcf7fdaa8445b79f8e5038. No code imported from trunk.

## WPF gap analysis

Reference pinned at e33ebf892c032e4a3213e28e495513608f2aa323:
Controls/SpiralFirstLightVisual.cs, Views/Tabs/SpiralTabView.xaml.cs:307-335, 700-858,
Services/Descent/DescentShowDirector.cs:400-475, MainWindow/MainWindow.SpiralRoom.cs:96.

The visual drawing and Core timeline envelopes already exist on the base: deterministic fog,
arm, station dots, embers, bloom, fade and reduced motion. The base also clears competing room
layers, holds the bloom, ends on navigation/error, and provides the shell rig seam.
The remaining independent gap was the deadline riding the parked frame clock; fixed here.

Remaining gaps depend on the unported WPF-head DescentService and DescentMigrationService:
reading a block already in hand, releasing the hold when BlockChanged supplies it, and refreshing
the ordinary room on later block/withhold changes (including withdrawal). A search for
'class DescentService|class DescentMigrationService' in CCP.Core and CCP.Avalonia has no matches.
Porting those network services and the native embed is outside this visual unit; no fake block
service or test-only production path is introduced.

There is no missing live ceremony caller to restore. Proof:
`git grep -n RunFirstLightReveal e33ebf892c032e4a3213e28e495513608f2aa323 -- ConditioningControlPanel`
returns only DescentShowDirector.cs:428 (the declaration).
Its lines 420-426 state the ceremony retired and only the owner's scratch capture rig calls it.
The existing Avalonia seam therefore remains internal. P122 is not used to claim wired while
block dependencies remain, and render-all cannot show a production first-light state.

## Assumptions

- Preserve the internal capture-rig entry, rather than invent a menu absent from WPF.
- Use the head's existing TimeProvider seam for the one-shot deadline and UI dispatch; retain the
  deliberate shared 30 fps drawing clock.
- Keep stub status until the block service and embed path can reach all handoff behaviour.

## Verification limits

All commands use Release configuration, .NET 10, and DiffEngine_Disabled=true for tests.

- Checkout verification: required base HEAD matched after fetch/checkout; clean local work branch.
- Regression before fix: filtered FirstLightHolds test, exit 1, two failures at the timeout
  visibility assertion (normal and minimised), proving the gap without a font failure.
- dotnet build CCP.Core/CCP.Core.csproj -c Release: exit 0, 0 errors (existing warnings).
- dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release: exit 0, 0 errors.
- dotnet build for every Linux test project listed in .github/workflows/build.yml: exit 0,
  0 errors each. Projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests,
  CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests,
  CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests,
  CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests.
- bash .github/scripts/core-guards.sh: exit 0, All Core guards passed.
- dotnet test Tests/CCP.Core.Tests -c Release: exit 0, 2822 passed, 5 skipped, 0 failed.
  The existing Vosk tests skip without the model; no skip was added or changed here.
- dotnet test Tests/CCP.Avalonia.Tests -c Release --filter
  'FullyQualifiedName~SpiralRoomShellTests|FullyQualifiedName~SpiralFirstLightVisualTests':
  exit 0, 8 passed, 0 skipped, 0 failed. Includes in-memory drawing proof.
- git diff --check: exit 0.

Windows cross-target build:
`dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`:
exit 0, 512 existing warnings, 0 errors. Compiled but not run on Windows.

Windows desktop execution, native embed execution,
full Avalonia suite, and render-all were not run. No wired/P122 or full visual parity claim is made.
