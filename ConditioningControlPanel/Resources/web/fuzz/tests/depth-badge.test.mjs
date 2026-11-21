// T&C Ascension round, lane B: the Depth badge (ui/depth-badge.js) reads ascensionView and nothing else.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { depthBadgeState, tipLines, BADGE_COPY } from '../ui/depth-badge.js';
import { ascensionView } from '../sim/ascension.js';
import * as P from '../sim/prestige.js';

const seeded = () => {
  const p = P.createPrestige();
  p.depth = 4; p.dust = 1400;
  for (const id of ['pay_1', 'pay_2', 'head_1', 'trig_1']) p.own[id] = 1;
  p.lit = { drop: 1, bi_end: 1 };
  p.later.own.pay_3 = 1;
  return { prestige: p };
};

test('hidden at Depth 0 with nothing owned, lit or bought for later', () => {
  for (const life of [null, {}, { prestige: null }, { prestige: P.createPrestige() }, { prestige: 'junk' }]) {
    assert.equal(depthBadgeState(life).visible, false);
  }
  const lit = { prestige: P.createPrestige() }; lit.prestige.lit = { drop: 1 };
  assert.equal(depthBadgeState(lit).visible, true, 'an ending star alone shows the badge');
  const later = { prestige: P.createPrestige() }; later.prestige.later.own.pay_1 = 1;
  assert.equal(depthBadgeState(later).visible, true, 'a star bought for next run alone shows the badge');
  const deep = { prestige: P.createPrestige() }; deep.prestige.depth = 1;
  const s = depthBadgeState(deep);
  assert.equal(s.visible, true);
  assert.equal(s.empty, true);
});

test('the badge carries the view rows as they are, in the view order', () => {
  const life = seeded();
  const v = ascensionView(life), s = depthBadgeState(life);
  assert.equal(s.numeral, '4');
  assert.equal(s.label, 'Depth 4. Your Renewal bonuses.');
  assert.equal(s.dust, '1,400 Stardust');
  assert.deepEqual(s.bonuses, v.bonuses);
  assert.deepEqual(s.next, v.next);
  assert.deepEqual(s.next.map((b) => b.id), ['pay_3']);
  assert.equal(s.empty, false);
});

test('a tooltip names the bonus, its line, its plain words, and the next-run mark only when it is one', () => {
  const s = depthBadgeState(seeded());
  const now = s.bonuses.find((b) => b.id === 'pay_1');
  const t = tipLines(now);
  assert.deepEqual(t.map((l) => l.k), ['name', 'line', 'plain']);
  assert.equal(t[0].t, now.name);
  const later = tipLines(s.next[0]);
  assert.equal(later.at(-1).k, 'next');
  assert.equal(later.at(-1).t, BADGE_COPY.nextTip);
  const end = tipLines(s.bonuses.find((b) => b.kind === 'ending'));
  assert.ok(end.some((l) => l.k === 'plain'));
});

test('copy: no dashes of the long kind, no exclamation marks', () => {
  for (const v of Object.values(BADGE_COPY)) assert.doesNotMatch(String(v), /[–—!]/);
});

test('the badge never swallows Escape and fades only under Gentle and reduced motion', () => {
  const js = readFileSync(new URL('../ui/depth-badge.js', import.meta.url), 'utf8');
  const esc = js.slice(js.indexOf("ev.key === 'Escape'") - 80, js.indexOf("ev.key === 'Escape'") + 120);
  assert.doesNotMatch(esc, /preventDefault|stopPropagation|stopImmediatePropagation/);
  const css = readFileSync(new URL('../styles.css', import.meta.url), 'utf8');
  assert.match(css, /body\.gentle \.db-bubble \{ animation-name: db-fade-in; \}/);
  assert.match(css, /prefers-reduced-motion: reduce\) \{ \.db-bubble \{ animation-name: db-fade-in; \}/);
  assert.match(js, /tc:prestige-changed/);
});
