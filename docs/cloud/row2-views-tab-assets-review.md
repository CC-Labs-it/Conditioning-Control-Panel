# PR 3741 review scope

PR: https://github.com/CC-Labs-it/Conditioning-Control-Panel/pull/3741

Review base: `5d48af3101eacdfb6183295932ada7e97a77fdd7` (`avalonia-port/cloud-r72-companion-picker-host`).
Prerequisite base: `74ac6ce3d5e0ae082c2871c00e864943e15aa2ad`.

Use the review's separate-prerequisite option. Review companion-picker-host in [PR #3214](https://github.com/CC-Labs-it/Conditioning-Control-Panel/pull/3214) and review this lane from the pinned review base above. The live GitHub PR base is companion-picker-host. Keep its code, tests and snapshots intact.

| Range | Review unit | Added | Removed | Text lines |
| --- | --- | ---: | ---: | ---: |
| `74ac6ce3d..5d48af310` | Companion prerequisite, separate review | 879 | 115 | 994 |
| `5d48af310..HEAD` | This lane, including this scope note | 217 | 11 | 228 |
| `74ac6ce3d..HEAD` | Combined stack | 1095 | 125 | 1220 |

The combined stack exceeds 600 text lines. No combined-range exemption is claimed. The 600-line lane cap is checked against the pinned PR parent; a gate fixed to 74ac6ce3d is measuring the combined stack and needs the review scope changed before it can pass. Removing the prerequisite to satisfy that gate would undo required WPF parity.

Reproduce with `git diff --stat 5d48af310..HEAD`, `git diff --numstat 5d48af310..HEAD`, and the same commands with `74ac6ce3d`. Count added plus removed text lines; report binary snapshot changes separately. Do not substitute one range's count for the other.

Parent preservation: companion implementation, companion tests and the snapshot project match 5d48af310 byte for byte. The parent hosts the card (WPF ModManagerDialog.xaml:191), restarts drain (WPF CompanionService.cs:322-334), and fades portraits (WPF CompanionPickerCard.xaml.cs:284-290).

The assets and companion ledger rows remain stub for their listed WPF gaps. Existing evidence stays intact; this note changes review scope only. Core, WPF, product code, tests, snapshot baselines and localization are unchanged by this scope correction. Validation: three Release builds pass; Core 4636 passed with 5 existing skips; both snapshots pass; focused lane tests 17/17 and parent tests 12/12 pass. The broad Avalonia run has 24 passes and 25 missing-font failures, so those shell behavior checks remain unverified in that run.
