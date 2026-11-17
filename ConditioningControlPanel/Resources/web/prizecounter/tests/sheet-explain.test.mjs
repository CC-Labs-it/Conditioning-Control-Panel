// Explanations (owner 2026-11-17: "someone hovers over bubbles v2 and doesnt know what it does"): every row the shop can
// show has a hover tip and a "What it does"; the tip is the page's own (never a native title), shows on hover and on
// keyboard focus, goes on leave, scroll, a press or a key, never pops on touch, never takes focus or eats a click.
import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { fakeDocument } from './fake-dom.mjs';
import { createMockServer } from '../mock/server.js';
import { previewSample, createPreviewServer } from '../mock/preview.js';
import { asHost } from '../host.js';
import { mountSheet } from '../sheet/main.js';
import { sheetT } from '../sheet/strings.js';
import { readPreview } from '../sheet/wire.js';
import { rowsOf } from '../sheet/model.js';
import { explainKeys, roomExplain, tipText, familyOf, EXPLAIN_LEX, TAB_TIPS, SIGN_TIPS, HELP } from '../sheet/explain.js';
import { createTips, placeTip, TIP_DELAY_MS } from '../sheet/tips.js';

const here = path.dirname(fileURLToPath(import.meta.url)), page = path.join(here, '..');
const SAMPLE = JSON.parse(fs.readFileSync(path.join(page, 'mock/state.sample.json'), 'utf8'));
const LANG_DIR = path.join(page, '../../../Localization/Languages');
const LANGS = ['de', 'en', 'es', 'fr', 'it', 'ja', 'ko', 'pt-BR', 'ru', 'zh-CN'];
const t = sheetT(null, 'Emi');

/** Every row the mock shows: the state's tabs plus the preview wire's effect rows. */
function mockRows() {
  const s = previewSample(SAMPLE), pv = readPreview({ ...s, preview: true });
  const st = { tabs: s.tabs.filter((tb) => tb.id !== 'effects') };
  return rowsOf(st, pv);
}

test('every mock row resolves a hover tip and a "What it does", from its own entry or its family', () => {
  const rows = mockRows();
  assert.ok(rows.length > 80, `found ${rows.length}`);
  const noTip = rows.filter((e) => !explainKeys(e).tip).map((e) => e.id);
  assert.deepEqual(noTip, []);
  const noRoom = rows.filter((e) => { const x = roomExplain(e, t); return !x.does && !x.at; }).map((e) => e.id);
  assert.deepEqual(noRoom, []);
  // the old items the owner named have their own words, not a guess from the family
  for (const id of ['bubbles_v2', 'flashes_v2', 'jackpot_remix', 'high_roller']) {
    const x = roomExplain(rows.find((e) => e.id === id), t);
    assert.ok(x.does && x.at && x.use, id);
  }
  assert.match(roomExplain(rows.find((e) => e.id === 'bubbles_v2'), t).use, /Bubble Pop/);
  assert.match(roomExplain(rows.find((e) => e.id === 'kart.body.petal'), t).use, /garage/);
  assert.equal(familyOf({ id: 'prime.banner.arena', tab: 'prime' }), 'chess_banner');
  // an unknown Back Room prize still says where it came from, and nothing it cannot know
  const odd = roomExplain({ id: 'brand_new_prize', tab: 'backroom', x: {} }, t);
  assert.equal(odd.use, null);
  assert.ok(tipText({ id: 'brand_new_prize', tab: 'backroom', x: {} }, t));
});

test('a chess finish says one buy covers every piece on both sides, worn one at a time with the rest', () => {
  for (const id of ['chess.finish.polka', 'chess.finish.marble', 'chess.finish.contour', 'chess.finish.opal', 'chess.finish.kintsugi']) {
    const e = mockRows().find((x) => x.id === id);
    assert.ok(e, id + ' is a mock row');
    assert.equal(familyOf(e), 'chess_finish', id);
    assert.equal(explainKeys(e).tip, 'shop_sheet_tip_chess_finish_' + id.split('.').pop(), id);
    const x = roomExplain(e, t);
    assert.match(x.does, /all six pieces/, id);
    assert.match(x.does, /one finish at a time/, id);
    assert.match(x.use, /Wardrobe in Piece by Piece/, id);
    assert.notEqual(t(explainKeys(e).tip), explainKeys(e).tip, id + ' tip has English');
  }
});

