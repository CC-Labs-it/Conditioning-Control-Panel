# Avalonia performance parity

Numbers come from `--perf-bench <out.json>` (`CCP.Avalonia/PerfBench.cs`), run on the real windowing platform, never headless.
Decision and threshold: `docs/avalonia-decisions.md` (2026-10-23). A row passes when the Avalonia median is ≤ WPF median × 1.10 + 20 ms
(memory: ≤ WPF × 1.10), both heads measured back to back on the same `windows-latest` runner. Linux is information only. A row
with no WPF number is a gap, not a pass.

## What is measured

- **Startup**: a `Stopwatch` started on the first line of `Main`, stopped when the render thread has drawn the first frame after
  the shell's `Opened`. The shell is built straight after `SetupWithoutStarting`: no splash, no account/session seeding, no
  launcher routing. `startup_since_process_ms` adds the runtime's own start-up (process start time to the same frame).
- **Tab switch**: `ShowTab` (the call every rail handler makes) to the next rendered frame, for every panel in
  `NavCheck.TabPanels`, 21 cycles with the first discarded; median (middle pair averaged) and p95 (nearest rank) per tab.
- **Memory**: working set after the cycles and two `GC.Collect` + `WaitForPendingFinalizers` passes.
- Exit 0 with the JSON written; 3 when a tab is missing or does not show; 4 on a frame timeout (30 s).

JSON shape `ccp-perf-bench/1` (the WPF twin writes the same shape, `head: "wpf"`):

```json
{ "schema": "ccp-perf-bench/1", "head": "avalonia", "os": "...", "runtime": "...",
  "startup_ms": 0, "startup_since_process_ms": 0, "working_set_mb": 0, "cycles": 20,
  "tabs": [ { "tab": "SettingsTab", "median_ms": 0, "p95_ms": 0 } ] }
```

CI: job `perf-bench-linux` in `.github/workflows/build.yml` (xvfb, report-only, artifact `perf-bench-linux`).

## Results

| metric | Linux Avalonia | Windows Avalonia | Windows WPF | verdict |
|---|---|---|---|---|
| startup (ms) | see ledger | pending perf-bench-wpf-compare | pending perf-bench-wpf-compare | gap |
| tab switch median, worst tab (ms) | see ledger | pending perf-bench-wpf-compare | pending perf-bench-wpf-compare | gap |
| tab switch p95, worst tab (ms) | see ledger | pending perf-bench-wpf-compare | pending perf-bench-wpf-compare | gap |
| working set after GC (MB) | see ledger | pending perf-bench-wpf-compare | pending perf-bench-wpf-compare | gap |

## Ledger

One line per run: date, machine/runner, commit, artifact or evidence path, headline numbers.

- 2026-10-23, CI ubuntu-latest under xvfb (job perf-bench-linux, run 39869706826, snapshot 10ae4e753), Release, sandboxed profile:
  startup 12860 ms, working set 909 MB, tab switch median 108-237 ms on 23 tabs, **ExclusivesTab 801 ms median / 821 ms p95**.
  Artifact `perf-bench-linux`.
