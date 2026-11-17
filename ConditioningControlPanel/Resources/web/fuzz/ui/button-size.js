/* ui/button-size.js - Big and Tiny Button as pure rules (owner 2026-11-10: "the big button moves things around").
 * Big grows the button IN PLACE: it fills the space #btn-zone already has and never changes the zone, so the counter
 * above and the shop and dock below stay exactly where they were. The zone's height lives in one variable, --zone-h
 * (styles.css), so a plain big button can fill it at every breakpoint. An art face cannot be sized in CSS (every face is
 * a different shape), so ui/unlocks/basics/look2.js measures it once at its natural size and zooms it by `bigFit`:
 * zoom is layout, so the press area grows with the drawing. Tiny keeps its finger-sized press area.
 * No DOM here: tests read these rules as they are. */

/** Room kept between a big button and its zone's edges, in px (each side gets half). */
export const BIG_PAD = 12;
/** The most an art face grows, even when its zone has room for more. */
export const BIG_MAX = 1.7;

/** The zoom for an art face of `faceW x faceH` (measured at zoom 1) in a zone of `zoneW x zoneH`: as big as fits, at most
 *  BIG_MAX, never under 1 (a face that already fills its zone stays as it is). Rounded so a re-measure settles. */
export function bigFit({ zoneW, zoneH, faceW, faceH, max = BIG_MAX }) {
  if (!(faceW > 0 && faceH > 0 && zoneW > 0 && zoneH > 0)) return 1;
  const room = Math.min((zoneW - BIG_PAD) / faceW, (zoneH - BIG_PAD) / faceH, max);
  return Math.max(1, Math.floor(room * 100) / 100);
}

/* Nothing in here may touch #btn-zone, #stage, #shop or the dock: tests/button-size.test.mjs checks that. */
export const SIZE_CSS = `
#btn[data-size=big]:not([data-art-face]) { width: min(70vw, 520px); height: calc(var(--zone-h, 108px) - ${BIG_PAD}px); font-size: clamp(24px, 5vh, 40px); border-radius: 18px; }
#btn[data-size=big][data-art-face] > .fz-face { zoom: var(--fz-big, 1); }
#btn[data-size=tiny]:not([data-art-face]) { width: 18px; height: 12px; min-height: 0; padding: 0; font-size: 0; border-radius: 3px; box-shadow: none; }
#btn[data-size=tiny]:not([data-art-face])::before { content: ""; position: absolute; left: 50%; top: 50%; width: 56px; height: 56px; margin: -28px 0 0 -28px; }
#btn[data-size=tiny]::after { content: ""; position: absolute; left: 50%; top: 50%; width: 46px; height: 46px; margin: -23px 0 0 -23px; border: 2px dashed var(--stamp); border-radius: 50%; pointer-events: none; }
#btn[data-size=tiny]:not([data-art-face]).down { transform: scale(.8); box-shadow: none; }
#world #btn[data-size=tiny][data-art-face] { min-height: 48px; min-width: 48px; }
#btn[data-size=tiny][data-art-face] > .fz-face { zoom: .5; }
#world #btn[data-size=tiny][data-art-face]::after { display: block; width: calc(100% + 14px); height: calc(100% + 14px); margin: 0; translate: -50% -50%; border-radius: 12px; }
#world #btn[data-size=tiny][data-art-face=bubble]::after { border-radius: 50%; }
`;
