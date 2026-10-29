import test from 'node:test';
import assert from 'node:assert/strict';
import {mediaRecap,createCourseMedia} from '../hud/courseMedia.js';

test('course recap names the chosen niches, local files and music without sharing another course default',()=>{
  assert.deepEqual(mediaRecap({source:'scrolller',catalog:[{id:'beta',label:'Beta'}],songName:'My song'},'keep'),{pictures:'Scrolller: Beta + censored',song:'My song'});
  assert.equal(mediaRecap({source:'files',files:4},'threshold').pictures,'My files (4)');
  assert.equal(mediaRecap({source:'files'},'threshold').pictures,'My files - choose files');
  assert.equal(mediaRecap({source:'off'},'threshold').pictures,'Pictures off');
  assert.equal(mediaRecap({source:'scrolller'},'boudoir').pictures,'Scrolller: sissy + bimbo');
  assert.equal(mediaRecap({songName:'<song>'},'threshold').song,'<song>');
});

function element(tag='') {
  const selectors=new Map();
  return {tag,dataset:{},children:[],attrs:{},open:false,style:{},
    append(...nodes){this.children.push(...nodes);},setAttribute(k,v){this.attrs[k]=v;},
    querySelector(s){if(!selectors.has(s))selectors.set(s,element(s));return selectors.get(s);},
    querySelectorAll(){return [];},contains(){return false;},addEventListener(){},
    showModal(){this.open=true;},close(){this.open=false;},remove(){},focus(){this.focused=true;},
  };
}

test('customization targets the requested course and emits independent source, niche, default and song choices',t=>{
  const previousDocument=globalThis.document,previousEvent=globalThis.CustomEvent;
  globalThis.document={createElement:element,body:element()};
  globalThis.CustomEvent=class{constructor(type,options){this.type=type;Object.assign(this,options);}};
  t.after(()=>{globalThis.document=previousDocument;globalThis.CustomEvent=previousEvent;});
  const root=element(),events=[];root.dispatchEvent=e=>events.push(e.detail);
  const picker=createCourseMedia({root,mediaFor:()=>({source:'scrolller',niches:['sissy'],scrolllerAvailable:false,catalog:[{id:'sissy',label:'Sissy'},{id:'bimbo',label:'Bimbo'}],songs:[{id:'boudoir',label:'Think Pink'}]})});
  picker.show({id:'boudoir',name:'Boudoir'});
  const dialog=root.children[0];assert.equal(picker.open,true);
  const sources=dialog.querySelector('.g-media-sources').children;
  assert.equal(sources[0].disabled,true);sources[1].onclick();
  const niches=dialog.querySelector('.g-media-chips').children;niches[1].onclick();niches[2].onclick();
  const songs=dialog.querySelector('.g-media-songs').children;songs[0].onclick();songs[1].onclick();
  assert.deepEqual(events,[
    {trackId:'boudoir',kind:'source',source:'files'},
    {trackId:'boudoir',kind:'niches',niches:['sissy','bimbo']},
    {trackId:'boudoir',kind:'niches',niches:null},
    {trackId:'boudoir',kind:'song',songId:'default'},
    {trackId:'boudoir',kind:'song',songId:'boudoir'},
  ]);
  picker.nav('back');assert.equal(picker.open,false);picker.dispose();
});
