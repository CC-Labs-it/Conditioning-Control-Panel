// Race a friend through a link (NET.md section 13): the lobby api on a fake transport, and the lobby card's claim
// flow and Invite link button on a tiny fake DOM (seated, waiting then seated, refusals, cancel, copy and revoke).
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createLobbyApi, LINK_TOKEN } from '../net/lobbyApi.js';
import { createLobby } from '../hud/lobby.js';

const TOKEN = 'AbCdEfGhIjKlMnOpQrStUv';   // 22 base64url chars, like the lobby mints
const look = { outfit: null, vehicle: 'emi-teacup-kart' };
const NOW = 1.7e12;   // server ms

function fakeTransport(answers) {
  const calls = [];
  const t = async (method, path, body) => { calls.push({ method, path, body }); const a = answers(method, path, body, calls.length); return a; };
  return { t, calls };
}

test('lobby api: link routes, methods, bodies, the token shape check, and refusals as codes', async () => {
  const { t, calls } = fakeTransport((m, p) => {
    if (p === '/v2/race/link') return { status: 200, body: { token: TOKEN, url: 'https://app.cclabs.app/r/' + TOKEN, exp: 1, maxUses: 8, uses: 0 } };
    if (p === '/v2/race/link/revoke') return { status: 200, body: { ok: true } };
    if (p === '/v2/race/link/' + TOKEN) return { status: 200, body: { live: true, name: 'Ana', exp: 1, left: 8 } };
    if (p === '/v2/race/link/claim') return { status: 403, body: { error: 'tier' } };
    if (p === '/v2/race/access') return { status: 200, body: { ok: true, host: false, tier: 0 } };
    return { status: 404, body: { error: 'gone' } };
  });
  const api = createLobbyApi(t);
  assert.equal((await api.makeLink()).token, TOKEN);
  assert.deepEqual(calls[0], { method: 'POST', path: '/v2/race/link', body: {} });
  assert.deepEqual(await api.revokeLink(), { ok: true });
  assert.deepEqual(calls[1], { method: 'POST', path: '/v2/race/link/revoke', body: {} });
  assert.equal((await api.linkInfo(TOKEN)).name, 'Ana');
  assert.equal(calls[2].method, 'GET');
  assert.deepEqual(await api.access(), { ok: true, host: false, tier: 0 });
  assert.equal(calls[3].path, '/v2/race/access');
  await assert.rejects(api.claimLink(TOKEN, look, 'web'), (e) => e.code === 'tier' && e.status === 403);
  assert.deepEqual(calls[4], { method: 'POST', path: '/v2/race/link/claim', body: { token: TOKEN, look, platform: 'web' } });
  // a bad token never leaves the page
  const n = calls.length;
  for (const bad of ['short', '../open', TOKEN + '/x', 'a'.repeat(65), '', null]) {
    await assert.rejects(api.claimLink(bad, look), (e) => e.code === 'gone');
    await assert.rejects(api.linkInfo(bad), (e) => e.code === 'gone');
  }
  assert.equal(calls.length, n);
  assert.ok(LINK_TOKEN.test(TOKEN) && LINK_TOKEN.test('a'.repeat(64)) && !LINK_TOKEN.test('a'.repeat(15)));
  // claim without a look or platform sends just the token
  await assert.rejects(api.claimLink(TOKEN), (e) => e.code === 'tier');
  assert.deepEqual(calls.at(-1).body, { token: TOKEN });
});

// ---- the lobby card on a fake DOM -------------------------------------------------------------------

function fakeDom() {
  const timers = [];
  let tid = 0;
  const win = {
    setTimeout: (fn, ms) => { timers.push({ id: ++tid, fn, ms }); return tid; },
    clearTimeout: (id) => { const i = timers.findIndex((x) => x.id === id); if (i >= 0) timers.splice(i, 1); },
    setInterval: () => 0, clearInterval: () => {},
    navigator: { clipboard: { text: null, ok: true, writeText(s) { if (!this.ok) return Promise.reject(new Error('no')); this.text = s; return Promise.resolve(); } } },
    localStorage: null,
  };
  const el = () => ({ hidden: false, textContent: '', innerHTML: '', dataset: {}, classList: { contains: () => false, add() {}, remove() {}, toggle() {} },
    contains: () => false, querySelector: () => null, querySelectorAll: () => [], appendChild() {} });
  const doc = { defaultView: win, activeElement: null, head: el(), body: el(), getElementById: () => ({}), createElement: el };
  const card = el(); card.dataset.kind = 'lobby';
  const parts = { doc, card, title: el(), note: el(), extra: el(), emit: (n, p) => events.push([n, p]), focusFirst() {}, exit() { exited++; } };
  const events = []; let exited = 0;
  return { win, timers, parts, events, get exited() { return exited; },
    async runTimers() { const due = timers.splice(0); for (const x of due) await x.fn(); await flush(); } };
}
const flush = async () => { for (let i = 0; i < 20; i++) await Promise.resolve(); };
const has = (html, act) => html.includes('data-act="' + act + '"');

