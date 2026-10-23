- Share WPF's local credit rules in Core `SparklePointRewards`: 1 point per level, 1 per 100 lifetime bubble pops, every crossed boundary in a batch, settings save before publishing, and only the actual capped increase in the cue. Both WPF producers now call these rules through CoreSettings; logging uses Serilog.
- Wire the real Avalonia level loop (`ProgressionBank.Add`) and ambient-pop event (`App.SeedBubbleAchievements` -> `AchievementEngine.TrackBubblePopped`). Keep the season peak and earned-point statistics; persist bubble progress every ten single pops and at batch end, and retain the existing 1000-pop achievement rule.
- Add focused Core producer tests and `SparkleWalletTests.RealProducersCueTheShellWallet` for both level and ambient-pop paths, including a pop with an exhausted daily XP bucket. Mark `shell-sparkle-wallet` wired; the Visit -> Back Room gap stays.
- Trunk credit: read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Adapted reward publishing from `c2a1b89b5949db5cb38ceb4a0d1a35d68ac01184` and batch boundary accounting from `c7332fcb0401c29a0fce398664a1665372087531`; the WPF producer files already matched the base. Existing files were merged in place, not replaced.

## Verification limits

- Linux, .NET SDK 10.0.402; `DiffEngine_Disabled=true` for tests. Used the required base `61a934110bdba5d4e4a1c4442f4cbd38ee6f2190`.
- `dotnet build <project> -c Release`: exit 0 and 0 errors for every project below. The WPF test build additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` and compiled the Windows head as a dependency; Windows code/tests were **not run** on Linux. Build warnings remain.

  - `CCP.Core/CCP.Core.csproj`
  - `CCP.Avalonia/CCP.Avalonia.csproj`
  - `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`
  - `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj`
  - `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj`
  - `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj`
  - `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj`
  - `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj`
  - `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj`
  - `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj`
  - `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj`
  - `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj`
  - `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj`
  - `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj`

- `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~SparkleAwardProducerTests`: exit 0, **13 passed, 0 failed, 0 skipped**. Red before the fix: level loop expected balance 42, actual 40 (exit 1); without the bubble credit call, four milestone cases failed (exit 1). The subsequent fixed suite is green. Tests also cover actual capped amounts, the existing 1000-pop achievement, and progress flush timing.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build` after rebuilding the final tests: exit 0, **3352 passed, 0 failed, 5 skipped** (3357 total, 38 s). The five existing Vosk model tests auto-skip because the model is unavailable; its download is blocked in this environment. No tests were weakened or newly skipped.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.” `git diff --check`: exit 0.
- `CCP.Avalonia.Tests` compiled after the final test edits with 0 errors. Headless tests were **not run**, per the task's cloud font limitation; the cloud-test-fonts lane is absent from this snapshot. Supervisor command pending: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~SparkleWalletTests|FullyQualifiedName~BubbleOverlayTests|FullyQualifiedName~SyncPushTests|FullyQualifiedName~HeaderLevelLiveTests'` with `DiffEngine_Disabled=true`. The added cases use the existing dispatcher and CCP_USERDATA_DIR sandbox, fresh settings, and a stepped animation clock.
- No real desktop/user profile, live account, or Windows runtime validation. The remaining wallet Visit -> Back Room gap is unchanged: Back Room is not on this head.
