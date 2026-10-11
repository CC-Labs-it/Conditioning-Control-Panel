#!/usr/bin/env python3
"""Diff the WPF and Avalonia UI Automation trees per view.

usage: uia-parity.py <parity.md> <wpf-dir> <avalonia-dir> <out-dir>

Both dirs hold <view>.json from .github/wpf-uia (FlaUI on Windows) and CCP.Avalonia --uia-dump
(headless peers): {head, view, window, title, nodes:[{depth,type,id,name,cls,enabled,offscreen,bounds}]}.
Per view it lists AutomationIds present in WPF but missing in Avalonia, extra ids, label (Name)
differences on a shared id, enabled-state differences on a shared id, and visible labels one head
shows and the other does not. Each view is tagged with the parity row whose WPF/Avalonia file
column names its window type. Writes parity-diff.md and parity-diff.json. Never fails on a diff.
"""
import json
import os
import re
import sys
from collections import Counter

CAP = 40


def stems(cell):
    return [t.rstrip("/").split("/")[-1].split(".")[0] for t in re.findall(r"`([^`]+)`", cell)]


def parity_rows(path):
    out = []
    for line in open(path, encoding="utf-8"):
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if not line.startswith("| ") or len(cells) < 4 or cells[0] in ("id", "---") or set(cells[0]) <= set("-"):
            continue
        out.append((cells[0].strip("`"), set(stems(cells[1])), set(stems(cells[2]))))
    return out


def load(d):
    views = {}
    for f in sorted(os.listdir(d)) if os.path.isdir(d) else []:
        if f.endswith(".json") and not f.startswith("_"):
            doc = json.load(open(os.path.join(d, f), encoding="utf-8"))
            views[doc["view"]] = doc
    return views


def shown(doc):
    return [n for n in doc["nodes"] if not n.get("offscreen")]


def ids(nodes):
    out = {}
    for n in nodes:
        i = n.get("id") or ""
        if i and not i.startswith("PART_") and not i.isdigit() and i not in out:
            out[i] = n
    return out


def labels(nodes):
    return Counter(n["name"].strip() for n in nodes if n.get("name", "").strip() and not n.get("id"))


def row_for(rows, wdoc, adoc):
    for rid, wst, ast in rows:
        if (wdoc and wdoc["window"] in wst) or (adoc and adoc["window"] in ast):
            return rid
    return ""


def diff(w, a):
    wn, an = shown(w), shown(a)
    wi, ai = ids(wn), ids(an)
    both = sorted(set(wi) & set(ai))
    wl, al = labels(wn), labels(an)
    return {
        "missing_ids": [f"{i} ({wi[i]['type']} '{wi[i]['name']}')" for i in sorted(set(wi) - set(ai))],
        "extra_ids": [f"{i} ({ai[i]['type']} '{ai[i]['name']}')" for i in sorted(set(ai) - set(wi))],
        "label_diffs": [f"{i}: WPF '{wi[i]['name']}' vs Avalonia '{ai[i]['name']}'" for i in both
                        if wi[i]["name"].strip() != ai[i]["name"].strip()],
        "enabled_diffs": [f"{i}: WPF {wi[i]['enabled']} vs Avalonia {ai[i]['enabled']}" for i in both
                          if wi[i]["enabled"] != ai[i]["enabled"]],
        "labels_missing": sorted((wl - al).elements()),
        "labels_extra": sorted((al - wl).elements()),
        "counts": {"wpf_nodes": len(wn), "avalonia_nodes": len(an), "shared_ids": len(both)},
    }


def main(parity, wdir, adir, out):
    os.makedirs(out, exist_ok=True)
    rows = parity_rows(parity)
    wv, av = load(wdir), load(adir)
    result = {}
    for key in sorted(set(wv) | set(av)):
        w, a = wv.get(key), av.get(key)
        entry = {"row": row_for(rows, w, a), "wpf_window": w and w["window"], "avalonia_window": a and a["window"]}
        if w and a:
            entry.update(diff(w, a))
        else:
            entry["unpaired"] = "wpf only" if w else "avalonia only"
        result[key] = entry
    json.dump(result, open(os.path.join(out, "parity-diff.json"), "w", encoding="utf-8"), indent=1)

    md = ["# WPF vs Avalonia automation-tree diff", "",
          "Per view: ids in WPF missing from Avalonia, extra ids, Name/IsEnabled differences on a shared id, "
          "and unnamed-id labels only one head shows. Headless Avalonia reports nothing offscreen, so WPF "
          "offscreen nodes are dropped before comparing.", "",
          "| view | row | WPF window | Avalonia window | missing | extra | label diffs | enabled diffs | labels missing | labels extra |",
          "|---|---|---|---|---:|---:|---:|---:|---:|---:|"]
    for key, e in result.items():
        if "unpaired" in e:
            md.append(f"| {key} | {e['row']} | {e['wpf_window'] or '-'} | {e['avalonia_window'] or '-'} | {e['unpaired']} |||||| ")
            continue
        md.append(f"| {key} | {e['row']} | {e['wpf_window']} | {e['avalonia_window']} | {len(e['missing_ids'])} | "
                  f"{len(e['extra_ids'])} | {len(e['label_diffs'])} | {len(e['enabled_diffs'])} | "
                  f"{len(e['labels_missing'])} | {len(e['labels_extra'])} |")
    for key, e in result.items():
        if "unpaired" in e:
            continue
        md += ["", f"## {key}" + (f" (row `{e['row']}`)" if e["row"] else ""), f"counts: {e['counts']}"]
        for k in ("missing_ids", "extra_ids", "label_diffs", "enabled_diffs", "labels_missing", "labels_extra"):
            if e[k]:
                md.append(f"- **{k}** ({len(e[k])}): " + "; ".join(e[k][:CAP]) + (" ..." if len(e[k]) > CAP else ""))
    open(os.path.join(out, "parity-diff.md"), "w", encoding="utf-8").write("\n".join(md) + "\n")
    print(f"uia-parity: {len(result)} views, {sum('unpaired' not in e for e in result.values())} paired -> {out}")


if __name__ == "__main__":
    if len(sys.argv) != 5:
        sys.exit(__doc__)
    main(*sys.argv[1:])
