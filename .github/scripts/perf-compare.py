#!/usr/bin/env python3
"""Compare ccp-perf-bench/1 reports from the same Windows runner (stdlib only)."""
import argparse
import copy
import glob
import json
import math
import statistics
import sys


def number(value):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
        raise ValueError(f"invalid measurement: {value!r}")
    return value


def metrics(report, head):
    if report['schema'] != 'ccp-perf-bench/1' or report['head'] != head or report['cycles'] != 20:
        raise ValueError(f"expected {head} ccp-perf-bench/1 with 20 cycles")
    result = {key: number(report[key]) for key in
              ('startup_ms', 'startup_since_process_ms', 'working_set_mb')}
    tabs = report['tabs']
    if not tabs:
        raise ValueError('empty tab list')
    seen = set()
    for tab in tabs:
        key = tab['tab']
        if not isinstance(key, str) or not key or key in seen:
            raise ValueError(f"invalid/duplicate tab: {key!r}")
        seen.add(key)
        for stat in ('median_ms', 'p95_ms'):
            result[f'{key} {stat}'] = number(tab[stat])
    return result


def compare(avalonia, wpf):
    if not avalonia or not wpf:
        raise ValueError('both heads require reports; a missing baseline cannot pass')
    groups = [[metrics(report, head) for report in reports]
              for head, reports in (('avalonia', avalonia), ('wpf', wpf))]
    expected = set(groups[1][0])
    for group in groups:
        for run in group:
            if set(run) != expected:
                raise ValueError(f"missing or extra tabs/metrics: {sorted(set(run) ^ expected)}")
    rows = []
    for key in groups[1][0]:
        a, w = (statistics.median(run[key] for run in group) for group in groups)
        limit = w * 1.10 + (0 if key == 'working_set_mb' else 20)
        rows.append((key, w, a, limit, a <= limit))
    return rows


def markdown(rows):
    lines = ['| metric | Windows WPF | Windows Avalonia | limit | verdict |',
             '|---|---:|---:|---:|---|']
    for key, w, a, limit, passed in rows:
        lines.append(f'| {key} | {w:.2f} | {a:.2f} | {limit:.2f} | {"pass" if passed else "FAIL"} |')
    return '\n'.join(lines)


def load_reports(patterns):
    paths = []
    for pattern in patterns:
        matches = sorted(glob.glob(pattern))
        if not matches:
            raise ValueError(f'no reports match {pattern}')
        paths.extend(matches)
    if len(paths) != len(set(paths)):
        raise ValueError('duplicate report paths')
    reports = []
    for path in paths:
        with open(path, encoding='utf-8-sig') as source:
            reports.append(json.load(source))
    return reports


def self_test():
    baseline = dict(schema='ccp-perf-bench/1', head='wpf', cycles=20,
                    startup_ms=100, startup_since_process_ms=200, working_set_mb=100,
                    tabs=[dict(tab='SettingsTab', median_ms=100, p95_ms=100),
                          dict(tab='PresetsTab', median_ms=100, p95_ms=100)])
    good = dict(copy.deepcopy(baseline), head='avalonia')
    assert all(row[-1] for row in compare([good], [baseline]))
    for key in ('startup_ms', 'startup_since_process_ms', 'working_set_mb', 'median_ms', 'p95_ms'):
        bad = copy.deepcopy(good)
        target = bad['tabs'][0] if key in ('median_ms', 'p95_ms') else bad
        target[key] *= 1.5
        assert not all(row[-1] for row in compare([bad], [baseline])), key
    edge = copy.deepcopy(good)
    edge.update(startup_ms=130, working_set_mb=110)
    assert all(row[-1] for row in compare([edge], [baseline]))  # inclusive thresholds
    # Median across runs, not mean, and independent of tab order.
    outlier = dict(copy.deepcopy(good), startup_ms=10000)
    good['tabs'].reverse()
    assert all(row[-1] for row in compare([good, outlier, good], [baseline] * 3))
    for head in ('avalonia', 'wpf'):
        missing = copy.deepcopy(good if head == 'avalonia' else baseline)
        missing['tabs'].pop()
        try:
            compare([good, missing] if head == 'avalonia' else [good],
                    [baseline, missing] if head == 'wpf' else [baseline])
        except ValueError:
            pass
        else:
            raise AssertionError(f'{head}: missing tab passed')
    print('self-test: PASS (50% regressions, thresholds, medians, tab order and missing tabs)')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--avalonia', nargs='+')
    parser.add_argument('--wpf', nargs='+')
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    if args.self_test:
        self_test()
        return 0
    if not args.avalonia or not args.wpf:
        parser.error('--avalonia and --wpf reports are required')
    try:
        rows = compare(load_reports(args.avalonia), load_reports(args.wpf))
    except (OSError, ValueError, KeyError, TypeError) as error:
        print(f'perf-compare: FAIL: {error}', file=sys.stderr)
        return 1
    print(markdown(rows))
    return 0 if all(row[-1] for row in rows) else 1


if __name__ == '__main__':
    sys.exit(main())
