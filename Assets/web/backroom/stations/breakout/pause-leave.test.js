import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

// The pause card's way out (owner, 2026-10-22). On the phone, back only pauses, and a Story board
// offered Resume and Options and nothing else. The card now carries Menu in every mode: Endless keeps
// Save and menu, Story and the demo drop the board. Escape still only pauses and resumes.

const source = readFileSync(new URL('./station.js', import.meta.url), 'utf8');
const slice = (from, to) => { const a = source.indexOf(from); assert.ok(a >= 0, from + ' moved: update this test'); const b = source.indexOf(to, a); assert.ok(b > a, to); return source.slice(a, b); };
const leaveCode = slice('  function returnToMenu() {', '  let raf = 0');
const pauseCode = slice('  const endingUnderway = () =>', '  function beginGame()');
const keyCode = slice("    on(window, 'keydown', (e) => {", "    on(window, 'keyup'");

function node() { return { hidden: true, textContent: '', focus() { this.focused = true; } }; }

function page({ endless = false, saves = true, ending = null } = {}) {
  const calls = [];
  const leave = node(), note = node(), best = node();
  const snapshot = () => ({ demoComplete: ending === 'demo', finale: ending === 'outro' ? { phase: 'outro' } : null, comboBest: 0 });
  const c = {
    calls, leave, note,
    menuOpen: false, activeEndless: endless, paused: false, audioOn: true, moved: true, startFresh: true, previewRun: {},
    input: {}, keysDown: {}, touchDrag: null, lastT: 1,
    game: { snapshot },
    officeEnding: { suspend() {} },
    pageFx: { cancelAll: () => calls.push('fx') },
    mouseLock: { release: () => calls.push('release') },
    audio: { stop: () => calls.push('audio-stop'), start: () => calls.push('audio-start') },
    document: { hidden: false },
    el: { classList: { add() {}, remove() {}, toggle() {} } },
    canvas: { focus() {} },
    ui: { menu: node(), 'ghost-hint': node(), 'run-hud': node(), play: node(),
      paused: { hidden: true, querySelector: s => s === '[data-menu="save-menu"]' ? leave : s === '.bo-save-note' ? note : best } },
    saveEndless: () => { calls.push('save'); return saves; },
    makeGame: () => calls.push('makeGame'),
    setSp: v => calls.push('sp' + v), setCombo: v => calls.push('combo' + v),
    syncModeMenu: () => calls.push('syncMenu'),
    back: () => assert.fail('the Menu button never leaves the page'),
  };
  c.ui.menu.hidden = true;
  vm.runInNewContext(pauseCode + leaveCode, c);
  return c;
}

for (const endless of [false, true]) {
  test(`${endless ? 'Endless' : 'Story'}: the pause card shows its Menu button`, () => {
    const c = page({ endless });
    vm.runInNewContext('setPaused(true)', c);
    assert.equal(c.ui.paused.hidden, false);
    assert.equal(c.leave.hidden, false, 'every mode has a way out');
    assert.equal(c.leave.textContent, endless ? 'Save and menu' : 'Menu');
    assert.equal(c.note.hidden, !endless, 'the save note is Endless only');
    assert.ok(c.calls.includes('release'), 'the card frees the mouse');
  });
}

test('Story: Menu drops the board and opens the menu, with no save', () => {
  const c = page();
  vm.runInNewContext('setPaused(true); returnToMenu()', c);
  assert.equal(c.menuOpen, true);
  assert.equal(c.ui.menu.hidden, false);
  assert.equal(c.paused, false);
  assert.equal(c.ui.paused.hidden, true, 'the pause card goes');
  assert.ok(!c.calls.includes('save'), 'Story writes no Endless checkpoint');
  assert.ok(c.calls.includes('makeGame'), 'a fresh board, so Play starts clean');
  assert.ok(c.calls.includes('sp0') && c.calls.includes('combo0'), 'the chips reset with the board');
  assert.ok(c.calls.includes('audio-stop'));
  assert.equal(c.audioOn, false);
  assert.ok(c.ui.play.focused, 'focus lands on Play');
});

test('Endless: Menu saves first and keeps the run', () => {
  const c = page({ endless: true });
  vm.runInNewContext('setPaused(true); returnToMenu()', c);
  assert.equal(c.menuOpen, true);
  assert.ok(c.calls.includes('save'));
  assert.ok(!c.calls.includes('makeGame'), 'Continue picks the saved board up');
  assert.equal(c.activeEndless, false);
});

test('Endless: a failed save keeps the player on the card', () => {
  const c = page({ endless: true, saves: false });
  vm.runInNewContext('setPaused(true); returnToMenu()', c);
  assert.equal(c.menuOpen, false);
  assert.equal(c.paused, true);
  assert.match(c.note.textContent, /could not save/);
});

test('the ending keeps its own Menu: the card button hides and the call does nothing', () => {
  const c = page({ ending: 'outro' });
  vm.runInNewContext('setPaused(true)', c);
  assert.equal(c.leave.hidden, true);
  vm.runInNewContext('returnToMenu()', c);
  assert.equal(c.menuOpen, false);
  assert.ok(!c.calls.includes('makeGame'));
});

test('a second call from the menu does nothing', () => {
  const c = page();
  vm.runInNewContext('returnToMenu(); returnToMenu()', c);
  assert.equal(c.calls.filter(x => x === 'makeGame').length, 1);
});

test('only a click reaches Menu: Escape never does', () => {
  assert.doesNotMatch(keyCode, /returnToMenu|save-menu/);
  assert.match(source, /if\(action==='save-menu'\)returnToMenu\(\);/);
  assert.match(source, /<button type="button" data-menu="save-menu" hidden>/, 'the button sits on the pause card, beside Resume and Options');
});
