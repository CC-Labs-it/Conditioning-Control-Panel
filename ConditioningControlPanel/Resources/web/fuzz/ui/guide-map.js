/* ui/guide-map.js - the Guide as a board-game overworld (owner 2026-11-08, v3 mockup acked; replaces The Road).
 * A trunk road runs top to bottom; every fork is a signpost; every route is an island of big icons split into typed rows (one
 * step along a row = the next upgrade of that kind); routes not taken stay on the map, dimmed; every-run lanes and the two
 * shops are pockets. The whole thing floats on a sea that gets deeper as the run goes down (ui/guide-sea.js). Layout and
 * states are pure data in ui/guide-world-model.js; this file draws them, updates them on every buy, and gives them feel.
 * Loaded the first time the Guide opens. */

import { BY_ID, FORKS, LANE_BY_ID } from '../data/items.js';
import { brandText } from '../data/brand.js';
import { setRich } from './rich-text.js';
import { STRAY_WORDS, THEME_STRAY_WORDS } from '../data/content/side.js';
import * as E from '../sim/economy.js';
import { iconFor } from './icons.js';
import { _loadDemos } from './fork.js';
import { THEME_IDS, THEME_PALETTES } from './palette.js';
import { sfx } from './unlocks/basics/fx.js';
import { islandState, layoutWorld, SHOP_BY_ID, TYPES, worldStates } from './guide-world-model.js';
import { createSea, depthLabel } from './guide-sea.js';
import { paintBadges, standingBadges } from './repeat-badge.js';
import { TIP_REPLAY } from '../data/content/tips.js';
import { shopDoor } from './shop.js';
import { devicePlatform } from './platform.js';
import { whole } from '../sim/format.js';
import { FATE_GUIDE_CSS, fateClasses, fateMarks } from './fate-look.js';

const STATUS = { own: 'You have this.', offer: 'On offer now.', skip: 'Walked past it. Still on offer.', later: 'Not open yet.', off: 'Not on your route this run.' };
const CHIP_NAME = { own: 'Owned', offer: 'On offer', skip: 'Walked past', later: 'Later', off: 'Not this run' };
const SVG = 'http://www.w3.org/2000/svg';
const CROSS = '<path d="m6 6 12 12M18 6 6 18" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round"/>';
const TICK = '<svg viewBox="0 0 16 16"><path d="M3 8.5l3.5 3.5L13 4"/></svg>';

