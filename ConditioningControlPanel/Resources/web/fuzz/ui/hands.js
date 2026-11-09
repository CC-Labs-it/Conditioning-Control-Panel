/* ui/hands.js - which mouse presses count as a press on the game button. Pure, so the rule is testable without a page.
 * Before Double Wielding (owner 2026-11-08) only the left button presses. After it the right button presses too, but only
 * from a real (trusted) event: a script can never farm it. A second button pressed while the first is held arrives as a
 * chorded pointermove (Pointer Events: `button` is the one that changed, `buttons` already includes it), so both
 * together make two presses. A held button sends nothing more, so nothing auto-repeats. Touch never sends button 2. */

/** ev.button -> its bit in ev.buttons. */
const BIT = { 0: 1, 1: 4, 2: 2 };

/** 'left', 'right' or '' for one pointer event on the main button. `twoHands`: Double Wielding is owned and switched on. */
export function handFor(ev, twoHands) {
  if (!ev) return '';
  if (ev.type === 'pointerdown') {
    if (ev.button === 0) return 'left';
    return ev.button === 2 && twoHands && ev.isTrusted ? 'right' : '';
  }
  /* A chord only counts once the run can use both buttons; before that the button behaves exactly as it always has. */
  if (ev.type === 'pointermove' && twoHands && ev.isTrusted && ev.pointerType === 'mouse' && (ev.button === 0 || ev.button === 2)) {
    return (ev.buttons & BIT[ev.button]) ? (ev.button === 2 ? 'right' : 'left') : '';
  }
  return '';
}

/** The same rule for a bouncing copy (ctx.button.pressFrom): every press there must be trusted, either hand. */
export function copyHandFor(ev, twoHands) {
  return ev?.isTrusted ? handFor(ev, twoHands) : '';
}
