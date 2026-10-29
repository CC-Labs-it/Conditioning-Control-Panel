# 7.2 - note for Mich and Mich's agents (2026-10-29)

Hi Mich. The owner has set the 7.2 plan and asked us to bring you in. The whole plan is in `72-LANES.md`
(attached; same file at `handoff-1029/72-LANES.md` in the owner's checkout). The parts that touch your work:

## What 7.2 is

- Windows keeps WPF (7.2.0 from `main`). Linux gets a PREVIEW from the Avalonia head (`port/7.2`). Both run in
  parallel for now; the unification decision comes later.
- Racing Thoughts 3D (Glassroad) ships on Windows, Linux and the phone. The 2D race is retired.
- New for both heads: Racing Thoughts rooms in the Lobby + friends invite, Sparkles payouts for every game,
  a shop.

## Your drafts

- Your stack (768 open `avalonia-port/*` drafts, tip `a3d9e984fb` = `avalonia-port/perf-bench-avalonia`, Oct 25)
  is 1077 commits past `port/7.2` (merge base `be09ff13f`) and does not contain the 76 fix-wave commits on
  `port/7.2` (`bbe782591`). About 40 files conflict, mostly panic key, voice commands, Lockdown, AI commands,
  Scrolller gate/allowance, updater, WebHost and remote control.
- Plan: we freeze your tip as `review/mich-1025` (your branches are never rewritten), review it BY AREA
  (safety, Scrolller/data, shared-code effects on WPF, Linux packaging, game hosts, tests/stubs), merge
  `port/7.2` into it, fix in small PRs, then fast-forward `port/7.2` to the result. Your drafts then close with a
  pointer to the landed commit.
- On a conflict, our side wins on safety and Scrolller unless yours is strictly stricter.

## What we need from you

1. Is `a3d9e984fb` your latest tip? If you have unpushed work, push it to a branch and tell us the sha before we
   freeze, or keep it for after the landing.
2. After the landing: new work goes as <= 600-line PRs against `port/7.2`, not as a new long stack.
3. Any known-broken or stubbed areas you already know about (saves the review time).
4. Racing Thoughts on Linux: Avalonia's WebView is WebKitGTK there, and the game is WebGPU-first with a WebGL2
   fallback. We would like real-hardware numbers from your Linux box (boot time, fps on two courses, memory).
   A one-command script and a report template will follow if you are up for it.

Reply in #admin-chat or to the owner. Thanks.

## Update 2026-10-29 evening: the Linux race kit

Branch `mich/rt-linux-spike` (off `port/7.2`) carries `handoff-1029/rt-linux-spike/`. On your Linux box:

    git fetch origin mich/rt-linux-spike && git checkout mich/rt-linux-spike
    cd handoff-1029/rt-linux-spike && bash run.sh

About 25 minutes, no sudo. It measures Racing Thoughts 3D in a pinned Electron, WebKitGTK and your browser on
Threshold and GOONWORKS and writes a report. Send the report to the owner (or push it to the same branch). The
scripts were syntax-checked but never run on Linux, so a first-run bug is possible: tell us and we fix it. The
bar the owner set: mean 57+ fps, worst second 50+, no frame over 100 ms. Decisions: `handoff-1029/B2-RT-LINUX-DECISIONS.md`.
