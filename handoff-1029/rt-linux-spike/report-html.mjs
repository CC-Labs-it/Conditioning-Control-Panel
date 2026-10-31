// rt-linux-spike/report-html.mjs - the report as one self-contained HTML page (inline CSS light/dark, a table, an inline
// SVG bar chart of fps mean / worst 1 s / worst frame per run per GPU, the raw numbers in
// <script type="application/json" id="rt-bench">). make-report.mjs writes <results dir>/report.html with it; several
// result dirs (one per GPU) combine into one page + one markdown summary:
//   node report-html.mjs --out <base> [--notes notes.md] <results dir> [<results dir> ...]   -> <base>.html, <base>.md
import { readFileSync, readdirSync, writeFileSync, existsSync } from 'node:fs';
import { join, basename } from 'node:path';
import { fileURLToPath } from 'node:url';

export const BAR = { mean: 57, worst1s: 50, maxFrameMs: 100 };

/** Owner bar (B2 survey 2026-10-29): fps mean >= 57, worst 1 s >= 50, no frame over 100 ms, hash matches Node. */
export function verdict(r) {
  if (r.failed) return 'FAIL (' + r.failed + ')';
  const x = r.race || {};
  const why = [];
  if (!(x.fpsMean >= BAR.mean)) why.push('mean ' + x.fpsMean);
  if (!(x.worst1sFps >= BAR.worst1s)) why.push('worst 1 s ' + x.worst1sFps);
  if (x.hitches100 > 0) why.push(x.hitches100 + ' frames > 100 ms');
  if (r.hashes && r.hashes.match === false) why.push('course hash differs');
  if (x.tier && x.tier !== 'high') why.push('tier ' + x.tier);
  return why.length ? 'BELOW BAR: ' + why.join(', ') : 'MEETS BAR';
}

const readJson = (p) => { try { return existsSync(p) ? JSON.parse(readFileSync(p, 'utf8')) : null; } catch (e) { return null; } };

/** One results dir -> { dir, gpu, runs: [{ run, gpu, engine, backend, window, mean, worst1s, maxFrameMs, ... }] }. */
export function loadDir(dir) {
  const raw = readdirSync(join(dir, 'runs')).filter((f) => f.endsWith('.json')).sort()
    .map((f) => readJson(join(dir, 'runs', f)) || { run: f, failed: 'unreadable' });
  // the GPU the box rendered on, from the first unmasked ANGLE string (WebKitGTK masks it as "Apple GPU");
  // <dir>/gpu.txt overrides when no Chromium-family run reached WebGL2
  const angle = raw.map((r) => r.caps && r.caps.webgl2 && r.caps.webgl2.renderer).find((s) => typeof s === 'string' && s.startsWith('ANGLE ('));
  const m = angle && /^ANGLE \([^,]+, (.+?), (?:OpenGL|Vulkan|ANGLE)/.exec(angle);
  const gpu = existsSync(join(dir, 'gpu.txt')) ? readFileSync(join(dir, 'gpu.txt'), 'utf8').trim() : (m ? m[1] : basename(dir));
  const runs = raw.map((r) => {
    const i = readJson(join(dir, 'engine', r.run + '.info.json')) || {};
    const x = r.race || {};
    const engine = i.webkit ? 'WebKitGTK ' + i.webkit + (i.flatpak ? ' flatpak' : '') + (i.gdkBackend ? ' GDK=' + i.gdkBackend : '')
      + (i.env && i.env.WEBKIT_DISABLE_DMABUF_RENDERER ? ' no-dmabuf' : '')
      : i.electron ? 'Electron ' + i.electron + ' ozone=' + (i.ozone || 'auto') : (r.caps && r.caps.ua ? r.caps.ua.replace(/^.*\) /, '').slice(0, 50) : '-');
    return { run: r.run, gpu, engine, backend: x.backend || null, tier: x.tier || null,
      window: r.caps ? r.caps.w + 'x' + r.caps.h + ' dpr ' + Math.round(r.caps.dpr * 100) / 100 : null,
      webgl2: r.caps && r.caps.webgl2 ? r.caps.webgl2.renderer : null, webgpuAdapter: r.caps ? r.caps.webgpuAdapter || null : null,
      mean: x.fpsMean ?? null, low1pct: x.low1pctFps ?? null, worst1s: x.worst1sFps ?? null, maxFrameMs: x.worstGapMs ?? null,
      over50: x.hitches50 ?? null, over100: x.hitches100 ?? null, garageS: r.garageS ?? null, warmS: r.warmS ?? null,
      hashOk: r.hashes ? r.hashes.match : null, failed: r.failed || null, verdict: verdict(r), pass: verdict(r) === 'MEETS BAR' };
  });
  return { dir: basename(dir), gpu, system: existsSync(join(dir, 'system.txt')) ? readFileSync(join(dir, 'system.txt'), 'utf8').trim() : '', runs };
}

