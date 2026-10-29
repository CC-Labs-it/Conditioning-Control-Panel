// rt-linux-spike/make-report.mjs - <results dir>/runs/*.json -> <results dir>/report.md (one table, a verdict per run).
// node make-report.mjs <results dir> [report-template.md]
// Owner bar (B2 survey 2026-10-29): the FULL desktop look at 60 fps, small dips OK = fps mean >= 57,
// worst 1 s >= 50, no frame over 100 ms while racing, and the course hash matches Node (online at launch).
import { readFileSync, readdirSync, writeFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';

const OUT = process.argv[2];
const TPL = process.argv[3];
const runs = readdirSync(join(OUT, 'runs')).filter((f) => f.endsWith('.json')).sort()
  .map((f) => { try { return JSON.parse(readFileSync(join(OUT, 'runs', f), 'utf8')); } catch (e) { return { run: f, failed: 'unreadable' }; } });
const info = (run) => { const p = join(OUT, 'engine', run + '.info.json'); try { return existsSync(p) ? JSON.parse(readFileSync(p, 'utf8')) : null; } catch (e) { return null; } };

function verdict(r) {
  if (r.failed) return 'FAIL (' + r.failed + ')';
  const x = r.race || {};
  const why = [];
  if (!(x.fpsMean >= 57)) why.push('mean ' + x.fpsMean);
  if (!(x.worst1sFps >= 50)) why.push('worst 1 s ' + x.worst1sFps);
  if (x.hitches100 > 0) why.push(x.hitches100 + ' frames > 100 ms');
  if (r.hashes && r.hashes.match === false) why.push('course hash differs');
  if (x.tier && x.tier !== 'high') why.push('tier ' + x.tier);
  return why.length ? 'BELOW BAR: ' + why.join(', ') : 'MEETS BAR';
}
const n = (v) => (v == null || v === '' ? '-' : v);
const rows = runs.map((r) => {
  const x = r.race || {}, i = info(r.run) || {};
  const engine = i.webkit ? 'WebKitGTK ' + i.webkit + (i.flatpak ? ' (flatpak)' : '') + ' accel=' + n(i.hardwareAcceleration)
    : i.electron ? 'Electron ' + i.electron + ' / Chrome ' + i.chrome : (r.caps && r.caps.ua ? r.caps.ua.replace(/^.*\) /, '').slice(0, 60) : '-');
  return '| ' + [r.run, engine, n(x.backend), n(x.tier) + '/' + n(x.renderScale), n(r.garageS), n(r.warmS), n(x.fpsMean), n(x.low1pctFps), n(x.worst1sFps),
    n(x.hitches50) + '/' + n(x.hitches100), n(x.worstGapMs), n(r.countdown && r.countdown.worstGapMs), n(x.cpuMedianMs) + '/' + n(x.cpuP95Ms),
    n(r.memory && r.memory.peakMB), n(r.audio && r.audio.driftMsPerMin), n(r.audio && r.audio.outputLatencyMs),
    r.hashes ? (r.hashes.match ? 'yes' : 'NO') : '-', (r.errors || []).length, verdict(r)].join(' | ') + ' |';
});

const lines = [];
lines.push('# Racing Thoughts 3D on Linux - spike report', '');
lines.push('Generated ' + new Date().toISOString() + ' by rt-linux-spike/make-report.mjs. Full look pinned before boot (quality=high, renderScale 1)');
lines.push('unless the run id ends in -auto. vsync on: fps is what a player sees, capped at the display rate.', '');
lines.push('## The box', '', '```', existsSync(join(OUT, 'system.txt')) ? readFileSync(join(OUT, 'system.txt'), 'utf8').trim() : '(no system.txt)', '```', '');
lines.push('## Runs', '');
lines.push('| run | engine | backend | tier/scale | garage s | warm s | fps mean | 1% low | worst 1 s | hitch >50/>100 ms | worst frame ms | countdown worst ms | cpu ms med/p95 | mem peak MB | audio drift ms/min | out latency ms | hash ok | errors | verdict |');
lines.push('|' + ' --- |'.repeat(19));
lines.push(...rows, '');
lines.push('## Details', '');
for (const r of runs) {
  const i = info(r.run);
  lines.push('### ' + r.run, '');
  if (r.failed) lines.push('- failed: ' + r.failed + (r.lastFailure ? ' / ' + JSON.stringify(r.lastFailure).slice(0, 400) : ''));
  if (r.caps) {
    lines.push('- webgpu: ' + r.caps.webgpu + (r.caps.webgpuAdapter ? ' ' + JSON.stringify(r.caps.webgpuAdapter) : ''));
    lines.push('- webgl2: ' + JSON.stringify(r.caps.webgl2));
    lines.push('- My files decode: image ' + r.caps.blobImageDecode + ', video ' + JSON.stringify(r.caps.canPlay));
    lines.push('- window ' + r.caps.w + 'x' + r.caps.h + ' dpr ' + r.caps.dpr + ', gamepad api ' + r.caps.gamepadApi);
  }
  if (r.race) lines.push('- tiers seen ' + JSON.stringify(r.race.tiersSeen) + ', scales seen ' + JSON.stringify(r.race.scalesSeen) + ', draw calls ' + n(r.race.drawCalls) + ', gpu ' + n(r.race.gpu));
  if (r.audio) lines.push('- audio ' + JSON.stringify(r.audio));
  if (r.memory) lines.push('- memory ' + JSON.stringify(r.memory));
  if (r.scrolllerCheck) lines.push('- Scrolller block check: scrolller.com ' + r.scrolllerCheck.scrolller + ', control site ' + r.scrolllerCheck.control + (i && i.scrolllerFilter ? ' (filter: ' + i.scrolllerFilter + ')' : ''));
  if (r.gamepads && r.gamepads.length) lines.push('- gamepads ' + JSON.stringify(r.gamepads));
  if (i) lines.push('- engine ' + JSON.stringify(i).slice(0, 700));
  if (r.errors && r.errors.length) lines.push('- errors: ' + r.errors.slice(0, 8).map((e) => '`' + e.replace(/`/g, "'") + '`').join('; '));
  lines.push('');
}
if (TPL && existsSync(TPL)) lines.push(readFileSync(TPL, 'utf8'));
writeFileSync(join(OUT, 'report.md'), lines.join('\n'));
console.log('report.md written: ' + runs.length + ' runs');
