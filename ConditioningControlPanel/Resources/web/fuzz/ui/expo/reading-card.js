// A visitor can lift a printed record into reading range while its narration continues.
export function createReadingCard(root,camera) {
  const doc=root.ownerDocument;
  const card=doc.createElement('aside'), meta=doc.createElement('p'), title=doc.createElement('h2');
  const body=doc.createElement('p'), close=doc.createElement('button');
  card.className='expo-reading-card';card.setAttribute('aria-label','Enlarged archive plaque');
  meta.className='expo-reading-date';close.type='button';close.textContent='Close';
  card.append(meta,title,body,close);card.hidden=true;root.append(card);
  const start=camera.position.clone(), direction=camera.position.clone(), sight=camera.position.clone();
  let station=null;
  function hide(){station=null;card.hidden=true;}
  close.addEventListener('click',()=>{hide();root.focus({preventScroll:true});});
  // While the view follows the mouse the pointer never rests on the card, so the wheel is handed to it here.
  const wheel=e=>{if(!card.hidden&&card.scrollHeight>card.clientHeight+1)card.scrollTop+=e.deltaY;};
  root.addEventListener('wheel',wheel,{passive:true});
  return {
    label(id){return station===id?'Close plaque':'Enlarge plaque';},
    toggle(id,record){
      if(station===id){hide();return;}
      if(!record)return;
      station=id;start.copy(camera.position);camera.getWorldDirection(direction);
      meta.textContent=[record.date,record.section].filter(Boolean).join('   ·   ');
      title.textContent=record.title;body.textContent=record.body;
      // A long record (the Rail talk) gets a wide card, so it is read whole without scrolling.
      card.toggleAttribute('data-long',isLongRecord(record));card.scrollTop=0;card.hidden=false;
    },
    update(){
      if(station&&(camera.position.distanceToSquared(start)>1.44 || camera.getWorldDirection(sight).dot(direction)<.68))hide();
      return station!==null;
    },
    release(id){if(station===id)hide();},
    dispose(){root.removeEventListener('wheel',wheel);card.remove();}
  };
}
/** Longer than the usual four-sentence record: the narrow card would need scrolling. */
export const isLongRecord=record=>(record?.body?.length||0)>560;
