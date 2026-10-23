# fin-shell-login

- Compared the single `shell-login` ledger row and its cited WPF `MainWindow.Login.cs` at base `5409d25573d187d89e5d24378ae8d1014491bc81`. Also traced the live callees in WPF `ProfileSyncService.cs`, `ProgressionService.cs`, and `MainWindow.Patreon.cs`.
- Initial named gaps: stats, consent flags, companion progress, quest history, the first web-XP claim explanation, and the Discord provider-link sharing offer. All have an application caller in this change.
- Adapted WPF V2 lifetime max-merge, contiguous login-streak adoption, quest-date/history union (90 days, newest 400 log entries), admin quest/streak resets and acknowledgements, conditioning minutes, and companion level/XP comparison. Mobile quest totals remain display-only. Restored cloud achievements enter the local store without unlock popups.
- The first push omits unknown progress. Subsequent pushes echo only server-returned stat keys and adopted companion/minute fields; logout disarms them. Local consent choices are independent and authoritative, including explicit false revocations. The privacy controls save and push them; a cooldown refusal queues the existing nudge.
- Feature-day logs flush before a push and rebaseline after a cloud lift, so another device's history is not credited to today.
- Added WPF's once-per-install `web-xp` toast (exact English copy, success style, nine seconds, seen flag spent on the UI thread). Settled claims count toward lifetime XP without crediting quests. The existing shell notification host needs no new panic registration.
- Successful Discord links now call the existing owned Yes/No achievement-sharing offer; failed links do not. Existing game/launcher/hidden-panel Inbox routing and explicit opt-in remain in that shared caller.
- Connected existing quest ownership: stamp before logout, preserve same-account progress, clear a different owner's file before cloud load; explicit switches reset without generating quests until cloud reconciliation.
- No trunk code imported. Read-only import source fetched: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Adaptations above come from the pinned base's WPF reference, not a wholesale replacement of existing head/Core files.

## Remaining WPF gaps

The ledger stays `stub`. These are live WPF paths, so P122 does not apply:

- The complete WPF post-login orchestration: 500 ms token-settle delay, achievement-popup suppression around the background load/push, and explicit post-load skill-tree / quest / unlock redraws. This head starts its read immediately and retains its stricter read-before-write failure gate instead of WPF's forced sync after a failed load.
- Full sync-response handling beyond this unit: authoritative metadata (display name, OG/highest level, whitelist, rerolls, streak-fix balances), anti-cheat downward clamps, unauthorized-session recovery, backup reconciliation, and recap/reset notifications. The new merges do not claim to replace the entire WPF ProfileSyncService.
- WPF account-surface teardown/refresh beyond the existing quick-login refresh: purchase-counter invalidation (`V2Purchase` dependency), full Descent reset/migration offer/stage-drip wiring, and clearing/repainting all cached profile-viewer surfaces. The countdown cache is not the full WPF Descent service.

## Verification limits

- Required Linux builds: `dotnet build -c Release` passed for CCP.Core, CCP.Avalonia, and all 11 Linux test csproj files named in `.github/workflows/build.yml` (exit 0, zero errors each). Generated outputs share a temporary directory via `BaseOutputPath` to fit the cloud disk limit; no project files were changed for this. The final expanded Avalonia test compilation also passed (exit 0; 270 warnings including rebuilt dependencies, zero errors).
- With `DiffEngine_Disabled=true`: `dotnet test Tests/CCP.Core.Tests -c Release` passed (exit 0; 2,831 passed, 5 existing Vosk-model-dependent cases skipped, 2,836 total). The final `--filter FullyQualifiedName~FinShellLoginTests` run passed 15/15 (exit 0), including two additional day-log/reset cases added after the full run. Restoring the prior missing behavior produced 8 failures / 5 passes (exit 1); the implementation was restored before final verification. No test was weakened or disabled.
- Avalonia behavior tests cover the real provider-link continuation, consent controls/push, one-shot toast, and quest ownership. Compiled only: the supplied environment limitation says this checkout lacks the cloud font-test fix and the supervisor runs these on GitHub. No real desktop, provider consent, or user profile was used.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (shared `BaseOutputPath`): exit 0, 512 warnings, zero errors. WPF app and tests cross-compiled; Windows code was not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, "All Core guards passed" (424 XAML type references checked). `git diff --check` and a scan of added files for private paths/email: clean. No Vosk download or real OAuth, keyring, cloud server, Windows desktop, or render-all run was attempted.

## Assumptions

- The task authorizes the wider fields left for follow-up by the 2026-10-22 decision; unknown server fields still stay omitted rather than sending defaults.
- Consent revocations obey the existing sync cooldown; the existing nudge retries afterward. Goon Rich Presence and Discord achievement/level sharing remain local choices, as in WPF.
- The explicit 1,000-added-line limit takes precedence over completing the remaining broad orchestration and full-sync work; the ledger is intentionally honest about that remainder.
