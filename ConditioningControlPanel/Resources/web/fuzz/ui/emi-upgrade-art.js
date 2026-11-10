/* Shop illustrations for EMI's four purchases and the Takeover ending. The same art
 * goes on the tile, terms card and receipt. All coordinates use the shop's 64px grid. */
const ink = '#272035', shell = '#625475', glass = '#30283e';
const pink = '#ff81ba', gold = '#ffcc62', mint = '#80d8c0', paper = '#fff3da';
const path = (d, fill = 'none', extra = '') => `<path d="${d}" fill="${fill}" ${extra}/>`;
const circle = (x, y, r, fill) => `<circle cx="${x}" cy="${y}" r="${r}" fill="${fill}"/>`;
const group = (transform, body) => `<g transform="${transform}">${body}</g>`;
const part = body => `<g data-emi-motion>${body}</g>`;

function monitor(expression = 'smile') {
  const face = {
    smile: 'M17 26v3m10-3v3m-9 4q4 4 8-1',
    wink: 'M16 29l3-3 3 2m6-2v3m-9 4q5 4 9-1',
    sync: 'M16 28h5m4 0h5m-10 5h7',
  }[expression];
  return path('M21 13V5') + circle(21, 4, 3, pink)
    + path('M7 20 12 14H35Q39 14 39 19V38Q39 42 35 42H12L6 38Z', shell)
    + path('M7 21 12 17V40L7 37Z', '#40364f', 'stroke="none"')
    + path('M15 20h18q2 0 2 3v10q0 3-3 3H15q-2 0-2-3V23q0-3 2-3Z', glass)
    + path(face, 'none', `stroke="${pink}" stroke-width="2.3"`)
    + circle(16, 39, 1.2, gold)
    + path('M15 16h17', 'none', 'stroke="var(--emi-shine)" stroke-width="1.25"');
}

function shoe(x, y, angle) {
  return group(`translate(${x} ${y}) rotate(${angle})`, path('M0 0v4l3 1q3 0 4 4H-5V3', pink)
    + path('M-4 8H6', 'none', `stroke="${paper}" stroke-width="1.4"`));
}
const pair = (x, y, scale) => group(`translate(${x} ${y}) scale(${scale})`, monitor('sync'));
const coin = (x,y,r) => circle(x,y,r,gold) + path(`M${x} ${y-r*.42}v${r*.84}`, 'none', 'stroke="#9c612c" stroke-width="1.8"');
// Bent sleeves, a balled hand and two broad high-tops stay legible at the 40px phone size.
function stridingEmi() {
  const sleeve = '#766890', cuff = '#9a88aa', limb = '#665979';
  const rearArm = path('M18 23Q12 21 10 28L8 31 6 30Q3 29 2 33L3 37Q4 40 8 38L14 34 20 28Z', sleeve)
    + path('m4 33 4 2', 'none', `stroke="${cuff}" stroke-width="2"`);
  const rearLeg = path('M23 34H32L27 46Q26 50 21 51L13 51 10 45 19 43Z', limb)
    + path('M14 42q2 1 4 6l-2 6-7 1-6-5 1-6 5-5Z', '#dd5a9c')
    + path('M3 48q4 5 12 5l1 3Q7 58 2 51Z', paper)
    + path('m8 43 4 3','none',`stroke="${paper}" stroke-width="1.8"`);
  const frontLeg = path('M31 35 38 34q4 3 5 7l4 8-6 4-8-10-4-3Z', sleeve)
    + path('m36 38 4 5', 'none', `stroke="${cuff}" stroke-width="2"`)
    + path('M39 48 46 44l4 4 6-2q5-1 6 4l-1 4-16 6q-4 1-6-4l-2-4Z', pink)
    + path('M40 55q2 2 6 1l15-6 1 4-17 7q-5 0-6-4Z', paper)
    + path('m48 49 2 3m2-5 2 3','none',`stroke="${paper}" stroke-width="1.6"`);
  const body = group('translate(7 -1) rotate(-5 24 27) scale(.98)', monitor('wink'));
  const frontArm = path('M42 27q4-1 5 2l2 2 3-4-1-3 3-4q2-2 4 0l2 4-1 5-2 2-5 7q-3 4-7 1l-5-6Z', sleeve)
    + path('m51 29 5 3','none',`stroke="${cuff}" stroke-width="2.5"`)
    + path('m55 24 2-1', 'none',`stroke="${cuff}" stroke-width="1.7"`);
  return rearArm + rearLeg + frontLeg + body + frontArm
    + path('m50 6 2 4 4 1-4 2-2 4-1-4-5-2 5-1Z', mint, 'stroke-width="1.5"');
}

const illustrations = {
  go_tx: () => group('translate(2 5) rotate(-6 28 32)', monitor() + shoe(14,42,3) + shoe(29,42,-4))
    + part(path('M37 9q9 0 12 8m-13-2q5 0 7 5', 'none', 'stroke-width="5.3"')
      + path('M37 9q9 0 12 8m-13-2q5 0 7 5', 'none', `stroke="${mint}" stroke-width="2.4"`)),
  go_will: () => part(stridingEmi()),
  go_corp: () => group('translate(0 0) scale(.84)', monitor('wink'))
    + part(coin(45,11,6) + coin(53,22,4.5))
    + path('m27 29-4-9 8 2 5-4 6 4 8-1-6 10c6 6 15 13 13 22-2 8-32 10-37 3-6-8 2-19 7-27Z','#d99a53')
    + path('M28 32q7 3 16-1l-1-5q-8 3-16 0Z',mint)
    + path('M28 38q-6 8-4 13', 'none', 'stroke="#f9ce88" stroke-width="3"')
    + coin(39,45,6.5),
  go_hive: () => path('M32 19v15M13 36v-5h38v5','none',`stroke="${ink}" stroke-width="5"`)
    + path('M32 19v15M13 36v-5h38v5','none',`stroke="${mint}" stroke-width="2.3"`)
    + part(pair(18,0,.69)) + pair(-3,30,.68) + pair(35,30,.68)
    + circle(32,31,3,mint),
  go_end: () => group('translate(2 3) scale(1.12)', monitor('wink'))
    + part(path('M41 29q2-3 4 0l14 24q2 4-3 4H30q-4 0-2-4Z',gold)
      + path('M43 38v8m0 5v.1','none','stroke-width="3.5"')),
};

export function emiUpgradeArt(id, glossy = false) {
  const draw = illustrations[id];
  if (!draw) return '';
  const shine = glossy ? '#cbbadd' : 'none';
  return `<g style="--emi-shine:${shine}" stroke="${ink}" stroke-width="2.5" stroke-linejoin="round" stroke-linecap="round">${draw()}</g>`;
}

export const EMI_ICON_BEATS = Object.freeze({
  go_tx: [{opacity:1},{opacity:.25},{opacity:1}],
  go_will: [{transform:'translateY(0)'},{transform:'translateY(-4px) rotate(-4deg)'},{transform:'translateY(0)'}],
  go_corp: [{transform:'translateY(0)'},{transform:'translateY(4px)'},{transform:'translateY(0)'}],
  go_hive: [{transform:'translateY(0)'},{transform:'translateY(-3px)'},{transform:'translateY(0)'}],
  go_end: [{transform:'rotate(-3deg)'},{transform:'rotate(3deg)'},{transform:'rotate(0)'}],
});