test('a card tip says why it cannot be bought; the tabs, signs and mechanics all have words', () => {
  const e = mockRows().find((x) => x.id === 'prime.trail.podium');
  assert.match(tipText(e, t, { kind: 'prime' }), /Prime members can buy it/);
  assert.match(tipText(e, t, { kind: 'todo', prime: true }), /Not on sale yet/);
  for (const k of [...Object.values(TAB_TIPS), ...Object.values(SIGN_TIPS), ...Object.values(HELP)]) assert.notEqual(t(k), k, k);
  for (const id of ['featured', 'effects', 'games', 'you', 'prime', 'backroom', 'owned']) assert.ok(TAB_TIPS[id], id);
});

test('tips fit a two-line paper slip in every language (about 90 Latin letters, CJK counted double)', () => {
  const tipKeys = Object.keys(EXPLAIN_LEX).filter((k) => /_(tip|sign|help)_/.test(k));
  const width = (s) => [...s].reduce((n, c) => n + (/[　-鿿가-힯＀-￯]/.test(c) ? 2 : 1), 0);
  for (const lang of LANGS) {
    const j = JSON.parse(fs.readFileSync(path.join(LANG_DIR, `${lang}.json`), 'utf8'));
    const long = tipKeys.filter((k) => width(j[k].replace('{companion}', 'Bambi')) > 90).map((k) => `${k}: ${j[k]}`);
    assert.deepEqual(long, [], lang);
    for (const k of Object.keys(EXPLAIN_LEX)) assert.doesNotMatch(j[k], /�|Ã|â€/, `${lang} ${k} mojibake`);
  }
});