const esc = (s) => String(s ?? '-').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

function chart(rows) {
  const W = 960, L = 330, ROW = 34, top = 34, half = (W - L - 40) / 2;
  const fpsMax = Math.max(60, ...rows.map((r) => r.mean || 0)), msMax = Math.max(120, ...rows.map((r) => r.maxFrameMs || 0));
  const fx = (v) => L + (v / fpsMax) * half, mx = (v) => L + half + 30 + (v / msMax) * (half - 20);
  const H = top + rows.length * ROW + 10;
  const out = [`<svg viewBox="0 0 ${W} ${H}" width="100%" role="img" aria-label="fps mean, worst second and worst frame per run" xmlns="http://www.w3.org/2000/svg">`,
    `<text x="${L}" y="14" class="ax">fps: mean (bar) / worst 1 s (thin); bar 57 / 50</text>`,
    `<text x="${L + half + 30}" y="14" class="ax">worst frame ms; bar 100</text>`];
  for (const [v, cls] of [[BAR.mean, 'ref'], [BAR.worst1s, 'ref2']]) out.push(`<line x1="${fx(v)}" x2="${fx(v)}" y1="20" y2="${H - 6}" class="${cls}"/>`);
  out.push(`<line x1="${mx(BAR.maxFrameMs)}" x2="${mx(BAR.maxFrameMs)}" y1="20" y2="${H - 6}" class="ref"/>`);
  rows.forEach((r, k) => {
    const y = top + k * ROW;
    out.push(`<text x="${L - 8}" y="${y + 15}" class="lb" text-anchor="end">${esc(r.gpu.split(" (")[0].slice(0, 24))} · ${esc(r.run)}</text>`);
    if (r.mean == null) { out.push(`<text x="${L + 4}" y="${y + 15}" class="fail">${esc((r.failed || 'no data').slice(0, 70))}</text>`); return; }
    out.push(`<rect x="${L}" y="${y + 2}" width="${fx(r.mean) - L}" height="14" class="${r.pass ? 'ok' : 'bad'}"/>`,
      `<text x="${fx(r.mean) + 4}" y="${y + 13}" class="v">${r.mean}</text>`,
      `<rect x="${L}" y="${y + 19}" width="${fx(r.worst1s || 0) - L}" height="7" class="w1"/>`,
      `<text x="${fx(r.worst1s || 0) + 4}" y="${y + 27}" class="v">${r.worst1s}</text>`,
      `<rect x="${L + half + 30}" y="${y + 2}" width="${mx(r.maxFrameMs || 0) - L - half - 30}" height="14" class="${(r.maxFrameMs || 0) > 100 ? 'bad' : 'ms'}"/>`,
      `<text x="${mx(r.maxFrameMs || 0) + 4}" y="${y + 13}" class="v">${r.maxFrameMs}</text>`);
  });
  out.push('</svg>');
  return out.join('\n');
}

