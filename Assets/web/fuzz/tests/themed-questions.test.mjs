import { test } from 'node:test';
import assert from 'node:assert/strict';
import { QUESTIONS, TRICKS } from '../data/content/questions.js';
import { QUIZ_THEMES, THEMED_QUESTIONS, TEXT_TRICKS, lessonAnswer, takeThemedQuestion } from '../data/content/themed-questions.js';

test('all eight themes have distinct short questions and four readable choices', () => {
  assert.equal(Object.keys(QUIZ_THEMES).length, 8);
  const seen = new Set();
  for (const [theme, bank] of Object.entries(THEMED_QUESTIONS)) {
    assert.ok(bank.length >= 8, theme);
    for (const q of bank) {
      assert.ok(!seen.has(q.id)); seen.add(q.id);
      assert.ok(q.text.length <= 70, q.id);
      assert.equal(q.answers.length, 4);
      assert.equal(new Set(q.answers.map((a) => a.label)).size, 4);
      assert.equal(q.answers.filter((a) => a.tone === 'yes').length, 1);
      for (const a of q.answers) assert.ok(a.label.length <= 12, `${q.id}: ${a.label}`);
      assert.ok(!/[\u2013\u2014!]/u.test(q.text + q.answers.map((a) => a.label).join('')));
    }
  }
  for (const trick of TEXT_TRICKS) assert.ok(TRICKS[trick], trick);
});

test('a saved deck does not repeat across tricks or reload until every question has appeared', () => {
  for (const theme of Object.keys(QUIZ_THEMES)) {
    let deck = null;
    const history = [];
    for (let i = 0; i < 24; i++) {
      const trick = [...TEXT_TRICKS][i % TEXT_TRICKS.size];
      const draw = takeThemedQuestion(theme, trick, deck, () => .2);
      assert.equal(draw.question.trick, trick);
      history.push(draw.question.id);
      deck = JSON.parse(JSON.stringify(draw.deck));
    }
    for (let i = 0; i < 24; i += 8) assert.equal(new Set(history.slice(i, i + 8)).size, 8);
    assert.ok(!history.slice(6, 8).includes(history[8]));
    assert.ok(!history.slice(14, 16).includes(history[16]));
  }
});

test('mechanical prompts retain their puzzle data and never consume the theme deck', () => {
  for (const trick of ['lean-slider', 'verify-checkbox', 'verify-grid', 'verify-transcribe', 'verify-regen', 'mono']) {
    const deck = { left: ['theme_drone_2'], recent: [] };
    assert.equal(takeThemedQuestion('drone', trick, deck), null);
    assert.deepEqual(deck.left, ['theme_drone_2']);
    assert.ok(QUESTIONS.some((q) => q.trick === trick));
  }
  assert.equal(takeThemedQuestion(null, 'none', null), null);
  assert.equal(takeThemedQuestion('not-a-theme', 'none', null), null);
});

test('lesson progress survives silly answers and the third intended answer earns one completion', () => {
  let progress = 0, completions = 0;
  for (const correct of [true, false, false, true, false, true, false]) {
    const next = lessonAnswer(progress, correct);
    if (!correct) assert.equal(next.progress, progress);
    progress = next.progress; completions += Number(next.complete);
  }
  assert.equal(completions, 1);
  assert.equal(progress, 0);
  assert.deepEqual(lessonAnswer(2, true), { progress: 0, complete: true });
  assert.deepEqual(lessonAnswer(NaN, false), { progress: 0, complete: false });
});

test('a malformed or stale saved bag cannot choose another theme or duplicate an entry', () => {
  const draw = takeThemedQuestion('trance', 'none', { left: ['theme_drone_0', 'theme_trance_2', 'theme_trance_2'], recent: 'bad' }, () => .3);
  assert.equal(draw.question.id, 'theme_trance_2');
  assert.deepEqual(draw.deck.left, []);
});
