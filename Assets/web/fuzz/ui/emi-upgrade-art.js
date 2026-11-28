/* Shared shop, Guide, purchase card and receipt art. Coordinates use a 64px grid. */
import { emiTileSprite } from './emi-tile-sprite.js';

const ink = '#272035', gold = '#ffcc62', mint = '#80d8c0';
const path = (d, fill = 'none', extra = '') => `<path d="${d}" fill="${fill}" ${extra}/>`;
const circle = (x, y, r, fill) => `<circle cx="${x}" cy="${y}" r="${r}" fill="${fill}"/>`;
const group = (transform, body) => `<g transform="${transform}">${body}</g>`;
const part = body => `<g data-emi-motion>${body}</g>`;
const coin = (x,y,r) => circle(x,y,r,gold) + path(`M${x} ${y-r*.42}v${r*.84}`, 'none', 'stroke="#9c612c" stroke-width="1.8"');
const prop = (shape,x=40,y=38,size=26) => `<image href="assets/clipart/${shape}.webp" x="${x}" y="${y}" width="${size}" height="${size}"/>`;
const companion = (pose, object) => emiTileSprite(pose, 0, 0, 60) + object;
const copies = () => emiTileSprite('body-idle', 0, 11, 45) + emiTileSprite('body-sway1', 33, 27, 30);

const illustrations = {
  em_head: () => emiTileSprite(),
  em_pet: () => companion('body-pet', prop('heart')),
  em_chat: () => companion('body-idle', prop('speech')),
  em_hat: () => emiTileSprite('body-idle', 3, 7, 57) + prop('hat', 19, -3, 28),
  em_jeal: () => companion('body-sad', prop('burst')),
  em_mini: copies,
  em_nap: () => group('rotate(-12 33 35)', emiTileSprite('body-idle', 0, 6, 56)) + prop('moon', 42, 0, 22),
  em_call: () => companion('body-idle', prop('phone')),
  em_play: () => companion('body-sway1', prop('brickgame')),
  em_boss: () => emiTileSprite('body-smug', 2, 6, 58) + prop('crown', 18, -4, 30),
  o_emi: () => companion('body-idle', prop('megaphone')),
  p_line: () => companion('body-idle', prop('headset')),
  m_watch: () => companion('body-idle', prop('eye')),
  h_amp: () => companion('body-idle', prop('burst')),
  h_oc: () => companion('body-sway1', prop('clock')),
  h_loud: () => companion('body-idle', prop('speaker')),
  h_rep: copies,
  h_over: () => companion('body-smug', prop('cursor')),
  go_tx: () => emiTileSprite('body-idle', -2, 5, 58)
    + part(path('M40 5q9 0 13 9m-14-3q5 0 8 5', 'none', 'stroke-width="5"')
      + path('M40 5q9 0 13 9m-14-3q5 0 8 5', 'none', `stroke="${mint}" stroke-width="2.4"`)),
  go_will: () => part(group('rotate(-4 32 36)', emiTileSprite('body-smug'))),
  go_corp: () => emiTileSprite('body-smug', -2, 3, 53)
    + group('translate(25 19) scale(.62)', part(coin(45,11,6) + coin(53,22,4.5))
      + path('m27 29-4-9 8 2 5-4 6 4 8-1-6 10c6 6 15 13 13 22-2 8-32 10-37 3-6-8 2-19 7-27Z','#d99a53')
      + path('M28 32q7 3 16-1l-1-5q-8 3-16 0Z',mint) + coin(39,45,6.5)),
  go_hive: () => path('M32 20v17M13 42V34h38v8','none',`stroke="${mint}" stroke-width="3"`)
    + part(emiTileSprite('body-idle',14,-1,37)) + emiTileSprite('body-idle',1,35,28) + emiTileSprite('body-idle',36,35,28),
  go_end: () => companion('body-smug', part(path('M45 36q2-3 4 0l12 21q2 3-2 3H35q-4 0-2-3Z',gold)
    + path('M47 42v8m0 5v.1','none','stroke-width="3.5"'))),
};

export function emiUpgradeArt(id) {
  const draw = illustrations[id];
  return draw ? `<g stroke="${ink}" stroke-width="2.5" stroke-linejoin="round" stroke-linecap="round">${EMI_ICON_BEATS[id] ? draw() : part(draw())}</g>` : '';
}

export const EMI_ICON_BEATS = Object.freeze({
  go_tx: [{opacity:1},{opacity:.25},{opacity:1}],
  go_will: [{transform:'translateY(0)'},{transform:'translateY(-4px) rotate(-4deg)'},{transform:'translateY(0)'}],
  go_corp: [{transform:'translateY(0)'},{transform:'translateY(4px)'},{transform:'translateY(0)'}],
  go_hive: [{transform:'translateY(0)'},{transform:'translateY(-3px)'},{transform:'translateY(0)'}],
  go_end: [{transform:'rotate(-3deg)'},{transform:'rotate(3deg)'},{transform:'rotate(0)'}],
});
