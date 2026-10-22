// "via Scrolller" mark (2026-10-17): the pure rule, the URL / id tests, and the pool's ping.
//
//   node Resources/web/goon/test/selftest-scrolllermark.js

import fs from 'node:fs';
import url from 'node:url';
import {
  SCROLLLER_TEXT, isScrolllerUrl, markShouldShow, createScrolllerMark, MARK_HOLD_MS, domShowsScrolller,
} from '../exec/scrolllerMark.js';
import { createGoonMediaPool } from '../exec/media.js';

let failures = 0;
let n = 0;
function ok(cond, label, extra = '') {
  n++;
  if (!cond) { failures++; console.error(`  FAIL ${label} ${extra}`); }
}
const read = (p) => fs.readFileSync(url.fileURLToPath(new URL(p, import.meta.url)), 'utf8').replace(/\r\n/g, '\n');

// 1. telling Scrolller's media
ok(SCROLLLER_TEXT === 'via Scrolller', 'brand text, three l');
ok(isScrolllerUrl('https://images.scrolller.com/a/b.jpg'), 'cdn host');
ok(isScrolllerUrl('https://api.scrolller.com/admin'), 'api host');
ok(isScrolllerUrl('https://scrolller.com/r/x'), 'bare host');
ok(isScrolllerUrl('https://frame.example/scrolller-media/sub/a.jpg'), 'discord proxy path');
ok(!isScrolllerUrl('https://ccp.assets/images/a.jpg'), 'local host url');
ok(!isScrolllerUrl('https://evilscrolller.com/a.jpg'), 'look-alike host');
ok(!isScrolllerUrl('blob:abc') && !isScrolllerUrl('') && !isScrolllerUrl(null), 'junk');

// 2. the pure rule
ok(!markShouldShow({}), 'nothing shown');
ok(markShouldShow({ pins: 1 }), 'a pinned surface');
ok(markShouldShow({ lastNoteAt: 1000, now: 1000 + MARK_HOLD_MS - 1 }), 'inside the hold');
ok(!markShouldShow({ lastNoteAt: 1000, now: 1000 + MARK_HOLD_MS }), 'hold over');

// 3. the mark without a document keeps state
{
  let t = 5000;
  const m = createScrolllerMark({ doc: { createElement: null }, now: () => t, holdMs: 100 });
  ok(!m.shown(), 'starts hidden');
  m.note(); ok(m.shown(), 'note shows');
  m.pin('duel', true); t += 500; m.note(); m.pin('x', false); ok(m.shown(), 'pin holds');
  m.pin('duel', false); t += 500; m.pin('duel', false); ok(!m.shown(), 'unpinned and past the hold hides');
  m.clear(); m.destroy();
}

// 3b. the DOM read: on-screen Scrolller img counts, local or hidden does not
{
  const fakeEl = (attrs, rect, cs = {}) => ({
    tagName: 'IMG', isConnected: true,
    getAttribute: (k) => attrs[k] || null,
    getBoundingClientRect: () => rect,
    _cs: { visibility: 'visible', opacity: '1', ...cs },
  });
  const win = { innerWidth: 800, innerHeight: 600, getComputedStyle: (e) => e._cs };
  const doc = (els) => ({ querySelectorAll: () => els });
  const big = { width: 100, height: 100, left: 10, top: 10, right: 110, bottom: 110 };
  ok(domShowsScrolller(doc([fakeEl({ src: 'https://images.scrolller.com/a.jpg' }, big)]), win), 'dom: scrolller img on screen');
  ok(!domShowsScrolller(doc([fakeEl({ src: 'https://ccp.assets/scrolller-notes.jpg' }, big)]), win), 'dom: local url with the word is not');
  ok(!domShowsScrolller(doc([fakeEl({ src: 'https://images.scrolller.com/a.jpg' }, { width: 0, height: 0 })]), win), 'dom: collapsed is not on screen');
  ok(!domShowsScrolller(doc([fakeEl({ src: 'https://images.scrolller.com/a.jpg' }, big, { opacity: '0' })]), win), 'dom: see-through is not');
  ok(domShowsScrolller(doc([fakeEl({ style: 'background-image: url("https://images.scrolller.com/b.jpg")' }, big)]), win), 'dom: inline background');
  ok(!domShowsScrolller(null) && !domShowsScrolller({}), 'dom: no document');
}

// 4. the pool pings only for online items
{
  const pool = createGoonMediaPool();
  let pings = 0;
  pool.setOnlineShownHandler(() => { pings++; });
  pool.setLocalLibrary([{ kind: 'image', name: 'mine.jpg', url: 'https://ccp.assets/mine.jpg' }]);
  for (let i = 0; i < 5; i++) pool.draw();
  ok(pings === 0, 'own files never ping', String(pings));
  pool.setLocalLibrary([]);
  pool.setOnlineLibrary({ images: [{ name: 'online30:a.jpg', url: 'https://ccp.assets/.temp/a.jpg' }] });
  pool.draw();
  ok(pings === 1, 'an online draw pings', String(pings));
}

// 5. wiring pins
{
  const boot = read('../boot.js');
  ok(boot.includes('setOnlineShownHandler(() => pageMark().note())'), 'boot wires the pool to the mark');
  const dv = read('../ui/duel/duelView.js');
  ok(dv.includes("pageMark().pin('duel', true)") && dv.includes("pageMark().pin('duel', false)"), 'duel pins and unpins');
  const css = read('../exec/scrolllerMark.js');
  ok(css.includes('pointer-events:none'), 'mark never takes input');
}

console.log(`selftest-scrolllermark: ${n - failures}/${n} passed`);
if (failures) process.exit(1);
