import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync,statSync} from 'node:fs';
import {fileURLToPath} from 'node:url';
import {COURSES} from '../track/catalog.js';
import {courseShot,createTrackPicker} from '../hud/trackPicker.js';

// A small fake DOM: every selector answers one stable child, attributes are kept for the asserts.
function element(tag=''){
  const selectors=new Map(),attrs={};
  return {tag,dataset:{},style:{setProperty(){}},children:[],attrs,
    setAttribute(k,v){attrs[k]=String(v);},getAttribute(k){return attrs[k];},append(...c){this.children.push(...c);},remove(){},focus(){},
    addEventListener(){},querySelectorAll(){return [];},
    querySelector(s){if(!selectors.has(s))selectors.set(s,element(s));return selectors.get(s);}};
}

test('every course has a chooser shot and a phone shot on disk, both small WebP files',()=>{
  for(const course of COURSES){
    const shot=courseShot(course.id),files=[shot.src,...shot.srcset.split(', ').map(x=>x.split(' ')[0])];
    for(const url of files){
      const bytes=readFileSync(fileURLToPath(url));
      assert.equal(bytes.subarray(8,12).toString(),'WEBP',url);
      assert.ok(bytes.length<260_000,url+' is '+bytes.length+' bytes');
    }
    assert.match(shot.srcset,/-phone\.webp 480w, .+\.webp 960w$/);
  }
  assert.equal(courseShot('nowhere').src,courseShot('threshold').src);
});

test('each tile leads with the shot and keeps the route map as a popup',()=>{
  const old=globalThis.document;globalThis.document={createElement:element};
  try{
    const root=element('root'),picker=createTrackPicker({root,courses:COURSES,trackId:'keep',change(){}});
    const dialog=root.children[0],tiles=dialog.querySelector('.g-track-grid').children.map(wrap=>wrap.children[0]);
    assert.equal(tiles.length,COURSES.length);
    for(const [i,tile] of tiles.entries()){
      const html=tile.innerHTML;
      assert.ok(html.indexOf('g-course-shot')<html.indexOf('g-course-map'),'shot first');
      assert.match(html,/<span class="g-course-map" aria-hidden="true"><svg viewBox="0 0 180 88">/);
      const img=tile.querySelector('.g-course-shot');
      assert.equal(img.attrs.src,courseShot(COURSES[i].id).src);
      assert.equal(img.attrs.srcset,courseShot(COURSES[i].id).srcset);
    }
    picker.dispose();
  }finally{globalThis.document=old;}
});

test('the map popup shows on hover, keyboard focus and the current pick, and only fades when calm',()=>{
  const css=readFileSync(new URL('../hud/garage.css',import.meta.url),'utf8');
  for(const state of [':hover',':focus-visible','[aria-pressed=true]'])
    assert.ok(css.includes('#race-garage .g-track-tile'+state+' .g-course-map'),state);
  assert.match(css,/#race-garage\.g-calm \.g-course-map\{transform:none;transition:opacity/);
  assert.match(css,/prefers-reduced-motion:reduce\)\{\.g-course-map\{transform:none;transition:opacity/);
});

test('the title card shows the logo picture with its name as alt text',()=>{
  const src=readFileSync(new URL('../hud/garage.js',import.meta.url),'utf8');
  assert.match(src,/<img src="\$\{LOGO\}" alt="Racing Thoughts"/);
  const logo=new URL('../art/ui/logo.webp',import.meta.url);
  assert.equal(readFileSync(logo).subarray(8,12).toString(),'WEBP');
  assert.ok(statSync(logo).size<300_000);
});
