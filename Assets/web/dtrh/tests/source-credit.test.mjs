import test from 'node:test';
import assert from 'node:assert/strict';

import {
  CREDIT_TEXT, isScrolllerUrl, isScrolllerEntry, creditText, createCreditClock, sourceCredit,
} from '../shared/sourceCredit.js';
import { createHostMediaSource } from '../hostMedia.js';

test('the credit is spelled with three l', () => {
  assert.equal(CREDIT_TEXT, 'via Scrolller');
  assert.equal(creditText(), 'via Scrolller');
  assert.equal(creditText('EroticHypnosis'), 'via Scrolller · r/EroticHypnosis');
  assert.equal(creditText('bad sub/../x'), 'via Scrolller');
});

test('scrolller hosts are Scrolller, look-alikes and local origins are not', () => {
  for (const u of ['https://images.scrolller.com/a/b.jpg', 'https://api.scrolller.com/admin', 'https://scrolller.com/r/x', 'https://cdn.scrolller.com/v.mp4']) {
    assert.equal(isScrolllerUrl(u), true, u);
  }
  for (const u of ['https://ccp.assets/x.jpg', 'https://evilscrolller.com/x', 'https://scrolller.com.evil.io/x', 'blob:abc', '', null, 'not a url']) {
    assert.equal(isScrolllerUrl(u), false, String(u));
  }
});

test('an entry is Scrolller by flag, by the host name marker or by its url', () => {
  assert.equal(isScrolllerEntry({ online: true }), true);
  // a copy kept on this machine: local origin, marker still says where it came from
  assert.equal(isScrolllerEntry({ name: 'online30:Sub-123.webm', url: 'https://ccp.assets/.temp/a.webm' }), true);
  assert.equal(isScrolllerEntry({ name: 'online100:loop:Sub-1.mp4', url: 'https://ccp.assets/x.mp4' }), true);
  assert.equal(isScrolllerEntry({ name: 'x.jpg', url: 'https://images.scrolller.com/x.jpg' }), true);
  assert.equal(isScrolllerEntry({ name: 'my-online-pic.jpg', url: 'https://ccp.assets/my-online-pic.jpg' }), false);
  assert.equal(isScrolllerEntry({ name: 'mod:spiral.gif', url: 'https://ccp.assets/m/spiral.gif' }), false);
  assert.equal(isScrolllerEntry(null), false);
});

test('the clock: a touch holds the mark for its window, a new touch only extends it', () => {
  const c = createCreditClock();
  assert.equal(c.visible(0), false);
  c.touch(1000, 500);
  assert.equal(c.visible(1200), true);
  c.touch(1100, 100);            // shorter: never shortens the window
  assert.equal(c.visible(1450), true);
  assert.equal(c.expiresAt(1450), 1500);
  assert.equal(c.visible(1500), false);
  assert.equal(c.expiresAt(1600), 0);
});

test('the clock: a hold stands until dropped, clear drops everything', () => {
  const c = createCreditClock();
  c.hold('race-wall');
  assert.equal(c.visible(1e12), true);
  assert.equal(c.expiresAt(5), 0);
  c.drop('race-wall');
  assert.equal(c.visible(0), false);
  c.hold('a'); c.touch(0, 1e9);
  c.clear();
  assert.equal(c.visible(1), false);
});

test('the page mark is inert without a document', () => {
  assert.doesNotThrow(() => { sourceCredit.touch(10); sourceCredit.hold('k'); sourceCredit.drop('k'); sourceCredit.clear(); });
});

test('hostMedia tags Scrolller picks, local picks stay unmarked', async () => {
  globalThis.window = globalThis.window || {};
  const m = createHostMediaSource();
  m.setManifest({
    images: [
      { name: 'own.jpg', url: 'https://ccp.assets/own.jpg' },
      { name: 'online100:Sub-1.jpg', url: 'https://ccp.assets/.temp/sub-1.jpg' },
      { name: 'online100:Sub-2.jpg', url: 'https://images.scrolller.com/sub-2.jpg' },
    ],
    videos: [{ name: 'online100:loop:Sub-3.mp4', url: 'https://ccp.assets/.temp/sub-3.mp4' }],
  });
  assert.equal(m.hasOnline(), true);
  const seen = new Map();
  for (let i = 0; i < 12; i++) { const p = m.draw(); seen.set(p.name, p.online); }
  assert.equal(seen.get('own.jpg'), false);
  assert.equal(seen.get('Sub-1.jpg'), true);
  const remote = m.drawRemoteDom('image');
  assert.equal(remote.online, true);
  const loop = m.drawLoop();
  assert.equal(loop.online, true);
  const got = await loop.acquire();
  assert.equal(got.online, true);

  m.setManifest({ images: [{ name: 'own.jpg', url: 'https://ccp.assets/own.jpg' }] });
  assert.equal(m.hasOnline(), false);
  assert.equal(m.draw().online, false);
});
