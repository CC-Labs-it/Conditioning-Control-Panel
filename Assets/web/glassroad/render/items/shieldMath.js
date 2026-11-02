/* Shield presentation follows the item clock. It never changes the contact envelope or item timers. */
import { ITEM_LOOK as L, AURA_LOOK as A } from './look.js';
const clamp = x => Math.max(0, Math.min(1, Number.isFinite(x) ? x : 0));
const smooth = (a, b, x) => { const u = clamp((x - a) / (b - a)); return u * u * (3 - 2 * u); };

/** Golden takes visual priority while the saved Glass shield continues underneath it. Echo stays separate. */
export function selectShieldShells(rows, out = []) {
  out.length = 0;
  for (const row of rows || []) {
    if (row.kind !== 'glass' && row.kind !== 'golden') continue;
    const i = out.findIndex(other => other.racer === row.racer);
    if (i < 0) out.push(row); else if (row.kind === 'golden') out[i] = row;
  }
  return out;
}

/** A stable shell with a gradual amber expiry cue and a shallow, slow dimming pulse. */
export function shieldLook(row, durationS, player = false, gentle = false, out = {}, underlay = null) {
  const remain = clamp(row.k === undefined ? 1 : row.k) * Math.max(.01, durationS || 1), age = Math.max(0, row.t || 0);
  const warning = 1 - smooth(0, Math.min(L.shieldWarnS, Math.max(.01, durationS * L.shieldWarnShare)), remain);
  const pulse = gentle ? 1 : 1 - warning * L.shieldPulseDepth * (.5 - .5 * Math.cos(age * Math.PI * 2 * L.shieldPulseHz));
  const envelope = smooth(0, L.shieldInS, age) * smooth(0, L.shieldOutS, remain);
  out.alpha = envelope
    * (player ? L.playerShieldAlpha : L.shieldAlpha) * (gentle ? L.shieldGentle : 1) * pulse;
  out.radius = player ? L.playerShieldR : L.shieldR;
  out.warning = warning; out.gold = row.kind === 'golden' ? 1 : 0;
  if (underlay && row.kind === 'golden') {
    // Glass already protects: keep its shell while Golden's tint joins and leaves smoothly.
    out.alpha = Math.max(out.alpha, underlay.alpha);
    out.gold = envelope;
    out.warning = underlay.warning * (1 - envelope) + warning * envelope;
  }
  return out;
}

/** Shield shells have one owner. Surge keeps a gentle steady rim and broad slow bands. */
export function auraStyle(kind, player = false, gentle = false, out = {}) {
  const surge = kind === 'surge';
  out.visible = !!kind && kind !== 'glass' && kind !== 'golden';
  out.alpha = surge ? (player ? A.surgePlayerAlpha : A.surgeAlpha) : (player ? A.playerAlpha : A.alpha);
  out.bandHz = surge ? A.surgeBandHz : A.bandClimbHz;
  out.bandPower = surge ? 1 : 3;
  out.bandColour = surge ? (gentle ? 0 : A.surgeBandColour) : .55;
  out.bandAlpha = surge ? (gentle ? 0 : A.surgeBandAlpha) : .6;
  return out;
}