test('no native title anywhere: not in the sheet code, not on any drawn node', async () => {
  for (const f of fs.readdirSync(path.join(page, 'sheet')).filter((x) => x.endsWith('.js'))) {
    const src = fs.readFileSync(path.join(page, 'sheet', f), 'utf8');
    assert.doesNotMatch(src, /\.title\s*=|setAttribute\(\s*'title'/, f);
  }
  const r = await rig();
  r.click('tab:backroom');
  assert.deepEqual(r.page.view.root.all().filter((n) => n.attrs.title != null).map((n) => n.tagName), []);
  const card = r.btn('it:bubbles_v2');
  const id = card.getAttribute('aria-describedby');
  assert.ok(id, 'described');
  const span = r.page.view.root.all().find((n) => n.id === id);
  assert.match(span.textContent, /rain down or spiral in/);
});

test('placeTip: above when it fits, flipped below when not, always inside the sheet, the tail on the target', () => {
  const box = { left: 0, top: 0, width: 1000, height: 560 };
  const up = placeTip({ left: 400, top: 300, width: 120, height: 150 }, { width: 220, height: 44 }, box);
  assert.equal(up.below, false); assert.equal(up.y, 300 - 44 - 10); assert.equal(up.x, 400 + 60 - 110);
  const down = placeTip({ left: 10, top: 20, width: 120, height: 150 }, { width: 220, height: 44 }, box);
  assert.equal(down.below, true); assert.equal(down.y, 180); assert.equal(down.x, 6);
  assert.equal(down.tail, 64);   // still points at the card's middle (70) from the clamped left edge
  const right = placeTip({ left: 960, top: 300, width: 40, height: 40 }, { width: 220, height: 44 }, box);
  assert.ok(right.x + 220 <= 1000 - 6);
});

/** createTips on the fake DOM with a hand-run clock. */
function tipsRig(win = {}) {
  const doc = fakeDocument(), root = doc.createElement('section'), timers = [];
  const later = (f, ms) => { timers.push({ f, ms }); return timers.length; };
  const tips = createTips({ doc, root, win, later, cancel: (h) => { if (timers[h - 1]) timers[h - 1].f = null; } });
  const run = () => timers.splice(0).forEach((x) => x.f && x.f());
  const card = doc.createElement('button');
  root.append(card);
  tips.bind(card, 'Every bubble becomes a mirror ball');
  return { doc, root, tips, card, run, timers };
}

test('the tip shows after a hover delay and goes on leave, a press, a scroll and a key', () => {
  const r = tipsRig();
  r.card.dispatch('pointerenter', { pointerType: 'mouse' });
  assert.equal(r.tips.el.hidden, true, 'not at once');
  assert.equal(r.timers[0].ms, TIP_DELAY_MS);
  r.run();
  assert.equal(r.tips.el.hidden, false);
  assert.equal(r.tips.el.getAttribute('role'), 'tooltip');
  assert.match(r.tips.el.text(), /mirror ball/);
  assert.match(r.tips.el.getAttribute('style'), /left:\d+px;top:-?\d+px;--tail:\d+px/);
  r.card.dispatch('pointerleave', {}); assert.equal(r.tips.el.hidden, true, 'leave');
  r.card.dispatch('pointerenter', { pointerType: 'mouse' }); r.run();
  r.root.dispatch('pointerdown', {}); assert.equal(r.tips.el.hidden, true, 'press');
  r.card.dispatch('pointerenter', { pointerType: 'mouse' }); r.run();
  r.root.dispatch('scroll', {}); assert.equal(r.tips.el.hidden, true, 'scroll');
  r.card.dispatch('pointerenter', { pointerType: 'mouse' }); r.run();
  r.tips.key({ key: ' ' }); assert.equal(r.tips.el.hidden, true, 'key');
  // a leave before the delay runs out shows nothing
  r.card.dispatch('pointerenter', { pointerType: 'mouse' }); r.card.dispatch('pointerleave', {}); r.run();
  assert.equal(r.tips.el.hidden, true, 'cancelled');
});

test('keyboard focus shows the tip after Tab, a pointer press refocus does not, blur hides it', () => {
  const r = tipsRig();
  r.card.dispatch('focus', {}); r.run();
  assert.equal(r.tips.el.hidden, true, 'a refocus after a click shows nothing');
  r.tips.key({ key: 'Tab' });
  r.card.dispatch('focus', {}); r.run();
  assert.equal(r.tips.el.hidden, false, 'Tab then focus');
  r.card.dispatch('blur', {}); assert.equal(r.tips.el.hidden, true);
  // the tip itself never takes focus and catches nothing
  assert.notEqual(r.tips.el.tagName, 'BUTTON');
  assert.equal(r.tips.el.tabIndex, undefined);
  const css = fs.readFileSync(path.join(page, 'sheet.css'), 'utf8');
  assert.match(css, /\.s-tip \{[^}]*pointer-events: none/);
});

test('touch: nothing pops up, not on a touch hover nor in a coarse-pointer window', () => {
  const r = tipsRig();
  r.card.dispatch('pointerenter', { pointerType: 'touch' }); r.run();
  assert.equal(r.tips.el.hidden, true);
  const coarse = tipsRig({ matchMedia: (q) => ({ matches: q.includes('coarse') }) });
  coarse.card.dispatch('pointerenter', { pointerType: 'mouse' }); coarse.run();
  assert.equal(coarse.tips.el.hidden, true);
  assert.equal(coarse.tips.touch(), true);
});

async function rig({ win: w = {}, init = {}, preview = false } = {}) {
  const server = preview ? createPreviewServer(SAMPLE) : createMockServer(SAMPLE);
  const host = asHost({ init: { sp: 1850, motion: 'full', ...init }, request: (op, b, idem) => server.handle(op, b, idem), wallet() {}, close() {}, wear() {}, log() {} });
  const doc = fakeDocument(), root = doc.createElement('main'), ls = {};
  const win = { addEventListener: (k, f) => (ls[k] ||= []).push(f), removeEventListener() {}, fire: (k, e) => (ls[k] || []).forEach((f) => f(e)), ...w };
  let T = 0, n = 0;
  const pageRef = await mountSheet(host, { root, doc, win, now: () => T, frame: (f) => f(), later() {}, loadIndex: () => null, newIdem: () => `idem-${String(++n).padStart(12, '0')}` });
  const btn = (key) => pageRef.view.root.all().find((x) => x.dataset && x.dataset.key === key) || null;
  return { page: pageRef, doc, win, btn, click: (key) => pageRef.view.root.dispatch('click', { target: btn(key), detail: 1 }), wait: (ms) => { T += ms; } };
}

test('the fitting room says what it does, where and how; a hovered card still picks on a click', async () => {
  const r = await rig({ init: { tab: 'backroom' } });
  const card = r.btn('it:bubbles_v2');
  card.dispatch('pointerenter', { pointerType: 'mouse' });   // the tip is armed (the rig's clock never runs it)
  r.click('it:bubbles_v2');
  assert.equal(r.page.shop.card().itemId, 'bubbles_v2', 'the click still picks');
  const what = r.page.view.room.all().find((n) => n.classList.contains('d-what'));
  assert.ok(what, 'block');
  const words = what.text();
  assert.match(words, /What it does/);
  assert.match(words, /Brain Drain and Magnet/);
  assert.match(words, /Where Bubble Pop/);
  assert.match(words, /How Pick them in Bubble Pop/);
  // the block sits above the pinned Buy row
  const kids = r.page.view.room.children, at = kids.indexOf(what), act = kids.findIndex((n) => n.classList.contains('d-act'));
  assert.ok(at > 0 && at < act, `${at} < ${act}`);
  assert.equal(r.page.view.room.all().filter((n) => n.classList.contains('d-tip')).length, 0, 'no tip line where hover exists');
});

test('touch: the tip line sits under the name in the fitting room and no "?" marks are drawn', async () => {
  const r = await rig({ win: { matchMedia: (q) => ({ matches: q.includes('coarse') }) }, init: { tab: 'backroom' }, preview: true });
  r.click('it:bubbles_v2');
  const kids = r.page.view.room.children, name = kids.findIndex((n) => n.classList.contains('d-name'));
  assert.ok(kids[name + 1].classList.contains('d-tip'), 'the line follows the name');
  assert.match(kids[name + 1].textContent, /rain down or spiral in/);
  r.click('tab:featured');
  assert.deepEqual(r.page.view.root.all().filter((n) => n.classList.contains('qm') && !n.hidden).map((n) => n.dataset.key), []);
});

test('the "?" marks: one line each, a press shows it, and a stray Space never buys through one', async () => {
  const r = await rig({ preview: true });
  const marks = r.page.view.root.all().filter((n) => n.classList.contains('qm') && !n.hidden).map((n) => n.dataset.key);
  assert.deepEqual(marks.sort(), ['help:can', 'help:gift', 'help:saving', 'help:stamps', 'help:wallet'].sort());
  r.click('help:gift');
  assert.equal(r.page.view.tips.el.hidden, false);
  assert.match(r.page.view.tips.el.text(), /once a week/);
  assert.equal(r.page.shop.card(), null, 'nothing picked, nothing bought');
});

test('init.fogColour reaches the view as v.fogColour (the Two-tone preview\'s colour one); junk does not', async () => {
  for (const [given, want] of [['#12abef', '#12abef'], ['pink', null], [undefined, null]]) {
    const r = await rig({ init: { fogColour: given }, preview: true });
    const paint = r.page.view.paint;
    let seen = null;
    r.page.view.paint = (v, on) => { seen = v; return paint(v, on); };
    r.page.pick('fx.fog.twotone');
    assert.equal(seen.fogColour, want, String(given));
    if (want) assert.equal(r.page.view.live.looks(seen).own, want);
  }
});

test('the Whisper look is a spiral back with the word coming at you from its centre', async () => {
  const { drawSpiralBack, backWord, BACK_ZOOM, BACK_LIFE } = await import('../sheet/looks.js');
  const calls = [];
  const g = new Proxy({}, { get: (o, k) => (k in o ? o[k] : (...a) => (calls.push([k, ...a]), { addColorStop() {} })), set: (o, k, v) => { o[k] = v; return true; } });
  drawSpiralBack(g, 'sink', 0.5, 10, 10, 40, 60, 0);
  const words = calls.filter((c) => c[0] === 'fillText' || c[0] === 'strokeText');
  assert.ok(words.length >= 2, 'the word, bold over an outline');
  assert.ok(words.every((c) => c[2] === 0 || Math.abs(c[2]) < 1), 'centred on the spiral');
  assert.ok(calls.findIndex((c) => c[0] === 'stroke') < calls.findIndex((c) => c[0] === 'fillText'), 'over the spiral');
  assert.ok(backWord(0.02).s < 0.3, 'small and far at first');
  assert.ok(backWord(BACK_ZOOM + 0.01).s > 0.95, 'full size after the zoom');
  assert.ok(backWord(BACK_LIFE - 0.05).s > 1.05 && backWord(BACK_LIFE - 0.05).a < 0.2, 'passes you as it fades');
  assert.equal(backWord(0.5, true).s, 1, 'motion off: it fades in place');
});