/** A connect() whose room says welcome on the next microtask: friends room, seat as given. */
function fakeConnect(seatNo = 1, host = false) {
  const joined = [];
  const connect = async (join) => {
    joined.push(join);
    const fns = {};
    const room = { on(t, fn) { fns[t] = fn; if (t === 'welcome') Promise.resolve().then(() => fn(welcome)); return () => {}; }, send() {}, close() {}, now: () => NOW };
    const seats = [{ seat: 0, kind: 'human', name: 'Ana', host: !host }, { seat: seatNo, kind: 'human', name: 'Me', host }];
    const welcome = { seat: seatNo, phase: 'lobby', cfg: { track: 'threshold', laps: 3, seats: 8, mode: 'friends' }, seed: 1, keep: [], seats, serverMs: NOW };
    return room;
  };
  return { connect, joined };
}

test('claim: seated at once goes straight into the room, and the address bar hook fires once', async () => {
  const d = fakeDom(), { connect, joined } = fakeConnect(1);
  let said = 0;
  const claims = [];
  const api = { claimLink: async (token, l, plat) => { claims.push([token, plat]); return { roomId: 'abcdefabcdef', ticket: 'tk', wsUrl: 'ws://x', seat: 1, expiresAt: 9, host: 'Ana' }; } };
  const lobby = createLobby(d.parts);
  lobby.use({ api, connect, identity: { displayName: 'Me', tier: 2 }, platform: 'web', linkClaimed: () => { said++; }, looks: async () => ({}) });
  lobby.show({ claim: TOKEN });
  assert.equal(d.parts.title.textContent, 'Race a friend');
  await flush();
  assert.deepEqual(claims, [[TOKEN, 'web']]);
  assert.equal(said, 1);
  assert.equal(joined[0].roomId, 'abcdefabcdef');
  assert.equal(lobby.view, 'room');
});

test('claim: waiting polls on retryMs with the host name, then seats; the hook fires only after the first answer', async () => {
  const d = fakeDom(), { connect } = fakeConnect(2);
  let n = 0, said = 0;
  const api = { claimLink: async () => (++n < 3 ? { waiting: true, retryMs: 3000, host: 'Ana' } : { roomId: 'abcdefabcdef', ticket: 'tk', wsUrl: 'ws://x', seat: 2, expiresAt: 9 }) };
  const lobby = createLobby(d.parts);
  lobby.use({ api, connect, identity: { displayName: 'Me', tier: 2 }, linkClaimed: () => { said++; } });
  lobby.show({ claim: TOKEN });
  await flush();
  assert.equal(d.parts.note.textContent, 'Waiting for Ana to open a room.');
  assert.ok(has(d.parts.extra.innerHTML, 'lb-back'), 'Cancel while waiting');
  assert.equal(d.timers.length, 1); assert.equal(d.timers[0].ms, 3000);
  await d.runTimers();
  assert.equal(n, 2); assert.equal(lobby.view, 'link');
  await d.runTimers();
  assert.equal(n, 3); assert.equal(said, 1);
  await flush();
  assert.equal(lobby.view, 'room');
});

test('claim: backing out stops the polling and lands on the tables', async () => {
  const d = fakeDom();
  let n = 0;
  const api = { claimLink: async () => { n++; return { waiting: true, retryMs: 2000, host: 'Ana' }; }, list: async () => ({ rows: [], serverMs: 1 }) };
  const lobby = createLobby(d.parts);
  lobby.use({ api, connect: fakeConnect().connect, identity: { displayName: 'Me', tier: 2 }, looks: async () => ({}) });
  lobby.show({ claim: TOKEN });
  await flush();
  lobby.back();
  assert.equal(lobby.view, 'home');
  assert.equal(d.timers.length, 0, 'no poll left');
  assert.equal(n, 1);
});

test('claim refusals: tier, gone, full each get their card with Tables and Menu', async () => {
  const want = { tier: 'Racing a friend needs Prime.', gone: 'This link has expired.', full: 'That race is full.' };
  for (const [code, copy] of Object.entries(want)) {
    const d = fakeDom();
    const api = { claimLink: async () => { const e = new Error('x'); e.code = code; throw e; } };
    const lobby = createLobby(d.parts);
    lobby.use({ api, connect: fakeConnect().connect, identity: { displayName: 'Me', tier: 0 } });
    lobby.show({ claim: TOKEN });
    await flush();
    assert.equal(lobby.view, 'link', code);
    assert.equal(d.parts.note.textContent, copy, code);
    assert.ok(has(d.parts.extra.innerHTML, 'lb-home') && has(d.parts.extra.innerHTML, 'lb-exit'), code + ' buttons');
    assert.equal(d.timers.length, 0, code + ' never polls');
  }
});

