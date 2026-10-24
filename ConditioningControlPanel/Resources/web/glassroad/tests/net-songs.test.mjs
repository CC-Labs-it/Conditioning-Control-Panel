// tests/net-songs.test.mjs - race songs from our own storage (NET.md section 14): lobbyApi.songs, the signed url
// validator (host/bridge.js lobbySongCharts) and when boot asks the lobby at all (wantLobbySongs).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createLobbyApi } from '../net/lobbyApi.js';
import { lobbySongCharts, wantLobbySongs, songCharts, ROOM_ORIGIN } from '../host/bridge.js';

const NOW = 1792699000000;
const SIG = 'A'.repeat(21) + '_-' + 'b'.repeat(20);
const url = (o = {}) => `${o.origin || ROOM_ORIGIN}/song/${o.id || 'pink-road'}/${o.file || 'chart.json'}?exp=${o.exp ?? NOW + 6 * 3600000}&sig=${o.sig ?? SIG}${o.tail || ''}`;

test('lobbyApi.songs: GET /v2/race/songs, the body back, refusals as codes', async () => {
  const calls = [];
  const body = { songs: [{ id: 'pink-road', title: 'Pink Road', chartUrl: url(), exp: NOW + 6 * 3600000 }] };
  const api = createLobbyApi(async (method, path, b) => { calls.push([method, path, b]); return { status: 200, body }; });
  assert.deepEqual(await api.songs(), body);
  assert.deepEqual(calls, [['GET', '/v2/race/songs', undefined]]);
  const shut = createLobbyApi(async () => ({ status: 403, body: { error: 'preview' } }));
  await assert.rejects(shut.songs(), (e) => e.code === 'preview' && e.status === 403);
  const dark = createLobbyApi(async () => ({ status: 404, body: { error: 'not_deployed' } }));
  await assert.rejects(dark.songs(), (e) => e.code === 'not_deployed');
});

test('lobbySongCharts: signed chart urls on the room origin only', () => {
  const good = url();
  assert.equal(ROOM_ORIGIN, 'https://glassroad-room.cclabs-race.workers.dev');
  assert.deepEqual(lobbySongCharts([{ chartUrl: good }], { nowMs: NOW }), [good]);
  const bad = [
    url({ origin: 'https://evil.example' }),                       // another origin
    url({ origin: 'http://glassroad-room.cclabs-race.workers.dev' }), // http on the room host
    url({ file: 'pink-road.mp3' }),                                 // not a chart
    url({ id: '../x' }), url({ id: 'Pink' }),                       // id shape
    url({ exp: NOW }), url({ exp: 'soon' }),                        // spent, not a number
    url({ sig: 'short' }), url({ sig: '' }),                        // sig shape
    url({ tail: '#frag' }),                                         // a hash
    'https://user:pw@glassroad-room.cclabs-race.workers.dev/song/pink-road/chart.json?exp=' + (NOW + 1000) + '&sig=' + SIG,
    '/song/pink-road/chart.json', 'not a url', 'x'.repeat(600),
  ];
  assert.deepEqual(lobbySongCharts(bad.map((chartUrl) => ({ chartUrl })), { nowMs: NOW }), []);
  assert.deepEqual(lobbySongCharts([null, 7, {}, { chartUrl: 3 }, { chartUrl: good }], { nowMs: NOW }), [good]);
  assert.deepEqual(lobbySongCharts(null), []);
  assert.deepEqual(lobbySongCharts({ songs: [] }), []);
  const many = Array.from({ length: 12 }, (_, i) => ({ chartUrl: url({ id: 'song-' + i }) }));
  assert.equal(lobbySongCharts(many, { nowMs: NOW }).length, 8);
});

test('lobbySongCharts: a local room on loopback http, never a plain http origin elsewhere', () => {
  const local = url({ origin: 'http://127.0.0.1:8812' });
  assert.deepEqual(lobbySongCharts([{ chartUrl: local }], { roomOrigin: 'http://127.0.0.1:8812', nowMs: NOW }), [local]);
  assert.deepEqual(lobbySongCharts([{ chartUrl: local }], { roomOrigin: 'http://127.0.0.1:8813', nowMs: NOW }), []);
  assert.deepEqual(lobbySongCharts([{ chartUrl: local }], { nowMs: NOW }), []);
  const lan = url({ origin: 'http://192.168.1.9:8812' });
  assert.deepEqual(lobbySongCharts([{ chartUrl: lan }], { roomOrigin: 'http://192.168.1.9:8812', nowMs: NOW }), []);
  assert.deepEqual(lobbySongCharts([{ chartUrl: local }], { roomOrigin: null, nowMs: NOW }), []);
  assert.deepEqual(lobbySongCharts([{ chartUrl: local }], { roomOrigin: 'nope', nowMs: NOW }), []);
});

test('wantLobbySongs: only when nothing named a chart (?chart and host songs win, ?chart=0 is none)', () => {
  assert.equal(wantLobbySongs({ search: '', charts: [] }), true);
  assert.equal(wantLobbySongs({ search: '?lobby=x', charts: [] }), true);
  assert.equal(wantLobbySongs({ search: '?chart=0', charts: [] }), false);
  assert.equal(wantLobbySongs({ search: '?chart=/a.json', charts: [] }), false);
  assert.equal(wantLobbySongs({ search: '', charts: ['/glassroad-audio/pink-road/chart.json'] }), false);
  // desktop sends caps.songs [] (CONTRACT section 7): no host songs, so the lobby is asked
  assert.equal(wantLobbySongs({ search: '', charts: songCharts({ songs: [] }) }), true);
  // the host songs validator is unchanged: an absolute lobby url is never a host song
  assert.deepEqual(songCharts({ songs: [url()] }), []);
  assert.deepEqual(wantLobbySongs(), true);
});

test('boot memoizes the song fallback and starts recordings that arrive after racing begins', () => {
  const src = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  const at = src.indexOf('lobbyApi.songs()');
  assert.ok(at > 0 && src.indexOf('lobbyApi.songs()', at + 1) < 0, 'one call');
  assert.ok(src.includes("!qs.has('chart')"), 'explicit chart choices never fetch a fallback');
  assert.ok(src.includes('fallback ||='), 'one memoized request also serves a failed host chart');
  assert.match(src.slice(at, at + 300), /lobbySongCharts\(/);
  assert.ok(src.includes('if (!CHARTS.length) CHARTS = list;'));
  assert.ok(src.includes("else if (state === 'race') void playSong"), 'a late song no longer leaves a silent race');
});
