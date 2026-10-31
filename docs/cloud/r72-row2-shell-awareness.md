# Shell awareness: shared preset notifications

Base: 884cbc282726dbb2f4b2bb4c2f21078c442e5b29.
Only shell-awareness advances. The row remains stub because the full feature is incomplete.
No WPF, Core, copy or language changes.

## Delivered path

Open Awareness, open an active preset, deactivate it in the detail dialog.
The card and advanced link refresh while the dialog is still open. Installation,
uninstallation and cloning use the existing Core event and settings mutations.

- WPF owns one publisher at ConditioningControlPanel/App.xaml.cs:1254,3230.
  Avalonia now owns it at CCP.Avalonia/App.axaml.cs:25; the tab uses it at
  CCP.Avalonia/Views/Tabs/AwarenessTabView.Presets.cs:28 and the dialog at
  CCP.Avalonia/Views/Dialogs/AwarenessPresetDetailDialog.axaml.cs:197.
- WPF subscribes and posts both refreshes in
  ConditioningControlPanel/MainWindow/MainWindow.Awareness.cs:102,114,124.
  Avalonia posts both at CCP.Avalonia/Views/Tabs/AwarenessTabView.Presets.cs:46.
  Visual attach/detach at AwarenessTabView.axaml.cs:98 bounds event retention;
  queued work ignores detached views. Reattach also rereads settings.
- Existing Core event emission stays unchanged at
  CCP.Core/Services/KeywordTriggerPresetService.cs:144,179,234.

## Base audit and part B

All items in the base row's missing list were checked before selecting this slice.

| Item | Evidence on base / remaining scope | WPF specification |
| --- | --- | --- |
| Recent app picker | Still absent: AwarenessTabView.axaml.cs:333; PR #3554 owns this work | MainWindow/MainWindow.Awareness.cs:575,622 |
| Last Detected / engine pulse | Static placeholder at AwarenessTabView.axaml:193; preference at AwarenessTabView.axaml.cs:149; PR #3605 owns runtime fire recording/status | MainWindow/MainWindow.Awareness.cs:144,375 |
| Demo credit | Comment only at Platform/KeywordTriggerHead.cs:183; no recording | Services/KeywordTriggerService.cs:393,426 |
| OCR / Linux keyboard | Windows-only source at Platform/KeywordTriggerHead.cs:28; OCR flag only at AwarenessTabView.axaml.cs:252 | MainWindow/MainWindow.Awareness.cs:443,480; Services/KeywordTriggerService.cs:939,1196 |
| Highlight/capture affinity | Settings only at AwarenessTabView.axaml.cs:273,281,411 | MainWindow/MainWindow.Awareness.cs:502,795 |
| Other actions/credit | Audio/comments only at Platform/KeywordTriggerHead.cs:165,174 | Services/KeywordTriggerService.cs:1339,1363 |
| Preset notifications | Previously separate publishers at AwarenessTabView.Presets.cs:27 and AwarenessPresetDetailDialog.axaml.cs:205; delivered above | MainWindow/MainWindow.Awareness.cs:102,114 |
| v2 observer/arbiter/ledger | Notes partly stale: privacy ledger already hosted at App.axaml.cs:36,503 on base; full observer, arbiter and delivered-keyword path still absent | App.xaml.cs:2853,2928,3114; Services/KeywordTriggerService.cs:1660 |

Already ported settings/editors: AwarenessTabView.axaml.cs:111,192,346,
WPF MainWindow/MainWindow.Awareness.cs:41,393,659.
Already ported comments: Platform/KeywordTriggerHead.cs:174,183 and
Platform/KeywordAvatarComment.cs:24,57, WPF Services/KeywordTriggerService.cs:1272,1548,1621.
Already ported mod visibility refresh: Views/Windows/MainShellWindow.ModSwitch.cs:180,
WPF MainWindow/MainWindow.xaml.cs:3230.
Already ported privacy/title routing: Views/AvatarTube/AvatarTubeWindow.Reactions.cs:32,
WPF AvatarTube/AvatarTubeWindow.Reactions.cs:46. No native Wayland title source.
Paths in the table are relative to CCP.Avalonia or ConditioningControlPanel as indicated.
Line numbers describing the base refer to 884cbc282; delivered references refer to this commit.

The larger OCR/actions/observer port exceeds this slice's 600-line budget and remains part B.
No work from #3554 or #3605 is copied, and their test removals are not carried forward.

## Verification

.NET SDK 10.0.403 installed with dotnet-install --channel 10.0 using the builds feed.

- dotnet build CCP.Core: 0 errors (50 existing warnings).
- dotnet build CCP.Avalonia: 0 errors (202 existing warnings).
- dotnet build Tests/CCP.Avalonia.Tests -m:1 -p:UseSharedCompilation=false:
  0 errors. A concurrent attempt hit a PDB lock; a retry lost an MSBuild worker;
  the sequential one-worker build passed.
- dotnet test Tests/CCP.Core.Tests: 4635 passed, 6 existing skips, 0 failed.
- Notification tests plus TestIsolationTests: 14 passed, 0 skipped, 0 failed,
  both before mutation and after restoring production code.
- Fail-proof: replacing the dialog's shared publisher with a private instance made
  DialogUninstallRefreshesAttachedCardsBeforeDialogCloses fail (expected border 1,
  actual 1.5). Removing the queued callback guard made
  DetachedTabIgnoresQueuedEventsAndReattachesOnce fail (card instance replaced).
  Both mutations were applied together; both tests failed, then sources were restored.
- The broader Awareness filter compiled, but execution hit an existing
  AwarenessLedgerPrivacyTests font failure (Consolas/Courier New glyphTypeface)
  and was terminated. That broader suite is compiled, not successfully run.
- bash .github/scripts/core-guards.sh: All Core guards passed.
- python3 .github/scripts/avalonia_docs.py: avalonia_docs self-check OK.
- No live desktop or native platform verification. No tests weakened or removed.
