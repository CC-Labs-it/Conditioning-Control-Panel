import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createPictures, feedNiches, FEED_NICHES } from '../ui/pictures.js';
import { THEME_IDS } from '../ui/palette.js';
import { createEngine } from '../ui/engine.js';
import * as E from '../sim/economy.js';
import { createLife } from '../sim/save.js';

test('the eight themes are the eight feed niches, Locked sent as chastity', () => {
  assert.equal(FEED_NICHES.length, 8);
  assert.deepEqual(THEME_IDS.map((t) => feedNiches([t])[0]).sort(), [...FEED_NICHES].sort());
  assert.deepEqual(feedNiches(['locked']), ['chastity']);
  assert.deepEqual(feedNiches([]), []);
  assert.deepEqual(feedNiches(undefined), []);
  assert.deepEqual(feedNiches(['goon', 'goon']), ['goon']);
  assert.deepEqual(feedNiches(['nope', 'sissy', 'trance', 'bimbo']), ['sissy', 'trance'], 'unknown ids drop, two at most');
});

test('the feed asks for the current niches and asks again only when they change', () => {
  const sent = [];
  let themes = [];
  const p = createPictures({ scrolller: { consent: true }, send: (...a) => sent.push(a), niches: () => themes });
  p.refreshNiches();
  assert.deepEqual(sent, [], 'nothing asked yet, so nothing to refresh');
  p.requestFeed();
  assert.deepEqual(sent.at(-1), ['scrolller-niches', { niches: [] }], 'no theme yet: the general mix');
  p.refreshNiches();
  assert.equal(sent.length, 1, 'same niches, no second ask');
  themes = ['locked'];
  p.refreshNiches();
  assert.deepEqual(sent.at(-1), ['scrolller-niches', { niches: ['chastity'] }]);
  themes = ['locked', 'goon'];
  p.refreshNiches();
  assert.deepEqual(sent.at(-1), ['scrolller-niches', { niches: ['chastity', 'goon'] }]);
  assert.deepEqual(p.niches, ['chastity', 'goon']);
  assert.equal(sent.length, 3);
});

test('a source other than Scrolller never asks for niches', () => {
  const sent = [];
  const p = createPictures({ source: 'own', scrolller: { consent: true }, send: (...a) => sent.push(a), niches: () => ['trance'] });
  p.requestFeed(); p.refreshNiches();
  assert.deepEqual(sent, []);
});

test('the engine re-asks on Pick a Theme, a Second Theme and switching the second off', () => {
  const sent = [];
  const run = E.createRun();
  const ui = { doc: {}, buttonFor: () => ({}), layer: () => ({}) };
  const engine = createEngine({ run, life: createLife(), ui, registry: {}, host: { send: (t, f) => sent.push([t, f]) },
    env: { hosted: true, caps: { scrolller: { consent: true } } } });
  assert.equal(engine.pictures.source, 'scrolller');
  engine.pictures.requestFeed();
  const asks = () => sent.filter(([t]) => t === 'scrolller-niches').map(([, f]) => f.niches);
  assert.deepEqual(asks(), [[]]);
  run.picks.f5 = 'trance';
  engine.emit('fork', { fork: 'f5', lane: 'trance' });
  assert.deepEqual(asks().at(-1), ['trance']);
  run.store.taste2 = { theme: 'locked' };
  engine.emit('taste2', { theme: 'locked' });
  assert.deepEqual(asks().at(-1), ['trance', 'chastity']);
  run.off.taste2 = 1;
  engine.emit('switch', { id: 'taste2', on: false });
  assert.deepEqual(asks().at(-1), ['trance']);
  assert.equal(asks().length, 4);
});
