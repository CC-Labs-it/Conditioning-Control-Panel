# Answers to Mich's 7.2 questions (CB, 2026-10-31)

Replies to the question list of 2026-10-31, same order. Where you asked for a choice, the answer below is
our recommendation; the last section asks whether it works for you and your agents.

## 1. A1 and the 420 PRs

**When A1 lands.** `review/mich-1025-merge` is at 950aedaa13. Every finding of the block-preview review is
fixed (#3120, #3123, #3125) or was fixed by the merge; findings in `handoff-1029/A1-REVIEW-FINDINGS.md`.
The one gate left is a full Avalonia suite run on that tip. The board has the fast-forward of `port/7.2` on
Nov 2-3, right before the Windows 7.2.0 pre-release. That date stands.

**#3140 and #3149 first: yes.** Order:

1. We take #3140 (main sync 9, merge commit, never squash) into `review/mich-1025-merge` after a read of the
   23 conflict resolutions and the compat commit (safety, Scrolller, updater and sync files get a line-by-line
   read; the rest is trusted). This is the one PR exempt from the 600-line cap, like the A1 freeze itself.
2. #3149 (port docs as HTML) lands on top the same way.
3. The full Avalonia suite runs once on the result.
4. `port/7.2` is fast-forwarded to it.
5. You re-target the ~420 PRs ONCE, straight onto `port/7.2`. Do not re-target onto the reconcile branch in
   between; one move is enough.

Until step 4, keep stacking on #3149 as you do now.

**How we review ~420 PRs.** Option 3 with one exception:

- A PR that touches a safety, Scrolller, updater, sync, settings or payment path gets a full read from us
  (panic, Lockdown, leash, voice commands, `ScrolllerWebGate`, `UpdateService`, `ProfileSync`, AppSettings
  migration, anything under Chaster or Prizes). Put a `review:safety` label on those yourself; we also filter
  by path.
- Everything else: two reviews already passed (Codex Cloud critical + green CI), so we spot-check about one
  in ten and merge in area batches. Batches land in the order shell, views, windows, sync rows, so a broken
  batch is easy to bisect.
- A PR we merge is merged as a merge commit onto `port/7.2`; nothing is rebased on our side.

**Closing the old drafts.** Yes, the owner runs the close script; agents are refused mass closes. We need
the replaced-by list in the repo first: push `recut-triage-20261031.html` (and a plain `old-pr,new-pr,sha`
CSV next to it) into `handoff-1031/` on a branch off the reconcile line. We write the script from the CSV
and the owner runs it right after the fast-forward, so every close comment can point at the landed commit.

## 2. Decisions from the owner

**B1, B2, B3 decision files exist** and are final (owner rounds closed on 2026-10-29 and 10-30):

| File | What it decides |
|---|---|
| `handoff-1029/B1-SPARKLES-DECISIONS.md` | Sparkles by place: every game pays through `/v2/sparkles/claim`, the host builds the `sparkles-receipt` slip, a receipt only raises the wallet. LIVE on main and on the server (`SPARKLES_PLACES=on`). |
| `handoff-1029/B2-RT-LINUX-DECISIONS.md` | Racing Thoughts on Linux: our own pinned Electron fetched on the first Linux race, CCP-owned window, panic ends it by pid, WebGL2 default until your numbers say otherwise, labelled fallbacks WebKitGTK then browser, "My files" only on Linux. Already on `origin/mich/rt-linux-spike`. |
| `handoff-1029/B3-SHOP-DECISIONS.md` | The shop: one 3D Prize Counter, Sparkles only, no refunds, no gifts, weekly stock, desktop door only for 7.2.0 (web and phone doors stay off). |

Only B2 was pushed so far; B1 and B3 sat in the owner's checkout. They go up with this reply on branch
`docs/72-answers-1031` off `review/mich-1025-merge`: the three DECISIONS files at the
`handoff-1029/` paths above, this file in `handoff-1031/`. Read them from there.

One scope note for A5: the Sparkles UI on Avalonia is the slip plus the wallet chip, nothing else; the
`sparkles-slip.js` module is copied byte for byte per web tree (`SparklesSlipParityTests` pins it). The shop
on Linux is NOT in the 7.2 preview (desktop WPF only); do not start a Linux shop lane.

**A2 is done.** Racing Thoughts 3D merged into WPF main on 2026-10-29 night: #3119 (the merge, which is also
the Glassroad freeze), #3122 (the tree ships), #3126 (every door opens Glassroad, the 2D race has no door),
#3132 (Prime only, solo included). Your #3140 already carries all of it into the reconcile line, so A5 waits
on A1 only. The web tree moved to `Assets/web/glassroad` on your side: keep that, and remember
`sync-arcademy.yml` on cclabs-web still hard-codes `Resources/web`, so that vendor path is part of A5.

**Linux test kit: already sent** (2026-10-29 evening, second half of `MICH-72-NOTE.md`). It is on
`origin/mich/rt-linux-spike`, folder `handoff-1029/rt-linux-spike/`:

```
git fetch origin mich/rt-linux-spike && git checkout mich/rt-linux-spike
cd handoff-1029/rt-linux-spike && bash run.sh
```

