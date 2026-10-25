# fin4-shell-possession

- Base verified at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`; the supplied snapshot initially had a different commit and was reset before implementation.
- Port the shell stage and target registry from the pinned WPF `MainWindow/MainWindow.Possession.cs`: passive ghost/rubble canvases, automatic leaf roles, innermost cards, hand-tag precedence, excluded/hidden subtree pruning, reserved names, scaled bounds, enabled/hit-test/placement gates, 24-label budget (nearest pointer, largest before pointer input), friendly display names, weak target identity, and cache invalidation/throttling.
- Restore WPF Role/Name/Exclude tags in the existing shell XAML; preserve the existing four-effect runtime and its panic registration.
- Add focused headless registry tests; use an injected clock to exercise cache timing without sleeps. Tests inherit the assembly's isolated `CCP_USERDATA_DIR` profile and memory-only SecretStore.
- Replace the duplicated historical ledger narrative with one honest `stub` row and update tester step 28.
- No trunk code imported. `git fetch origin parity/trunk` resolved to `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; WPF on the required base is the implementation source.

## WPF gap inventory

At the pinned base, the Avalonia shell partial had no members. WPF's 673-line partial supplied all stage/registry behavior listed above, plus a live attach/detach path to the director and exact takeover service gates. The base's Core director/deck/rules/audio/remember and four head effects were already present; none were connected to the live shell.

Remaining gaps after this unit (the ledger stays **stub**, never wired/verified):

1. Live `PossessionDirectorHead` and effect adapters; attach/detach to the Core director, Lockdown activation/deactivation/tick/restart lifecycle, intensity/photosafe/settings reactions, rung readout and pips. Core already owns cadence, concurrency, cooldown and restart rules; reuse it rather than introduce a second scheduler.
2. Attribution: approximately 400 ms ember charge (600 ms static photosafe alternative), possessed outline/tint, refcounted cursor ring, edge pulses, and release-all. WPF `POSSESSION.md` requires this before effects start. This unit deliberately does not run unattributed transform effects.
3. Pointer velocity and reactive events/tripwires: attach/detach input and settings hooks, route escape attempts into the director, preserve throttles and target ancestry matching.
4. Remaining 25 effects: crack, delete dialog, dissolve, dodge, doki dialog, drop, fall, ghost cursor, glitch portrait, glyphrot, melt, misroute, relabel, reorder doors, retitle, rewrite, room warp, scroll hijack, slider creep, steal card, swap, toast, toggle lie, typo, xp drain. Four transform effects already existed; they are still not reachable in a running lockdown.
5. Scene choreography and slot accounting through adapted scene effects; animated reassembly on normal exit (synchronous panic restoration already exists in the head runtime).
6. Warden knock/stare/leave/return, naming/rung/rules/restart/tripwire barks and shell shake; Core audio output-suppression hook and remember activation/window/escape wiring.
7. Exact usability checks for DTRH, Loom, Arcademy, Bureau, Goon, JustDrop, FYP, Intake and Emergency Exit takeovers. Lock Card is checked directly; visible owned windows conservatively pause new picks pending those head service seams. Ordinary video playback must eventually remain allowed, as WPF specifies.

The WPF attribution/events/warden/effects/scenes source alone is 8,074 lines. Completing those adapters and their behavioral tests alongside the host cannot fit this unit's 1,000-added-line cap. This is a partial host port, not a dependency-only NOOP or a P122 dead-caller claim. WPF has live callers through `InitializePossessionHost` and `App.Possession.AttachHost`.

## Assumptions

- Preserve WPF target selection and timing, including explicit Timer tags overriding the registry's name blocklist. The existing runtime's stronger off-limits safety gate is unchanged.
- Use Avalonia `Button` for WPF `ButtonBase` and `TextBox` for both WPF text/password boxes; Avalonia represents password entry with `TextBox.PasswordChar`.
- Conservatively pause behind visible owned surfaces until the precise takeover seams exist. No new transient effects or interactive surfaces are introduced; empty passive canvases need no panic teardown. Existing `PanicSurfaces` possession registration remains intact.

## Verification limits

All commands used .NET SDK 10.0.402 and Release configuration. Test runs used `DiffEngine_Disabled=true`.

| Command | Result |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | Exit 0. |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | Initial API errors corrected; final clean serial rebuild (`--no-restore -t:Rebuild -m:1 -nr:false`) exit 0, zero errors. |
| `dotnet build` for all 12 Linux test projects named in `.github/workflows/build.yml` | All ultimately exit 0. Includes Core, Core.Content, Core.Settings, Avalonia, CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot suites. Consent retried serially after an MSBuild worker exit. |
| `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-restore -m:1 -nr:false` | Final exit 0, 74 existing warnings, zero errors. An overlapping earlier build saw a stale app reference assembly; the clean serial rebuild resolved it. |
| `bash .github/scripts/core-guards.sh` | Exit 0; all guards passed. |
| `bash .github/scripts/test-plan-coverage.sh` | Exit 127: script absent on this base. Existing numbered step 28 updated. |
| `dotnet test Tests/CCP.Core.Tests -c Release` | Exit 0: 3,953 passed, five existing Vosk tests skipped, zero failed (3,958 total). No tests weakened or disabled by this change. |
| `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~PossessionHostTests&#124;FullyQualifiedName~PossessionRuntimeTests'` | Exit 1: all 12 fail during font creation, before behavioral assertions (`Could not create glyphTypeface`, Inter/Segoe UI). Includes seven new host tests and five existing runtime tests. Requires a working-font runner; no behavioral pass or fail-proof claimed. |
| WPF test build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | Restore completed, compilation attempted, then interrupted after sustained memory pressure and an executor disconnect. The wrapper recorded exit 0 after termination but there is no successful completion summary; treat this compilation as unverified. |
| `git diff --check` and staged personal-path check | Exit 0; no whitespace errors or prohibited personal paths in additions. |

An initial overlapping build also hit an output-file lock; final builds were serialized. Windows desktop behavior and WPF tests were not run. Render-all was not run; this is not a P122 promotion. Headless behavior still needs the supervisor's working-font runner. The row remains incomplete regardless of compile/test results.
