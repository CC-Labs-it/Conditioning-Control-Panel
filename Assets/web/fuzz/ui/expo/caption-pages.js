// Short pages keep the spoken line readable above the phone controls.
export function captionPages(text,limit=76){
  const pages=[];
  let words=[],length=0;
  function finish(){
    if(words.length)pages.push(words.join(' '));
    words=[];length=0;
  }
  for(const word of text.trim().split(/\s+/)){
    if(!word)continue;
    if(length+word.length+1>limit)finish();
    words.push(word);length+=word.length+(words.length>1?1:0);
    if(/[.!?][”"')]*$/.test(word)||(length>=42&&/[,;:]$/.test(word)))finish();
  }
  finish();
  let covered=0;
  return pages.map(value=>({value,end:(covered+=value.length+1)}));
}
export function captionAt(pages,progress){
  if(!pages.length)return '';
  const offset=Math.min(1,Math.max(0,progress))*pages.at(-1).end;
  return (pages.find(page=>offset<page.end)||pages.at(-1)).value;
}
