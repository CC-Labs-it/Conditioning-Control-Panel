/* Save carry (lane S7b, CONTRACT.md 6.3): an offer only from a press and only when it fits both caps, a line for each
 * reason, an incoming run never applied without accept, accept in small keeps cut rows asleep and round-trips them,
 * a broken incoming run leaves the run alone, nothing shows without a save slot, one call at a time, and the one press
 * rule takes the event first and falls back to userActivation. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as Ed from '../ui/edition.js';
import * as E from '../sim/economy.js';
import { createLife, parse, serialize } from '../sim/save.js';
import { SAVE_CARRY_MAX_BYTES } from '../data/editions.js';
import { realPress } from '../ui/nudges.js';
import { SAVE_CARRY_COPY, SAVE_REASONS, createSaveCarry, incomingLine, mountSaveCarry } from '../ui/save-carry.js';

const small = Ed.readEdition({ caps: { edition: 'small', platform: 'web', saveSlot: true }, identity: {} }, { hostname: 'tc.cclabs.app', hosted: true });
const goodSave = () => { const s = E.createRun({ seed: 3 }); E.lump(s, 500); return serialize(s, createLife(), {}); };

test('the one press rule: the event first, userActivation when there is no event, never dead without userActivation', () => {
  const withUA = (active) => ({ navigator: { userActivation: { isActive: active } } });
  const noUA = { navigator: {} };
  assert.equal(realPress(noUA, { isTrusted: true })(), true, 'no userActivation: the trusted event still counts');
  assert.equal(realPress(withUA(true), { isTrusted: false })(), false, 'a script-made event never counts');
  assert.equal(realPress(withUA(true))(), true, 'no event: the browser activation');
  assert.equal(realPress(withUA(false), {})(), false, 'an event with no isTrusted falls back');
  assert.equal(realPress(noUA)(), false);
});

test('an offer goes only from a press, only when it fits both caps, one call at a time', () => {
  const sent = [], said = [];
  let press = false, save = goodSave();
  const carry = createSaveCarry({ send: (t, f) => sent.push([t, f]), inPress: () => press, exportSave: () => save, say: (l) => said.push(l) });
  assert.equal(carry.offer(), false, 'no press, no offer');
  press = true;
  save = '{"v":2,"pad":"' + 'x'.repeat(SAVE_CARRY_MAX_BYTES) + '"}';
  assert.equal(carry.offer(), false, 'too big to travel');
  assert.equal(said.at(-1), SAVE_CARRY_COPY['too-big']);
  save = goodSave();
  assert.equal(carry.offer(), true);
  assert.equal(carry.offer(), false, 'a second press while one flies does nothing');
  assert.equal(carry.askForRun(), false);
  assert.deepEqual(sent, [['save-offer', { save }]]);
  carry.result({ ok: true });
  assert.equal(said.at(-1), SAVE_CARRY_COPY.sent);
  assert.equal(carry.busy, null);
});

test('each reason has its own line, and an unknown one reads as refused', () => {
  for (const reason of [...SAVE_REASONS, 'weird']) {
    const said = [];
    const carry = createSaveCarry({ inPress: () => true, exportSave: goodSave, say: (l) => said.push(l) });
    carry.offer();
    carry.result({ ok: false, reason });
    assert.equal(said.at(-1), SAVE_CARRY_COPY[reason === 'weird' ? 'refused' : reason], reason);
  }
  assert.equal(new Set(SAVE_REASONS.map((r) => SAVE_CARRY_COPY[r])).size, 5);
});

test('in small a send that went carries the full-game line; the full edition does not', () => {
  const lines = [];
  for (const isSmall of [true, false]) {
    const carry = createSaveCarry({ inPress: () => true, exportSave: goodSave, small: isSmall, say: (l, o = {}) => lines.push(!!o.full) });
    carry.offer(); carry.result({ ok: true });
  }
  assert.deepEqual(lines, [false, true, false, false]);
});

test('an incoming run never applies without accept, and only answers our own ask', () => {
  const save = goodSave(), applied = [], asked = [];
  const carry = createSaveCarry({ inPress: () => true, accept: (s) => { applied.push(s); return true; }, ask: (run, answer) => asked.push([run, answer]) });
  const frame = { save, savedAt: Date.UTC(2026, 10, 22, 18, 30), from: 'desktop' };
  assert.equal(carry.incoming(frame), false, 'nobody asked: ignored');
  carry.askForRun();
  assert.equal(carry.incoming(frame), true);
  assert.deepEqual(applied, [], 'shown, not applied');
  assert.equal(asked[0][0].from, 'desktop');
  asked[0][1](false);
  assert.deepEqual(applied, [], 'keep mine');
  carry.askForRun(); carry.incoming(frame);
  asked[1][1](true);
  assert.deepEqual(applied, [save], 'accept applies it');
  assert.match(incomingLine(frame, 'en-GB'), /S7-save-from-desktop/);
  assert.match(incomingLine(frame, 'en-GB'), /2026/, 'the date in the player\'s locale');
});

test('a broken incoming run is refused and the run is untouched', () => {
  const said = [], applied = [];
  const carry = createSaveCarry({ inPress: () => true, accept: (s) => { applied.push(s); return false; }, ask: (run, answer) => answer(true), say: (l) => said.push(l) });
  carry.askForRun();
  assert.equal(carry.incoming({ save: '{nope', savedAt: 1, from: 'web' }), false);
  assert.equal(said.at(-1), SAVE_CARRY_COPY.broken);
  assert.deepEqual(applied, []);
  /* Parses as a frame but the game's own load path refuses it (main.js: parse fails -> nothing stored). */
  carry.askForRun();
  carry.incoming({ save: '{"v":999}', savedAt: 5, from: 'web' });
  assert.equal(said.at(-1), SAVE_CARRY_COPY.broken);
});

