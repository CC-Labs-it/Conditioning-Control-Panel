# Avalonia performance parity

Numbers come from `--perf-bench <out.json>` (`CCP.Avalonia/PerfBench.cs`, `ConditioningControlPanel/PerfBench.cs`), run on the real windowing platform, never headless.
Decision and threshold: `docs/avalonia-decisions.md` (2026-10-23). A row passes when the Avalonia median is ≤ WPF median × 1.10 + 20 ms
(memory: ≤ WPF × 1.10), both heads measured back to back on the same `windows-latest` runner. Linux is information only. A row
with no WPF number is a gap, not a pass.

## What is measured

- **Startup**: a `Stopwatch` started on the first line of `Main`, stopped when the render thread has drawn the first frame after
  the shell's `Opened`. The shell is built straight after `SetupWithoutStarting`: no splash, no account/session seeding, no
  launcher routing. WPF starts the clock on the first line of its custom `Main`, runs its normal service startup,
  then shows the real main window directly. It uses `CompositionTarget.Rendering`, sampling after that Render-priority
  pass returns. Splash, launcher routing and modal onboarding are bypassed only under the flag; the WPF startup ladder
  is held and the sandbox represents an onboarded offline user. `startup_since_process_ms` adds the runtime's own start-up (process start time to the same frame).
  **Startup is informational, not a gate:** the scopes differ (WPF includes its full service init, Avalonia does not),
  so `perf-compare.py` reports both startup rows with verdict `info` and never fails on them.
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

CI: `perf-bench-linux` (xvfb, artifact `perf-bench-linux`) and `perf-bench-windows` (artifact `perf-bench-windows`)
in `.github/workflows/build.yml`. Windows publishes both heads as they ship (Avalonia win-x64 profile, WPF ReadyToRun single-file),
and runs five Avalonia/WPF pairs sequentially on the same runner, with a fresh `CCP_USERDATA_DIR` per process.
WPF requires that absolute sandbox path (exit 2 otherwise). The job is report-only until three consecutive stable
runs have been reviewed; no automatic promotion is implied. Each process has a four-minute outer timeout, in
addition to each head's 30-second frame timeout. The WPF missing-tab negative run must exit 3 without JSON.

Compare the downloaded reports (Python stdlib only):

```sh
python3 .github/scripts/perf-compare.py --self-test
python3 .github/scripts/perf-compare.py --avalonia 'perf-results/avalonia-*.json' --wpf 'perf-results/wpf-*.json'
```

The script exits 1 for a regression, missing reports, missing/extra tabs in either head or across runs, or invalid
measurements. It takes each metric's median across runs, including per-tab median and p95, and prints Markdown.
Tab keys are the shared `NavCheck.TabPanels` names. WPF's legacy `ExclusivesTab` routes through `ShowTab("exclusives")`
to the current lazy Premium page, whose visibility is checked. No absent tab is treated as a successful switch.

## Results

Paste `perf-results/comparison.md` here, then add the date, commit and artifact URL to the ledger. The format
below matches the script exactly; it expands to two rows per tab. Units are ms except `working_set_mb` (MiB).
Until Windows runs on GitHub, explicit pending values keep the unmeasured WPF baseline visible.

| metric | Windows WPF | Windows Avalonia | limit | verdict |
|---|---:|---:|---:|---|
| startup_ms | pending Windows CI | pending Windows CI | pending baseline | info |
| startup_since_process_ms | pending Windows CI | pending Windows CI | pending baseline | info |
| working_set_mb | pending Windows CI | pending Windows CI | pending baseline | gap |
| SettingsTab median_ms | pending Windows CI | pending Windows CI | pending baseline | gap |
| SettingsTab p95_ms | pending Windows CI | pending Windows CI | pending baseline | gap |

## Ledger

One line per run: date, machine/runner, commit, artifact or evidence path, headline numbers.

- 2026-10-23, CI ubuntu-latest under xvfb (job perf-bench-linux, run 39869706826, snapshot 10ae4e753), Release, sandboxed profile:
  startup 12860 ms, working set 909 MB, tab switch median 108-237 ms on 23 tabs, **ExclusivesTab 801 ms median / 821 ms p95**.
  Artifact `perf-bench-linux`.
