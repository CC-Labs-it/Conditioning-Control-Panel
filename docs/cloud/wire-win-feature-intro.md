# wire-win-feature-intro: startup modal and overflow slice

- Base: `avalonia-port/goongame-core-c` (`0e1aa0473`). Branch: `avalonia-port/cloud-wire-win-feature-intro`.
- WPF specification read: `Windows/FeatureIntroPopup.xaml` / `.xaml.cs`, `MainWindow.TabNavigation.cs`, `MainWindow.Patreon.cs`, `MainWindow.Lab.cs`, and `MainWindow.ProfileVat.cs`; existing Avalonia implementations remain authoritative when already present.
- Register the actual What's New, season recap and welcome windows with the existing startup presenter. The global startup gate includes every currently open registered window and preserves the separate caller-owned wizard/offer gate. Construction alone does not block intros, and closing one of two windows does not release the other.
- Feed the existing Core `QuietInputs.ModalUp` rule from startup/update state, so intro offers become unread Inbox rows while a modal is up; the card's seen flag remains unspent.
- Bring over WPF's scrollable explainer column and current text sizing/colors, keeping long cards' dismiss action reachable.
- Source imports: none. `parity/trunk` was fetched read-only; no trunk code was copied, so there are no imported trunk commit hashes.
- Ledger remains `stub`. Both existing duplicate `win-feature-intro` rows are updated consistently; no other ledger rows change.

## Follow-up lane: wire-win-feature-intro-b

The remaining feature work crosses the line budget: the vat introduction must fire only from the server-backed `ApplyDescentToVat` when the vat is armed and on screen (the head still lacks its DescentService/poll/faucet wiring); possession rules must be offered on the first actual possession-enabled lockdown with its bark, after the missing director/host is wired. Do not introduce either card on navigation to stand in for those lifecycle events. Complete the shared startup modal ladder and launch callers for What's New / automatic season recap, and premium's live-rise/`EntitlementTierSync` celebration path (the launch/per-provider retry path already exists).

The base already includes the Assets remote-media caller, FirstShow early return, welcome-back modal flag, and per-tier premium retry hook; the old ledger's claims that these are all absent were stale. This slice does not claim those paths were newly implemented.

## Verification limits

Environment: .NET SDK 10.0.402 in `/tmp/ccp-dotnet`; `DiffEngine_Disabled=true` for tests. Builds used `-m:1` and all three SDK hardlink flags (`CreateHardLinksForCopyFilesToOutputDirectoryIfPossible`, `CreateHardLinksForAdditionalFilesIfPossible`, `CreateHardLinksForCopyLocalIfPossible`) to avoid duplicating native runtimes. Auxiliary test builds used `-p:BuildProjectReferences=false` after the app/Core were built; their generated bin directories were removed afterward to keep within the workspace disk limit.

- `dotnet build CCP.Core/CCP.Core.csproj` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj`: pass, 0 errors.
- Explicit `dotnet build` of every Linux workflow test project: pass, 0 errors. Projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests. These auxiliary suites were compiled, not executed.
- `bash .github/scripts/core-guards.sh`: all Core guards passed (424 XAML type references checked).
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`: 2810 passed, 6 existing skips, 0 failed (2816 total). Five skips need the Vosk model, which was not fetched here; one is the existing frame-budget skip.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj --filter FullyQualifiedName~FeatureIntroWiringTests`: 7 passed, 0 failed, 0 skipped. Valid before-fix proof: the three new tests failed and the four existing tests passed. The scrolling test uses a 400-DIP constrained work area and asserts overflow, nonzero scroll offset and a fully visible dismiss button. The harness now uses the shipped app's font setup (`Program.BuildAvaloniaApp`), matching RenderProof; the original bare builder failed constructing PremiumSpark on this Linux font setup. Two earlier reruns aborted with SIGBUS when the workspace disk filled; the hardlink rebuild produced the successful final result above.
- `CCP_USERDATA_DIR=/tmp/ccp-worker1-render-profile dotnet run --project CCP.Avalonia --no-build -- --render-all /tmp/ccp-worker1-render`: exit 0, 224/224 views rendered. Inspected `/tmp/ccp-worker1-render/FeatureIntroPopup.png`: One Account copy, accent, rail and both CTA/dismiss buttons render legibly. Other PNGs were generated but not individually reviewed.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (plus the hardlink flags): restore succeeded, but generated `ConditioningControlPanel_*_wpftmp.csproj` compilation did not finish after 732 seconds under concurrent compiler load. The optional WPF attempt was stopped; no WPF build or test pass is claimed.

Windows execution, real-desktop input and signed-in account integration cannot run in this Linux headless environment. The shared startup ladder/launch callers and blocked vat/possession runtime paths listed above were not ported or tested. No new runtime surface or panic-owned activity is introduced.

## Publish blocker

The final normal push to `avalonia-port/cloud-wire-win-feature-intro` was rejected with HTTP 403: `Permission to CodeBambi/Conditioning-Control-Panel---CSharp-WPF.git denied to ClearlyNotMich.` The remote branch remains at claim commit `d01881d96d62a7ea5fcb73171f8b5428777f09ce`; the implementation is complete locally but has not been published. The parent worker also reported a GitHub connector write denial (`Resource not accessible by integration`), so no alternate write was attempted here. No further brief was claimed. A format-patch covering claim..HEAD is preserved at `/workspace/scratch/wire-win-feature-intro.patch` for publication once repository write access is restored.

## Account correction for publication retry

The publication-block notes above describe the earlier `ClearlyNotMich` attempt. The user selected `ObviouslyNotMich` for this retry; that connection reports repository push permission and successfully uploaded a Git blob. The current origin branch head is the publication source of truth.
