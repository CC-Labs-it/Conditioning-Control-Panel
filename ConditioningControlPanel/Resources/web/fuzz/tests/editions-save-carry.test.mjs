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
import { SAVE_CARRY_COPY, SAVE_REASONS, createSaveCarry, incomingLine, whenLine, mountSaveCarry } from '../ui/save-carry.js';

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
  const at = new Date(frame.savedAt), two = (n) => String(n).padStart(2, '0');
  assert.equal(incomingLine(frame), `Saved on the app, ${at.getDate()} Nov ${two(at.getHours())}:${two(at.getMinutes())}. Play it here instead of this one?`);
  assert.equal(whenLine(new Date(2026, 10, 22, 14, 5).getTime()), '22 Nov 14:05', 'day, short month, 24-hour local time');
  assert.equal(whenLine(new Date(2026, 0, 3, 9, 7).getTime()), '3 Jan 09:07');
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

/* Wave 4 lane E item 1 (desk run 2026-11-24, 02-tc-save-fetch*.png): the incoming card opened BEHIND the pause card
 * (#cards z 90, #pause 95) and was seen only after Resume. Now the real card queue lifts itself over the pause card
 * while that card shows, its first button takes the focus, and the queue drops back when it is answered. */
function fakeDom() {
  const make = (tag) => {
    const el = { tag, children: [], listeners: {}, attrs: {}, textContent: '', hidden: false, className: '', style: {}, parent: null,
      appendChild(c) { c.parent = this; this.children.push(c); return c; }, setAttribute(k, v) { this.attrs[k] = v; },
      addEventListener(t, f) { (this.listeners[t] ||= []).push(f); }, click(ev = { isTrusted: true }) { for (const f of this.listeners.click || []) f(ev); },
      remove() { if (this.parent) this.parent.children = this.parent.children.filter((c) => c !== this); this.parent = null; },
      focus() { doc.activeElement = this; },
      querySelector(q) { return q === 'button' ? find(this, (n) => n.tag === 'button') : null; } };
    const set = new Set();
    el.classList = { add: (c) => set.add(c), remove: (c) => set.delete(c), contains: (c) => set.has(c) };
    return el;
  };
  const find = (n, ok) => { for (const c of n.children) { if (ok(c)) return c; const d = find(c, ok); if (d) return d; } return null; };
  const doc = { activeElement: null, createElement: make };
  return doc;
}

test('the incoming run card shows over the pause card and is answerable at once', async () => {
  const { createCards } = await import('../ui/cards.js');
  const doc = fakeDom(), host = doc.createElement('div'), pauseCard = doc.createElement('div'), handlers = {}, sent = [];
  const byId = new Map();
  doc.getElementById = (id) => byId.get(id) || null;
  doc.querySelector = (q) => (q === '#pause .card' ? pauseCard : null);
  const make = doc.createElement;
  doc.createElement = (tag) => { const el = make(tag); Object.defineProperty(el, 'id', { set(v) { byId.set(v, el); }, get() { return null; } }); return el; };
  const cards = createCards(doc, host);
  const applied = [];
  mountSaveCarry({ doc, win: { navigator: {} }, slot: true, cards, send: (t) => sent.push(t), on: (t, fn) => { handlers[t] = fn; },
    exportSave: goodSave, accept: (s) => { applied.push(s); return true; } });
  cards.hold(true);   // the pause card is open (ui/app.js holdWorld): the queue is inert under it
  assert.equal(host.inert, true);
  const askBtn = pauseCard.children[0].children[1];
  askBtn.click({ isTrusted: true });
  assert.deepEqual(sent, ['save-ask']);
  assert.equal(host.classList.contains('over-pause'), false, 'nothing lifted before the run arrives');
  handlers['save-incoming']({ save: goodSave(), savedAt: Date.UTC(2026, 10, 24, 12, 10), from: 'desktop' });
  assert.equal(host.children.length, 1, 'the card shows at once');
  assert.equal(host.classList.contains('over-pause'), true, 'the queue sits over the pause card while it shows');
  assert.equal(host.inert, false, 'and takes presses and keys: the pause hold does not reach it');
  const keep = doc.activeElement;
  assert.equal(keep?.textContent, SAVE_CARRY_COPY.keep, 'its first button has the focus, not Resume under it');
  keep.click({ isTrusted: true });
  assert.equal(host.children.length, 0);
  assert.equal(host.classList.contains('over-pause'), false, 'answered: the queue drops back under the pause card');
  assert.equal(host.inert, true, 'still paused: the queue is inert again');
  assert.deepEqual(applied, [], 'keep mine applies nothing');
  /* An ordinary card never lifts the queue. */
  cards.open({ title: 'Today', buttons: [{ label: 'Fine' }] });
  assert.equal(host.classList.contains('over-pause'), false);
  assert.equal(host.inert, true, 'an ordinary card stays inert under the pause card');
  cards.hold(false);
  assert.equal(host.inert, false, 'resumed: the queue takes presses again');
});

/* Wave 5 (W4 review of 461ce9e623): the incoming card waited behind ANY other open card, inert under the pause (small
 * edition: the save-sleep corner note is up from boot), and its answer left focus on the page body. */
