# sync7-conversion-a — conversion Inbox slice

- WPF sources read: `a54ce18f5`, `401d13aed`, `63391d41a`, `23416827e`, `b244fda78`, `ab4276f69`, and their current files on `avalonia-port/main-20261015-compat`.
- Moved the existing pure `ConversionRules` and `InviteClaimNotice` to CCP.Core; WPF consumes the same copies. No trunk code imported (`parity/trunk` fetched read-only).
- Profile load/focus reads remember a lower server tier without shortening grace, apply additive free-member/paid-until/post-grant fields, notify the existing Daily Gift service, and trigger the Inbox pass. Focus reads use WPF's 20-second throttle and discard replies after an account change.
- Added WPF's neutral passive notice card, once-per-lapse warnings/closed/reconnect rows, once-ever free-member hello, and one-time invite claim/subscription notices. Quiet startup/session routing remains on StartupLadder. The free-member notice reads its second gift at open time. Notice cards are registered with panic.
- Main-sync rows 90 and 107 ported; rows 85 and 86 remain partial, and rows 98/99 remain pending. The matching Inbox parity row stays **stub**.

## Verification limits

- `bash .github/scripts/core-guards.sh`: passed after hydrating the WPF source needed by the XAML namespace guard.
- `dotnet test Tests/CCP.Core.Tests`: passed, 2883 passed / 6 skipped / 2889 total. Existing skips: one BoardTiles frame-budget test and five Vosk model tests; no tests were changed or disabled to obtain this result.
- `dotnet build` CCP.Core, CCP.Avalonia, and all eleven Linux test projects named in `.github/workflows/build.yml`: passed. Isolated test-project builds used `-p:BuildProjectReferences=false` after their dependencies had compiled.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: passed (561 existing warnings, zero errors). WPF app/tests cross-compiled only; Windows execution is unavailable here.
- `dotnet test Tests/CCP.Avalonia.Tests --filter 'FullyQualifiedName~AccountSeed|FullyQualifiedName~ConversionInboxTests|FullyQualifiedName~PanicSurfacesTests|FullyQualifiedName~InboxFlyoutTests|FullyQualifiedName~ShellCopyPortTests|FullyQualifiedName~IconGuardTests'`: 66 passed, zero failed/skipped. The UI fixtures use the shipped font registration (also repaired the three existing related fixtures without changing assertions), and the profile fixture supplies an in-memory auth-token store.
- `dotnet run --project CCP.Avalonia --no-build -- --render-all /tmp/ccp-worker4-render`: passed, 226/226 views. `InboxNoticeCard.png` (400x780, renderer-forced height) was inspected; heading, body and both actions are visible. This is headless output, not a WPF side-by-side.
- Authenticated regression probe: temporarily compiled out the profile ingestion call; `AccountSeedConversionTests.ProfileLoadRemembersLowerTierAndAdditiveMemberFieldsWithoutRemovingGrace` failed (expected ServerTierSeen 0, actual null; 1 failed). Restored source and rebuilt the final implementation. No regression-probe edits remain.
- All builds/tests use .NET SDK 10.0.402, `DOTNET_PROCESSOR_COUNT=2`, `-m:1`, and both MSBuild copy-hardlink properties. Focused test runs also set `DiffEngine_Disabled=true`.
- No real account, external checkout, camera/microphone, Windows execution, or WPF visual comparison was exercised. Test and render profiles are sandboxes.

## Follow-up: sync7-conversion-a-next

- Row 85: ingest heartbeat `effective_tier` into the entitlement-reading path. This slice covers profile load/focus only; the existing Core heartbeat intentionally discards its response body.
- Row 86: port the long-absence companion greeting after the greeting host exists. The Avalonia tube still explicitly omits WPF's greeting timer/speech partial. Lapse Inbox rows are present.
- Rows 98/99: preserve/exclude the five conversion fields when cloud backup is designed. The head intentionally has no backup/restore implementation (`MainShellWindow.CloudBackup.cs`, existing owner decision), so no upload path was invented here.
- The existing StartupLadder still lacks the complete WPF game/modal/launcher quiet-world inputs; conversion notices reuse its current shared routing.
