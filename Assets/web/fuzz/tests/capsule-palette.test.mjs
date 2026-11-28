import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {createCapsulePalette} from '../ui/unlocks/arcade/capsule-palette.js';

test('removing and inserting a capsule cannot recolor a surviving ball', () => {
  const {frames}=JSON.parse(readFileSync(new URL('../assets/arcade/dispenser/motion.json',import.meta.url)));
  const initial=createCapsulePalette([frames[0]]);
  const all=createCapsulePalette(frames);
  assert.equal(new Set(initial.values()).size,4);
  const moved=frames.at(-1).filter((p,i)=>initial.has(p.id)&&p.id!==frames[0][i].id);
  assert(moved.length>4);
  for(const p of moved)assert.equal(all.get(p.id),initial.get(p.id));
  const saved=frames.at(-1).map(p=>({...p,shade:all.get(p.id)})).reverse();
  const restored=createCapsulePalette(frames,saved);
  for(const p of saved)assert.equal(restored.get(p.id),p.shade);
  const old=frames.at(-1);
  const upgraded=createCapsulePalette(frames,old);
  for(const [i,p] of old.entries())assert.equal(upgraded.get(p.id),initial.get(frames[0][i].id));
});