async function pausedCarry() {
  const { createCards } = await import('../ui/cards.js');
  const doc = fakeDom(), host = doc.createElement('div'), handlers = {}, sent = [];
  const byId = new Map();
  doc.getElementById = (id) => byId.get(id) || null;
  const pause = doc.createElement('div'), pauseCard = doc.createElement('div');
  pause.hidden = false;
  byId.set('pause', pause);
  const row = pauseCard.appendChild(doc.createElement('div'));
  const resume = row.appendChild(doc.createElement('button'));
  resume.textContent = 'Resume';
  doc.querySelector = (q) => (q === '#pause .card' ? pauseCard : null);
  const cards = createCards(doc, host);
  mountSaveCarry({ doc, win: { navigator: {} }, slot: true, cards, send: (t) => sent.push(t), on: (t, fn) => { handlers[t] = fn; },
    exportSave: goodSave, accept: () => true });
  const askBtn = pauseCard.children[1].children[1];
  const incoming = () => handlers['save-incoming']({ save: goodSave(), savedAt: Date.UTC(2026, 10, 24, 12, 10), from: 'web' });
  return { doc, host, cards, pause, resume, askBtn, incoming, sent };
}

test('the incoming run card shows at once over the pause card even while another card is open', async () => {
  const { doc, host, cards, askBtn, incoming } = await pausedCarry();
  const note = cards.open({ title: 'Some clauses are asleep.', corner: true, buttons: [{ label: 'Carry on' }] });
  const noteDom = host.children[0];
  cards.hold(true);   // the player pauses with the corner note still up
  assert.equal(host.inert, true);
  askBtn.click({ isTrusted: true });
  incoming();
  assert.equal(note.closed, false, 'the open card is not lost');
  assert.equal(noteDom.hidden, true, 'it steps aside while the incoming card shows');
  const shown = host.children.filter((c) => !c.hidden);
  assert.equal(shown.length, 1, 'one card showing at a time');
  assert.notEqual(shown[0], noteDom, 'and it is the incoming card');
  assert.equal(host.classList.contains('over-pause'), true);
  assert.equal(host.inert, false, 'answerable at once under the pause');
  assert.equal(doc.activeElement?.textContent, SAVE_CARRY_COPY.keep);
  doc.activeElement.click({ isTrusted: true });
  assert.deepEqual(host.children, [noteDom], 'answered: the note is back as it was');
  assert.equal(noteDom.hidden, false);
  assert.equal(host.classList.contains('over-pause'), false);
  assert.equal(host.inert, true, 'the note gains no input while the pause is up');
  cards.hold(false);
  assert.equal(host.inert, false, 'resumed: the note takes presses again');
  noteDom.querySelector('button').click({ isTrusted: true });
  assert.equal(note.closed, true);
  assert.equal(host.children.length, 0);
  assert.equal(cards.busy, false);
});

test('after the answer the focus goes back to the pause card, only while it is up', async () => {
  const { doc, cards, pause, resume, askBtn, incoming } = await pausedCarry();
  cards.hold(true);
  askBtn.click({ isTrusted: true });
  incoming();
  doc.activeElement.click({ isTrusted: true });   // Keep mine
  assert.equal(doc.activeElement, resume, "the pause card's first control, not the page body");
  askBtn.click({ isTrusted: true });
  incoming();
  pause.hidden = true;   // Escape resumed under the card
  cards.hold(false);
  doc.activeElement.click({ isTrusted: true });
  assert.notEqual(doc.activeElement, resume, 'no focus pulled to a hidden pause card');
});

test('the lifted queue really is above the pause card in the stylesheet', () => {
  const css = readFileSync(new URL('../styles.css', import.meta.url), 'utf8');
  const z = (sel) => Number(new RegExp(`^${sel.replace(/[.#]/g, '\\$&')} \\{ z-index: (\\d+); \\}`, 'm').exec(css)?.[1]);
  assert.ok(z('#pause.backdrop') > 0);
  assert.ok(z('#cards.over-pause') > z('#pause.backdrop'), 'over-pause above the pause card');
  /* The pause hold goes through the queue, so a lifted card is never left inert (desk run: on top but unpressable). */
  const app = readFileSync(new URL('../ui/app.js', import.meta.url), 'utf8');
  assert.ok(app.includes('cards.hold(true);'), 'the pause hold reaches the queue');
  assert.ok(app.includes('cards.hold(false);'));
  assert.ok(!app.includes('el.cards.inert'), 'nothing sets the queue inert behind its back');
});

test('nothing here reads the edition itself', () => {
  for (const f of ['../ui/save-carry.js', '../ui/nudges.js']) {
    const src = readFileSync(new URL(f, import.meta.url), 'utf8');
    assert.doesNotMatch(src, /caps\??\.(edition|saveSlot)|\.saveSlot\b|isSmall\(/, f);
  }
  const main = readFileSync(new URL('../main.js', import.meta.url), 'utf8');
  assert.match(main, /slot: saveSlotOn\(ed\), small: isSmall\(ed\)/);
});
