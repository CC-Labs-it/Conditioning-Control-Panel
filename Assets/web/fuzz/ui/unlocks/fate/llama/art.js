/* ui/unlocks/fate/llama/art.js - The Panel ending's pictures. The first cut drew placeholder llamas, a bench, a file and a
 * paper wad in code (crayon and ink, like ../outside/art.js); the owner then asked for realistic named llamas
 * in the wild, one small item each (2026-11-21, handoff-1121/llamas/BRIEF.md), generated and approved, so every owed drawing is now a real picture and the
 * code-drawn pieces are gone. Paths are relative to the game root. */

/** The pictures the ending shows. */
export const ART = Object.freeze({
  panel: 'assets/fate/llama/panel.webp',
  chew: 'assets/fate/llama/chair-chew.webp',
  spit: 'assets/fate/llama/chair-spit.webp',
  sticker: 'assets/stickers/e_ll_end.webp',
  chair: 'assets/adcard/chair.webp',
});

/** What the first cut owed, and what each slot is now. Nothing is still owed. */
export const OWED = Object.freeze([
  { file: ART.panel, size: '1376 x 768 (about 16:9), shown full scene width', what: 'Scene 1: the panel standing behind a rustic table in an Andes meadow, Dame Margaret (pearls), Sir Reginald (the chair, monocle) and Henry (gold chain), your file on the table.', now: 'real (generated photo)' },
  { file: ART.chew, size: '640 x 794 (about 4:5), shown at scene height', what: 'Scene 2: Sir Reginald on a mountain ridge, chewing a page of your file.', now: 'real (generated photo)' },
  { file: ART.spit, size: '640 x 794 (about 4:5), shown at scene height', what: 'Scene 2: Sir Reginald a beat later, the paper wad on its way out. The photo carries the wad, so the drawn wad went.', now: 'real (generated photo)' },
  { file: ART.sticker, size: '512 x 512, round die-cut with a white border', what: 'Scene 3 and the Sticker Book: Sir Reginald, head and monocle.', now: 'real (portrait crop)' },
  { file: ART.chair, size: '160 x 160, round, transparent outside', what: 'The chair as it sits first in the Living Ad card herd row.', now: 'real (portrait crop)' },
]);
