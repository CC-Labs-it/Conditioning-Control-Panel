import test from 'node:test';
import assert from 'node:assert/strict';
import { scanPose, scanClock, scanZones, createMainframeScan } from '../sim/mainframeScan.js';
import { selectTrack, allowsOnline, courseSongs } from '../track/selection.js';
import { songCharts } from '../host/bridge.js';
import { createState, S, RESPAWN } from '../sim/state.js';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';

const track = loadTrack(JSON.parse(readFileSync(new URL('../tracks/mainframe.json', import.meta.url)))), zone = scanZones(track)[0];
function setup(x = 0) {
  const events = [], scan = createMainframeScan(track, { emit: (name, data) => events.push({ name, ...data }) });
  const r = { i: 0, st: createState() }; r.st[S.s] = zone.s + 3; r.st[S.lateral] = x;
  return { scan, r, events };
}
test('course selection is bundled, with no Mainframe online admission before a server card exists', () => {
  assert.equal(selectTrack('?track=mainframe'), 'mainframe');
  for (const q of ['', '?track=../../private', '?track=https://other']) assert.equal(selectTrack(q), 'threshold');
  assert.equal(allowsOnline('mainframe'), false); assert.equal(allowsOnline('threshold'), true);
});

test('Mainframe never inherits dev or host songs through an absent or blank chart choice', () => {
  const songs = ['/host/bambi/chart.json'];
  for (const devHost of [false, true]) {
    for (const search of ['?track=mainframe', '?track=mainframe&chart', '?track=mainframe&chart=', '?track=mainframe&chart=%20%09']) {
      const charts = songCharts({ search, devHost, songs });
      assert.ok(charts.length > 0, 'the real song selector would otherwise supply defaults or whitespace');
      assert.deepEqual(courseSongs('mainframe', search, charts), [], search);
      assert.deepEqual(courseSongs('threshold', search, charts), charts, 'Threshold keeps its songs');
    }
  }
});

test('Mainframe preserves an explicit song choice and chart=0 silence', () => {
  const selected = 'https://glassroad-room.cclabs-race.workers.dev/song/chosen/chart.json?exp=9999999999999&sig=selected';
  const search = '?track=mainframe&chart=' + encodeURIComponent(selected);
  const charts = songCharts({ search, devHost: true, songs: ['/host/bambi/chart.json'] });
  assert.deepEqual(courseSongs('mainframe', search, charts), [selected]);
  assert.deepEqual(courseSongs('mainframe', '?track=mainframe', [selected]), [], 'an injected song list never implies a choice');
  const muted = '?track=mainframe&chart=0';
  assert.deepEqual(courseSongs('mainframe', muted, songCharts({ search: muted, devHost: true })), []);
});
test('scan phases forecast the sweep and keep an outside bypass for the whole cycle', () => {
  assert.equal(scanPose(zone, 0).phase, 'clear'); assert.equal(scanPose(zone, 2.5).phase, 'warn');
  assert.equal(scanPose(zone, 4.5).phase, 'sweep'); assert.equal(scanPose(zone, 6.1).phase, 'hold');
  assert.equal(scanZones(track).length, 6);
  for (const hazard of scanZones(track)) for (let t = 0; t < 8; t += 0.01) {
    const p = scanPose(hazard, t), width = track.frameAt(hazard.s).w;
    assert.ok(width / 2 - Math.abs(p.x) - hazard.width / 2 >= 2.99, 'each bank keeps a permanent bypass');
  }
});
test('one wall contact costs 0.5 seconds once until the kart leaves it', () => {
  const { scan, r, events } = setup();
  scan.update([r], 5); assert.equal(events.length, 1); assert.ok(Math.abs(r.st[S.stalled] - 0.5) < 1e-6);
  r.st[S.stalled] = 0.2; scan.update([r], 5.1);
  assert.equal(events.length, 1); assert.ok(r.st[S.stalled] < 0.21);
});
test('canal walls make the bank route useful while keeping their outer support safe', () => {
  for (const hazard of scanZones(track).slice(1, 3)) {
    const events = [], scan = createMainframeScan(track, { emit: (name, data) => events.push({ name, ...data }) });
    const r = { i: 0, st: createState() }; r.st[S.s] = hazard.s + 3; r.st[S.lateral] = 16;
    const activeS = (9 - hazard.offset + 16) % 16 / 2;
    scan.update([r], activeS); assert.equal(events.length, 0, 'upper bank bypass');
    r.st[S.lateral] = 0; scan.update([r], activeS + 0.01); assert.equal(events.length, 1, 'channel must dodge the active sweep');
  }
});
test('scan contact matches the rendered height above the curved canal, including airborne bank routes', () => {
  const zones = scanZones(track), canal = zones[1];
  assert.equal(zones[0].height, 5.5); assert.equal(canal.height, 15);
  for (const [hazard, x, h, hits] of [[zones[0], 0, 6, 0], [canal, 0, 6, 1], [canal, 0, 16, 0],
    [canal, 12, 8, 1], [canal, 12, 9, 0], [canal, 16, 0, 0]]) {
    const events = [], scan = createMainframeScan(track, { emit: (name, data) => events.push({ name, ...data }) });
    const racer = { i: 0, st: createState() };
    racer.st[S.s] = hazard.s + 3; racer.st[S.lateral] = x; racer.st[S.height] = h;
    const activeS = (11.9 - hazard.offset + 16) % 16 / 2;
    scan.update([racer], activeS);
    assert.equal(events.length, hits, JSON.stringify({ zone: hazard.id, x, h }));
  }
});
test('warning, outside bypass, high flight, rescue and finished racers never receive a hit', () => {
  for (const mode of ['warn', 'outside', 'high', 'rescue', 'finished']) {
    const { scan, r, events } = setup(mode === 'outside' ? 10 : 0);
    if (mode === 'high') r.st[S.height] = 6;
    if (mode === 'rescue') r.st[S.respawnPhase] = RESPAWN.GRACE;
    if (mode === 'finished') r.finished = true;
    scan.update([r], mode === 'warn' ? 3 : 5); assert.equal(events.length, 0, mode);
  }
});
test('swept contact catches a crossing between samples; teleport does not', () => {
  for (const teleport of [false, true]) {
    const { scan, r, events } = setup(); r.st[S.s] = zone.s - (teleport ? 30 : 2);
    scan.update([r], 5); r.st[S.s] = zone.s + zone.len + 2; scan.update([r], 5.1);
    assert.equal(events.length, teleport ? 0 : 1);
  }
});
test('song seek rearms through a whole warning bar; reset removes prior contacts', () => {
  const { scan, r, events } = setup(); scan.update([r], 0); scan.update([r], 5);
  assert.equal(scan.ready, false); assert.equal(events.length, 0);
  for (let t = 5.1; t <= 7.1; t += 0.1) scan.update([r], t);
  assert.equal(scan.ready, true); scan.reset(); scan.update([r], 5); assert.equal(events.length, 1);
});
test('scan uses audible song position and downbeat, or the paused race clock without a song', () => {
  assert.deepEqual(scanClock({ song: { bpm: 135, downbeatS: 0.4 }, songS: 3.4 }, 90), { timeS: 3, bpm: 135 });
  assert.deepEqual(scanClock(null, 5), { timeS: 5, bpm: 120 });
  assert.deepEqual(scanZones({ id: 'threshold' }), []);
});
