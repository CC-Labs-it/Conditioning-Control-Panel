import {paintCharged} from './charged-lettering.js';
export const EDITOR_PRICE = 12000;
export const wordPalettes = {
  theme: {name:'Mod colors'}, candy:{name:'Candy',a:'#ff89c6',b:'#bca3ff'},
  lagoon:{name:'Lagoon',a:'#60e8e0',b:'#89bfff'}, solar:{name:'Solar',a:'#ffce65',b:'#ff9272'},
  orchid:{name:'Orchid',a:'#d19dff',b:'#fa80dc'}, frost:{name:'Frost',a:'#eefaff',b:'#a2d8ff'},
};
export const wordStyles = {
  bold:'Bold', outline:'Outline', flicker:'Neon flicker', bounce:'Glossy bounce',
  radiate:'Radiating', electric:'Electric',
};
export const wordScales = {small:.8, medium:1, large:1.25, jumbo:1.55};
export const defaultLook = Object.freeze({palette:'theme',style:'bold',size:'medium'});

export function readWordLook(value={}){
  return {
    palette:Object.hasOwn(wordPalettes,value?.palette)?value.palette:defaultLook.palette,
    style:Object.hasOwn(wordStyles,value?.style)?value.style:defaultLook.style,
    size:Object.hasOwn(wordScales,value?.size)?value.size:defaultLook.size,
  };
}

const roundedFace='ShopRound, "Arial Rounded MT Bold", sans-serif';
export function wordFace(g){
  return g.canvas?.ownerDocument?.documentElement?.hasAttribute('data-bubble-type')
    ? roundedFace : '"Segoe UI Black", "Arial Black", "Segoe UI", sans-serif';
}

// Paint the same glyph in the live trail and the shop swatch. The position and
// opacity belong to the trail so each style inherits the same coast and fade.
export function paintWord(g,{text,x,y,size,slot,time,alpha,colors,style,face=wordFace(g)}){
  const main=slot%2?colors.b:colors.a,other=slot%2?colors.a:colors.b;
  const phase=time*.001+slot*.74;
  g.save();
  g.font='900 '+size+'px '+face;
  g.textAlign='center';g.textBaseline='middle';g.lineJoin='round';
  const width=g.measureText(text).width;
  g.globalAlpha=alpha;g.strokeStyle=main;g.fillStyle=main;g.lineWidth=.55;
  if(style==='bold'){
    if(face===roundedFace){g.strokeStyle='#382b40';g.lineWidth=Math.max(1.2,size*.13);}
    g.strokeText(text,x,y);g.fillText(text,x,y);g.restore();return width;
  }
  g.translate(x,y);
  if(style==='electric'){
    paintCharged(g,{text,size,width,main,other,phase,alpha});
  }else if(style==='outline'){
    // An open glass face with a colored bevel and a narrow light running along the lip.
    g.lineWidth=Math.max(1.6,size*.087);g.strokeStyle=other;
    g.globalAlpha=alpha*.55;g.strokeText(text,0,size*.065);
    const glint=g.createLinearGradient(-width*.5,0,width*.5,0);
    const center=.15+.70*(.5+.5*Math.sin(phase*2));
    glint.addColorStop(0,main);glint.addColorStop(center-.1,main);
    glint.addColorStop(center,'#ffffff');glint.addColorStop(center+.1,other);glint.addColorStop(1,other);
    g.globalAlpha=alpha;g.strokeStyle=glint;g.lineWidth=Math.max(.95,size*.055);g.strokeText(text,0,0);
  }else if(style==='bounce'){
    // A wave of lacquered letters compresses on contact, then lifts along the word.
    const shine=g.createLinearGradient(0,-size*.38,0,size*.4);
    shine.addColorStop(0,'#ffffff');shine.addColorStop(.14,main);shine.addColorStop(.41,main);
    shine.addColorStop(.47,'#fcf9ff');shine.addColorStop(.55,main);shine.addColorStop(1,other);
    for(let i=0;i<text.length;i++){
      const letter=text[i];if(letter===' ')continue;
      const center=g.measureText(text.slice(0,i+1)).width-g.measureText(letter).width/2-width/2;
      const hop=Math.max(0,Math.sin(phase*4.1-i*.57));
      const impact=Math.max(0,Math.cos(phase*4.1-i*.57))**7;
      g.save();g.translate(center,-size*.26*hop);g.rotate(-.045*hop);
      g.scale(1+.075*impact,1+.055*hop-.075*impact);
      g.fillStyle=other;g.fillText(letter,0,size*.085);
      g.fillStyle=shine;g.fillText(letter,0,0);g.restore();
    }
  }else if(style==='flicker'){
    const dip=Math.max(0,Math.cos(phase*4.5))**8;
    g.lineWidth=size*.14;g.strokeStyle='#231632';g.strokeText(text,0,0);
    g.globalAlpha=alpha*(.78-.27*dip);g.shadowColor=main;g.shadowBlur=size*.28;
    g.strokeStyle=main;g.lineWidth=Math.max(1.8,size*.09);g.strokeText(text,0,0);
    g.shadowBlur=0;g.fillStyle=other;g.globalAlpha=alpha*.32;g.fillText(text,0,0);
    g.globalAlpha=alpha*(.93-.22*dip);g.strokeStyle='#fff5fc';g.lineWidth=Math.max(.65,size*.036);g.strokeText(text,0,0);
  }else if(style==='radiate'){
    // Two expanding impressions leave the original letterforms crisp.
    for(const offset of [0,.5]){
      const wave=(phase*.78+offset)%1;
      g.save();g.globalAlpha=alpha*.60*(1-wave)**1.4;g.scale(1+.32*wave,1+.93*wave);
      g.lineWidth=.75;g.strokeStyle=offset?other:main;g.strokeText(text,0,0);g.restore();
    }
    for(let i=0;i<8;i++){
      const a=i*Math.PI/4+.18,life=(phase*.78+i*.37)%1;
      const x=Math.cos(a)*(width*.5+size*.14+7*life),y=Math.sin(a)*size*(.45+.38*life);
      g.globalAlpha=alpha*.66*Math.sin(Math.PI*life);g.strokeStyle=i%2?main:other;g.lineWidth=.85;
      g.beginPath();g.moveTo(x,y);g.lineTo(x+Math.cos(a)*size*.17,y+Math.sin(a)*size*.17);g.stroke();
    }
    g.globalAlpha=alpha;g.fillStyle=main;g.fillText(text,0,0);
  }
  g.restore();
  return width;
}
