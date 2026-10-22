/* input/keyboard.js - keyboard InputSource (CONTRACT.md section 4), lifted from dtrh/race/input.js.
 *
 * W / Up throttle, S / Down brake (reverse when stopped), A / D or Left / Right steer, Shift or Space
 * hop and drift, E item (B1: hold S / Down with E to drop it behind), Escape pause. Raw digital values: the kart slews the stick itself. Edges (hopPressed,
 * pause) are latched until the next poll. A field being typed in keeps its keys; a focused button
 * keeps its Space; everywhere else game keys never scroll the page. No axis is ever remapped.
 */
const HELD = {
  KeyW: 'throttle', ArrowUp: 'throttle', KeyS: 'brake', ArrowDown: 'brake',
  KeyA: 'left', ArrowLeft: 'left', KeyD: 'right', ArrowRight: 'right',
  ShiftLeft: 'hop', ShiftRight: 'hop', Space: 'hop', KeyE: 'item',
};
const TYPING = /^(INPUT|TEXTAREA|SELECT)$/;
const CONTROLS = /^(BUTTON|INPUT|SELECT|TEXTAREA|A)$/;

export function createKeyboard({ target = window } = {}) {
  const down = new Set();
  let hopEdge = false, pauseEdge = false, itemEdge = false;
  const held = (what) => { for (const c of down) if (HELD[c] === what) return true; return false; };

  function onKey(e) {
    if (e.altKey || e.metaKey || e.ctrlKey) return;
    const code = e.code || '', tag = (e.target && e.target.tagName) || '';
    if (TYPING.test(tag)) return;
    const what = HELD[code];
    if (e.type === 'keydown') {
      if (code === 'Escape') { if (!e.repeat) pauseEdge = true; return; }
      if (!what || (code === 'Space' && CONTROLS.test(tag))) return;
      e.preventDefault();
      if (e.repeat) return;
      if (what === 'hop' && !held('hop')) hopEdge = true;
      if (what === 'item') itemEdge = true;
      down.add(code);
    } else if (what) down.delete(code);
  }
  const onBlur = () => { down.clear(); hopEdge = false; itemEdge = false; };

  target.addEventListener('keydown', onKey);
  target.addEventListener('keyup', onKey);
  target.addEventListener('blur', onBlur);

  function poll(into) {
    into.steer = (held('right') ? 1 : 0) - (held('left') ? 1 : 0);
    into.throttle = held('throttle') ? 1 : 0;
    into.brake = held('brake') ? 1 : 0;
    into.hop = held('hop');
    into.hopPressed = hopEdge;
    into.pause = pauseEdge;
    into.item = itemEdge;
    into.back = held('brake');
    hopEdge = false; pauseEdge = false; itemEdge = false;
  }
  function dispose() {
    target.removeEventListener('keydown', onKey);
    target.removeEventListener('keyup', onKey);
    target.removeEventListener('blur', onBlur);
    down.clear();
  }
  return { poll, dispose };
}
