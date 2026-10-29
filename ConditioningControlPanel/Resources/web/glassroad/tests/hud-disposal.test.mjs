import test from 'node:test';
import assert from 'node:assert/strict';
import {registerHooks} from 'node:module';
import {createBus} from '../core/events.js';
const moduleUrl=source=>'data:text/javascript;base64,'+Buffer.from(source).toString('base64');
const mocks={
  './cards.js':moduleUrl(`export function createCards(card,{emit}){globalThis.__lateHudEmit=emit;return{dispose(){emit('online-left');},menuAction:emit};}`),
  './minimap.js':moduleUrl('export const createMinimap=()=>({});'),
  './kit.js':moduleUrl('export const createKit=()=>({dispose(){}});export const placeArt=()=>null;'),
};
const hooks=registerHooks({resolve(specifier,context,next){
  if(context.parentURL?.endsWith('/hud/hud.js')&&mocks[specifier])return{url:mocks[specifier],shortCircuit:true};
  return next(specifier,context);
}});
const {createHud}=await import('../hud/hud.js');hooks.deregister();
function element(){const nodes=new Map();return{dataset:{},classList:{add(){},remove(){}},addEventListener(){},
  querySelector(key){if(!nodes.has(key))nodes.set(key,element());return nodes.get(key);},
  querySelectorAll(){return[];},
};}
test('HUD disposal blocks queued card/lobby emissions and clears both listener APIs',()=>{
  const hud=createHud({root:element(),card:element()},{get:()=>false},createBus(),
    {doc:{createElement:element},devices:{on:()=>()=>{}}});
  let calls=0;hud.on('race',()=>calls++);hud.on('online-left',()=>calls++);hud.onStart(()=>calls++);
  const late=globalThis.__lateHudEmit;late('race');assert.equal(calls,1);
  hud.dispose();hud.dispose();late('online-left');late('start');late('race');
  hud.on('race',()=>calls++);hud.onStart(()=>calls++);late('race');late('start');
  assert.equal(calls,1);delete globalThis.__lateHudEmit;
});
