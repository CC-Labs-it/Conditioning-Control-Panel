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
const illustrations = {
  go_tx: () => group('translate(2 5) rotate(-6 28 32)', monitor() + shoe(14,42,3) + shoe(29,42,-4))
    + part(path('M37 9q9 0 12 8m-13-2q5 0 7 5', 'none', 'stroke-width="5.3"')
      + path('M37 9q9 0 12 8m-13-2q5 0 7 5', 'none', `stroke="${mint}" stroke-width="2.4"`)),
  go_will: () => path('M2 42h10m-8 7h7m-4 7h8', 'none', 'stroke="#967185" stroke-width="2"')
    + part(group('translate(14 3) rotate(9 23 26)', monitor('wink')
      + path('M10 30 3 35m34-3 6-6', 'none', 'stroke-width="4"')
      + path('M17 42 9 47m20-5 8 3', 'none', 'stroke-width="4"') + shoe(10,46,45) + shoe(35,44,-42)))
    + path('m6 14 2-5 2 5 5 2-5 2-2 5-2-5-4-2Z', mint, 'stroke-width="1.4"'),
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
