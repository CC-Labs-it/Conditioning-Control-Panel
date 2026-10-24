/* ============================================================================
 * smoke/scrolller-mark-smoke.mjs - node checks for the "via Scrolller" mark
 * (2026-10-17): the pure rule, and the host pool pings only for online draws.
 *
 *   node smoke/scrolller-mark-smoke.mjs        (no server, no browser)
 * ==========================================================================*/

import assert from 'node:assert/strict';
import fs from 'node:fs';
import { createHostMedia, HOST_MSG } from '../ramp/media.js';
import { SCROLLLER_TEXT, isScrolllerUrl, markShouldShow, createScrolllerMark, MARK_HOLD_MS } from '../ramp/scrolllerMark.js';

let checks = 0;
const ok = (cond, name) => { assert.ok(cond, name); checks++; };

ok(SCROLLLER_TEXT === 'via Scrolller', 'brand text');
ok(isScrolllerUrl('https://images.scrolller.com/x.jpg') && !isScrolllerUrl('https://ccp.assets/.temp/x.jpg'), 'host test');
ok(!markShouldShow({}) && markShouldShow({ pins: 1 }), 'pins');
ok(markShouldShow({ lastNoteAt: 10, now: 10 + MARK_HOLD_MS - 1 }) && !markShouldShow({ lastNoteAt: 10, now: 10 + MARK_HOLD_MS }), 'hold');
{
  let t = 1;
  const m = createScrolllerMark({ doc: {}, now: () => t, holdMs: 50 });
  m.note(); ok(m.shown(), 'note shows');
  t += 60; m.pin('a', false); ok(!m.shown(), 'past the hold hides');
  m.destroy();
}

function rig() {
  const bridge = { postMessage() {}, addEventListener() {}, removeEventListener() {} };
  const host = createHostMedia([], { bridge, requestGapMs: 0, rnd: Math.random });
  let pings = 0;
  host.onOnlineShown = () => { pings++; };
  return { host, pings: () => pings };
}

{
  const { host, pings } = rig();
  host.handleMessage({ type: 'pbp:media', images: ['https://ccp.assets/lib/a.png', 'https://ccp.assets/lib/b.png'], gifs: [], videos: [] });
  for (let i = 0; i < 20; i++) host.draw('image');
  ok(pings() === 0, 'own pictures never ping');
}
{
  const { host, pings } = rig();
  host.handleMessage({ type: HOST_MSG.online, state: 'ready', share: 100, images: ['https://ccp.assets/.temp/s0.jpg', 'https://ccp.assets/.temp/s1.jpg'], clips: [] });
  host.draw('image');
  ok(pings() >= 1, 'an online draw pings');
}
{
  const { host, pings } = rig();
  host.handleMessage({ type: 'pbp:media', images: ['https://images.scrolller.com/a.jpg'], gifs: [], videos: [] });
  host.draw('image');
  ok(pings() === 1, 'a deck row on a Scrolller host pings');
}
{
  const boot = fs.readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  ok(boot.includes('pool.onOnlineShown = () => pageMark().note()'), 'boot wires the mark');
}

console.log(`scrolller-mark-smoke: ${checks} checks passed`);
