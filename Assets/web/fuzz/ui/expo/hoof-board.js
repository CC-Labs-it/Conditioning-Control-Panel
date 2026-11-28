export const hoofPictures = [
  ['llama-chestnut','Llama'],['llama-gray','Llama'],['llama-black','Llama'],['llama-piebald','Llama'],
  ['alpaca-cream','Alpaca'],['alpaca-brown','Alpaca'],['sheep','Sheep'],['goat','Goat'],
  ['camel','Camel'],['traffic-light','Traffic light'],['bicycle','Bicycle'],['chair','Chair'],
  ['pear','Pear'],['watering-can','Watering can'],['postbox','Postbox'],['umbrella','Umbrella']
].map(([id,label])=>({id,label}));

function shuffled(items,random){
  const a=[...items];
  for(let i=a.length-1;i>0;i--){const j=Math.floor(random()*(i+1));[a[i],a[j]]=[a[j],a[i]];}
  return a;
}
export function dealHoofPhotos(last=[],random=Math.random){
  const picks=(a,b,n)=>shuffled(hoofPictures.slice(a,b),random).slice(0,n);
  const cards=shuffled([...picks(0,4,3),...picks(4,6,1),...picks(6,16,5)],random);
  if(last.every((id,i)=>cards[i]?.id===id)&&last.length===9)cards.push(cards.shift());
  return cards.map(card=>card.id);
}
export function answerMask(deal){
  return deal.reduce((mask,id,i)=>id.startsWith('llama-')?mask|1<<i:mask,0);
}

// Grid coordinates are measured in tile pitches, so the drawn hoof and its contact area agree at any resolution.
export const hoofRadius=1.2;
export function contactMask(x,y,worn){
  if(!worn)return x>=0&&x<3&&y>=0&&y<3?1<<(Math.floor(y)*3+Math.floor(x)):0;
  let mask=0;
  for(let i=0;i<9;i++)if((x-i%3-.5)**2+(y-Math.floor(i/3)-.5)**2<=hoofRadius**2+1e-9)mask|=1<<i;
  return mask;
}
export function markCount(mask){
  let n=0;while(mask){mask&=mask-1;n++;}return n;
}
export function markHoof(state,mask){
  state.hoofMask^=mask;
  if(mask&&!state.hoofWorn)state.hoofUsedFinger=true;
  return markCount(mask);
}
export const board={width:1280,height:704,left:88,top:76,pitch:180,verify:[715,537,486,99]};
export function boardHit(u,v,worn){
  const px=u*board.width,py=v*board.height,[left,top,width,height]=board.verify;
  if(px>=left&&px<=left+width&&py>=top&&py<=top+height)return {verify:true};
  const x=(px-board.left)/board.pitch,y=(py-board.top)/board.pitch;
  if(x<-.48||x>3.35||y<-.39||y>3.48)return null;
  return {x,y,mask:contactMask(x,y,worn)};
}
export function verifyHoof(state,random=Math.random){
  if(state.hoof!=='pictures')return null;
  if(state.hoofDeal?.length===9 && state.hoofMask===answerMask(state.hoofDeal)){
    state.hoof='complete';return {correct:true,edge:state.hoofWorn&&!state.hoofUsedFinger};
  }
  state.misses++;state.hoofDeal=dealHoofPhotos(state.hoofDeal,random);
  state.hoofMask=0;state.hoofUsedFinger=false;
  return {correct:false,cue:state.misses===1?'hoof_4':state.misses===2?'r5_hoof_circle':'r5_hoof_off'};
}