const CSS = `
.gr-back { position: fixed; inset: 0; pointer-events: auto; background: rgba(28, 16, 54, .38); }
.gr { --paper: #fbf6ee; --paper-2: #f3eadb; --stamp: #fffdf8; --ink: #2a2031; --ink-soft: #6d6273; --line: #2a2031; --shade: rgba(42, 28, 36, .28);
  --pink: #ff6fb5; --pink-soft: #ffd3e8; --gold: #f6c544; --gold-ink: #8a5c08; --skip: #e8395f; --ghost: #b9adb4;
  position: absolute; left: 10px; right: 10px; top: 52px; bottom: 0; display: flex; flex-direction: column; min-height: 0; overflow: hidden; color: var(--ink); background: var(--paper); border: 2.5px solid var(--line); border-bottom: 0; border-radius: 16px 16px 0 0; box-shadow: 0 -4px 0 var(--shade); font: 600 15px/1.25 var(--bubble, "Trebuchet MS", system-ui, sans-serif); }
.gr button { font: inherit; color: inherit; background: none; border: 0; padding: 0; cursor: pointer; touch-action: manipulation; -webkit-tap-highlight-color: transparent; }
.gr-head { flex: none; display: flex; align-items: center; gap: 8px; height: 54px; padding: 0 10px 0 14px; background: var(--paper); position: relative; z-index: 5; }
.gr-head h2 { margin: 0; font-size: 22px; font-weight: 700; display: flex; align-items: center; gap: 6px; }
.gr-head h2 .ico { width: 38px; height: 38px; }
.gr-head .sp { flex: 1; }
.gr .x { flex: none; width: 40px; height: 40px; border: 2px solid var(--line); border-radius: 50%; background: var(--stamp); display: grid; place-items: center; box-shadow: 0 2px 0 var(--line); transition: transform .08s, box-shadow .08s; }
.gr .x:active { transform: translateY(2px); box-shadow: none; }
.gr .x svg { width: 16px; height: 16px; }
.gr .zb { flex: none; width: 34px; height: 34px; border: 2px solid var(--line); border-radius: 10px 8px 11px 7px; background: var(--stamp); font-size: 18px; line-height: 1; box-shadow: 0 2px 0 var(--line); }
.gr .zb:active, .gr .here:active { transform: translateY(2px); box-shadow: none; }
.gr .here { flex: none; height: 34px; padding: 0 12px; border: 2px solid var(--line); border-radius: 999px; background: var(--pink); color: #fff; font-weight: 700; box-shadow: 0 2px 0 var(--line); white-space: nowrap; }
.gr-bar { flex: none; display: flex; gap: 6px; align-items: center; padding: 0 12px 8px 14px; overflow-x: auto; scrollbar-width: none; border-bottom: 2px solid var(--line); background: var(--paper); position: relative; z-index: 5; }
.gr-bar::-webkit-scrollbar { display: none; }
.gr .chip { flex: none; font-size: 13px; border: 2px solid var(--ink); background: var(--stamp); border-radius: 999px; padding: 4px 10px 4px 6px; display: inline-flex; gap: 6px; align-items: center; line-height: 1; white-space: nowrap; }
.gr .chip[aria-pressed="true"] { background: var(--ink); color: var(--paper); }
.gr .chip .n { font-variant-numeric: tabular-nums; }
.gr .chip i { width: 16px; height: 16px; border-radius: 6px 5px 7px 4px; border: 2px solid var(--ink); display: inline-block; }
.gr .chip.own i { background: var(--pink-soft); } .gr .chip.offer i { background: var(--gold); border-color: var(--gold-ink); }
.gr .chip.skip i { border-color: var(--skip); border-style: dashed; background: var(--gold); } .gr .chip.off i { border-style: dashed; border-color: var(--ghost); background: transparent; }
.gr .chip.later i { background: var(--paper); } .gr .chip[aria-pressed="true"] i { border-color: var(--paper); }
.gr .sws { flex: none; display: flex; gap: 5px; align-items: center; margin-left: 8px; padding-left: 10px; border-left: 2px dotted color-mix(in srgb, var(--ink) 35%, transparent); }
.gr .sws .lbl { font-size: 12px; color: var(--ink-soft); margin-right: 2px; white-space: nowrap; }
.gr .sw { flex: none; width: 24px; height: 24px; border-radius: 9px 7px 10px 8px; border: 2px solid var(--ink); background: var(--c); transition: transform .18s ease; }
.gr .sw[aria-pressed="true"] { transform: scale(1.18); box-shadow: 0 0 0 3px var(--paper), 0 0 0 5px var(--c); }
.gr .sw:hover { transform: scale(1.1); }

.gr-stage { position: relative; flex: 1 1 auto; min-height: 0; overflow: hidden; background: #1c1036; }
.gr-sea { position: absolute; left: 0; top: 0; width: 100%; height: 100%; pointer-events: none; }
.gr-scroll { position: absolute; inset: 0; overflow: auto; scrollbar-width: none; cursor: grab; overscroll-behavior: contain; }
.gr-scroll::-webkit-scrollbar { display: none; }
.gr-scroll.drag { cursor: grabbing; user-select: none; }
.gr-sizer { position: relative; }
.gr-world { position: absolute; left: 0; top: 0; transform-origin: 0 0; }
.gr-world > svg { position: absolute; left: 0; top: 0; overflow: visible; pointer-events: none; }
.gr-world .sp .plank, .gr-world .sp .board { pointer-events: auto; }
.gr .roads .bank { fill: none; stroke: rgba(251, 246, 238, .8); stroke-linecap: round; stroke-linejoin: round; }
.gr .roads .trunk { fill: none; stroke: var(--ink); stroke-width: 12; stroke-linecap: round; }
.gr .roads .trunk.sink { stroke-width: 10; stroke-dasharray: 18 14; opacity: .85; }
.gr .roads .trunk2 { fill: none; stroke: var(--paper); stroke-width: 2.5; stroke-dasharray: 10 12; stroke-linecap: round; }
.gr .roads .rd { fill: none; stroke: var(--ink); stroke-width: 9; stroke-linecap: round; }
.gr .roads .rd2 { fill: none; stroke: var(--paper); stroke-width: 2; stroke-dasharray: 8 10; stroke-linecap: round; }
.gr .roads .rd.open { stroke: var(--c); stroke-width: 7; }
.gr .roads .rd.shut { stroke: color-mix(in srgb, var(--c) 60%, var(--ghost)); stroke-width: 5; stroke-dasharray: 2 12; }
.gr .roads .rd2.shut, .gr .roads .rd2.open { display: none; }
.gr .roads .fp { fill: none; stroke: var(--ink); stroke-width: 3.5; stroke-dasharray: 1 9; stroke-linecap: round; }
.gr .roads .tw { fill: none; stroke: var(--ink); stroke-width: 6; stroke-linecap: round; stroke-linejoin: round; }
.gr .roads .tw2 { fill: none; stroke: var(--c); stroke-width: 2.5; stroke-linecap: round; stroke-dasharray: 6 7; }
.gr .roads .tw.shut { stroke: color-mix(in srgb, var(--c) 50%, var(--ghost)); stroke-width: 3; stroke-dasharray: 2 8; }
.gr .roads .tw2.shut { display: none; }
.gr .roads .knot { fill: var(--c); stroke: var(--ink); stroke-width: 3; } .gr .roads .knot.shut { fill: var(--paper); stroke: var(--ghost); }
.gr .roads .sel { stroke: var(--c) !important; stroke-width: 10 !important; stroke-dasharray: none !important; }

.gr .isl { position: absolute; border: 3px solid var(--ink); border-radius: 30px 22px 34px 20px / 24px 30px 20px 32px; background: color-mix(in srgb, var(--c) 32%, var(--paper)); box-shadow: 4px 5px 0 rgba(20, 10, 40, .45); transition: box-shadow .25s ease, border-color .25s ease, background .25s ease; }
.gr .isl:nth-child(odd) { transform: rotate(-.35deg); } .gr .isl:nth-child(even) { transform: rotate(.3deg); }
.gr .isl .tt { position: absolute; left: 12px; top: -17px; z-index: 2; white-space: nowrap; font-size: 14px; font-weight: 700; background: var(--paper); border: 2px solid var(--ink); border-radius: 10px 6px 10px 6px; padding: 3px 10px 3px 8px; transform: rotate(-2deg); display: inline-flex; gap: 6px; align-items: center; line-height: 1.1; box-shadow: 2px 2px 0 var(--ink); }
.gr .isl .tt::before { content: ""; width: 11px; height: 11px; border-radius: 50%; background: var(--c); border: 2px solid var(--ink); }
.gr .isl.pocket .tt::before { border-radius: 2px; transform: rotate(45deg); width: 9px; height: 9px; }
.gr .isl.shut { background: repeating-linear-gradient(135deg, transparent 0 9px, color-mix(in srgb, var(--ink) 7%, transparent) 9px 11px), color-mix(in srgb, var(--c) 10%, var(--paper)); border-color: var(--ghost); border-style: dashed; box-shadow: none; }
.gr .isl.shut .tt { color: var(--ink-soft); border-color: var(--ghost); box-shadow: none; border-style: dashed; }
.gr .isl.shut .tt::before { background: color-mix(in srgb, var(--c) 45%, var(--paper)); border-color: var(--ghost); }
.gr .isl.picked { border-width: 4px; }
.gr .isl.picked .tt::after { content: ""; width: 8px; height: 8px; border-radius: 50%; background: var(--pink); box-shadow: 0 0 0 2px var(--ink); margin-left: 2px; }
.gr .isl.sel { border-color: var(--ink); border-style: solid; box-shadow: 0 0 0 5px var(--c), 4px 5px 0 rgba(20, 10, 40, .45); background: color-mix(in srgb, var(--c) 48%, var(--paper)); }
.gr .isl.sel .tt { background: var(--c); border-color: var(--ink); color: var(--ink); border-style: solid; }
.gr .lad { position: absolute; border-radius: 16px 12px 18px 12px; background: color-mix(in srgb, var(--c) 18%, var(--paper)); border: 2px solid color-mix(in srgb, var(--c) 55%, var(--ink)); }
.gr .lad:nth-of-type(even) { background: color-mix(in srgb, var(--c) 30%, var(--paper)); }
.gr .lad.shut { border-style: dashed; border-color: var(--ghost); background: color-mix(in srgb, var(--c) 6%, var(--paper)); }
.gr .lad .hd { position: absolute; top: 50%; translate: 0 -50%; width: max-content; max-width: 104px; display: inline-flex; align-items: center; gap: 5px; min-height: 28px; padding: 3px 8px 3px 5px; border: 2px solid var(--ink); border-radius: 9px 6px 9px 6px; background: var(--stamp); font-size: 11px; font-weight: 700; line-height: 1.05; box-shadow: 1px 2px 0 var(--ink); z-index: 1; }
.gr .lad .hd img { width: 20px; height: 20px; object-fit: contain; flex: none; }
.gr .lad.shut .hd { border-color: var(--ghost); color: var(--ink-soft); box-shadow: none; border-style: dashed; } .gr .lad.shut .hd img { filter: grayscale(1); opacity: .5; }
.gr .lad .lk { position: absolute; top: 50%; height: 6px; margin-top: -3px; width: 22px; margin-left: -11px; background: var(--ink); border-radius: 3px; }
.gr .lad .lk.dot { background: repeating-linear-gradient(to right, var(--ink) 0 4px, transparent 4px 8px); }
.gr .lad.shut .lk { background: var(--ghost); } .gr .lad.shut .lk.dot { background: repeating-linear-gradient(to right, var(--ghost) 0 4px, transparent 4px 8px); }
.gr .lad .lk::after { content: ""; position: absolute; top: 50%; width: 8px; height: 8px; margin-top: -5px; border-right: 3px solid var(--ink); border-bottom: 3px solid var(--ink); }
.gr .lad .lk.R::after { right: -2px; rotate: -45deg; } .gr .lad .lk.L::after { left: -2px; rotate: 135deg; }
.gr .lad.shut .lk::after { border-color: var(--ghost); }

.gr .tk { position: absolute; width: 70px; height: 70px; margin: -35px 0 0 -35px; border: 3px solid var(--ink); border-radius: 24px 18px 26px 16px / 18px 26px 16px 24px; background: var(--stamp); display: grid; place-items: center; transition: transform .18s cubic-bezier(.3,1.6,.5,1), box-shadow .18s ease; box-shadow: 2px 3px 0 var(--ink); z-index: 2; }
.gr .tk .ico, .gr .tk > img { width: 56px; height: 56px; object-fit: contain; pointer-events: none; }
.gr .tk:hover, .gr .tk.sel { transform: scale(1.14); z-index: 5; box-shadow: 3px 4px 0 var(--ink); }
.gr .tk.head { width: 80px; height: 80px; margin: -40px 0 0 -40px; } .gr .tk.head .ico { width: 64px; height: 64px; }
.gr .tk.end { width: 84px; height: 84px; margin: -42px 0 0 -42px; border-width: 4px; border-radius: 50% 50% 46% 46% / 50% 50% 40% 40%; box-shadow: 0 0 0 4px var(--paper), 0 0 0 7px var(--ink), 3px 4px 0 var(--ink); }
.gr .tk.end .ico { width: 64px; height: 64px; }
.gr .tk.pic { width: 108px; height: 70px; margin: -35px 0 0 -54px; border-radius: 14px 10px 16px 10px; padding: 3px; }
.gr .tk.pic > img { width: 98px; height: 58px; border-radius: 8px; }
.gr .tk.own { background: var(--pink-soft); }
.gr .tk .bd { position: absolute; right: -9px; top: -9px; width: 22px; height: 22px; border-radius: 50%; border: 2px solid var(--ink); display: none; place-items: center; }
.gr .tk.own .bd, .gr .tk.offer .bd, .gr .tk.skip .bd { display: grid; }
.gr .tk.own .bd { background: var(--pink); } .gr .tk .bd svg { width: 12px; height: 12px; stroke: var(--ink); stroke-width: 3; fill: none; display: none; } .gr .tk.own .bd svg { display: block; }
.gr .tk.offer { background: var(--gold); border-color: var(--gold-ink); animation: gr-bob 2.6s ease-in-out infinite; }
.gr .tk.offer .bd { background: var(--gold); border-color: var(--gold-ink); } .gr .tk.offer .bd::after { content: ""; width: 8px; height: 8px; border-radius: 50%; border: 2px solid var(--gold-ink); }
.gr .tk.skip { background: var(--gold); border-color: var(--skip); border-style: dashed; animation: gr-nudge 2.2s ease-in-out infinite; }
.gr .tk.skip .bd { background: var(--paper); border-color: var(--skip); } .gr .tk.skip .bd::after { content: ""; width: 10px; height: 3px; background: var(--skip); border-radius: 2px; transform: rotate(-20deg); }
.gr .tk.later .ico { opacity: .9; }
.gr .tk.off { background: color-mix(in srgb, var(--paper) 55%, transparent); border-style: dashed; border-color: var(--ghost); box-shadow: none; }
.gr .tk.off .ico, .gr .tk.off > img { filter: grayscale(1) contrast(.8); opacity: .32; }
.gr .tk.off:hover .ico, .gr .tk.off:hover > img { opacity: .7; }
.gr .tk .dent { position: absolute; left: 50%; top: 50%; width: 120px; height: 120px; margin: -60px 0 0 -60px; border-radius: 50%; border: 3px solid var(--pink); pointer-events: none; opacity: 0; }
@keyframes gr-bob { 0%, 100% { translate: 0 0; } 50% { translate: 0 -5px; } }
@keyframes gr-nudge { 0%, 70%, 100% { rotate: 0deg; } 78% { rotate: -6deg; } 86% { rotate: 6deg; } 93% { rotate: -3deg; } }
.gr .tk.offer:hover, .gr .tk.skip:hover { animation: none; }
.gr .next-tag { position: absolute; z-index: 7; pointer-events: none; margin: -66px 0 0 18px; }
.gr .next-tag b { display: block; padding: 4px 10px 4px 16px; font: 700 16px "Courier New", Courier, monospace; color: #2a1c24; background: var(--gold); border: 2px solid #2a1c24; clip-path: polygon(10px 0, 100% 0, 100% 100%, 10px 100%, 0 50%); transform: rotate(8deg); transform-origin: 0 50%; }
.gr .pin { position: absolute; width: 46px; height: 60px; margin: -74px 0 0 -23px; pointer-events: none; animation: gr-pin 2.4s ease-in-out infinite; transform-origin: 50% 100%; z-index: 6; filter: drop-shadow(2px 3px 0 var(--ink)); }
@keyframes gr-pin { 0%, 100% { translate: 0 0; } 50% { translate: 0 -6px; } }

.gr .sp .post { fill: var(--ink); stroke: var(--paper); stroke-width: 3; paint-order: stroke; }
.gr .sp .board { fill: var(--paper); stroke: var(--ink); stroke-width: 3; cursor: pointer; }
.gr .sp .board-t { fill: var(--ink); font-size: 17px; font-weight: 700; text-anchor: middle; dominant-baseline: central; pointer-events: none; }
.gr .sp .plank { cursor: pointer; }
.gr .sp .plank path { fill: var(--c); stroke: var(--ink); stroke-width: 2.5; stroke-linejoin: round; transition: transform .18s ease; }
.gr .sp .plank text { fill: var(--ink); font-size: 15px; font-weight: 700; dominant-baseline: central; pointer-events: none; }
.gr .sp .plank.shut path { fill: color-mix(in srgb, var(--c) 22%, var(--paper)); stroke: var(--ghost); stroke-dasharray: 3 3; }
.gr .sp .plank.shut text { fill: var(--ink-soft); }
.gr .sp .plank.picked path { stroke-width: 3.5; }
.gr .sp .plank:hover path { transform: translateY(-2px); }
.gr .sp .plank.sel path { fill: var(--c); stroke: var(--ink); stroke-dasharray: none; stroke-width: 3.5; }
.gr .sp .pinb { fill: var(--pink); stroke: var(--ink); stroke-width: 2; }

.gr-world.f-own .tk:not(.own), .gr-world.f-offer .tk:not(.offer), .gr-world.f-skip .tk:not(.skip), .gr-world.f-off .tk:not(.off), .gr-world.f-later .tk:not(.later) { opacity: .18; animation: none; }

.gr .mini { position: absolute; right: 10px; bottom: 10px; width: 92px; border: 2px solid var(--ink); border-radius: 8px; overflow: hidden; box-shadow: 2px 2px 0 var(--ink); background: linear-gradient(#fff4e6, #fde3ea 10%, #efd8f2 30%, #cdb8ee 50%, #8e77d2 68%, #4b3590 84%, #1c1036); z-index: 8; cursor: pointer; }
.gr .mini i { position: absolute; border-radius: 3px; background: var(--c); border: 1px solid rgba(42, 32, 49, .5); }
.gr .mini i.shut { opacity: .35; }
.gr .mini .tr { position: absolute; background: var(--ink); border-radius: 2px; }
.gr .mini .vp { position: absolute; border: 2px solid var(--pink); border-radius: 3px; background: rgba(255, 111, 181, .18); pointer-events: none; }
.gr .depth { position: absolute; left: 10px; bottom: 10px; z-index: 8; display: flex; align-items: center; gap: 8px; padding: 5px 12px 5px 8px; border: 2px solid var(--ink); border-radius: 12px 8px 12px 8px; background: var(--stamp); box-shadow: 2px 2px 0 var(--ink); font-size: 13px; pointer-events: none; }
.gr .depth svg { width: 18px; height: 26px; flex: none; }
.gr .depth small { display: block; font-size: 10px; color: var(--ink-soft); letter-spacing: .5px; text-transform: uppercase; }
.gr .depth b { display: block; font-size: 15px; line-height: 1.05; }

.gr-tip { position: absolute; z-index: 20; pointer-events: none; max-width: 220px; padding: 6px 10px; background: var(--stamp); border: 2px solid var(--ink); border-radius: 10px 7px 10px 7px; box-shadow: 2px 2px 0 var(--ink); font-size: 13px; line-height: 1.25; }
.gr-tip b { display: block; font-size: 14px; } .gr-tip span { color: var(--ink-soft); }

.gr-card { position: absolute; z-index: 30; top: 104px; right: 16px; width: 300px; max-height: calc(100% - 120px); overflow-y: auto; overscroll-behavior: contain; background: var(--paper); color: var(--ink); border: 2.5px solid var(--line); border-radius: 12px; box-shadow: 4px 4px 0 var(--shade); padding: 14px 16px 16px; scrollbar-width: thin; }
.gr-card .x { position: absolute; right: 10px; top: 10px; width: 34px; height: 34px; }
.gr-card .hd { display: flex; gap: 12px; align-items: center; padding-right: 36px; }
.gr-card .ic { flex: none; width: 92px; height: 92px; border-radius: 10px; background: var(--stamp); border: 2px solid var(--line); display: grid; place-items: center; overflow: hidden; }
.gr-card .ic .ico { width: 80px; height: 80px; } .gr-card .ic img { width: 86px; height: 86px; object-fit: contain; }
.gr-card h3 { margin: 0; font-size: 20px; font-weight: 700; line-height: 1.1; }
.gr-card .stl { margin: 4px 0 0; font-size: 14px; color: var(--ink-soft); font-weight: 700; }
.gr-card p { margin: 10px 0 0; font-size: 15px; line-height: 1.35; }
.gr-card p.line { font-style: italic; color: var(--ink-soft); }
.gr-card .demo { position: relative; margin-top: 12px; border: 2px dashed color-mix(in srgb, var(--line) 40%, transparent); border-radius: 8px; overflow: hidden; background: var(--paper-2); }
.gr-card .pv { display: block; width: 100%; max-height: 160px; object-fit: contain; margin-top: 12px; border: 2px solid var(--line); border-radius: 10px; background: var(--paper-2); }
.gr-card h4 { margin: 14px 0 6px; font-size: 13px; letter-spacing: .5px; color: var(--ink-soft); font-weight: 700; text-transform: uppercase; }
.gr-card .list { display: flex; flex-wrap: wrap; gap: 6px; }
.gr-card .chip { display: inline-flex; align-items: center; gap: 4px; padding: 2px 8px 2px 2px; border: 2px solid var(--line); border-radius: 18px; background: var(--stamp); color: #2a1c24; font-size: 13px; font-weight: 700; }
.gr-card .chip .ico { width: 28px; height: 28px; }
.gr-card .chip.shut { opacity: .55; filter: grayscale(1); }
.gr-card .foot { margin-top: 14px; padding-top: 10px; border-top: 2px dotted color-mix(in srgb, var(--line) 35%, transparent); display: flex; justify-content: space-between; gap: 8px; font-size: 14px; font-weight: 700; }
.gr-card .opt { margin-top: 8px; padding: 8px 10px; border: 2px solid var(--line); border-radius: 8px; background: var(--stamp); color: #2a1c24; font-size: 14px; }
.gr-card .opt.shut { background: transparent; color: var(--ink-soft); border-style: dashed; }
.gr-card .opt b { display: block; }
.gr-card .grab { display: none; }
.gr-card .buy { display: block; width: 100%; min-height: 44px; margin-top: 12px; border: 2.5px solid var(--line); border-radius: 12px 10px 13px 9px; background: var(--pink); color: #fff; font-size: 17px; font-weight: 700; box-shadow: 0 3px 0 var(--line); transition: transform .08s, box-shadow .08s; }
.gr-card .buy:active { transform: translateY(3px); box-shadow: none; }
.gr-card .buy:disabled { background: var(--paper-2); color: var(--ink-soft); box-shadow: none; cursor: default; }

@media (max-width: 759px) {
  .gr { left: 0; right: 0; top: 48px; border-left: 0; border-right: 0; border-radius: 14px 14px 0 0; }
  .gr-head { padding: 0 8px 0 10px; gap: 6px; height: 50px; }
  .gr-head h2 { font-size: 19px; } .gr-head h2 .ico { width: 30px; height: 30px; }
  .gr .here { padding: 0 9px; font-size: 13px; }
  .gr-bar { padding: 0 8px 7px 10px; }
  .gr .mini { width: 54px; right: 8px; bottom: 8px; }
  .gr .depth { left: 8px; bottom: 8px; }
  .gr-card { left: 0; right: 0; top: auto; bottom: 0; width: auto; max-height: 60%; border-radius: 16px 16px 0 0; border-bottom: 0; box-shadow: 0 -4px 0 var(--shade); padding-top: 18px; }
  .gr-card .grab { display: block; position: absolute; left: 50%; top: 7px; width: 44px; height: 5px; margin-left: -22px; border-radius: 3px; background: color-mix(in srgb, var(--line) 30%, transparent); }
  .gr-card .ic { width: 76px; height: 76px; } .gr-card .ic .ico { width: 66px; height: 66px; }
}
@media (hover: none) { .gr-tip { display: none; } }
@media (prefers-reduced-motion: reduce) { .gr .tk, .gr .pin, .gr .sp .plank path, .gr .sw, .gr .isl { animation: none !important; transition: none !important; } }
body.gentle .gr .tk, body.gentle .gr .pin { animation: none !important; }
`;

