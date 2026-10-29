import { test } from 'node:test';
import assert from 'node:assert/strict';
import { wordIdentity, wordIdentityExact, phraseWordIdentity, WORD_EFFECTS, WORD_IDENTITY_COUNT } from '../sim/wordIdentity.js';
import { scriptRoad, createRoadRun, ROAD_LOOK } from '../sim/roadWords.js';
import { pickCoins } from '../sim/wordCoins.js';
import { fakeTrack } from './sim-fake.mjs';

const TERMS = ['SLIDE', 'SPARK', 'PINK', 'SINK', 'GLASS', 'ROLLING', 'RESET', 'EMPTY', 'LOCKED', 'BURN'];
const word = (w, startS, extra = {}) => ({ w, startS, endS: startS + .08, line: 0, ...extra });

test('trigger phrases retain context while matching supported complete words', () => {
  assert.equal(phraseWordIdentity('quiet and empty'), 8);
  assert.equal(phraseWordIdentity('please reset'), 7);
  assert.equal(phraseWordIdentity('unit reset'), 7);
  assert.equal(phraseWordIdentity('program reset'), 7, 'a technical prefix cannot replace the original trigger signature');
  assert.equal(phraseWordIdentity('brain empty'), 8);
  assert.equal(phraseWordIdentity('hear the voice'), 12, 'a generic phrase still receives its first semantic mechanism');
  assert.equal(phraseWordIdentity('unlocked compartment'), 0);
  assert.equal(wordIdentity('quiet and empty'), 0, 'road tokens remain individual words');
});

test('ten stable identities match complete words with normal speech punctuation', () => {
  TERMS.forEach((term, i) => {
    assert.equal(wordIdentity(term), i + 1);
    assert.equal(wordIdentityExact(term), i + 1);
    assert.equal(wordIdentity(`  "${term.toLowerCase()},"  `), i + 1);
    assert.equal(wordIdentity(`\u201c${term.toLowerCase()}\u201d`), i + 1);
  });
});

test('known inflections preserve identity without substring or phrase guessing', () => {
  for (const [text, id] of [['slid', 1], ['sparking', 2], ['sank', 4], ['glassy', 5], ['rolled', 6],
    ['resetting', 7], ['emptied', 8], ['locking', 9], ['burnt', 10]]) {
    assert.equal(wordIdentity(text), id);
    assert.equal(wordIdentityExact(text), 0);
  }
  for (const text of [null, undefined, {}, 0, '', ' ', 'sparkplug', 'pinkness', 'glassware', 'unlocked',
    'burnout', 'slide show', 'let it sink', 'sli/de', 'reset-pink', 'burn2', 'constructor', '__proto__'])
    assert.equal(wordIdentity(text), 0, String(text));
});

test('the numeric registry covers every implemented family without changing original ids', () => {
  assert.equal(WORD_IDENTITY_COUNT, 36);
  assert.equal(WORD_EFFECTS.length, WORD_IDENTITY_COUNT + 1);
  assert.equal(new Set(WORD_EFFECTS).size, WORD_EFFECTS.length);
  WORD_EFFECTS.slice(1).forEach((name, index) => assert.equal(wordIdentity(name), index + 1, name));
  assert.ok(Object.isFrozen(WORD_EFFECTS));
});

test('physical vocabulary has deliberate related forms and keeps connectors plain', () => {
  for (const [text, family] of [
    ['whispers', 12], ['eyes', 13], ['dripping', 14], ['breaking', 15], ['softer', 16], ['stairs', 17],
    ['pressing', 18], ['throbbing', 19], ['gone', 20], ['cage', 21], ['fourteen', 22], ['42', 22],
    ['pulled', 23], ['stopping', 24], ['around', 25], ['edge', 26], ['brighter', 27], ['cold', 28],
    ['curl', 29], ['exit', 30], ['raindrop', 31], ['pleasure', 32], ['worries', 33], ['rainbow', 34],
    ['steel', 35], ['GREEN', 36], ['pinkish', 3], ['rolling', 6], ['tiny', 23], ['wave', 6],
  ]) assert.equal(wordIdentity(text), family, text);
  for (const text of ['the', 'and', 'are', 'can', 'do', 'is', 'you', 'let', 'need', 'could', 'will'])
    assert.equal(wordIdentity(text), 0, 'connective stays plain: ' + text);
  assert.equal(wordIdentity('gold'), 35);
  assert.notEqual(wordIdentity('green'), wordIdentity('pink'), 'literal colours retain separate ink');
});

