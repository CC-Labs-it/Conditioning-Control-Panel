/** Activate local card media only through the visible menu's real Race button.
 * Native Enter/Space clicks have detail 0 and are accepted. Synthetic clicks and
 * race state changes do not activate anything. A failed request can retry next time.
 */
export function bindCardMediaActivation(start, { document = globalThis.document } = {}) {
  let disposed = false, pending = false, activated = false;
  const visible = node => {
    if (!node || node.hidden || !node.getClientRects?.().length) return false;
    const style = document?.defaultView?.getComputedStyle?.(node);
    return !style || (style.visibility !== 'hidden' && style.visibility !== 'collapse' && style.display !== 'none');
  };
  function onClick(event) {
    if (disposed || pending || activated || !event.isTrusted || event.defaultPrevented) return;
    const card = document?.getElementById?.('card');
    if (card?.dataset?.kind !== 'menu') return;
    const garage = document?.getElementById?.('race-garage');
    const button = event.target?.closest?.('button[data-act="race"],button[data-g="race"]');
    const menuVisible = (visible(card) && card.contains(button)) || (visible(garage) && garage.contains(button));
    if (!button || !menuVisible || !visible(button) || button.disabled
      || button.matches?.(':disabled') || button.getAttribute?.('aria-disabled') === 'true') return;
    pending = true;
    try {
      // Call synchronously inside the trusted event; only completion crosses an async boundary.
      Promise.resolve(start()).then(result => { activated = result !== false; }, () => {})
        .finally(() => { pending = false; });
    } catch { pending = false; }
  }
  document?.addEventListener?.('click', onClick, true);
  return () => {
    if (disposed) return;
    disposed = true;
    document?.removeEventListener?.('click', onClick, true);
  };
}