/** Build the sheet once. Returns { show, hide, select }: closing only hides it, so it opens again where it was. */
export function createGuide(ctx) {
  const doc = ctx.button.el.ownerDocument;
  const win = doc.defaultView;
  const still = () => ctx.gentle || ctx.reduced;
  const phone = () => ctx.bounds().w < 760;
  const W = layoutWorld();
  ctx.css(CSS);
  ctx.css(FATE_GUIDE_CSS);   // the Fate pocket: the shop's fate card look (ui/fate-look.js)
  /* Rows this device cannot use (ui/platform.js) read as off, with the reason on their card. */
  const platform = devicePlatform(win, () => ctx.caps || {});
  const statusOf = (id, st) => (view?.device?.[id] ? platform.whyNot(id) : STATUS[st]) || '';

  const mk = (tag, cls, parent, text) => {
    const e = doc.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) setRich(e, String(text));
    if (parent) parent.appendChild(e);
    return e;
  };
  const icon = (id) => {
    try { return iconFor(doc, BY_ID[id] || { id: 'box' }); } catch { try { return iconFor(doc, { id: 'box' }); } catch { return mk('span', 'ico'); } }
  };
  const esc = (s) => String(s).replace(/&/g, '&amp;').replace(/"/g, '&quot;').replace(/</g, '&lt;');
  const cross = () => { const s = doc.createElementNS(SVG, 'svg'); s.setAttribute('viewBox', '0 0 24 24'); s.setAttribute('aria-hidden', 'true'); s.innerHTML = CROSS; return s; };
  const play = (el, kf, o) => (el?.animate ? el.animate(kf, o) : null);
  const nameOf = (id) => brandText(BY_ID[id]?.name || SHOP_BY_ID[id]?.name || id);
  const isl = Object.fromEntries(W.islands.map((i) => [i.id, i]));
  const colourOf = (lane) => isl[lane]?.color || '#999';

  /* ---- the shell ---- */
  const cards = doc.getElementById('cards');
  const back = mk('div', 'gr-back');
  cards.prepend(back);   // under any card that comes up, which stays the host's last child
  const root = mk('section', 'gr', back);
  root.setAttribute('role', 'dialog');
  root.setAttribute('aria-label', 'Guide');
  const head = mk('div', 'gr-head', root);
  const h2 = mk('h2', '', head);
  h2.append(icon('guide'), 'Guide');
  mk('span', 'sp', head);
  const zout = mk('button', 'zb', head, '-'); zout.type = 'button'; zout.setAttribute('aria-label', 'Zoom out');
  const zin = mk('button', 'zb', head, '+'); zin.type = 'button'; zin.setAttribute('aria-label', 'Zoom in');
  const hereBtn = mk('button', 'here', head, 'You are here'); hereBtn.type = 'button';
  /* First-run tips replay (ui/tips.js listens for the event once the Guide has closed). */
  const tipsBtn = mk('button', 'zb tips', head, TIP_REPLAY.label); tipsBtn.type = 'button'; tipsBtn.setAttribute('aria-label', TIP_REPLAY.aria); tipsBtn.style.cssText = 'width:auto;padding:0 10px;font-size:14px;font-weight:700';
  tipsBtn.addEventListener('click', () => { hide(); ctx.emit('tips-replay'); });
  const closeBtn = mk('button', 'x', head);
  closeBtn.type = 'button';
  closeBtn.setAttribute('aria-label', 'Close the guide');
  closeBtn.appendChild(cross());
  closeBtn.addEventListener('click', () => hide());
  const bar = mk('div', 'gr-bar', root);
  const chipEls = {};
  for (const s of ['own', 'offer', 'skip', 'later', 'off']) {
    const c = mk('button', `chip ${s}`, bar);
    c.type = 'button'; c.dataset.f = s; c.setAttribute('aria-pressed', 'false');
    c.append(mk('i'), CHIP_NAME[s] + ' ');
    chipEls[s] = mk('span', 'n', c, '0');
  }
  const sws = mk('div', 'sws', bar);
  mk('span', 'lbl', sws, 'Themes');
  for (const t of THEME_IDS) {
    if (!isl[t]) continue;
    const b = mk('button', 'sw', sws);
    b.type = 'button'; b.dataset.sel = t; b.style.setProperty('--c', colourOf(t));
    b.title = brandText(isl[t].name); b.setAttribute('aria-label', brandText(isl[t].name)); b.setAttribute('aria-pressed', 'false');
  }
  const stage = mk('div', 'gr-stage', root);
  const scroll = mk('div', 'gr-scroll', stage);
  const sizer = mk('div', 'gr-sizer', scroll);
  const world = mk('div', 'gr-world', sizer);
  const mini = mk('div', 'mini', stage);
  mini.title = 'Map';
  const depth = mk('div', 'depth', stage);
  depth.innerHTML = '<svg viewBox="0 0 18 26" aria-hidden="true"><path d="M9 1v16" stroke="#2a2031" stroke-width="2.4" stroke-dasharray="2 3" stroke-linecap="round"/><path d="M4 16h10l-2 8H6z" fill="#ff6fb5" stroke="#2a2031" stroke-width="2" stroke-linejoin="round"/></svg><span><small>Depth</small><b></b></span>';
  const depthText = depth.querySelector('b');
  const tip = mk('div', 'gr-tip', root);
  tip.hidden = true;

  let scale = 0.85, filter = null, laneSel = null, card = null, selected = null, stopDemo = null, demoToken = 0;
  let view = null, seen = null;
  let shown = false, built = false, memo = null, outAnim = null;
  const tokens = {};

  /* ---- the world, drawn once; states are classes, updated on every buy ---- */
  function build() {
    world.style.width = W.W + 'px';
    world.style.height = W.H + 'px';
    let h = `<svg class="roads" width="${W.W}" height="${W.H}" viewBox="0 0 ${W.W} ${W.H}">`;
    /* light banks under every road, so ink roads still read on the dark water */
    for (const p of W.paths) h += `<path class="bank" style="stroke-width:10" d="${p.d}"/>`;
    h += `<path class="bank" style="stroke-width:22" d="${W.trunkD}"/><path class="bank" style="stroke-width:16;stroke-dasharray:1 26" d="${W.sinkD}"/>`;
    h += `<path class="trunk sink" d="${W.sinkD}"/><path class="trunk2" d="${W.sinkD}"/>`;
    for (const r of W.roads) h += `<path class="bank" style="stroke-width:15" d="${r.d}"/>`;
    for (const t of W.twigs) h += `<path class="bank" style="stroke-width:12" d="${t.d}"/>`;
    for (const p of W.paths) h += `<path class="fp" d="${p.d}"/>`;
    h += `<path class="trunk" d="${W.trunkD}"/><path class="trunk2" d="${W.trunkD}"/>`;
    for (const t of W.twigs) {
      h += `<path class="tw" data-lane="${t.lane}" style="--c:${colourOf(t.lane)}" d="${t.d}"/><path class="tw2" data-lane="${t.lane}" style="--c:${colourOf(t.lane)}" d="${t.d}"/>`;
      for (const [kx, ky] of t.knots) h += `<circle class="knot" data-lane="${t.lane}" style="--c:${colourOf(t.lane)}" cx="${kx}" cy="${ky}" r="6"/>`;
    }
    for (const r of W.roads) h += `<path class="rd" data-lane="${r.lane}" style="--c:${colourOf(r.lane)}" d="${r.d}"/><path class="rd2" data-lane="${r.lane}" d="${r.d}"/>`;
    h += '</svg>';
    for (const i of W.islands) h += `<div class="isl ${i.kind}" data-isl="${i.id}" style="--c:${i.color};left:${i.x}px;top:${i.y}px;width:${i.w}px;height:${i.h}px"><button type="button" class="tt" data-sel="${i.id}">${esc(brandText(i.name))}</button></div>`;
    for (const i of W.islands) for (const l of i.ladders) {
      h += `<div class="lad" data-lad="${i.id}" style="--c:${i.color};left:${l.x}px;top:${l.y}px;width:${l.w}px;height:${l.h}px"><span class="hd" style="left:${l.stampX - l.x}px"><img alt="" src="assets/clipart/${l.shape}.webp">${esc(l.name)}</span>${l.links.map((k) => `<i class="lk ${k.kind} ${l.side}" style="left:${k.x - l.x}px"></i>`).join('')}</div>`;
    }
    for (const f of W.forks) h += signpost(f);
    world.innerHTML = h;
    for (const n of W.nodes) {
      if (n.fork) continue;
      const b = mk('button', ['tk', n.head ? 'head' : '', n.end ? 'end' : '', n.pic ? 'pic' : '', ...fateClasses(BY_ID[n.id])].filter(Boolean).join(' '), world);
      b.type = 'button';
      b.dataset.id = n.id;
      b.style.left = n.x + 'px';
      b.style.top = n.y + 'px';
      if (n.pic) { const im = mk('img', '', b); im.alt = ''; im.src = n.pic; im.loading = 'lazy'; im.onerror = () => { im.replaceWith(icon('box')); }; }
      else b.appendChild(icon(n.id));
      const bd = mk('span', 'bd', b);
      bd.innerHTML = TICK;
      b.append(...fateMarks(doc, BY_ID[n.id]));
      tokens[n.id] = b;
    }
    const pin = doc.createElementNS(SVG, 'svg');
    pin.setAttribute('class', 'pin'); pin.setAttribute('viewBox', '0 0 46 60'); pin.setAttribute('aria-hidden', 'true');
    pin.innerHTML = '<path d="M23 58C23 58 4 34 4 21a19 19 0 0 1 38 0c0 13-19 37-19 37z" fill="#ff6fb5" stroke="#2a2031" stroke-width="3"/><circle cx="23" cy="21" r="8" fill="#fbf6ee" stroke="#2a2031" stroke-width="3"/>';
    world.appendChild(pin);
    const nextTag = mk('span', 'next-tag', world);
    mk('b', '', nextTag, 'next');
    let mh = '<span class="tr"></span>';
    for (const i of W.islands) mh += `<i data-mini="${i.id}"></i>`;
    mini.innerHTML = mh + '<span class="vp"></span>';
  }
  function signpost(f) {
    const { x, y } = f;
    const nL = f.planks.filter((p) => p.side === 'L').length, nR = f.planks.length - nL;
    const bottom = y - 6 + Math.max(nL, nR) * 32 + 12;
    const bw = Math.max(120, f.name.length * 10.5 + 30);
    let s = `<svg class="sp" width="${W.W}" height="${W.H}" viewBox="0 0 ${W.W} ${W.H}">`;
    s += `<rect class="post" x="${x - 7}" y="${y - 58}" width="14" height="${bottom - (y - 54) + 20}" rx="4"/>`;
    s += `<g transform="rotate(-2 ${x} ${y - 46})" data-fork="${f.id}"><rect class="board" x="${x - bw / 2}" y="${y - 66}" width="${bw}" height="38" rx="7"/><text class="board-t" x="${x}" y="${y - 47}">${esc(brandText(f.name))}</text></g>`;
    for (const p of f.planks) {
      const dir = p.side === 'L' ? -1 : 1, x0 = x + dir * 10, x1 = x + dir * 122, tipX = x + dir * 138, py = p.y, hh = 14;
      s += `<g class="plank" data-plank="${p.lane}" data-fork="${f.id}" style="--c:${colourOf(p.lane)}"><path d="M${x0},${py - hh} L${x1},${py - hh} L${tipX},${py} L${x1},${py + hh} L${x0},${py + hh} Z"/><text x="${(x0 + x1) / 2}" y="${py}" text-anchor="middle">${esc(brandText(p.name))}</text><circle class="pinb" cx="${x0 + dir * 8}" cy="${py - hh}" r="5" display="none"/></g>`;
    }
    return s + '</svg>';
  }
  function sizeMini() {
    const MW = mini.clientWidth || 92, k = MW / W.W;
    mini.style.height = Math.round(W.H * k) + 'px';
    for (const el of mini.querySelectorAll('i')) { const i = isl[el.dataset.mini]; el.style.cssText = `--c:${i.color};left:${i.x * k}px;top:${i.y * k}px;width:${i.w * k}px;height:${i.h * k}px`; }
    mini.querySelector('.tr').style.cssText = `left:${Math.round(W.CX * k) - 1}px;top:${Math.round(100 * k)}px;width:2px;height:${Math.round((W.H - 600) * k)}px`;
  }

  function applyStates() {
    view = worldStates(ctx.state, W, { canOffer: platform.canOffer });
    const picked = E.fateOf(ctx.state);
    for (const [id, b] of Object.entries(tokens)) {
      const st = view.states[id];
      for (const s of ['own', 'offer', 'skip', 'later', 'off']) b.classList.toggle(s, s === st);
      b.setAttribute('aria-label', `${nameOf(id)}, ${statusOf(id, st)}`);
      if (BY_ID[id]) paintBadges(b, standingBadges(BY_ID[id], ctx.state));   // level and copies, ui/repeat-badge.js
      if (BY_ID[id]?.fate) {   // the picked set lit; a seed this run can never take wears the stamp (ui/fate-look.js)
        b.classList.toggle('fate-lit', picked === BY_ID[id].fate);
        b.classList.toggle('fate-stamp', BY_ID[id].step === 1 && st === 'off');
      }
    }
    for (const i of W.islands) {
      const st = islandState(ctx.state, i);
      const el = world.querySelector(`.isl[data-isl="${i.id}"]`);
      el.classList.toggle('picked', st === 'picked');
      el.classList.toggle('shut', st === 'shut');
      for (const l of world.querySelectorAll(`.lad[data-lad="${i.id}"]`)) l.classList.toggle('shut', st === 'shut');
      for (const t of world.querySelectorAll(`.roads [data-lane="${i.id}"]`)) {
        t.classList.toggle('shut', st === 'shut');
        t.classList.toggle('open', st === 'open');
      }
      mini.querySelector(`[data-mini="${i.id}"]`)?.classList.toggle('shut', st === 'shut');
    }
    for (const p of world.querySelectorAll('.plank')) {
      const pick = ctx.state.picks?.[p.dataset.fork];
      p.classList.toggle('picked', pick === p.dataset.plank);
      p.classList.toggle('shut', Boolean(pick) && pick !== p.dataset.plank);
      p.querySelector('.pinb').setAttribute('display', pick === p.dataset.plank ? 'inline' : 'none');
    }
    for (const [s, el] of Object.entries(chipEls)) el.textContent = String(view.counts[s] || 0);
    const here = W.nodes.find((n) => n.id === view.here);
    const pin = world.querySelector('.pin');
    if (here) { pin.style.left = here.x + 'px'; pin.style.top = here.y + 'px'; pin.style.display = ''; } else pin.style.display = 'none';
    const next = W.nodes.find((n) => n.id === view.next);
    const tag = world.querySelector('.next-tag');
    if (next && next.id !== view.here) { tag.style.left = next.x + 'px'; tag.style.top = next.y + 'px'; tag.style.display = ''; } else tag.style.display = 'none';
    sea.dirty();
    /* GHOST RUN HOOK (Renewal Sky): ui/unlocks/prestige/ghost.js draws its marker into `world`; nobody listens without a ghost star. */
    ctx.emit('guide-paint', { world, nodes: W.nodes });
  }

  /* ---- zoom, pan, minimap, depth ---- */
  function applyScale(keep) {
    const cx = scroll.scrollLeft + scroll.clientWidth / 2, cy = scroll.scrollTop + scroll.clientHeight / 2;
    const old = +sizer.dataset.scale || scale;
    sizer.style.width = W.W * scale + 'px';
    sizer.style.height = W.H * scale + 'px';
    world.style.transform = `scale(${scale})`;
    sizer.dataset.scale = scale;
    if (keep) scroll.scrollTo(cx * scale / old - scroll.clientWidth / 2, cy * scale / old - scroll.clientHeight / 2);
    onView();
  }
  const zoom = (d) => { scale = Math.min(1.25, Math.max(0.35, +(scale + d).toFixed(2))); applyScale(true); };
  zin.addEventListener('click', () => zoom(0.15));
  zout.addEventListener('click', () => zoom(-0.15));
  scroll.addEventListener('wheel', (e) => { if (!e.ctrlKey) return; e.preventDefault(); zoom(e.deltaY < 0 ? 0.1 : -0.1); }, { passive: false });
  let lastDepth = '';
  function onView() {
    const vp = mini.querySelector('.vp');
    const MW = mini.clientWidth || 92, k = MW / W.W / scale;
    if (vp) {
      vp.style.left = scroll.scrollLeft * k + 'px'; vp.style.top = scroll.scrollTop * k + 'px';
      vp.style.width = Math.min(MW, scroll.clientWidth * k) + 'px'; vp.style.height = scroll.clientHeight * k + 'px';
    }
    const name = depthLabel(W, (scroll.scrollTop + scroll.clientHeight / 2) / scale);
    if (name !== lastDepth) {
      const was = lastDepth;
      lastDepth = name;
      depthText.textContent = name;
      if (was && !still() && shown) play(depth, [{ transform: 'scale(1.12) rotate(-2deg)' }, { transform: 'none' }], { duration: 220, easing: 'ease-out' });
    }
    sea.dirty();
  }
  scroll.addEventListener('scroll', onView, { passive: true });
  mini.addEventListener('click', (e) => {
    const r = mini.getBoundingClientRect();
    const fx = (e.clientX - r.left) / r.width, fy = (e.clientY - r.top) / r.height;
    scroll.scrollTo({ left: fx * W.W * scale - scroll.clientWidth / 2, top: fy * W.H * scale - scroll.clientHeight / 2, behavior: still() ? 'auto' : 'smooth' });
  });
  function centreOn(x, y, instant) {
    scroll.scrollTo({ left: x * scale - scroll.clientWidth / 2, top: y * scale - scroll.clientHeight / 2, behavior: instant || still() ? 'auto' : 'smooth' });
  }
  function goHere(instant) {
    const n = W.nodes.find((m) => m.id === view?.here);
    if (n) centreOn(n.x, n.y, instant);
  }
  hereBtn.addEventListener('click', () => goHere());
  let drag = null;
  scroll.addEventListener('pointerdown', (e) => { if (e.pointerType !== 'mouse' || e.button !== 0) return; drag = { x: e.clientX, y: e.clientY, l: scroll.scrollLeft, t: scroll.scrollTop, moved: false }; });
  scroll.addEventListener('pointermove', (e) => {
    if (drag) {
      const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
      if (Math.abs(dx) + Math.abs(dy) > 4) { drag.moved = true; scroll.classList.add('drag'); tip.hidden = true; }
      if (drag.moved) { scroll.scrollLeft = drag.l - dx; scroll.scrollTop = drag.t - dy; }
      return;
    }
    if (e.pointerType !== 'mouse') return;
    const tk = e.target.closest?.('.tk');
    if (!tk) { tip.hidden = true; return; }
    showTip(tk, e.clientX, e.clientY);
  });
  const endDrag = () => {
    if (drag?.moved) { const kill = (ev) => { ev.stopPropagation(); ev.preventDefault(); }; scroll.addEventListener('click', kill, { capture: true, once: true }); win.setTimeout(() => scroll.removeEventListener('click', kill, { capture: true }), 0); }
    drag = null; scroll.classList.remove('drag');
  };
  scroll.addEventListener('pointerup', endDrag);
  scroll.addEventListener('pointercancel', endDrag);
  scroll.addEventListener('pointerleave', () => { endDrag(); tip.hidden = true; });
  function showTip(tk, cx, cy) {
    const id = tk.dataset.id, st = view?.states[id];
    const cost = BY_ID[id] ? E.price(ctx.state, id) : SHOP_BY_ID[id]?.cost;
    tip.innerHTML = `<b>${esc(nameOf(id))}</b><span>${esc(statusOf(id, st))}${st !== 'own' && cost ? ' ' + esc(ctx.fmt(cost)) + ' ' + esc(ctx.brand.currency) : ''}</span>`;
    tip.hidden = false;
    const r = root.getBoundingClientRect();
    let x = cx - r.left + 16, y = cy - r.top + 16;
    if (x + tip.offsetWidth > r.width - 8) x = cx - r.left - tip.offsetWidth - 16;
    if (y + tip.offsetHeight > r.height - 8) y = cy - r.top - tip.offsetHeight - 16;
    tip.style.left = x + 'px'; tip.style.top = y + 'px';
  }

  /* ---- filters and lanes ---- */
  bar.addEventListener('click', (e) => {
    const c = e.target.closest('.chip');
    if (c) {
      filter = filter === c.dataset.f ? null : c.dataset.f;
      for (const el of bar.querySelectorAll('.chip')) el.setAttribute('aria-pressed', String(el.dataset.f === filter));
      world.className = 'gr-world' + (filter ? ' f-' + filter : '');
      return;
    }
    const s = e.target.closest('.sw');
    if (s) selectIsland(s.dataset.sel, true);
  });
  function selectIsland(lane, glide) {
    laneSel = laneSel === lane ? null : lane;
    for (const el of world.querySelectorAll('.isl')) el.classList.toggle('sel', el.dataset.isl === laneSel);
    for (const el of world.querySelectorAll('.plank')) el.classList.toggle('sel', el.dataset.plank === laneSel);
    for (const el of world.querySelectorAll('.roads .rd, .roads .tw')) el.classList.toggle('sel', el.dataset.lane === laneSel);
    for (const b of bar.querySelectorAll('.sw')) b.setAttribute('aria-pressed', String(b.dataset.sel === laneSel));
    const i = isl[laneSel];
    if (i && glide) {
      const vx = scroll.scrollLeft / scale, vy = scroll.scrollTop / scale, vw = scroll.clientWidth / scale, vh = scroll.clientHeight / scale;
      const inView = i.x + i.w / 2 > vx && i.x + i.w / 2 < vx + vw && i.y > vy && i.y < vy + vh;
      if (!inView) scroll.scrollTo({ left: (i.x + i.w / 2) * scale - scroll.clientWidth / 2, top: i.y * scale - 90, behavior: still() ? 'auto' : 'smooth' });
    }
  }
  world.addEventListener('click', (e) => {
    const tk = e.target.closest('.tk');
    if (tk) { e.stopPropagation(); if (selected === tk.dataset.id && card) shutCard(); else select(tk.dataset.id); return; }
    const tt = e.target.closest('.tt');
    if (tt) { e.stopPropagation(); selectIsland(tt.dataset.sel, false); return; }
    const pl = e.target.closest('.plank');
    if (pl) { e.stopPropagation(); selectIsland(pl.dataset.plank, true); selectLane(pl.dataset.fork, pl.dataset.plank); return; }
    const bd = e.target.closest('[data-fork]');
    if (bd) { e.stopPropagation(); select(bd.dataset.fork); return; }
    if (card) shutCard();
  });

  /* ---- the card ---- */
  function dropDemo() { demoToken += 1; stopDemo?.(); stopDemo = null; }
  function startDemo(host, id) {
    dropDemo();
    /* Previews are not checked against the 3 Hz rule, so Gentle goes without them. */
    if (doc.body.classList.contains('gentle') || ctx.paused) { host.hidden = true; return; }
    const mine = demoToken;
    _loadDemos().then((Demo) => {
      if (mine !== demoToken || !card) return;
      if (!Demo.has(id)) { host.hidden = true; return; }
      stopDemo = Demo.play(host, id);
    }).catch((err) => console.error('demos failed', err));
  }
  function shutCard(animate = true) {
    dropDemo();
    const old = card;
    card = null;
    if (selected) tokens[selected]?.classList.remove('sel');
    selected = null;
    if (!old) return;
    if (!animate || still()) { old.remove(); return; }
    const a = play(old, phone() ? [{ transform: 'none' }, { transform: 'translateY(100%)' }] : [{ transform: 'none', opacity: 1 }, { transform: 'translateX(40px)', opacity: 0 }], { duration: 150, easing: 'ease-in' });
    if (a) a.finished.then(() => old.remove(), () => old.remove()); else old.remove();
  }
  function newCard() {
    const had = !!card;
    dropDemo();
    card?.remove();
    card = mk('div', 'gr-card', root);
    card.setAttribute('role', 'dialog');
    mk('span', 'grab', card);
    const x = mk('button', 'x', card);
    x.type = 'button';
    x.setAttribute('aria-label', 'Close this card');
    x.appendChild(cross());
    x.addEventListener('click', () => shutCard());
    if (had || still()) play(card, [{ opacity: 0 }, { opacity: 1 }], { duration: still() ? 120 : 90 });
    else play(card, phone() ? [{ transform: 'translateY(100%)' }, { transform: 'none' }] : [{ transform: 'translateX(40px)', opacity: 0 }, { transform: 'none', opacity: 1 }], { duration: 180, easing: 'ease-out' });
    return card;
  }
  function header(c, art, title, line) {
    const hd = mk('div', 'hd', c);
    const ic = mk('span', 'ic', hd);
    ic.appendChild(art);
    const t = mk('div', '', hd);
    mk('h3', '', t, title);
    if (line) mk('p', 'stl', t, line);
  }
  function chips(c, label, ids) {
    if (!ids.length) return;
    mk('h4', '', c, label);
    const list = mk('div', 'list', c);
    for (const id of ids) {
      const b = mk('button', `chip${view?.states[id] === 'off' ? ' shut' : ''}`, list);
      b.type = 'button';
      b.append(icon(id), nameOf(id));
      b.addEventListener('click', () => { select(id); const n = W.nodes.find((m) => m.id === id); if (n) centreOn(n.x, n.y); });
    }
  }
  const laneLine = (lane) => {
    const i = isl[lane];
    if (!i) return lane === 'trunk' ? 'The main road' : '';
    return i.kind === 'route' ? `${brandText(i.name)} route` : `${brandText(i.name)}, every run`;
  };
  const mark = (id) => { if (selected) tokens[selected]?.classList.remove('sel'); selected = id; tokens[id]?.classList.add('sel'); };
  function select(id) {
    if (SHOP_BY_ID[id]) { selectShop(id); return; }
    const it = BY_ID[id];
    if (!it) return;
    const c = newCard();
    mark(id);
    const st = view?.states[id] || 'later';
    const lane = LANE_BY_ID[it.lane];
    const where = Object.keys(FORKS).some((k) => FORKS[k].opts[it.lane]) ? `Only on the ${lane.name} route. ` : '';
    header(c, icon(id), it.name, `${where}${statusOf(id, st)}`);
    if (it.kind === 'fork') {
      mk('p', '', c, FORKS[id].ask);
      for (const [l, opt] of Object.entries(FORKS[id].opts)) {
        const pick = ctx.state.picks[id];
        const o = mk('div', `opt${pick && pick !== l ? ' shut' : ''}`, c);
        mk('b', '', o, opt.name);
        o.append(brandText(opt.what));
      }
    } else {
      if (it.what) mk('p', '', c, it.what);
      if (it.line) mk('p', 'line', c, it.line);
      startDemo(mk('div', 'demo', c), id);
    }
    chips(c, 'Needs', [...(it.req || []), ...(it.any || [])].filter((q) => BY_ID[q] && q !== 'start'));
    chips(c, 'Opens', Object.values(BY_ID).filter((r) => r.group === 'run' && (r.req?.includes(id) || r.any?.includes(id))).map((r) => r.id).slice(0, 8));
    const foot = mk('div', 'foot', c);
    const type = W.nodes.find((n) => n.id === id)?.type;
    mk('span', '', foot, [laneLine(it.lane), type && TYPES[type] && it.lane !== 'trunk' ? `${TYPES[type][0]} row` : ''].filter(Boolean).join(', '));
    mk('span', '', foot, st !== 'own' && it.cost ? ctx.fmt(E.price(ctx.state, id)) + ' ' + ctx.brand.currency : '');
    buyButton(c, id, st);
  }
  /* ---- buying from the map (owner 2026-11-09): only what the shop offers right now, through the shop's own press, so the
   * terms card, the guards, the price check and the purchase feel are the same ones a tile gets (ui/app.js press()). ---- */
  let buy = null;
  function buyButton(c, id, st) {
    buy = null;
    if ((st !== 'offer' && st !== 'skip') || !shopDoor?.offers(id)) return;
    const b = mk('button', 'buy', c);
    b.type = 'button';
    b.dataset.id = id;
    b.addEventListener('click', () => { if (!b.disabled && shopDoor?.offers(id)) shopDoor.press(id, b); });
    buy = b;
    paintBuy();
  }
  function paintBuy() {
    if (!buy?.isConnected) { buy = null; return; }
    const id = buy.dataset.id, p = shopDoor.price(id), f = shopDoor.funds(id), fork = BY_ID[id]?.kind === 'fork';
    const text = f < p ? `${whole(p - f)} short` : fork ? 'Choose' : 'Buy';
    if (buy.textContent !== text) buy.textContent = text;
    buy.disabled = f < p;
  }
  ctx.every(250, () => { if (shown && buy) paintBuy(); });
  function selectShop(id) {
    const g = SHOP_BY_ID[id];
    const c = newCard();
    mark(id);
    const st = view?.states[id] || 'later';
    const im = mk('img'); im.alt = ''; im.src = g.pic;
    header(c, im, g.name, STATUS[st]);
    mk('p', '', c, g.what);
    const pv = mk('img', 'pv', c); pv.alt = ''; pv.src = g.pic;
    const foot = mk('div', 'foot', c);
    mk('span', '', foot, `Shops, ${TYPES[g.type][0]}`);
    mk('span', '', foot, st !== 'own' && g.cost ? ctx.fmt(g.cost) + ' ' + ctx.brand.currency : '');
  }
  function selectLane(fid, lane) {
    const f = FORKS[fid], opt = f.opts[lane];
    const pick = ctx.state.picks[fid];
    const headNode = W.nodes.find((n) => n.lane === lane && n.head);
    const c = newCard();
    header(c, icon(headNode?.id || fid), opt.name, pick === lane ? 'Your route this run.' : pick ? STATUS.off : `One way at ${f.name}.`);
    mk('p', '', c, opt.what);
    if (headNode) startDemo(mk('div', 'demo', c), headNode.id);
    const rows = Object.values(BY_ID).filter((r) => r.lane === lane && r.group === 'run').sort((a, b) => a.cost - b.cost).map((r) => r.id);
    chips(c, 'On this route', rows.slice(0, 10));
  }

  /* ---- the sea ---- */
  const sea = createSea(doc, stage, W, () => ({ left: scroll.scrollLeft, top: scroll.scrollTop, scale, w: stage.clientWidth, h: stage.clientHeight }), {
    still,
    theme: () => THEME_PALETTES[ctx.state.picks?.f5] || null,
    words: () => [...(THEME_STRAY_WORDS[ctx.state.picks?.f5] || STRAY_WORDS)],
    shut: (i) => islandState(ctx.state, i) === 'shut',
    font: win.getComputedStyle(root).fontFamily || 'sans-serif',
  });
  const resize = () => { if (shown) { sizeMini(); onView(); } };
  win.addEventListener('resize', resize);
  ctx.cleanup?.(() => { win.removeEventListener('resize', resize); sea.destroy(); });

  /* ---- feel: what was bought since the sheet last closed stamps itself in ---- */
  function arrive() {
    const now = new Set(Object.keys(view.states).filter((id) => view.states[id] === 'own'));
    const fresh = seen ? [...now].filter((id) => !seen.has(id) && tokens[id]) : [];
    seen = now;
    let delay = 260;
    for (const id of fresh.slice(0, 12)) {
      const el = tokens[id];
      const at = delay;
      delay += 120;
      if (still()) { play(el, [{ opacity: 0 }, { opacity: 1 }], { duration: 120, delay: at, fill: 'backwards' }); continue; }
      play(el, [{ transform: 'scale(1.5) rotate(5deg)', opacity: 0 }, { transform: 'scale(.94) rotate(-1deg)', opacity: 1, offset: 0.7 }, { transform: 'none' }], { duration: 240, delay: at, easing: 'ease-out', fill: 'backwards' });
      const dent = mk('span', 'dent', el);
      const d = play(dent, [{ transform: 'scale(.4)', opacity: 0.8 }, { transform: 'scale(1.15)', opacity: 0 }], { duration: 420, delay: at + 160, easing: 'ease-out', fill: 'backwards' });
      if (d) d.finished.then(() => dent.remove(), () => dent.remove()); else dent.remove();
      ctx.after?.(at + 160, () => sfx(ctx, 'sfx/thud.mp3', 0.22));
    }
  }

  /* ---- living alongside the game ---- */
  const repaint = () => { if (!shown) return; applyStates(); arrive(); if (selected && card) select(selected); };
  ctx.on('buy', repaint);
  ctx.on('fork', repaint);
  ctx.on('pause', dropDemo);
  ctx.on('suspend', ({ on }) => { if (on) dropDemo(); });
  ctx.on('resume', () => { if (shown && selected && card && !stopDemo) { const h = card.querySelector('.demo'); if (h) { h.hidden = false; startDemo(h, selected); } } });

  function show() {
    if (shown) return;
    shown = true;
    outAnim?.cancel();
    back.hidden = false;
    if (!built) {
      built = true;
      build();
      scale = phone() ? 0.6 : stage.clientWidth >= 900 ? 0.85 : Math.max(0.6, Math.min(1, (stage.clientWidth - 20) / 1100));
    }
    applyScale(false);
    sizeMini();
    applyStates();
    /* The first time, start where the player is. After that, where they left it. */
    if (memo) { scale = memo.scale; applyScale(false); scroll.scrollLeft = memo.left; scroll.scrollTop = memo.top; }
    else goHere(true);
    onView();
    sea.start();
    if (still()) play(root, [{ opacity: 0 }, { opacity: 1 }], { duration: 160 });
    else {
      play(root, [{ transform: 'translateY(100%)' }, { transform: 'translateY(-6px)', offset: 0.8 }, { transform: 'none' }], { duration: 260, easing: 'ease-out' });
      play(back, [{ opacity: 0 }, { opacity: 1 }], { duration: 200 });
    }
    arrive();
    closeBtn.focus?.({ preventScroll: true });
  }
  function hide() {
    if (!shown) return;
    shown = false;
    memo = { left: scroll.scrollLeft, top: scroll.scrollTop, scale };
    shutCard(false);
    tip.hidden = true;
    sea.stop();
    seen = new Set(Object.keys(view?.states || {}).filter((id) => view.states[id] === 'own'));
    if (still()) { back.hidden = true; return; }
    outAnim = play(root, [{ transform: 'none' }, { transform: 'translateY(100%)' }], { duration: 200, easing: 'ease-in' });
    play(back, [{ opacity: 1 }, { opacity: 0 }], { duration: 200 });
    if (outAnim) outAnim.finished.then(() => { if (!shown) back.hidden = true; }, () => {}); else back.hidden = true;
  }
  back.hidden = true;
  back.addEventListener('click', (e) => { if (e.target === back) hide(); });
  return {
    show, hide, select,
    get shown() { return shown; }, get scroll() { return scroll; }, get root() { return root; }, get view() { return view; }, get sea() { return sea; },
    centre(id) { const n = W.nodes.find((m) => m.id === id); if (n) centreOn(n.x, n.y, true); },
    zoomTo(v) { scale = v; applyScale(true); },
  };
}