export function renderHtml(sets, { title = 'Racing Thoughts 3D on Linux - bench report', notes = '' } = {}) {
  const rows = sets.flatMap((s) => s.runs);
  const cols = ['gpu', 'run', 'engine', 'backend', 'window', 'mean', 'low1pct', 'worst1s', 'maxFrameMs', 'over50', 'over100', 'garageS', 'warmS', 'hashOk', 'verdict'];
  const head = ['GPU', 'run', 'engine', 'backend', 'window (css px)', 'fps mean', '1% low', 'worst 1 s', 'worst frame ms', '>50 ms', '>100 ms', 'garage s', 'warm s', 'hash', 'verdict'];
  const table = ['<table><thead><tr>' + head.map((h) => `<th>${h}</th>`).join('') + '</tr></thead><tbody>',
    ...rows.map((r) => '<tr class="' + (r.pass ? 'pass' : 'nopass') + '">' + cols.map((c) => `<td>${esc(r[c] === true ? 'yes' : r[c] === false ? 'NO' : r[c])}</td>`).join('') + '</tr>'),
    '</tbody></table>'].join('\n');
  const data = JSON.stringify({ generated: new Date().toISOString(), bar: BAR, sets }, null, 1).replace(/</g, '\\u003c');
  return `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>${esc(title)}</title>
<style>
:root{--bg:#fff;--fg:#1b1b1f;--mut:#666;--line:#ddd;--ok:#2e8b57;--bad:#c0392b;--w1:#7f8c8d;--ms:#5b6ee1;--th:#f3f3f6}
@media (prefers-color-scheme:dark){:root{--bg:#141418;--fg:#e8e8ee;--mut:#9a9aa8;--line:#33333d;--ok:#3fbf7f;--bad:#e5604f;--w1:#8a9aa0;--ms:#8392ff;--th:#1f1f26}}
body{background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,sans-serif;margin:24px;max-width:1400px}
h1{font-size:20px}h2{font-size:16px;margin-top:28px}table{border-collapse:collapse;font-size:12px;width:100%}
th,td{border:1px solid var(--line);padding:3px 6px;text-align:left;vertical-align:top}th{background:var(--th)}
tr.pass td:last-child{color:var(--ok);font-weight:600}tr.nopass td:last-child{color:var(--bad)}
pre{white-space:pre-wrap;background:var(--th);padding:10px;border:1px solid var(--line);font-size:12px}
svg .ax,svg .lb,svg .v{fill:var(--fg);font-size:11px}svg .fail{fill:var(--bad);font-size:11px}svg .ok{fill:var(--ok)}svg .bad{fill:var(--bad)}
svg .w1{fill:var(--w1)}svg .ms{fill:var(--ms)}svg .ref{stroke:var(--fg);stroke-dasharray:4 3}svg .ref2{stroke:var(--mut);stroke-dasharray:2 3}
.mut{color:var(--mut)}
</style></head><body>
<h1>${esc(title)}</h1>
<p class="mut">Bar (B2, owner): fps mean &ge; ${BAR.mean}, worst 1 s &ge; ${BAR.worst1s}, no frame over ${BAR.maxFrameMs} ms, course hash = Node. Full look pinned (quality high, render scale 1) unless the run ends in -auto. vsync on.</p>
<h2>Chart</h2>
${chart(rows)}
<h2>Runs</h2>
${table}
${notes ? '<h2>Notes</h2>\n<pre>' + esc(notes) + '</pre>' : ''}
${sets.map((s) => `<h2>Box (${esc(s.gpu)}, ${esc(s.dir)})</h2>\n<pre>${esc(s.system)}</pre>`).join('\n')}
<script type="application/json" id="rt-bench">${data}</script>
</body></html>
`;
}

export function renderMd(sets, { title = 'Racing Thoughts 3D on Linux - bench report', notes = '' } = {}) {
  const L = ['# ' + title, '', 'Bar: fps mean >= 57, worst 1 s >= 50, no frame over 100 ms, course hash = Node. Per-dir detail: each dir\'s report.md.', '',
    '| GPU | run | engine | backend | window | fps mean | worst 1 s | worst frame ms | >100 ms | hash | verdict |', '|' + ' --- |'.repeat(11)];
  for (const r of sets.flatMap((s) => s.runs)) {
    L.push('| ' + [r.gpu, r.run, r.engine, r.backend, r.window, r.mean, r.worst1s, r.maxFrameMs, r.over100, r.hashOk == null ? '-' : r.hashOk ? 'yes' : 'NO', r.verdict]
      .map((v) => String(v ?? '-').replace(/\|/g, '/')).join(' | ') + ' |');
  }
  if (notes) L.push('', notes.trim());
  return L.join('\n') + '\n';
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const a = process.argv.slice(2), get = (k) => { const i = a.indexOf(k); return i >= 0 ? a.splice(i, 2)[1] : null; };
  const out = get('--out'), notesFile = get('--notes');
  if (!out || !a.length) { console.error('usage: node report-html.mjs --out <base> [--notes notes.md] <results dir>...'); process.exit(2); }
  const sets = a.map(loadDir), notes = notesFile ? readFileSync(notesFile, 'utf8') : '';
  writeFileSync(out + '.html', renderHtml(sets, { notes }));
  writeFileSync(out + '.md', renderMd(sets, { notes }));
  console.log('wrote ' + out + '.html and ' + out + '.md: ' + sets.reduce((n, s) => n + s.runs.length, 0) + ' runs');
}
