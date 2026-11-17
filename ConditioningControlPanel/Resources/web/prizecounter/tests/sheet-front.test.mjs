// The Featured tab on the fake DOM: every part of the mockup's front page from the preview wire, and none of it on
// today's server body.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fakeDocument } from './fake-dom.mjs';
import { createMockServer } from '../mock/server.js';
import { createPreviewServer } from '../mock/preview.js';
import { asHost } from '../host.js';
import { mountSheet } from '../sheet/main.js';

const SAMPLE = JSON.parse(readFileSync(new URL('../mock/state.sample.json', import.meta.url), 'utf8'));
const fakeWin = () => ({ addEventListener() {}, removeEventListener() {} });
async function mountOn(server, init = {}) {
  const calls = [];
  const host = asHost({ init: { motion: 'full', ...init }, request: (op, b, idem) => { calls.push({ op, b }); return server.handle(op, b, idem); },
    wallet: (sp) => calls.push({ type: 'wallet', sp }), close: (reason) => calls.push({ type: 'close', reason }), wear: (id) => calls.push({ type: 'wear', id }) });
  const doc = fakeDocument(), root = doc.createElement('main');
  const page = await mountSheet(host, { root, doc, win: fakeWin(), now: () => 1e6, frame: (f) => f(), later() {}, loadIndex: () => null });
  const btn = (key) => page.view.root.all().find((x) => x.dataset && x.dataset.key === key) || null;
  return { page, root, calls, btn, text: () => page.view.list.text() };
}

test('the preview opens on Featured: spotlight, gift, stamp card, saving for, receipt, You can buy now', async () => {
  const r = await mountOn(createPreviewServer(SAMPLE));
  assert.equal(r.page.ui.tab, 'featured');
  const s = r.text();
  for (const w of ['New this week', 'Marble bubbles', 'Try it on', 'Also new', 'Heart bubbles', 'Gifts and stamps', 'Monday gift', 'Open, +25',
    'Stamp card', 'Stamp day 5', '1 free item', '3 of 6 stamps', 'Saving for', 'Spiral bubbles', '320 to go', 'Earned and spent',
    'Won at chess', 'The Suggestible Duck', 'In the wallet', 'You can buy now', 'with the 410 in your wallet']) assert.ok(s.includes(w), w);
  assert.match(r.page.view.head.text(), /Preview: nothing here is real/);
  assert.match(r.btn('tab:featured').text(), /2/);
  assert.ok(r.btn('gift') && !r.btn('gift').disabled);
  r.btn('spot:fx.bubble.marble').onpress(false);
  assert.equal(r.page.ui.fx, 'fx.bubble.marble');
  assert.match(r.btn('spot:fx.bubble.marble').text(), /Trying it on/);
  r.btn('also:fx.bubble.heart').onpress(false);
  assert.equal(r.page.ui.spot, 'fx.bubble.heart');
  assert.match(r.text(), /New this week Heart bubbles/);
});

test('today\'s server body draws no Featured tab, no slip and no front page parts', async () => {
  const r = await mountOn(createMockServer(SAMPLE), { tab: 'kart' });
  assert.equal(r.btn('tab:featured'), null);
  assert.equal(r.page.view.head.all().find((n) => n.className === 's-slip').hidden, true);
  assert.equal(r.page.ui.tab, 'games');
  assert.doesNotMatch(r.text(), /Monday gift|Stamp card|Saving for/);
});

test('a front page with only some fields draws only those parts', async () => {
  const server = createPreviewServer(SAMPLE), handle = server.handle;
  server.handle = async (op, b, i) => { const res = await handle(op, b, i); if (op === 'shop-state') { delete res.stamps; delete res.goal; delete res.fresh; } return res; };
  const r = await mountOn(server);
  const s = r.text();
  assert.ok(s.includes('Monday gift') && s.includes('Earned and spent'));
  assert.ok(!s.includes('Stamp card') && !s.includes('Saving for') && !s.includes('New this week'));
});
