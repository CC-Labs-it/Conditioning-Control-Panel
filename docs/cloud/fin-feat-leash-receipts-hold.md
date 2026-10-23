# fin-feat-leash-receipts-hold

- Add the WPF hold tick: `chaos/countdown_tick.mp3`, master volume times 0.10, output suppression, mod override and shipped-content fallback, existing `LeashSfxRules`, and the shared one-shot backend. The Core flow emits seconds 1 through 4; second 5 asks instead.
- Add portal `Deactivated` handling alongside `Activated`, session/shortcut filtering, duplicate-event suppression, release on unbind/error, and a hold clock independent of XWayland key state. Retain the portal binding while its key is held, including after the first press stops effects. Ignore synthetic XWayland releases during portal holds and avoid releasing another listener's hold during idle portal cleanup.
- Match WPF confirmation hover/pressed backgrounds and retain its foreground/border colors and keyboard swallowing. Add regression coverage that Yes closes a locked punishment before cutting through the existing path.
- Keep one ledger row with status `stub`; the remaining holder paths depend on `feat-leash` drawer cards. No new surface was introduced: reuse the existing hold ring, confirmation window, punishment window and cut/task panic path.
- No trunk code imported or existing files overwritten. Read-only `parity/trunk` reference inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; changes adapt the frozen WPF reference at base `db079bf91474c77a5ea67ec306f6337a7de44b83` into this head.

## WPF audit at the pinned base

Read `Controls/Leash/LeashHoldRing.cs`, `LeashCutConfirmWindow.cs`, `LeashHolderCard.cs`, `LeashFx.cs`, `MainWindow/MainWindow.Leash.cs` and `Services/Leash/AppLeashTaskHost.cs` under `ConditioningControlPanel`, plus the shared Core receipt and hold rules. The old ledger's WPF `Services/Leash/LeashReceipts.cs` path has moved to Core. The task host has no separate receipt/hold behavior.

Gaps found before this unit:

1. The Core flow already produced whole-second ticks, but the shell discarded them. Implemented playback with WPF timing, gain and suppression rules.
2. Confirmation buttons lacked the WPF purple No hover and pink Yes hover. Implemented through the existing Fluent button resource mechanism.
3. Portal activation only ran panic; no release signal or hold entry existed. Implemented while bound, including release cleanup and retaining the binding during a hold.
4. The old row claimed Yes did not close the punishment window. Already implemented on this base: `CutLeashAsync` calls `LeashTaskHost.EndTask`, which calls `LeashPunishWindow.CloseNow`. Added an explicit locked-window regression assertion.
5. `LeashSentList.For` has no live holder caller. WPF `LeashHolderCard.Render` calls `SentSection`; this head has no holder card. Remains dependent on `feat-leash` holder drawer cards.
6. WPF holder Let go calls `LeashCutConfirmWindow.Confirm` from `LeashHolderCard.Head`. This head has neither that card nor its action. Remains dependent on the same feature; do not add an unused API or a partial substitute card.
7. The portal remains unbound for native Wayland focus outside effects by the existing platform decision. Idle native Wayland hold parity is therefore still limited; X11/Windows hold paths retain their current availability.

The holder callers are live in WPF, so P122 does not apply. Evidence commands against the pinned checkout:

```sh
rg -n 'SentSection|LeashCutConfirmWindow.Confirm' ConditioningControlPanel/Controls/Leash/LeashHolderCard.cs
rg -n 'LeashSentList.For|LeashCutConfirmWindow.Confirm|class LeashHolderCard' CCP.Avalonia
```

The first finds the WPF receipt and Let go callers; the second has no matches. The row cannot honestly become `wired`.

## Verification limits

All commands use .NET 10.0.100 and Release configuration. Test commands set `DiffEngine_Disabled=true`.

| Command/check | Result |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | Exit 0, 0 errors. |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | Exit 0, 0 errors. |
| `dotnet build <test-project> -c Release` for every Linux test csproj in `.github/workflows/build.yml` | All passed, 0 errors: CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, Avalonia.Tests, Core.Content, Core.Settings and Core.Tests. Scheduler's first attempt exited 1 with MSB4166 (child node exited); retry with `-m:1` passed. |
| `bash .github/scripts/core-guards.sh` | Exit 0: all Core guards passed; 429 XAML references checked. The first read-only sandbox invocation could not create temporary files; rerun with filesystem access passed. |
| `dotnet test Tests/CCP.Core.Tests -c Release` | Exit 0: 3553 passed, 5 existing Vosk tests skipped, 0 failed. The Vosk model is unavailable; no tests were edited or weakened. |
| `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~LeashHoldTests\|FullyQualifiedName~LeashHoldAudioTests\|FullyQualifiedName~PortalLeashHoldTests'` | Exit 1: 4 passed, 2 failed during shell construction at the known `Could not create glyphTypeface` error for Segoe UI Black. Those two end-to-end shell tests compiled but their hold assertions could not run. No further shell runs attempted. |
| Final `dotnet build Tests/CCP.Avalonia.Tests -c Release -m:1` and filtered audio/portal/X11-route/receipt tests without shell construction | Build exit 0, 0 errors; test exit 0: 4 passed, 0 failed. |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | Exit 0, 554 existing warnings, 0 errors. Initial multi-worker attempt failed with MSB4166; single-worker retry passed. Windows/WPF code compiled but not run. |
| Regression proof with one fix removed at a time | Both mutation builds exited 0. Removing audio playback made LeashHoldAudioTests exit 1 (`Assert.Single`: empty). Removing portal release made PortalLeashHoldTests exit 1 (`IsHeld` stayed true). Restored the fixes afterward. These are the limits of local red/green proof; shell assertions remain font-blocked. |

Windows input and real compositor/audio hardware behavior cannot be exercised on this Linux headless host. Render-all and live desktop checks were not run because of the font limitation. The supervisor must run the full focused shell tests on GitHub with fonts available. Tests use the existing isolated `CCP_USERDATA_DIR` profile and memory-only secret store; no real desktop, notification service, key grab or audio device is opened by the new tests.

## Assumptions

- Preserve the effects-only portal binding decision in `docs/avalonia-decisions.md`; this unit adds hold/release inside that binding rather than globally consuming an idle key.
- Keep holder-card work in `feat-leash`; adding the uncalled Confirm API alone would not make the holder behavior reachable.
- Keep the existing punishment-close implementation and test it instead of duplicating teardown.
