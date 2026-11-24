import {defaultLook,wordPalettes,wordScales,paintWord,wordFace} from './word-looks.js';
import {createWordRibbon} from './word-ribbon.js';

const words=['RELAX','SINK','DEEPER','LET GO','DROP','BLANK'];
const wordSizes=[15,12,14,13,16,12];

export function createFollowingWords(){
  const ribbon=createWordRibbon(words.length);
  let layoutKey='';
  function draw(g,now,palette,look=defaultLook){
    if(!ribbon.alive(now))return;
    const colors=wordPalettes[look.palette]?.a?wordPalettes[look.palette]:palette;
    const scale=wordScales[look.size]||1;
    const first=(ribbon.stroke+1)%words.length;
    // Widths and identities are fixed throughout a stroke, including its coast.
    const face=wordFace(g);
    const key=first+':'+scale+':'+face;
    if(key!==layoutKey){
      g.save();
      const widths=ribbon.links.map((_,slot)=>{
        const index=(first+slot)%words.length;
        g.font='900 '+(wordSizes[index]*scale)+'px '+face;
        return g.measureText(words[index]).width;
      });
      g.restore();ribbon.layout(widths);layoutKey=key;
    }
    ribbon.advance(now);
    for(let slot=0;slot<ribbon.links.length;slot++){
      const link=ribbon.links[slot];if(link.opacity<=.006)continue;
      const index=(first+slot)%words.length;
      paintWord(g,{text:words[index],x:link.x,y:link.y,size:wordSizes[index]*scale,slot,
        time:now,alpha:link.opacity,colors,style:look.style,face});
    }
  }
  return {move:ribbon.move,draw,clear:ribbon.clear,leave:ribbon.leave,alive:ribbon.alive};
}
