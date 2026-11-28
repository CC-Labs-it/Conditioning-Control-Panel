import {createExpoTaps} from './touch-taps.js';

const glyphs={
  play:'M8 5v14l11-7z',
  pause:'M6 5h4v14H6z M14 5h4v14h-4z',
  skip:'M4 5v14l11-7z M16 5h3v14h-3z',
  volume:'M3 9h4l5-4v14l-5-4H3z M14.5 8l1.2-1.3c3.3 2.9 3.3 7.7 0 10.6L14.5 16c2.4-2.3 2.4-5.7 0-8z',
  muted:'M3 9h4l5-4v14l-5-4H3z M14 9l1.4-1.4 2.6 2.6 2.6-2.6L22 9l-2.6 2.6 2.6 2.6-1.4 1.4-2.6-2.6-2.6 2.6-1.4-1.4 2.6-2.6z'
};
function icon(doc,name){
  const svg=doc.createElementNS('http://www.w3.org/2000/svg','svg');
  svg.setAttribute('viewBox','0 0 24 24');svg.setAttribute('width','19');svg.setAttribute('height','19');svg.setAttribute('aria-hidden','true');
  const path=doc.createElementNS(svg.namespaceURI,'path');path.setAttribute('fill','currentColor');path.setAttribute('d',glyphs[name]);svg.append(path);
  return svg;
}

// Use the host mixer and master mute so volume settings and changes stay in sync across endings.
export function mountExpoRadio(doc,music,arm){
  const host=doc.getElementById('game-controls'),audio=doc.getElementById('audio-controls');
  const toggle=doc.getElementById('audio-toggle'),mute=doc.getElementById('audio-mute'),mixer=doc.getElementById('audio-mixer');
  if(!host||!audio||!toggle||!mute||!mixer)return ()=>{};
  const group=doc.createElement('div');
  group.className='expo-radio';group.dataset.museumTransport='';group.setAttribute('role','group');group.setAttribute('aria-label','Expo lo-fi music');
  const button=(name,label)=>{
    const el=doc.createElement('button');el.type='button';el.className='expo-radio-'+name;el.setAttribute('aria-label',label);el.title=label;
    el.append(icon(doc,name));group.append(el);return el;
  };
  const play=button('play','Play lo-fi music'),skip=button('skip','Next lo-fi track');
  audio.insertBefore(group,mute);
  const details=doc.createElement('p');details.className='expo-track-name';
  mixer.querySelector('h2')?.after(details);
  const speaker=icon(doc,'volume');speaker.classList.add('expo-volume-icon');toggle.append(speaker);
  const media=doc.defaultView.matchMedia('(pointer:coarse), (max-width:660px)');
  const syncSpeaker=()=>speaker.firstChild.setAttribute('d',glyphs[mute.getAttribute('aria-pressed')==='true'?'muted':'volume']);
  syncSpeaker();
  const changes=new doc.defaultView.MutationObserver(syncSpeaker);
  changes.observe(mute,{attributes:true,attributeFilter:['aria-pressed']});
  function layout(){
    host.toggleAttribute('data-expo-compact',media.matches);
    if(media.matches)mixer.insertBefore(mute,mixer.querySelector('.mix-master'));
    else audio.insertBefore(mute,toggle);
  }
  media.addEventListener('change',layout);layout();
  let displayed=false,pressIntent=null;
  const off=music.subscribe(({title,available,playing})=>{
    displayed=playing;play.disabled=skip.disabled=!available;
    play.firstChild.firstChild.setAttribute('d',glyphs[playing?'pause':'play']);
    const label=playing?'Pause lo-fi music':'Play lo-fi music';
    play.setAttribute('aria-label',label);play.title=title?label+' · '+title:label;
    skip.title=title?'Next lo-fi track · '+title:'Next lo-fi track';
    details.hidden=!title;details.textContent=title?'Lo-fi · '+title:'';
  });
  const taps=createExpoTaps({afterPointer:()=>{
    const target=doc.querySelector('.expo-trail-card:not([hidden])')||doc.querySelector('.expo-root');
    target?.focus({preventScroll:true});
  }});
  play.addEventListener('pointerdown',()=>{pressIntent=displayed;});
  taps.bind(play,event=>{
    if(play.disabled)return;
    const pointer=event.type==='pointerup'||event.detail>0;
    const pause=pointer&&pressIntent!==null?pressIntent:displayed;pressIntent=null;
    if(!pause&&event.isTrusted)arm?.(event);
    music.setUserPaused(pause);
  });
  taps.bind(skip,event=>{if(!skip.disabled){if(event.isTrusted)arm?.(event);music.skip();}});
  return ()=>{
    off();taps.cancel();changes.disconnect();media.removeEventListener('change',layout);host.removeAttribute('data-expo-compact');
    audio.insertBefore(mute,toggle);speaker.remove();group.remove();details.remove();
  };
}

export const RADIO_STYLE=`
.expo-radio{display:flex;align-items:stretch}
.expo-radio button{display:grid;place-items:center;width:36px;height:36px;padding:0;background:#d4d0c8;color:#171717;border:2px outset white;touch-action:manipulation;cursor:pointer}
.expo-radio button:disabled{opacity:.57}.expo-radio button:focus-visible{outline:2px solid #cd6640;outline-offset:-3px}
#audio-toggle .expo-volume-icon{display:none}
#audio-mixer .expo-track-name{margin:5px 0 0;font:500 13px/1.3 system-ui}
#game-controls[data-expo-compact]{gap:5px}
#game-controls[data-expo-compact] #audio-controls{align-items:stretch;background:#173b34ed;color:#fbf4df;border-radius:8px;box-shadow:0 1px 4px #12251f45}
#game-controls[data-expo-compact] :is(.expo-radio button,#audio-toggle,#wake){box-sizing:border-box;min-width:44px;height:44px;min-height:44px;background:transparent;color:#fff8e9;border:0;font:650 13px/1.1 system-ui;touch-action:manipulation}
#game-controls[data-expo-compact] :is(.expo-radio button,#audio-toggle):active,#game-controls[data-expo-compact] #audio-toggle[aria-expanded=true]{background:#3a5f51}
#game-controls[data-expo-compact] #audio-toggle{border-left:1px solid #edf5dd3b;border-radius:0 8px 8px 0}
#game-controls[data-expo-compact] #audio-toggle svg{display:none}
#game-controls[data-expo-compact] #audio-toggle .expo-volume-icon{display:block;transform:none}
#game-controls[data-expo-compact] #wake{padding:0 10px;border-radius:8px;background:#f3eddc;color:#1c3930;box-shadow:0 1px 4px #13291c4d}
#game-controls[data-expo-compact] #audio-mixer #audio-mute{position:absolute;right:8px;top:3px;display:grid;place-items:center;width:44px;height:44px;background:transparent;color:#1d3932;border:0}
#game-controls[data-expo-compact] #audio-mixer{background:#f5f0df;color:#25382e}
body:has(#pause[hidden]) #game-controls[data-expo-compact] > :not(#audio-controls,#wake){display:none}
body:has(#pause:not([hidden])) .expo-radio{display:none}
@media(max-width:350px){#game-controls[data-expo-compact]{gap:3px}#game-controls[data-expo-compact] #wake{padding:0 6px;font-size:12px}}
`;
