// Keep menu navigation inside its visible surface, away from the kart carousel.
export function createGarageNav({ root, select, customize, isCustom, isVisible, isBusy,
  trackPicking, trackNav, activeElement = () => root.ownerDocument.activeElement }) {
  const available = selector => [...root.querySelectorAll(selector)].filter(el =>
    !el.disabled && !el.hidden && el.getClientRects().length > 0);
  return function nav(dir) {
    if (!isVisible() || isBusy()) return;
    if (trackPicking()) { trackNav(dir); return; }
    if (!['up', 'down', 'left', 'right', 'ok', 'back'].includes(dir)) return;
    const custom = isCustom(), focused = activeElement();
    if (dir === 'back') { if (custom) customize(false); return; }
    const buttons = available(custom ? '.g-custom button,.g-custom input' : '.g-menu-options button');
    if (dir === 'ok') {
      const target = buttons.includes(focused) ? focused : buttons[0];
      target?.click(); return;
    }
    if (!custom && (dir === 'left' || dir === 'right')) {
      select(dir === 'left' ? -1 : 1, true); return;
    }
    const i = buttons.indexOf(focused), step = ['up', 'left'].includes(dir) ? -1 : 1;
    const next = i < 0 ? 0 : (i + step + buttons.length) % buttons.length;
    buttons[next]?.focus({ preventScroll: true });
  };
}
