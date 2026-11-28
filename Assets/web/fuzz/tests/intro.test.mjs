// The contract: its words, its rules, what the save remembers. The presses themselves are driven in dev/intro-drive.mjs.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import * as T from '../data/content/terms.js';
import { termSize, updateLine } from '../ui/intro.js';
import { createLife, parse, serialize } from '../sim/save.js';
import { CONFIG } from '../data/config.js';
import * as E from '../sim/economy.js';

const { TERMS, EGG_LINES, EGG_POINTS, UPDATE_LINES, JUMP_LINES, X_LINES, X_LINES_PICTURES, INTRO, PICTURE_CARD } = T;
const MOCK = 'C:/Projects/Conditioning-Control-Panel---CSharp-WPF/handoff-1102/tc-intro-mockup.html';
const strip = (s) => s.replace(/<[^>]*>/g, ' ');
const copy = [...TERMS.map(strip), ...UPDATE_LINES, ...JUMP_LINES, ...X_LINES, ...X_LINES_PICTURES, ...Object.values(EGG_LINES).map((e) => e[0]), ...Object.values(INTRO).map(strip), ...Object.values(PICTURE_CARD)];

test('house style: no em-dash, en-dash or exclamation mark', () => {
  for (const line of copy) assert.ok(!/[\u2014\u2013!]/.test(line), line);
});

test('the names come from tokens; "Sowwy" is on purpose', () => {
  for (const line of copy) assert.ok(!/\bfuzz\b/i.test(line), line);
  assert.ok(JUMP_LINES.every((l) => /Sowwy/.test(l)));
  assert.ok(INTRO.firstHead.includes('{short}') && INTRO.firstTitle.includes('{game}') && INTRO.have.includes('{cur}'));
});

test('no school-age framing anywhere in the contract', () => {
  for (const line of copy) assert.ok(!/\b(school\w*|student\w*|pupil\w*|teen\w*|underage|kid\w*|child\w*|baby|babies|little (one|girl|boy)|homework|detention|teacher\w*)\b/i.test(line), line);
});

test('every clause is the owner\'s word for word', { skip: !existsSync(MOCK) }, () => {
  const src = readFileSync(MOCK, 'utf8');
  const a = src.indexOf('const TERMS = [');
  const b = src.indexOf('];', src.indexOf('The llamas were a bit.'));
  // eslint-disable-next-line no-new-func
  const theirs = new Function(`return [${src.slice(a + 'const TERMS = ['.length, b)}];`)();
  assert.deepEqual([...TERMS], theirs);
});

test('the text shrinks from the top to the end', () => {
  assert.equal(termSize(0), 15.5);
  assert.equal(termSize(TERMS.length - 1), 6.5);
  for (let i = 1; i < TERMS.length; i++) assert.ok(termSize(i) < termSize(i - 1));
});

test('eight hidden things, and each one the text can reach', () => {
  assert.equal(Object.keys(EGG_LINES).length, 8);
  const text = TERMS.join(' ');
  for (const key of ['llama', 'redact', 'flip', 'ghost']) assert.ok(text.includes(`id="${key}"`), key);
  for (const key of ['exit', 'dont']) assert.ok(text.includes(`data-egg="${key}"`), key);
  assert.ok(text.includes('id="optout"') && text.includes('class="lln"'));
  const most = Object.values(EGG_LINES).reduce((sum, e) => sum + (e[1] || EGG_POINTS), 0);
  assert.equal(most, 225);
});

test('the update line changes each visit and comes round again', () => {
  assert.equal(new Set(UPDATE_LINES).size, UPDATE_LINES.length);
  assert.ok(UPDATE_LINES.length >= 6);
  for (const l of UPDATE_LINES) assert.match(l, /llama/i);
  const seen = new Set();
  for (let u = 0; u < UPDATE_LINES.length; u++) seen.add(updateLine({ updates: u }));
  assert.equal(seen.size, UPDATE_LINES.length);
  assert.equal(updateLine({ updates: UPDATE_LINES.length }), UPDATE_LINES[0]);
});

test('the lifetime remembers the contract and the update count', () => {
  const fresh = createLife();
  assert.equal(fresh.contract, false);
  assert.equal(fresh.updates, 0);
  const life = createLife();
  life.contract = true;
  life.age = true;
  life.updates = 3;
  const back = parse(serialize(null, life, {})).life;
  assert.equal(back.contract, true);
  assert.equal(back.updates, 3);
  assert.equal(parse(JSON.stringify({ v: 2, run: null, life: { age: true, contract: 'yes', updates: -4 } })).life.updates, 0);
});

test('the points land as a lump in the run', () => {
  const run = E.createRun();
  E.lump(run, 75, 'intro');
  assert.equal(run.fuzz, 75);
  assert.equal(run.sources.intro, 75);
});

test('pictures start empty until the player chooses', () => {
  assert.equal(CONFIG.PICTURES_DEFAULT, 'none');
});
