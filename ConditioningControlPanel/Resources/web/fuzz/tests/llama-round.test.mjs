import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { herdOf, herdWords, newcomers, section12, penLine, penModel, penStep, namedCounts, PEN_MAX } from '../ui/llama-pen.js';
import { tickerMs, iphoneSafari, standalone, homeTipDue, HOME_TIP, HOME_TIP_KEY, PHONE_QUERY } from '../ui/phone.js';
import { UPDATE_LINES } from '../data/content/terms.js';

const read = (p) => readFileSync(new URL(p, import.meta.url), 'utf8');

test('the pen shows the saved herd, three to twelve, in contract words', () => {
  assert.equal(herdOf({}), 3);
  assert.equal(herdOf({ llamas: 5 }), 5);
  assert.equal(herdOf({ llamas: 40 }), 12);
  assert.equal(herdOf({ llamas: 'x' }), 3);
  assert.equal(herdWords(5), 'five (5)');
  assert.equal(herdWords(12), 'twelve (12)');
  assert.equal(penModel({ llamas: 11 }, 11).gate, false);
  assert.equal(penModel({ llamas: PEN_MAX }, 12).gate, true, 'the gate shows at twelve');
});

test('llamas that joined since the pen last showed walk in, a save from before the pen counts from three', () => {
  assert.equal(newcomers(undefined, 3), 0);
  assert.equal(newcomers(undefined, 7), 4);
  assert.equal(newcomers(5, 7), 2);
  assert.equal(newcomers(7, 7), 0);
  assert.equal(newcomers(9, 7), 0, 'a smaller herd never walks anyone out');
});

test('twelve llamas fit the corner above the dock band', () => {
  const w = (n) => 12 + penStep(n) * n + 6 + 20;   // padding + steps + tail + gate
  assert.ok(w(12) < 200, `twelve llamas are ${w(12)} px, the rail is at least 200`);
  assert.ok(w(6) < 200);
});

test('the press line is the last update accepted, or Section 12 with the herd number before any update', () => {
  assert.equal(penLine({ updates: 1, llamas: 4 }), UPDATE_LINES[0]);
  assert.equal(penLine({ updates: 3 }), UPDATE_LINES[2]);
  assert.equal(penLine({ updates: UPDATE_LINES.length + 1, llamas: 4 }), UPDATE_LINES[0]);
  const first = penLine({ updates: 0, llamas: 6 });
  assert.equal(first, section12(6));
  assert.match(first, /^12\. Charitable Obligations\. .*six \(6\) llamas/);
  assert.doesNotMatch(first, /[<>]/);
});

test('the bubble never names a llama count the pen does not show', () => {
  assert.deepEqual(namedCounts('Section 12 now covers five llamas. One of them has read your file.'), [5]);
  assert.deepEqual(namedCounts('One llama has been promoted to supervisor.'), [1]);
  assert.deepEqual(namedCounts('The llamas now have dental.'), []);
  assert.deepEqual(namedCounts('Shelter 3 llamas.'), [3]);
  assert.equal(penLine({ updates: 2, llamas: 12 }), section12(12), 'five llamas against a herd of twelve falls back');
  assert.equal(penLine({ updates: 2, llamas: 5 }), UPDATE_LINES[1], 'five llamas with a herd of five stands');
  for (let n = 3; n <= 12; n += 1) {
    for (let u = 0; u <= UPDATE_LINES.length * 2; u += 1) {
      const line = penLine({ updates: u, llamas: n });
      assert.ok(namedCounts(line).every((c) => c === n), `herd ${n}, update ${u}: ${line}`);
    }
  }
});

test('phone ticker runs slower than the desktop one, desktop speed unchanged', () => {
  assert.equal(tickerMs(700, 280, false), Math.max(4000, (980 / 70) * 1000));
  assert.ok(tickerMs(360, 300, true) > tickerMs(360, 300, false) * 1.5);
  assert.ok(tickerMs(10, 10, true) >= 7000);
  const css = read('../styles.css');
  assert.ok(css.includes(`@media ${PHONE_QUERY} {\n  #stage:has(> #l-ticker.on)`), 'the phone rules use the same query as the script');
});

test('the home screen tip: iPhone Safari only, not standalone, once', () => {
  const safari = 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1';
  const chrome = 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) CriOS/131.0 Mobile/15E148 Safari/604.1';
  const webview = 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148';
  const ipad = 'Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1';
  assert.equal(iphoneSafari(safari), true);
  for (const ua of [chrome, webview, ipad, 'Mozilla/5.0 (Linux; Android 14) Chrome/142.0 Mobile Safari/537.36']) assert.equal(iphoneSafari(ua), false, ua);
  const mem = () => { const m = new Map(); return { getItem: (k) => m.get(k) ?? null, setItem: (k, v) => m.set(k, v) }; };
  const win = (ua, extra = {}) => ({ navigator: { userAgent: ua, ...extra }, matchMedia: () => ({ matches: false }) });
  const st = mem();
  assert.equal(homeTipDue(win(safari), st), true);
  assert.equal(homeTipDue(win(safari, { standalone: true }), st), false, 'already on the home screen');
  assert.equal(standalone({ navigator: {}, matchMedia: (q) => ({ matches: q === '(display-mode: standalone)' }) }), true);
  st.setItem(HOME_TIP_KEY, '1');
  assert.equal(homeTipDue(win(safari), st), false, 'remembered');
  assert.equal(homeTipDue(win(safari), { getItem() { throw new Error('blocked'); } }), false, 'storage that throws never nags');
  assert.equal(HOME_TIP, 'Tip: Share, then Add to Home Screen.');
});

test('New run sits beside Resume with the Forget two-tap, and calls the life-keeping newRun', () => {
  const html = read('../index.html');
  assert.match(html, /<button id="resume"[^>]*>Resume<\/button><button id="new-run" type="button">New run<\/button>/);
  const app = read('../ui/app.js');
  assert.ok(app.includes("again.textContent = 'Sure. Start over.'"));
  assert.ok(app.includes('hooks.newRun()'));
  const main = read('../main.js');
  assert.ok(main.includes('const newRun = () => { leaving = true; extras.again = 1; store(storage, null, life, extras); location.reload(); };'), 'keeps life, skips the terms');
});

test('copy voice: no dashes or exclamation marks in the new lines', () => {
  for (const s of [HOME_TIP, 'New run', 'Sure. Start over.', herdWords(5), section12(5), ...UPDATE_LINES]) assert.doesNotMatch(s, /[–—!]/, s);
});
