import * as THREE from 'three';
import {livePanel} from './display.js';

function panel(group,name) {
  const mount=group.getObjectByName(name);
  let face;
  mount?.traverse(node=>{if(node.isMesh && !face)face=node;});
  if(!face)return;
  const {expoWidth,expoHeight}=mount.userData;
  const width=1024, height=Math.round(width*expoHeight/expoWidth);
  return livePanel(THREE,face,{width,height,emission:.7});
}

function wrap(ctx, value, width) {
  const lines = [];
  let line = '';
  for (const word of value.split(/\s+/)) {
    const next = line ? line+' '+word : word;
    if (line && ctx.measureText(next).width>width) {lines.push(line);line=word;}
    else line=next;
  }
  if(line)lines.push(line);
  return lines;
}

const chapterInk = ['#b7cdab','#a6cfd4','#c9c6a6','#bcd29a','#b1d3c9','#d4bc96'];
const sans = '"Segoe UI", Arial, sans-serif';
const display = 'Georgia, "Times New Roman", serif';

function writeLines(ctx,text,x,top,width,space,maxSize,face,weight,leading) {
  let fontSize=maxSize, lines;
  do {
    ctx.font=weight+' '+fontSize+'px '+face;
    lines=wrap(ctx,text,width);
    if((lines.length-1)*fontSize*leading+fontSize<=space || fontSize<=14)break;
    fontSize--;
  } while(true);
  lines.forEach((line,i)=>ctx.fillText(line,x,top+i*fontSize*leading));
}

function replayFinish(cap) {
  const faces=[],clones=new Map();
  function neutral(material) {
    if(material.name!=='action_coral')return material;
    if(!clones.has(material)){
      const grey=material.clone();
      grey.name='Replay awaiting first record';
      grey.map=null;grey.color.setHex(0x939e96);
      clones.set(material,grey);
    }
    return clones.get(material);
  }
  cap?.traverse(face=>{
    if(!face.isMesh)return;
    const original=face.material;
    if(![original].flat().some(m=>m.name==='action_coral'))return;
    const muted=Array.isArray(original)?original.map(neutral):neutral(original);
    faces.push({face,original,muted});
  });
  let available;
  function set(value) {
    if(value===available)return;
    available=value;for(const {face,original,muted} of faces)face.material=value?original:muted;
  }
  return {set,dispose(){set(true);for(const copy of clones.values())copy.dispose();}};
}

export function archiveDisplays(group,id,hasHistory=false) {
  const title=panel(group,'expo_label_'+id);
  const caption=panel(group,'expo_caption_'+id);
  const replay=replayFinish(group.getObjectByName('expo_review_'+id));
  replay.set(hasHistory);
  let painted;
  function base(g,w,h,surface) {
    g.fillStyle=surface;g.fillRect(0,0,w,h);
    g.textBaseline='top';g.textAlign='left';
  }
  function show(row,total,hasPrevious,secondViewing=false) {
    replay.set(hasPrevious);
    if(!row){
      if(painted!==null){
        painted=null;
        title?.draw((g,w,h)=>base(g,w,h,'#123039'));
        caption?.draw((g,w,h)=>base(g,w,h,'#f1ecd9'));
      }
      return;
    }
    const key=row.id+'-'+secondViewing;
    if(painted===key)return;
    painted=key;
    const a=row.archive;
    const accent=chapterInk[a.chapter-1] || chapterInk[0];
    title?.draw((g,w,h)=>{
      base(g,w,h,'#123039');
      g.fillStyle=accent;g.fillRect(0,0,9,h);
      g.font='600 26px '+sans;
      g.fillText('ARCHIVE  '+String(a.number).padStart(2,'0')+' / '+total+'     ·     '+a.date,32,10,w-64);
      g.fillStyle='#fef5dc';writeLines(g,a.title,30,43,w-57,h-50,78,display,'bold',1.01);
    });
    caption?.draw((g,w,h)=>{
      base(g,w,h,'#f1ecd9');
      g.fillStyle='#264b4a';g.font='bold 25px '+sans;
      const heading=secondViewing?'Second viewing. Several things have been improved.':(a.chapter?'CHAPTER '+a.chapter+'     ·     ':'')+a.section;
      g.fillText(heading,27,8,w-170);
      for(let i=0;i<chapterInk.length;i++){
        g.fillStyle=i<a.chapter?'#315d55':'#b4bfaf';g.fillRect(w-141+i*20,17,15,6);
      }
      g.fillStyle='#8e9f91';g.fillRect(28,40,w-56,2);
      g.fillStyle='#192f31';writeLines(g,a.body,27,49,w-54,h-57,39,sans,'500',1.17);
    });
  }
  show(null,0,hasHistory);
  return {show,dispose(){replay.dispose();title?.dispose();caption?.dispose();}};
}
