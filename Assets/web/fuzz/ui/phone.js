/* ui/phone.js - small-screen touches (owner 2026-11-21, llama round change 4). On a phone only: the Scrolling Thoughts
 * strip runs bigger, brighter (styles.css, same query) and slower, and holds still while a finger rests on it. On iPhone
 * Safari, when the game is not already on the home screen, one tip says how to put it there; a tap clears it and it never
 * comes back. Desktop is unchanged. Pure rules first, so tests need no page. */

/** A phone: a touch screen under the narrow layout. The same query as the phone rules in styles.css. */
export const PHONE_QUERY = '(pointer: coarse) and (max-width: 759px)';
export const TICKER_PX_S = 70, PHONE_TICKER_PX_S = 38;
export const TICKER_MIN_MS = 4000, PHONE_TICKER_MIN_MS = 7000;
export const HOME_TIP = 'Tip: Share, then Add to Home Screen.';
export const HOME_TIP_KEY = 'fuzz:home-tip';
export const HOME_TIP_DELAY_MS = 6000;

export const isPhone = (win) => { try { return !!win?.matchMedia?.(PHONE_QUERY)?.matches; } catch { return false; } };

/** One thought's crossing time: the strip plus the text, at the strip's speed. */
export function tickerMs(stripW, textW, phone = false) {
  const span = Math.max(0, stripW) + Math.max(0, textW);
  return Math.max(phone ? PHONE_TICKER_MIN_MS : TICKER_MIN_MS, (span / (phone ? PHONE_TICKER_PX_S : TICKER_PX_S)) * 1000);
}

/** iPhone (or iPod) Safari itself: other iOS browsers and in-app web views name themselves or drop the Safari token. */
export function iphoneSafari(ua = '') {
  return /iP(hone|od)/.test(ua) && /Safari\//.test(ua) && !/CriOS|FxiOS|EdgiOS|OPiOS|OPT\/|GSA\/|YaBrowser|DuckDuckGo|Brave|FBAN|FBAV|Instagram|Line\//.test(ua);
}

/** Already opened from the home screen. */
export function standalone(win) {
  if (win?.navigator?.standalone === true) return true;
  try { return !!win?.matchMedia?.('(display-mode: standalone)')?.matches; } catch { return false; }
}

const seenTip = (storage) => { try { return !!storage?.getItem?.(HOME_TIP_KEY); } catch { return true; } };

/** The tip is due: iPhone Safari, not on the home screen, never shown before. Storage that throws counts as shown. */
export function homeTipDue(win, storage) {
  return iphoneSafari(win?.navigator?.userAgent || '') && !standalone(win) && !seenTip(storage);
}

/** Shows the tip once, a little after the run starts. Remembered as soon as it shows; a tap clears it. */
export function mountHomeTip({ doc = document, win = doc.defaultView, storage, delay = HOME_TIP_DELAY_MS } = {}) {
  if (!homeTipDue(win, storage)) return null;
  const show = () => {
    if (!homeTipDue(win, storage) || doc.getElementById('home-tip')) return;
    try { storage?.setItem?.(HOME_TIP_KEY, '1'); } catch { /* shown once this visit either way */ }
    const tip = doc.createElement('button');
    tip.id = 'home-tip';
    tip.type = 'button';
    tip.textContent = HOME_TIP;
    tip.addEventListener('click', () => tip.remove());
    doc.body.append(tip);
  };
  win?.setTimeout?.(show, delay);
  return { show };
}
