- Extend the existing shared ambient bubble host; read Trigger Bubbles settings and route ambient pops to existing effect surfaces, with a four-trigger cap and harmless treat expiry.
- Keep heavy effects out of the input handler, wait up to 90 seconds for rain, and recheck stop/video gates. Register bubble-host in PanicSurfaces and SafetyCritical; invalidate queued deliveries on stop.
- Port Natasha's red-bubble hold state to Core and wire Chaster booking; the Avalonia input window plus pointer capture acts as the press catcher.
- Add timed pink/spiral holds without rewriting settings and extend BubbleCount's overlay pinning seam to Windows.
- Reference: fetched parity/trunk at a4ee52d40cd347d288dcf7fdaa8445b79f8e5038. No trunk files imported or overwritten; implementation adapted from the checked-in WPF reference. Related requested sync7 commits: 33ae795c7, c52e7f6d8, 1feb49131 (remaining host consumers require follow-up parity checks).

## Verification limits

Verification results are recorded below after the final checks. Windows native calls are compiled only. Headless tests cannot prove desktop input-region behavior, compositor stacking, multi-monitor DPI, media output or real Chaster network booking. No real desktop/profile was used.

## Assumptions and remaining parity

- Reuse Avalonia's input-region bubble window and pointer capture instead of creating WPF's separate low-level-hook press-catcher window.
- FloatUp remains the host's supported motion. Brain Drain v2 ownership, variant art/labels, Natasha halo/arc polish, whisper, HT link and bouncing-text payload one-shots remain follow-up work; unsupported payloads log a warning. Chaos run payload firing is intentionally unchanged.
- Windows pinning reasserts HWND_TOPMOST through the existing Win32 overlay seam, matching WPF; X11 sibling stacking stays unchanged.

## Check results

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia -c Release`: final exit 0, 0 errors. An earlier overlapping build hit an Avalonia PDB lock; sequential rerun passed.
- All Linux test projects listed in `.github/workflows/build.yml` compiled (exit 0): Core, Core.Settings, Core.Content; Avalonia, Avalonia.CatalogueStartup, Avalonia.Consent, Avalonia.GeneralSettings, Avalonia.Language, Avalonia.Migration, Avalonia.Report, Avalonia.Scheduler, Avalonia.Snapshot.
- WPF test project cross-build with `EnableWindowsTargeting=true` and `ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 warnings, 0 errors. Windows runtime behavior not run.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: first run 3963 passed / 5 skipped. After expiry coverage, one run had an unrelated DtrhRemoteMediaTests random selection failure; isolated rerun passed. Final full `--no-build` run: exit 0, 3964 passed / 5 Vosk tests skipped / 3969 total. No tests were weakened or disabled.
- Core filter `AmbientPayloadRunnerTests|DtrhRemoteMediaTests`: exit 0, 20 passed.
- Head filter `BubblePayloadHostTests|PanicSurfacesTests.EveryIdIsUniqueAndEverySafetyCriticalIdIsRegistered|PanicSurfacesTests.EveryStarterIsRegisteredOrAllowlisted`: final exit 0, 5 passed. Test bootstrap was corrected to be independent of ordering.
- Wider head filter including BubbleOverlayTests: exit 1, 5 passed / 1 pre-existing font failure (`Could not create glyphTypeface`, Inter Bold). No real desktop was used.
- Fail-proof: temporarily removed `b.BeginResist(Field.Clock())`; RedPressIsCaughtAndReleasePopsOnlyOnce failed with Assert.True expected true / actual false. Restored production and the case passed in the final filtered run.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. The base has no `.github/scripts/test-plan-coverage.sh`; numbered tester steps were added for each new row.
- Shared-host scope: this does not implement BubbleCount's complete optional effects-hold/reconciliation lifecycle. It extends the native pinning entry point its existing bubble caller already uses. That remaining integration, cross-surface bubble/flash stacking on a desktop, and full WPF visual parity require follow-up.
