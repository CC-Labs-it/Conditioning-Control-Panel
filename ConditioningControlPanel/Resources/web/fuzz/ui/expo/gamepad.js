const DEADZONE = .18;
const BUTTONS = Object.entries({jump:0, interact:2, spit:7, back:1, start:9, up:12, down:13, left:14, right:15});

function stick(x, y) {
  x = Number.isFinite(x) ? x : 0;
  y = Number.isFinite(y) ? y : 0;
  const radius = Math.hypot(x,y);
  if (radius <= DEADZONE) return [0,0];
  const gain = (Math.min(1,radius)-DEADZONE)/((1-DEADZONE)*radius);
  return [x*gain,y*gain];
}

function buttonsDown(buttons) {
  let mask = 0;
  for (const [,i] of BUTTONS) {
    const b = buttons?.[i];
    if (b?.pressed || (b?.value ?? b) > .5) mask |= 1 << i;
  }
  return mask;
}

/** A standard-mapping reader for one input owner. Stick Y increases downwards.
 * Reset when ownership changes. An already-held control must return to neutral
 * before it can operate the newly opened menu or gallery. */
export function createExpoGamepad(readPads = () => globalThis.navigator?.getGamepads?.()) {
  const previous = new Map();
  let selected = null;
  function reset() { previous.clear(); selected = null; }
  function disconnect(index) {
    if (selected === index) reset();
    else previous.delete(index);
  }
  function poll() {
    let pads;
    try { pads = readPads(); } catch { reset(); return null; }
    if (selected !== null) {
      const owner = pads?.[selected];
      if (!owner || owner.connected === false || owner.mapping !== 'standard' || owner.id !== previous.get(selected)?.id) {
        // Hand over at rest even if a different connected player is holding a stick or button.
        reset();
      }
    }
    const candidates = new Map();
    let claimed = null;
    for (const pad of pads || []) {
      if (!pad || pad.connected === false || pad.mapping !== 'standard') continue;
      const index = pad.index;
      const old = previous.get(index);
      const known = old && old.id === pad.id;
      const [mx,my] = stick(pad.axes?.[0],pad.axes?.[1]);
      const [lx,ly] = stick(pad.axes?.[2],pad.axes?.[3]);
      const moving = !!(mx || my), looking = !!(lx || ly);
      const moveReady = !moving || (known && old.moveReady);
      const lookReady = !looking || (known && old.lookReady);
      const mask = buttonsDown(pad.buttons);
      const edges = known ? mask & ~old.mask : 0;
      const input = {index, moveX:moveReady ? mx : 0, moveY:moveReady ? my : 0,
        lookX:lookReady ? lx : 0, lookY:lookReady ? ly : 0};
      for (const [action,bit] of BUTTONS) input[action] = !!(edges & (1 << bit));
      input.spitReleased=!!(known&&(old.mask&(1<<7))&&!(mask&(1<<7)));
      input.activity = !!(input.moveX || input.moveY || input.lookX || input.lookY || edges);
      if (edges || (known && ((moving && moveReady && !old.moving) || (looking && lookReady && !old.looking)))) {
        if (claimed === null || index === selected) claimed = index;
      }
      candidates.set(index,input);
      previous.set(index,{id:pad.id,mask,moving,looking,moveReady,lookReady});
    }
    for (const index of previous.keys()) if (!candidates.has(index)) disconnect(index);
    selected = claimed ?? (candidates.has(selected) ? selected : candidates.keys().next().value ?? null);
    return candidates.get(selected) || null;
  }
  return {poll,reset,disconnect};
}
