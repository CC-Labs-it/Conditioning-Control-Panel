/* data/cloud-lines.js - the Bambi Cloud button face's label (owner 2026-11-10). Pure; Math.random only, never the run RNG.
 * Login-style lines plus the owner's Bambi trigger phrases; ui/button-faces.js swaps one in every 6 to 12 s. Every line
 * stays no wider than the face's first line, so the wide face never grows or overflows (tests/theme-text.test.mjs). */
import { BAMBI_TRIGGERS } from './content/theme-text.js';

export const CLOUD_FIRST = 'Sign in with Bambi Cloud';
export const CLOUD_LOGINS = Object.freeze([CLOUD_FIRST, 'Continue as Bambi', 'Remember me, Bambi', 'Stay signed in, Bambi', 'Forgot your thoughts?']);
export const CLOUD_LABELS = Object.freeze([...CLOUD_LOGINS, ...BAMBI_TRIGGERS]);
export const CLOUD_GAP_MS = Object.freeze([6000, 12000]);

/** The next label: any line but the one showing. */
export function nextCloudLabel(current, rand = Math.random) {
  const pool = CLOUD_LABELS.filter((l) => l !== current);
  return pool[Math.floor(rand() * pool.length)] ?? CLOUD_FIRST;
}
/** How long a label stays up, in ms. */
export const cloudGap = (rand = Math.random) => CLOUD_GAP_MS[0] + rand() * (CLOUD_GAP_MS[1] - CLOUD_GAP_MS[0]);
