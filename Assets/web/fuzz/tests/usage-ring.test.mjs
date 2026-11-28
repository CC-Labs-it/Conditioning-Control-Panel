import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { usageRingState, RING_COPY, PRIME_URL } from '../ui/usage-ring.js';
import { createPictures } from '../ui/pictures.js';
import { allowedLink } from '../ui/links.js';

test('the ring shows only for Scrolller on the web host with a known percent', () => {
  assert.equal(usageRingState({ source: 'scrolller', percent: 40, web: false }).visible, false);
  assert.equal(usageRingState({ source: 'own', percent: 40, web: true }).visible, false);
  assert.equal(usageRingState({ source: 'scrolller', percent: null, web: true }).visible, false);
  assert.equal(usageRingState({ source: 'scrolller', percent: 101, web: true }).visible, false);
  const s = usageRingState({ source: 'scrolller', percent: 40, web: true });
  assert.equal(s.visible, true);
  assert.equal(s.label, 'Online pictures today: 40% used');
  assert.equal(s.nudge, false);
  assert.equal(s.tone, 'ok');
});

test('the Prime line appears from 80% only when the host says a bigger share exists', () => {
  assert.equal(usageRingState({ source: 'scrolller', percent: 79, upgrade: true, web: true }).nudge, false);
  assert.equal(usageRingState({ source: 'scrolller', percent: 85, upgrade: true, web: true }).nudge, true);
  assert.equal(usageRingState({ source: 'scrolller', percent: 85, upgrade: false, web: true }).nudge, false);
  const spent = usageRingState({ source: 'scrolller', percent: 100, upgrade: true, web: true });
  assert.equal(spent.spent, true);
  assert.equal(spent.nudge, true);
  assert.equal(spent.line, 'Today\'s online pictures are used up. Local files still work.');
});

test('the Prime line names the multiple the host computed, else stays plain', () => {
  const at = (upgradeX) => usageRingState({ source: 'scrolller', percent: 85, upgrade: true, upgradeX, web: true }).nudgeLine;
  assert.equal(at(20), 'Need more pictures? Prime gets 20x more pictures a day.');
  assert.equal(at(3), 'Need more pictures? Prime gets 3x more pictures a day.');
  assert.equal(at(1), 'Need more pictures? Prime gets the most pictures a day.');
  assert.equal(at(null), 'Need more pictures? Prime gets the most pictures a day.');
  assert.equal(at(2.5), 'Need more pictures? Prime gets the most pictures a day.');
});

test('the copy is plain: percent only, no sizes, no em dashes, no exclamation marks', () => {
  for (const line of Object.values(RING_COPY)) {
    assert.doesNotMatch(line, /[–—!]/);
    assert.doesNotMatch(line, /\b(MB|GB|KB|megabyte)/i);
  }
  assert.equal(allowedLink(PRIME_URL), PRIME_URL);
});

test('pictures keep the upgrade flag only beside a meter reading', () => {
  const p = createPictures({ scrolller: { consent: true }, send: () => {} });
  p.receiveScrolller({ v: 1, rows: [], usedPercent: 85, upgrade: true });
  assert.equal(p.usedPercent, 85);
  assert.equal(p.meterUpgrade, true);
  assert.equal(p.meterUpgradeX, null);
  p.receiveScrolller({ v: 1, rows: [], usedPercent: 86, upgrade: true, upgradeX: 20 });
  assert.equal(p.meterUpgradeX, 20);
  p.receiveScrolller({ v: 1, rows: [], usedPercent: 86, upgrade: false, upgradeX: 20 });
  assert.equal(p.meterUpgradeX, null, 'no multiple without an upgrade');
  p.receiveScrolller({ v: 1, rows: [], upgrade: true });
  assert.equal(p.usedPercent, null);
  assert.equal(p.meterUpgrade, false);
});

test('the ring sits in the top controls, outside inPress, and checks its own click', () => {
  const src = readFileSync(new URL('../ui/usage-ring.js', import.meta.url), 'utf8');
  assert.match(src, /inPress: \(\) => ev\.isTrusted/);
  assert.doesNotMatch(src, /requestAnimationFrame|\.animate\(/);
  const css = readFileSync(new URL('../styles.css', import.meta.url), 'utf8').split(/\r?\n/).filter(l => /usage-ring|\.ur-/.test(l)).join('\n');
  assert.ok(css.length > 0);
  assert.doesNotMatch(css, /animation|transition/);
});
