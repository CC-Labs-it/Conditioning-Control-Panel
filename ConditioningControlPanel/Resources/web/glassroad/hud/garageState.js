/* The garage stores appearance only. LOCKED is the catalog's dark list; logbook unlocks lift karts off it. */
import {UNLOCKS} from '../meta/unlocks.js';
import {ACHIEVEMENTS} from '../meta/achievements.js';
export const KARTS = [['pendulum','Pendulum'],['reverie','Reverie'],['house-edge','House Edge'],['checkmate','Checkmate'],['lovebomb','Lovebomb'],['feedback','Feedback'],['velvet-lock','Velvet Lock'],['undertow','Undertow']];
export const OUTFITS = [['none','Classic'],['varsity','Varsity'],['croupier','Croupier'],['drone-protocol','Protocol'],['builder','Builder'],['cheer','Cheer'],['pool','Pool'],['snow-day','Snow'],['swim','Swim']];
export const LOCKED = new Set(['velvet-lock','undertow']);
export const PRESETS = [
  {name:'Blush',primary:'#e7a7c0',secondary:'#80344e',antenna:'#d74b80',hands:'#f1cdd7',shoes:'#c94e7d',face:'#fff1d2'},
  {name:'Seafoam',primary:'#8ed4bd',secondary:'#275e65',antenna:'#72e2ca',hands:'#a6d0c6',shoes:'#397c86',face:'#b5ffe0'},
  {name:'Ink',primary:'#292c4a',secondary:'#e5b254',antenna:'#dbaa51',hands:'#a9b7cf',shoes:'#485779',face:'#f6d47d'},
  {name:'Sorbet',primary:'#f0bf70',secondary:'#d3718e',antenna:'#ffd28b',hands:'#ffd6ae',shoes:'#d3718e',face:'#ffdfe8'},
  {name:'Porcelain',primary:'#e9e5db',secondary:'#6385a7',antenna:'#83a8cc',hands:'#d3e4ed',shoes:'#6385a7',face:'#a7d7ff'},
];
export const COLOR_KEYS = ['primary','secondary','antenna','hands','shoes','face'];
export const DEFAULT = {kart:'lovebomb',outfit:'drone-protocol',...PRESETS[0],original:true,pattern:0,skin:'Original'};
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
/** The karts that stay dark: the catalog list minus every kart an unlock id in `unlocked` names. */
export function lockedKarts(unlocked=new Set()) {
  const out = new Set(LOCKED);
  for (const id of unlocked) { const kart = UNLOCKS[id]?.garage; if (kart) out.delete(kart); }
  return out;
}
/** What lifts a dark kart: 'Unlocks with <achievement>'. Null for a kart nothing unlocks. */
export function lockLine(kart) {
  const entry = Object.values(UNLOCKS).find(u => u.garage === kart);
  if (!entry) return null;
  const a = ACHIEVEMENTS.find(x => x.id === entry.by);
  return a ? 'Unlocks with '+(a.hidden ? 'a hidden achievement' : a.name) : 'Unlocks with an achievement';
}
export function raceLook(s) {
  return {vehicle:'emi-vehicle-'+s.kart,outfit:s.outfit==='none'?null:'emi-outfit-'+s.outfit,
    ...(s.original?{}:{skin:COLOR_KEYS.map(k=>s[k].slice(1).toLowerCase()).join('')+s.pattern})};
}

export function skinFromWire(skin){
  if(typeof skin!=='string'||!/^[0-9a-f]{36}[0-3]$/.test(skin))return {...DEFAULT};
  return {...Object.fromEntries(COLOR_KEYS.map((k,i)=>[k,'#'+skin.slice(i*6,i*6+6)])),pattern:Number(skin[36]),original:false};
}
