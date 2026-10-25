- Port ambient trigger rolling, selected pools, ownership-gated Brain Drain, chance, four-effect ceiling including Chaos treats, benign pop rewards/payloads and seven-second quiet expiry onto the existing overlay. Heavy payloads return the click first and recheck video/cascade/stop gates.
- Port owned Rain, Spiral In and Mix choices, Off fallback, Reduced speed/turns and fade-to-core quiet exit. Trigger motions reuse RainDown/RoamBounce/SideDrift and capture dashboard speed boost at spawn.
- Resolve pop/chime audio through the existing mod audio seam, with suppression/missing-chime fallback and WPF volume curve. Add pink/gold spark counts, gravity and fade; use cheap gold rings instead of a per-bubble blur (ponytail).
- Credit pop counters, the 1000-pop achievement, each 100-pop sparkle point and each 50-pop persistence flush. Retain quest/haptic credit; reject repeated pops before side effects. Keep ambient payload bubbles separate from Chaos count/teardown.
- Replace the ledger's repeated historical description with one honest `stub` row and add numbered tester steps. No new surface: the existing engine/panic path calls CoreBubbles.Stop, and existing effect surfaces own their panic registrations.
- Sources: WPF reference at base `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`. Read-only parity/trunk fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Motion/path math adapted from the pinned WPF AmbientBubbleMotion.cs; original head files were merged, not overwritten.

## WPF gap inventory and remaining scope

At the pinned base, the live WPF feature has settings for motion/gaze/Brain Drain absent from the Avalonia card; trigger selection did not affect ambient spawning; all bubbles floated up; lucky/boost spark visuals, mod sound resolution and achievement credit were absent. The service also has Natasha's red mark/hold-resist/Chaster outcome, avatar-egg age/one-shot roll/voice/choreography/cancellation/cooldown, gaze target and dwell integration, and a heavy payload deferred cascade queue. The existing plain XP bucket, caps, monitor targets, haptics and mod bubble image were already wired and reused.

Remaining (row stays stub): Natasha's red mark/hold-resist/Chaster outcome; avatar egg; gaze target/dwell plus card controls; trigger-specific sprite/label/Brain Drain breathing visuals; milestone popup; and WPF's bounded deferred heavy-payload cascade queue (this unit drops a heavy pop during cascade). These need additional head/service behavior and tests beyond this unit's 1000-added-line budget; partial implementations are not described as parity. Trigger types are currently distinguished by tint under the existing sprite. WPF also has optional persona art plates and prize presentation beyond the owned motion picker.

Pull/Magnet is retired, not a remaining implementation gap: at the pinned base, `git grep -n 'Pull is retired\|Retired, including old saved selections' 73b1ccf0d793bb3fde6091c18a9fa6b5c692964a -- ConditioningControlPanel/Services/BubbleService.cs` finds the pool exclusion and builder null return (1159, 1217). This does not promote the row under P122; other live behavior remains missing.

## Verification limits

Windows-specific overlay code is compiled but cannot run in this Linux environment. No desktop, real user profile, notification or window test is run outside the existing sandbox/headless harness. No PR was opened.

## Assumptions

- Prioritize user-visible ambient spawning/motion/reward/sound gaps within the line cap; keep the ledger stub rather than assert full WPF coverage.
- Gold rings are an intentional rendering shortcut to obey the no-Effect rule on looping FX surfaces.
- Suppressed lucky announcements use the persisted SuppressPerkNotifications setting; XP and sparkles remain active.

- Final SDK: .NET 10.0.402. Channel installation initially hit a discovery 403 but completed via the builds endpoint; no package or credential workaround was used.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release --no-restore`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release --no-restore`: exit 0, 0 errors.
- Final sequential `dotnet build <project> -c Release --no-restore` sweep: all 13 test projects named in build.yml exited 0, 0 errors: Core, Core.Settings, Core.Content; Avalonia, Avalonia.Snapshot, Avalonia.Language, Avalonia.CatalogueStartup, Avalonia.Migration, Avalonia.Scheduler, Avalonia.Report, Avalonia.Consent, Avalonia.GeneralSettings; ConditioningControlPanel.Tests. WPF test build used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; compiled only, not run.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: final exit 0; 3972 passed, 0 failed, 5 pre-existing Vosk model-dependent skips. New bubble parity cases all pass. No tests weakened or disabled.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~BubbleOverlayTests|FullyQualifiedName~ChaosLiveBubbleTests|FullyQualifiedName~HapticsHeadTests'`: exit 1; 12 passed, 7 failed. Every failure is glyphTypeface creation (Inter/Segoe UI), including the existing press/card test; all new bubble tests passed. ChaosLiveBubbleTests matches no class on this base, so the real ChaosRunHostTests class was run separately.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~ChaosRunHostTests`: exit 1; 1 passed, 13 failed. Failures include Segoe UI font creation and missing overlays after StartRun catches startup exceptions; these host interactions remain unvalidated here. A font-capable runner must run the UI tests before promotion.
- Fail-before-fix proof: removing only the repeat-pop guard made `Trigger_pop_fires_payload_and_pays_once_even_when_reentered` fail (exit 1, expected 1 payload, actual 2); automatically restored the guard; the final focused run passes this test.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (429 XAML references checked). Initial attempts without temporary-file write permission failed; rerun with write access passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127, script absent on the pinned base; numbered WPF/Rows steps were added manually.
- `git diff --check`: exit 0. No live desktop, audio device, Windows overlay or notification validation was performed; only headless/sandbox tests ran. Other test assemblies were compiled, not executed.
