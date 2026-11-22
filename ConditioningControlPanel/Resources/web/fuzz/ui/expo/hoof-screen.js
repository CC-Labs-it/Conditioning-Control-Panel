import {board,hoofPictures,markCount} from './hoof-board.js';

const cards=new Map(hoofPictures.map(card=>[card.id,card]));
const paper='#fff0cd',mint='#b6dac1',green='#163e38';
const sans=' "Segoe UI",sans-serif';
const images=new Map();

export function loadHoofPhotos(assetURL,refresh){
  let mounted=true;
  for(const {id} of hoofPictures){
    const url=assetURL('hoof-photos/'+id+'.webp');
    let entry=images.get(url);
    if(!entry){
      const img=new Image();entry={img,ready:false,wait:[]};images.set(url,entry);
      img.onload=()=>{entry.ready=true;for(const done of entry.wait.splice(0))done();};
      img.onerror=()=>{images.delete(url);entry.wait.length=0;};
      img.crossOrigin='anonymous';img.src=url;
    }
    if(!entry.ready)entry.wait.push(()=>{if(mounted)refresh();});
  }
  return {image:id=>{const entry=images.get(assetURL('hoof-photos/'+id+'.webp'));return entry?.ready?entry.img:null;},
    dispose(){mounted=false;}};
}
function text(g,value,x,y,size,color=paper,family=sans,weight=600){
  g.fillStyle=color;g.font=weight+' '+size+'px'+family;g.fillText(value,x,y);
}
export function paintHoofScreen(panel,state,photos,message){
  panel?.draw((g,w,h)=>{
    g.fillStyle=green;g.fillRect(0,0,w,h);
    text(g,'APPLIED HOOF SCIENCES',40,48,26,mint);
    g.fillStyle='#7c9b7f';g.fillRect(40,59,w-80,2);
    if(state.hoof!=='pictures'){
      const complete=state.hoof==='complete',practice=state.hoof==='practice';
      text(g,complete?'Test complete':practice?'The practice button':'Fit a pair of hooves',65,207,79,paper,' Georgia',700);
      text(g,complete?'Hoofed stamp issued.':practice?'Press the large button on the desk.':'Pick up the hooves on the desk.',69,305,42);
      text(g,complete?'You may keep wearing the hooves or return them here.':
        practice?'The circle shows where your hoof will land.':'Your first job is one large button.',69,391,35,mint);
      if(practice){
        g.fillStyle='#bb9868';g.fillRect(75,489,164,111);g.strokeStyle=paper;g.lineWidth=6;
        g.beginPath();g.arc(132,530,62,0,Math.PI*2);g.stroke();
        text(g,'Anywhere on the button counts.',279,560,37);
      }
      return;
    }
    const {left,top,pitch}=board;
    state.hoofDeal.forEach((id,i)=>{
      const x=left+(i%3)*pitch,y=top+Math.floor(i/3)*pitch,selected=!!(state.hoofMask&1<<i);
      g.fillStyle=selected?'#ffd785':'#698a79';g.fillRect(x+1,y+1,pitch-2,pitch-2);
      const img=photos?.image(id);
      if(img)g.drawImage(img,x+6,y+6,pitch-12,pitch-12);
      g.fillStyle=selected?'#315741':'#203b30';g.fillRect(x+6,y+pitch-44,pitch-12,38);
      text(g,cards.get(id).label,x+13,y+pitch-15,25,paper);
      if(selected){
        g.fillStyle='#ffd785';g.fillRect(x+pitch-48,y+7,39,37);
        g.strokeStyle='#244838';g.lineWidth=5;g.beginPath();
        g.moveTo(x+pitch-41,y+24);g.lineTo(x+pitch-34,y+31);g.lineTo(x+pitch-18,y+15);g.stroke();
      }
    });
    const x=700;
    text(g,'Select all squares',x,149,50,paper,' Georgia',700);
    text(g,'with a llama.',x,211,50,paper,' Georgia',700);
    text(g,'Then press Verify.',x,273,46,paper,' Georgia',700);
    const recovery=state.misses>=3&&state.hoofWorn;
    if(message?.error)text(g,'Please try again.',x,348,39,'#ffdaac');
    else if(message?.contact!==undefined){
      const n=message.contact;
      text(g,(state.hoofWorn?'Hoof':'Finger')+' marked '+n+' square'+(n===1?'':'s')+'.',x,348,33,'#ffdaac');
    }
    text(g,markCount(state.hoofMask)+' of 9 selected',x,400,33,mint);
    if(recovery){
      text(g,'Return the hooves to the desk.',x,464,31);
      text(g,'One finger marks one square.',x,509,31);
    }else{
      text(g,state.hoofWorn?'Press again to undo the same marks.':'One finger marks one square.',x,480,28,mint);
    }
    const [bx,by,bw,bh]=board.verify;
    g.fillStyle='#eed39c';g.fillRect(bx,by,bw,bh);g.strokeStyle='#f9eabf';g.lineWidth=2;g.strokeRect(bx+7,by+7,bw-14,bh-14);
    g.textAlign='center';text(g,'Verify',bx+bw/2,by+67,49,'#23473c',' Georgia',700);
  });
}