test('preview: the Race online door says the closed preview line instead of an error, and stops polling', async () => {
  const d = fakeDom();
  let lists = 0;
  const api = { list: async () => { lists++; const e = new Error('x'); e.code = 'preview'; e.status = 403; throw e; } };
  const lobby = createLobby(d.parts);
  lobby.use({ api, identity: { displayName: 'Me', tier: 2 }, looks: async () => ({}) });
  lobby.show({ view: 'home' });
  lobby.act({ dataset: { act: 'lb-refresh' } });
  await flush();
  assert.equal(d.parts.note.textContent, 'Online racing is in a closed preview.');
  assert.ok(!has(d.parts.extra.innerHTML, 'lb-quick'), 'no tables, no quick match');
  assert.equal(lists, 1);
});

test('Invite link: a friends room host with access().host copies the url, says seats and hours, and can revoke', async () => {
  const d = fakeDom(), { connect } = fakeConnect(0, true);
  const exp = NOW + 24 * 3600 * 1000;
  let made = 0, revoked = 0, asked = 0;
  const api = {
    open: async () => ({ roomId: 'abcdefabcdef', ticket: 'tk', wsUrl: 'ws://x', seat: 0, expiresAt: 9 }),
    access: async () => { asked++; return { ok: true, host: true, tier: 2 }; },
    makeLink: async () => { made++; return { token: TOKEN, url: 'https://app.cclabs.app/r/' + TOKEN, exp, maxUses: 8, uses: 0 }; },
    revokeLink: async () => { revoked++; return { ok: true }; },
  };
  const lobby = createLobby(d.parts);
  lobby.use({ api, connect, identity: { displayName: 'Me', tier: 0 }, looks: async () => ({}) });
  lobby.show({ view: 'home' });
  lobby.act({ dataset: { act: 'lb-host', id: 'friends' } });
  lobby.act({ dataset: { act: 'lb-open' } });
  await flush();
  assert.equal(lobby.view, 'room');
  assert.equal(asked, 1);
  assert.ok(has(d.parts.extra.innerHTML, 'lb-link'), 'Invite link shows');
  assert.ok(!has(d.parts.extra.innerHTML, 'lb-unlink'), 'no Revoke before a link');
  lobby.act({ dataset: { act: 'lb-link' } });
  await flush();
  assert.equal(made, 1);
  assert.equal(d.win.navigator.clipboard.text, 'https://app.cclabs.app/r/' + TOKEN);
  assert.equal(d.parts.note.textContent, 'Link copied. 8 seats, 24 h.');
  assert.ok(has(d.parts.extra.innerHTML, 'lb-unlink'));
  assert.ok(!d.parts.extra.innerHTML.includes('lb-url'), 'no field when the clipboard took it');
  lobby.act({ dataset: { act: 'lb-unlink' } });
  await flush();
  assert.equal(revoked, 1);
  assert.equal(d.parts.note.textContent, 'Link revoked.');
  // clipboard refused: the url sits in a read-only field to copy by hand
  d.win.navigator.clipboard.ok = false;
  lobby.act({ dataset: { act: 'lb-link' } });
  await flush();
  assert.equal(d.parts.note.textContent, 'Copy the link below. 8 seats, 24 h.');
  assert.ok(/<p class="lb-url"[^>]*>https:\/\/app\.cclabs\.app\/r\/AbCd/.test(d.parts.extra.innerHTML));
});

test('Invite link: never for a guest, nor for a host without access().host', async () => {
  for (const [host, access] of [[false, { host: true }], [true, { host: false }]]) {
    const d = fakeDom(), { connect } = fakeConnect(host ? 0 : 1, host);
    const api = { join: async () => ({ roomId: 'abcdefabcdef', ticket: 'tk', wsUrl: 'ws://x', seat: 1, expiresAt: 9 }), access: async () => access,
      claimLink: async () => ({ roomId: 'abcdefabcdef', ticket: 'tk', wsUrl: 'ws://x', seat: 1, expiresAt: 9 }) };
    const lobby = createLobby(d.parts);
    lobby.use({ api, connect, identity: { displayName: 'Me', tier: 2 } });
    lobby.show({ claim: TOKEN });
    await flush();
    assert.equal(lobby.view, 'room');
    assert.ok(!has(d.parts.extra.innerHTML, 'lb-link'), 'host ' + host);
  }
});
