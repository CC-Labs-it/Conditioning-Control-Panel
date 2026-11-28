import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

// Runs the WebKitGTK document-start motion script itself against a small CSSOM.
const source = readFileSync(new URL('../../CCP.Avalonia/Views/Controls/WebHost.axaml.cs', import.meta.url), 'utf8');
const script = source.match(/MotionScript = @"([\s\S]*?)";/)[1].replace(/""/g, '"');

function page(reduce) {
  const listeners = {}, observers = [], asked = [];
  const media = text => ({ mediaText: text });
  const rule = (text, inner = []) => ({ media: media(text), cssRules: inner });
  const sheet = { media: media('(prefers-reduced-motion: reduce)'), cssRules: [
    rule('(prefers-reduced-motion: reduce)'),
    rule('screen and (prefers-reduced-motion:no-preference)'),
    { cssRules: [rule('not (prefers-reduced-motion)')] },
    { styleSheet: { media: media('all'), get cssRules() { throw new Error('cross-origin'); } } },
    rule('(pointer: coarse)'),
  ] };
  const document = {
    styleSheets: [sheet],
    addEventListener(type, fn) { listeners[type] = fn; },
  };
  const window = { matchMedia(q) { asked.push(q); return { media: q }; } };
  class MutationObserver { constructor(fn) { this.fn = fn; observers.push(this); } observe() {} }
  runInNewContext(script.replace('__REDUCE__', String(reduce)), { window, document, MutationObserver });
  return { sheet, document, window, listeners, observers, asked, rule };
}

test('style sheets and matchMedia follow the app setting, not the OS', () => {
  for (const [reduce, on, off] of [[true, '(grid:0)', '(grid)'], [false, '(grid)', '(grid:0)']]) {
    const p = page(reduce);
    p.listeners.DOMContentLoaded();
    assert.equal(p.sheet.media.mediaText, on);
    assert.equal(p.sheet.cssRules[0].media.mediaText, on);
    assert.equal(p.sheet.cssRules[1].media.mediaText, 'screen and ' + off);
    assert.equal(p.sheet.cssRules[2].cssRules[0].media.mediaText, 'not ' + on);
    assert.equal(p.sheet.cssRules[4].media.mediaText, '(pointer: coarse)');
    p.window.matchMedia('(prefers-reduced-motion: reduce) and (min-width: 1px)');
    assert.deepEqual(p.asked, [on + ' and (min-width: 1px)']);
  }
});

test('a style element added later is rewritten, other mutations are ignored', () => {
  const p = page(false);
  const late = { media: { mediaText: 'all' }, cssRules: [p.rule('(prefers-reduced-motion: reduce)')] };
  p.document.styleSheets.push(late);
  p.observers[0].fn([{ target: { nodeName: 'DIV' }, addedNodes: [{ nodeName: 'SPAN' }] }]);
  assert.equal(late.cssRules[0].media.mediaText, '(prefers-reduced-motion: reduce)');
  p.observers[0].fn([{ target: { nodeName: 'HEAD' }, addedNodes: [{ nodeName: 'STYLE' }] }]);
  assert.equal(late.cssRules[0].media.mediaText, '(grid)');
  const linked = { media: { mediaText: '(prefers-reduced-motion: no-preference)' }, cssRules: [] };
  p.document.styleSheets.push(linked);
  p.listeners.load({ target: { nodeName: 'LINK' } });
  assert.equal(linked.media.mediaText, '(grid:0)');
});