test('accept in small: cut rows stay asleep, nothing deleted, the round trip keeps them', () => {
  const s = E.createRun({ seed: 9 });
  for (const id of ['toy_box', 'swap', 'buzz']) E.grant(s, id);
  const raw = serialize(s, createLife(), {});
  const storage = new Map();
  /* The same steps as main.js acceptCarried, on a map. */
  const accept = (text) => { if (!parse(text, { ed: small })) return false; storage.set('fuzz.save.v2.bak', 'old'); storage.set('fuzz.save.v2', text); return true; };
  const carry = createSaveCarry({ inPress: () => true, accept, ask: (run, answer) => answer(true) });
  carry.askForRun();
  carry.incoming({ save: raw, savedAt: 10, from: 'desktop' });
  assert.equal(storage.get('fuzz.save.v2.bak'), 'old', 'the local save kept first');
  const here = parse(storage.get('fuzz.save.v2'), { ed: small });
  for (const id of ['toy_box', 'swap', 'buzz']) {
    assert.equal(E.has(here.run, id), false, `${id} asleep in small`);
    assert.equal(here.run.owned[id], s.owned[id], `${id} still owned`);
  }
  const back = parse(serialize(here.run, here.life, here.extras));
  for (const id of ['toy_box', 'swap', 'buzz']) assert.equal(E.has(back.run, id), true, `${id} awake again in full`);
});

function fakeDoc() {
  const byId = new Map();
  const make = (tag) => ({ tag, children: [], listeners: {}, attrs: {}, textContent: '', hidden: false, className: '',
    set id(v) { this._id = v; byId.set(v, this); }, get id() { return this._id; },
    appendChild(c) { this.children.push(c); return c; }, setAttribute(k, v) { this.attrs[k] = v; },
    addEventListener(t, f) { (this.listeners[t] ||= []).push(f); }, click(ev = {}) { for (const f of this.listeners.click || []) f(ev); } });
  const card = make('div');
  return { card, createElement: make, getElementById: (id) => byId.get(id) || null, querySelector: (q) => (q === '#pause .card' ? card : null) };
}

test('mounted: hidden without a save slot; with one, two buttons that act on a trusted press', () => {
  const none = fakeDoc();
  assert.equal(mountSaveCarry({ doc: none, win: {}, slot: false, cards: {}, exportSave: goodSave }), null);
  assert.equal(none.card.children.length, 0, 'nothing shows');
  const doc = fakeDoc(), sent = [], handlers = {};
  const carry = mountSaveCarry({ doc, win: { navigator: {} }, slot: true, cards: { open: () => ({}) }, send: (t, f) => sent.push(t),
    on: (t, fn) => { handlers[t] = fn; }, exportSave: goodSave, accept: () => true });
  assert.ok(carry);
  const [box, status] = doc.card.children;
  const [sendBtn, askBtn] = box.children;
  assert.equal(sendBtn.textContent, SAVE_CARRY_COPY.send);
  assert.equal(askBtn.textContent, SAVE_CARRY_COPY.ask);
  sendBtn.click({ isTrusted: false });
  assert.deepEqual(sent, []);
  sendBtn.click({ isTrusted: true });
  assert.deepEqual(sent, ['save-offer'], 'works with no userActivation at all');
  handlers['save-result']({ ok: false, reason: 'offline' });
  assert.equal(status.textContent, SAVE_CARRY_COPY.offline);
  assert.equal(status.hidden, false);
  assert.equal(mountSaveCarry({ doc, win: {}, slot: true, cards: {}, exportSave: goodSave }), null, 'once per card');
});

test('nothing here reads the edition itself', () => {
  for (const f of ['../ui/save-carry.js', '../ui/nudges.js']) {
    const src = readFileSync(new URL(f, import.meta.url), 'utf8');
    assert.doesNotMatch(src, /caps\??\.(edition|saveSlot)|\.saveSlot\b|isSmall\(/, f);
  }
  const main = readFileSync(new URL('../main.js', import.meta.url), 'utf8');
  assert.match(main, /slot: saveSlotOn\(ed\), small: isSmall\(ed\)/);
});
