#!/usr/bin/env python3
"""Diff the WPF and Avalonia UI Automation trees per view.

usage: uia-parity.py <parity.html> <wpf-dir> <avalonia-dir> <out-dir>

Both dirs hold <view>.json from .github/wpf-uia (FlaUI on Windows) and CCP.Avalonia --uia-dump
(headless peers): {head, view, window, title, nodes:[{depth,type,id,name,cls,enabled,offscreen,bounds}]}.
Per view it lists AutomationIds present in WPF but missing in Avalonia, extra ids, label (Name)
differences on a shared id, enabled-state differences on a shared id, and visible labels one head
shows and the other does not. Each view is tagged with the parity rows that own the tab views it
shows (Avalonia `tabs`) or its dialog's window type; shell-only views stay untagged. Writes parity-diff.md and parity-diff.json. Never fails on a diff.
"""
import json
import os
import re
import sys
from collections import Counter

import avalonia_docs

CAP = 40


def stems(cell):
    return [t.rstrip("/").split("/")[-1].split(".")[0] for t in re.findall(r"`([^`]+)`", cell)]


def parity_rows(path):
    out = []
    for r in avalonia_docs.rows(open(path, encoding="utf-8").read(), "id"):
        cells = list(r.values())
        if len(cells) < 4:
            continue
        out.append((cells[0].strip("`"), stems(cells[1]), stems(cells[2])))
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


# The shell hosts every tab, so its type names no row: tag a view by the tab views it shows, or by its
# own window type for a dialog. The row whose columns START with that type wins (both columns beat one),
# then the row listing the fewest files.
SHELL = {"MainShellWindow", "MainWindow", "Window"}


def row_for(rows, wdoc, adoc):
    names = [t for t in (adoc or {}).get("tabs", [])] + [d["window"] for d in (adoc, wdoc) if d]
    found = []
    for n in names:
        if n in SHELL:
            continue
        ranked = sorted((-(w[:1] == [n]) - (a[:1] == [n]), len(w) + len(a), rid)
                        for rid, w, a in rows if n in w or n in a)
        rid = ranked[0][2] if ranked else ""
        if rid and rid not in found:
            found.append(rid)
    return ", ".join(found)


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
    # UIA sees a WPF window's title, never its type; Avalonia keys an opened window by type. Pair them
    # through the shared title so a startup card or dialog lands under the Avalonia type name.
    for key, w in list(wv.items()):
        title = w["title"].strip()
        if key in av or not title or not key.endswith("-" + title):
            continue
        prefix = key[:-len(title)]
        match = [k for k, a in av.items() if k == prefix + a["window"] and a["title"].strip() == title]
        if len(match) == 1 and match[0] not in wv:
            wv[match[0]] = dict(w, view=match[0])
            del wv[key]
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