test('a semantic keyword never merges with its neighbours, while filler still combines', () => {
  for (const [i, term] of TERMS.entries()) {
    const song = { words: [word('a', 2), word(term, 2.06), word('to', 2.12), word('the', 2.18)] };
    const script = scriptRoad(song);
    assert.deepEqual(script.map(e => e.text), ['a', term, 'to the'], term);
    assert.deepEqual(script.map(e => e.identity), [0, i + 1, 0]);
    assert.deepEqual(script.map(e => e.phrase), [0, 0, 0], 'splitting glyphs does not invent extra coin phrases');
    assert.equal(script[1].startS, 2.06);
    assert.equal(script[1].endS, 2.14);
    assert.deepEqual([...pickCoins(script)], [0, 1, 0]);
  }
});

test('semantic occurrences win over shorter filler without adding filler coins', () => {
  const script = scriptRoad({ words: [word('the', 2), word('rolling', 2.2), word('pink', 2.4),
    word('and', 4, { line: 1 }), word('burn', 4.2, { line: 1 })] });
  assert.deepEqual([...pickCoins(script)], [0, 1, 1, 0, 1]);
});

test('every semantic occurrence is eligible including repeats, inflections and broad vocabulary', () => {
  const entries = ['roll', 'pink', 'rolling', 'pink', 'spark', 'rolling', 'the'];
  const script = entries.map((text, i) => ({ i, text, chars: text.length, phrase: 0, identity: wordIdentity(text) }));
  assert.deepEqual([...pickCoins(script)], [1, 1, 1, 1, 1, 1, 0], 'repeated meaningful words keep their own pickups');
  const next = [{ text: 'the', chars: 3, phrase: 1 }, { text: 'breath', chars: 6, phrase: 1 },
    { text: 'cold', chars: 4, phrase: 1 }, { text: 'green', chars: 5, phrase: 1 }];
  assert.deepEqual([...pickCoins(next)], [0, 1, 1, 1], 'ids beyond the first ten are equally eligible');
});

test('bubble, gate and trigger ownership beats semantic identity and retains nearby clearance', () => {
  const song = {
    words: [word('PINK', 2, { bubble: true }), word('SPARK', 2.1), word('SLIDE', 3, { line: 1 }),
      word('LOCKED', 9, { gate: true, line: 2 }), word('GLASS', 9.1, { line: 2 }),
      word('RESET', 16, { line: 3 }), word('EMPTY', 16.1, { line: 3 }), word('SINK', 17, { line: 4 })],
    triggers: [{ kind: 'word', w: 'RESET', startS: 16 }],
  };
  const script = scriptRoad(song);
  assert.deepEqual(script.map(e => e.text), ['SLIDE', 'SINK']);
  assert.deepEqual([...script.bubbles], [2, 9, 16]);
  assert.deepEqual([...pickCoins(script)], [1, 1]);
});

test('punctuation removal cannot manufacture a semantic coin from a different token', () => {
  const script = scriptRoad({ words: [word('the', 2), word('sli/de', 2.2), word('burn', 2.4)] });
  assert.equal(script[1].text, 'slide');
  assert.equal(script[1].identity, 0);
  assert.deepEqual([...pickCoins(script)], [0, 0, 1]);
});

test('live road markings carry their identity through pause and song restart', () => {
  const song = { words: [word('glass', 4)] }, track = fakeTrack({ nodes: 400, spacing: 10, w: 14 });
  const run = createRoadRun(song, track);
  const player = { s: 30, lat: 0, speedMps: 30 };
  const first = run.update(4 - ROAD_LOOK.leadS + .1, player)[0];
  assert.equal(first.identity, 5);
  const position = first.s;
  assert.equal(run.update(4 - ROAD_LOOK.leadS + .1, player)[0].s, position);
  run.reset();
  assert.equal(run.update(4 - ROAD_LOOK.leadS + .1, player)[0].identity, 5);
});
