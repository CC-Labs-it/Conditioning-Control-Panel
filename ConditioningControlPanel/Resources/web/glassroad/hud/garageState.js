/* The garage stores appearance only. Every kart body but FREE_KART is sold in the Prize Counter (owner 2026-10-29,
 * B3 section 9): the garage offers the free kart, the bodies the account owns (`cos.kart.body.<id>` grants,
 * meta/owned.js) and the two the logbook still earns (meta/unlocks.js); the rest stay visible and dark.
 * Looks only: the sim, the course hash and the room never read a vehicle. */
import {UNLOCKS} from '../meta/unlocks.js';
import {ACHIEVEMENTS} from '../meta/achievements.js';
export const KARTS = [['pendulum','Pendulum'],['reverie','Reverie'],['house-edge','House Edge'],['checkmate','Checkmate'],['lovebomb','Lovebomb'],['feedback','Feedback'],['velvet-lock','Velvet Lock'],['undertow','Undertow'],['petal','Petal'],['trinket','Trinket']];
export const OUTFITS = [['none','Classic'],['varsity','Varsity'],['croupier','Croupier'],['drone-protocol','Protocol'],['builder','Builder'],['cheer','Cheer'],['pool','Pool'],['snow-day','Snow'],['swim','Swim']];
/** The kart every player has: a new player's pick and the fallback for a pick that is locked now. */
export const FREE_KART = 'lovebomb';
/** The Prize Counter grant that opens a kart body (CCP-Server proxy/shop.js: row `kart.body.<id>`, grant `cos.<row>`). */
export const kartGrant = id => 'cos.kart.body.'+id;
/** The dark list with nothing owned or earned: every body but the free one. */
export const LOCKED = new Set(KARTS.map(([id])=>id).filter(id=>id!==FREE_KART));
export const PRESETS = [
  {name:'Blush',primary:'#e7a7c0',secondary:'#80344e',antenna:'#d74b80',hands:'#f1cdd7',shoes:'#c94e7d',face:'#fff1d2'},
  {name:'Seafoam',primary:'#8ed4bd',secondary:'#275e65',antenna:'#72e2ca',hands:'#a6d0c6',shoes:'#397c86',face:'#b5ffe0'},
  {name:'Ink',primary:'#292c4a',secondary:'#e5b254',antenna:'#dbaa51',hands:'#a9b7cf',shoes:'#485779',face:'#f6d47d'},
  {name:'Sorbet',primary:'#f0bf70',secondary:'#d3718e',antenna:'#ffd28b',hands:'#ffd6ae',shoes:'#d3718e',face:'#ffdfe8'},
  {name:'Porcelain',primary:'#e9e5db',secondary:'#6385a7',antenna:'#83a8cc',hands:'#d3e4ed',shoes:'#6385a7',face:'#a7d7ff'},
];
export const COLOR_KEYS = ['primary','secondary','antenna','hands','shoes','face'];
export const DEFAULT = {kart:FREE_KART,outfit:'drone-protocol',...PRESETS[0],original:true,pattern:0,skin:'Original'};
export const STORAGE = 'glassroad.garage.v1';
export function cleanGarage(saved,locked=LOCKED) {
  const s = {...DEFAULT};
  if (!saved || typeof saved !== 'object') return s;
  if (KARTS.some(([id]) => id === saved.kart) && !locked.has(saved.kart)) s.kart = saved.kart;
  const outfit = saved.outfit === 'labcoat' ? 'builder' : saved.outfit;
  if (OUTFITS.some(([id]) => id === outfit)) s.outfit = outfit;
  for (const k of COLOR_KEYS) if (/^#[0-9a-f]{6}$/i.test(saved[k])) s[k] = saved[k];
  if ([0,1,2,3].includes(saved.pattern)) s.pattern = saved.pattern;
  s.original = saved.original !== false;
  s.skin = typeof saved.skin === 'string' ? saved.skin.slice(0,32) : 'Original';
  return s;
}
export function loadGarage(storage,locked=LOCKED) {
  try { return cleanGarage(JSON.parse(storage.getItem(STORAGE) || storage.getItem('rt.garage-proposal.v2')),locked); }
  catch { return cleanGarage(null,locked); }
}
export function routeTo(from,to,locked=LOCKED) {
  if (to === from || !KARTS[to] || locked.has(KARTS[to][0])) return null;
  const right = (to-from+KARTS.length)%KARTS.length;
  return {index:to,steps:right<=KARTS.length/2?right:right-KARTS.length};
}
export function stepKart(from,dir,locked=LOCKED) {
  let index = from;
  do { index = (index+dir+KARTS.length)%KARTS.length; } while (locked.has(KARTS[index][0]) && index !== from);
  return routeTo(from,index,locked);
}
/** The kart ids a grant list opens (anything but `cos.kart.body.<known id>` is ignored). */
export function ownedKarts(grants=[]) {
  const have = new Set(Array.isArray(grants) || grants instanceof Set ? grants : []);
  return new Set(KARTS.map(([id])=>id).filter(id=>have.has(kartGrant(id))));
}
/** The karts that stay dark: every body but the free one, minus what the logbook earned and what the account owns. */
export function lockedKarts(unlocked=new Set(),owned=new Set()) {
  const out = new Set(LOCKED);
  for (const id of unlocked) { const kart = UNLOCKS[id]?.garage; if (kart) out.delete(kart); }
  for (const id of owned) out.delete(id);
  return out;
}
export const COUNTER_LINE = 'In the Prize Counter';
/** What lifts a dark kart: the Prize Counter, and for the two logbook karts their achievement too. Null for the free kart. */
export function lockLine(kart) {
  if (kart === FREE_KART || !KARTS.some(([id])=>id===kart)) return null;
  const entry = Object.values(UNLOCKS).find(u => u.garage === kart);
  if (!entry) return COUNTER_LINE;
  const a = ACHIEVEMENTS.find(x => x.id === entry.by);
  return (a ? 'Unlocks with '+(a.hidden ? 'a hidden achievement' : a.name) : 'Unlocks with an achievement')+', or in the Prize Counter';
}
export function raceLook(s) {
  return {vehicle:'emi-vehicle-'+s.kart,outfit:s.outfit==='none'?null:'emi-outfit-'+s.outfit,
    ...(s.original?{}:{skin:COLOR_KEYS.map(k=>s[k].slice(1).toLowerCase()).join('')+s.pattern})};
}

export function skinFromWire(skin){
  if(typeof skin!=='string'||!/^[0-9a-f]{36}[0-3]$/.test(skin))return {...DEFAULT};
  return {...Object.fromEntries(COLOR_KEYS.map((k,i)=>[k,'#'+skin.slice(i*6,i*6+6)])),pattern:Number(skin[36]),original:false};
}