About 25 minutes, no sudo. It benches Threshold and GOONWORKS in a pinned Electron, WebKitGTK and your
browser, and `make-report.mjs` fills `report-template.md`. Run it on the RTX 4080 and again on the Radeon
iGPU; WebGL2 first, and WebGPU in WebKitGTK as a third row if you can switch it on. The scripts were only
syntax-checked, never run on Linux: a first-run bug is likely, tell us and we fix it the same day. Bar:
mean 57+ fps, worst second 50+, no frame over 100 ms. Send the HTML report by pushing it to that branch.

## 3. Confirmations

**Windows installer, switch-ready, not shipped (#3432): yes, that is the shape.** Conditions, all of which
you describe as met: `installer.iss` and `build-installer.bat` byte-identical to main (the files carry CRLF
and a BOM on the csproj side; keep them), the Avalonia installer compiled as a CI artifact only, an asset
name the WPF updater never matches, and the contract test on main's updater values. One addition: the test
must also pin that no release asset of the Avalonia build is ever uploaded as Latest; the Linux preview goes
out as a pre-release that is never marked Latest (the WPF updater reads Latest).

**Settings migration (#3495).** No real user file. Nobody's `settings.json` leaves their
machine, including the owner's (it carries auth tokens, Chaster tokens, the email hash). What you get instead:
a scrubbed sample made from a clean sandbox profile on the 7.2.0 pre-release (every token replaced by a
marked dummy, PatreonTier set by hand, display name "qa"), sent privately, kept local-only, never committed.
We produce it after the pre-release is cut.

**600-line cap: agreed**, with one condition. Generated Verify snapshots, verbatim copies and byte-identical
restores do not count; hand-written lines do. The PR description must say which files are generated or
copied, so the reviewer can skip them on purpose. A PR whose hand-written part passes 600 still splits.

**Parity ledger (#3433): one row per feature is right.** We do not edit `docs/avalonia-parity.html` on our
side; the port side owns it. Our records stay in `handoff-*/` on the WPF side. "Verified" only from tester
reports or the live safety checks, as you wrote.

**Flatpak (#3257).** Publish the bundle (`.flatpak`) as an asset of a GitHub PRE-RELEASE on
this repo, tag `linux-preview-<version>`, never marked Latest, next to the tarball. No OSTree repo for the
preview; a hosted Flatpak repo is a decision for after tester feedback. Keep the headless smoke test in CI
and leave WPE out until a tester asks for it.

## 4. CI and process

**CI load.** 25 at once is too many while the 7.2.0 pre-release runs through the same queue
this week. Rule from now on: 8 concurrent runs during the owner's day (08:00-24:00 CET), 25 between 00:00
and 08:00 CET. Cancel-in-progress on your own branches stays on. Release lane runs (`release/*`,
`feat/72-prerelease`, `main`) take priority; if you see one queued behind yours, cancel yours.

**Known flakes.** Yes, we see both:

- `BackRoomClipDealTests.WarmPool_RunsSeveralDownloadsAtOnce` reads Ready 23 vs 24 under full-suite load and
  is 3/3 green alone (recorded on 2026-10-18). It did not reappear on the merged A1 tip, so it is load, not
  a port change. Leave it.
- `ContentHitTestTests.ContentButtonsReceiveThePointerAtTheirCentre` fails on the avalonia-windows job and
  was pre-existing at the A1 base (`A1-REVIEW-FINDINGS.md`, R6). The diagnostics in your PR are welcome; if
  they show a DPI or render-scale cause, fix the test, not the control.

#3677 (five flaky headless tests, test-only changes) goes in the first batch after the fast-forward.

**Testers.** Same people as the Windows pre-release: the #tier-2-subscribers channel, one post
per pre-release with the Linux tarball and Flatpak links and a link to `docs/avalonia-test-plan.html`.
Reports go into a thread under that post; anything reproducible becomes a ccp-bugs issue with the label
`linux-preview`. The port side reads the thread and moves "Verified" rows from the reports.

**Main syncs: only when main moves.** Main is frozen at aa9918833 on purpose: the 7.2.0 pre-release is
being cut on the trunk `feat/72-prerelease` (tip 6e0a0cd764 on 2026-10-31: kart stats, toys, the guide
card, Rain V2 behind a compile-time switch) and lands on main as one merge. The standing rule is one
main -> `port/7.2` catch-up merge per release, so: no weekly PR, one sync PR after each release merge
(the 7.2.0 one is days away), and a hotfix sync only if a safety fix lands on main between releases.

## Open items on our side

- Full Avalonia suite on 950aedaa13, then #3140 + #3149 in, then the fast-forward (Nov 2-3).
- The close script, once your triage CSV is in the repo.
- A scrubbed settings fixture after the 7.2.0 pre-release cut.
- A fix for any first-run bug in the Linux kit.

## Does this work for you?

The choices above (review mode and the safety label, the single re-target onto `port/7.2`, the scrubbed
settings fixture, the Flatpak pre-release, the CI windows, the tester channel, syncs per release) are our
recommendations, not the only workable shapes. Tell us if any of them does not fit how you or your agents work,
with the shape you would prefer, and we settle it in one round. Silence means we go with what is written.

CB, 2026-10-31.
