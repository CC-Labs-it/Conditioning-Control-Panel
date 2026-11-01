# Chaos E-Stim overlay review checklist

Branch: `avalonia-port/cloud-r72-row2-chaos-estim-overlay`.
Review base: `74ac6ce3d5e0ae082c2871c00e864943e15aa2ad`.

- [x] Carry the inherited generated-snapshot size exemption into this review.
  Authority: commit `5d48af3101eacdfb6183295932ada7e97a77fdd7` records the supervisor decision that `Tests/CCP.Avalonia.Snapshot.Tests/Snapshots/release-content-mod-manager-rows.verified.txt` (+557/-60, 617 generated lines) is exempt from the 600-line limit.
  This review applies that same exemption to that same inherited file only. It grants no exemption for hand-written changes.
- [x] Preserve the generated text and PNG snapshots unchanged from `5d48af310`.
- [x] Report the raw diff as well as the adjusted count; do not claim the raw diff is below 600.
  Before this follow-up: +978/-122 = 1,100 text lines; excluding the 617 exempt lines leaves 483.
  Including this checklist: +995/-122 = 1,117 text lines; excluding the 617 exempt lines leaves 500, below 600.
- [x] Keep the parity rows `stub`: charged-pop producers, toy effects and video re-stacking remain missing.
- [x] Preserve the existing lifecycle test and its recorded fail-proof. This follow-up changes documentation only.

Binary snapshot changes inherited from the base are reported by Git separately from text line counts.
Verification: `git diff 74ac6ce3d HEAD --numstat`, `git diff 5d48af310 HEAD -- Tests/CCP.Avalonia.Snapshot.Tests/Snapshots/release-content-mod-manager-rows.verified.*`, `git diff --check`, Core guards and the Avalonia docs self-check.
