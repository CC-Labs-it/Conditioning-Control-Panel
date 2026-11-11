import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import {
  COPY_PRICES, bounceTextLayout, bouncingLook, chosenCopy, copyScale, emiPose, faceFit, grandfatherCopies, ownedCopy,
} from '../ui/bouncing-art.js';
import { BAMBI_TRIGGERS } from '../data/content/theme-text.js';

test('long trigger captions keep every word on two compact lines', () => {
  for (const phrase of BAMBI_TRIGGERS.filter(text => text.length > 14)) {
    const layout = bounceTextLayout(phrase.toUpperCase());
    const lines = layout.text.split('\n');
    assert.equal(lines.length, 2, phrase);
    assert.equal(lines.join(' '), phrase.toUpperCase(), phrase);
    assert.ok(lines.every(line => line.length <= 13), phrase);
    assert.equal(layout.rows, 2);
    assert.equal(layout.columns, Math.max(...lines.map(line => line.length)));
  }
});

test('short bouncing captions stay on one line', () => {
  for (const phrase of ['DROP', 'BAMBI SLEEP', 'BAMBI FREEZE', 'GOOD GIRL']) {
    assert.deepEqual(bounceTextLayout(phrase), {text: phrase, rows: 1, columns: phrase.length});
  }
});

test('only Match button, Mixed faces and Normal are free; the rest are priced', () => {
  for (const [field, row] of Object.entries(COPY_PRICES)) {
    const free = Object.entries(row).filter(([, p]) => p === 0).map(([v]) => v);
    assert.equal(free.length, 1, field);
    for (const [v, p] of Object.entries(row)) if (p) assert.ok(p > 0 && p < 6000, field + ':' + v);
  }
  assert.equal(ownedCopy({}, 'bouncer', 'auto'), true);
  assert.equal(ownedCopy({}, 'bouncer', 'emi'), false);
  assert.equal(ownedCopy({ copyOwned: { 'bouncer:emi': true } }, 'bouncer', 'emi'), true);
  assert.equal(ownedCopy({ copyOwned: { 'bouncer:nope': true } }, 'bouncer', 'nope'), false);
});

test('an unbought pick never shows: the copies fall back to the free default', () => {
  assert.equal(chosenCopy({ bouncer: 'dvd' }, 'bouncer'), 'auto');
  assert.equal(chosenCopy({ bouncer: 'dvd', copyOwned: { 'bouncer:dvd': true } }, 'bouncer'), 'dvd');
  assert.equal(copyScale({}), 2, 'Normal is twice the first draft');
  assert.equal(copyScale({ copySize: 'big' }), 2);
  assert.equal(copyScale({ copySize: 'big', copyOwned: { 'copySize:big': true } }), 3);
  assert.equal(copyScale({ copySize: 'small', copyOwned: { 'copySize:small': true } }), 1);
  assert.ok(['happy', 'annoyed', 'rage'].includes(emiPose({ emiPose: 'happy' }, 1)), 'unowned pose cycles');
  assert.equal(emiPose({ emiPose: 'rage' }, 0), 'happy');
  assert.equal(emiPose({ emiPose: 'rage', copyOwned: { 'emiPose:rage': true } }, 0), 'rage');
});

test('a save that chose a look while it was free keeps it, once', () => {
  const kept = grandfatherCopies({ bouncer: 'emi', emiPose: 'annoyed', copySize: 'normal', copyOwned: { x: true } });
  assert.deepEqual(kept, { x: true, 'bouncer:emi': true, 'emiPose:annoyed': true });
  assert.equal(grandfatherCopies({ bouncer: 'emi', emiPose: 'annoyed', copyOwned: kept }), null);
  assert.equal(grandfatherCopies({ bouncer: 'auto' }), null);
  assert.equal(grandfatherCopies({ bouncer: 'bogus' }), null);
  assert.equal(grandfatherCopies(undefined), null);
});

test('Match button copies the face the button wears, the bubble, or the DVD logo on a plain button', () => {
  assert.equal(bouncingLook({}, 'bi-lips'), 'face');
  assert.equal(bouncingLook({}, 'tr-pendulum'), 'face');
  assert.equal(bouncingLook({}, 'some-new-face', false, true), 'face', 'any face the live button wears');
  assert.equal(bouncingLook({}, 'bubble'), 'bubble');
  assert.equal(bouncingLook({}, 'round'), 'dvd');
  assert.equal(bouncingLook({}, ''), 'dvd');
  assert.equal(bouncingLook({}, 'bi-lips', true), 'dvd', 'the big Bouncing Word stays a DVD word');
  assert.equal(bouncingLook({ bouncer: 'emi' }, 'bi-lips'), 'face', 'an unbought choice falls back');
  assert.equal(bouncingLook({ bouncer: 'emi', copyOwned: { 'bouncer:emi': true } }, 'bi-lips'), 'emi');
});

test('a face copy fits its size box and keeps its proportions', () => {
  for (const k of [1, 2, 3]) {
    const f = faceFit(224, 48, k);
    assert.ok(f.w <= Math.ceil(75 * k) && f.h <= Math.ceil(28 * k), String(k));
    assert.ok(Math.abs(f.w / f.h / (224 / 48) - 1) < .1, String(f.w / f.h));
  }
  assert.ok(faceFit(224, 48, 3).w > faceFit(224, 48, 1).w);
  assert.equal(faceFit(0, 0, 2).z, 0);
});

test('a copy press goes through the trusted button path, never a script click', () => {
  const app = readFileSync(new URL('../ui/app.js', import.meta.url), 'utf8');
  const basics = readFileSync(new URL('../ui/unlocks/basics.js', import.meta.url), 'utf8');
  assert.match(app, /pressFrom\(ev\) \{\s*if \(!ev\?\.isTrusted/);
  assert.match(basics, /ctx\.button\.pressFrom\?\.\(ev\)/);
  assert.doesNotMatch(basics, /el\.addEventListener\('click'/, 'no click path a script could fire');
});
