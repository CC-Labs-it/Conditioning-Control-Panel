"use strict";

globalThis.FuzzFM = (() => {
  const skinIds = ["trance","bimbo","drone","sissy","goon","chastity","censored","infection"];
  const homeSets = {goon:"infection", censored:"trance"};
  const inks = {trance:"#625581",bimbo:"#aa5078",drone:"#267a80",sissy:"#89669d",
    goon:"#ac643c",chastity:"#864963",censored:"#a74a44",infection:"#68823b"};
  const imageCache = new Map();
  function skinFor(value) {
    const id = String(value || "").trim().toLowerCase();
    if (id==="locked") return "chastity";
    return skinIds.includes(id) ? id : null;
  }
  function getImage(url) {
    if (!imageCache.has(url)) imageCache.set(url,new Promise((resolve,reject) => {
      const image = new Image();
      image.onload = () => resolve(image);
      image.onerror = () => {imageCache.delete(url);reject(new Error(url));};
      image.src = url;
    }));
    return imageCache.get(url);
  }
  function mount(host, options) {
    const doc=host.ownerDocument;
    const skin=skinFor(options.niche) || "trance";
    const home=homeSets[skin] || skin;
    const tracks=globalThis.FUZZ_TRACKS;
    const queue=[...tracks.filter(t=>t.station===home), ...tracks.filter(t=>t.station!==home)];
    const meta=globalThis.FUZZ_ART[skin];
    const prefix=options.base || "";
    const root=doc.createElement("div");
    root.className="fm-object";
    root.dataset.skin=skin;
    root.style.setProperty("--fm-ink",inks[skin]);
    root.innerHTML=`<canvas class="fm-art" width="1060" height="795" data-dock-surface role="img"></canvas>
      <div class="fm-pill" role="group" aria-label="Lo-fi radio controls" data-dock-interactive>
        <div class="fm-display" aria-hidden="true"><div class="fm-tape"></div></div>
        <div class="fm-controls">
        <button type="button" class="fm-prev" aria-label="Previous track" title="Previous track"><i></i></button>
        <button type="button" class="fm-toggle" aria-label="Play" title="Play"><i></i></button>
        <button type="button" class="fm-next" aria-label="Next track" title="Next track"><i></i></button>
        <canvas class="fm-wave" width="144" height="48" aria-hidden="true"></canvas>
        </div>
      </div><span class="fm-sr" role="status"></span>`;
    host.append(root);
    const art=root.querySelector(".fm-art");
    const stage=art.getContext("2d");
    const wave=root.querySelector(".fm-wave"), meter=wave.getContext("2d");
    const previous=root.querySelector(".fm-prev"), toggle=root.querySelector(".fm-toggle");
    const next=root.querySelector(".fm-next"), pill=root.querySelector(".fm-pill"), info=root.querySelector(".fm-sr");
    const tape=root.querySelector(".fm-tape");
    const name=skin[0].toUpperCase()+skin.slice(1);
    art.setAttribute("aria-label",name+" lo-fi desk and companion");
    let current=0, stream=null, intent=false, dead=false, failed=false;
    let poses=null, pose="", variant=0, cue=0, remaining=0, lastTick=null, waveAt=-1;
    let envelope="", displayedTime=-1, scrollPeriod=12;
    function setReadout(track) {
      const entry=doc.createElement("span");
      entry.className="fm-entry";
      const title=doc.createElement("strong");
      title.textContent=track.title;
      const niche=doc.createElement("span");
      niche.className="fm-niche";
      niche.textContent=" · "+track.station[0].toUpperCase()+track.station.slice(1);
      entry.append(title,niche);
      tape.replaceChildren(entry,entry.cloneNode(true));
      tape.style.transform="";
      scrollPeriod=Math.max(10,entry.textContent.length*.42);
    }
    function paintPose(state) {
      if (!poses || state===pose) return;
      pose=state;root.dataset.pose=state;
      stage.clearRect(0,0,art.width,art.height);
      stage.drawImage(poses.rest,-70,-60);
      if (state==="rest") return;
      const [x,y,w,h]=meta.poses[state];
      stage.clearRect(x-70,y-60,w,h);
      stage.drawImage(poses[state],x-70,y-60);
    }
    Promise.all(["rest",...Object.keys(meta.poses)].map(async state=>
      [state,await getImage(prefix+"art/"+skin+"/"+state+".webp")])).then(entries=>{
      if (!dead) {poses=Object.fromEntries(entries);paintPose("rest");}
    }).catch(()=>{if(!dead) info.textContent="Radio artwork unavailable";});

    function paintWave(seconds,reduced) {
      meter.clearRect(0,0,wave.width,wave.height);
      const count=27;
      const samples=envelope.length;
      const center=reduced ? 0 : seconds*globalThis.FUZZ_WAVES.rate;
      for(let i=0;i<count;i++){
        const sample=Math.min(samples-1,Math.max(0,center+i-count/3));
        const lo=Math.floor(sample), frac=sample-lo;
        const value=samples ? ((1-frac)*envelope.charCodeAt(lo)+frac*envelope.charCodeAt(Math.min(samples-1,lo+1)))/255 : 0;
        const height=3+value*(wave.height-7);
        meter.fillStyle=inks[skin];
        meter.fillRect(i*wave.width/count,(wave.height-height)/2,3.2,height);
      }
    }
    function transport() {
      const title=queue[current]?.title || "No tracks";
      const action=intent ? "Pause" : "Play";
      toggle.classList.toggle("fm-playing",intent);
      toggle.setAttribute("aria-label",action);
      toggle.title=action+" · "+title;
      pill.title=failed ? title+" could not play. Use previous or next." : (tape.firstChild?.textContent || title)+" · "+(current+1)+"/"+queue.length;
      info.textContent=pill.title;
      root.dataset.track=queue[current]?.id || "";
    }
    function select(index,play) {
      if(dead || !queue.length) return;
      const old=stream;
      stream=null;
      old?.stop();
      current=(index+queue.length)%queue.length;
      const track=queue[current];
      intent=play;failed=false;
      setReadout(track);
      envelope=atob(globalThis.FUZZ_WAVES.tracks[track.id] || "");
      displayedTime=-1;paintWave(0,false);
      const handle=options.openStream(prefix+track.src);
      stream=handle;
      function reportError() {
        if(stream!==handle || dead) return;
        failed=true;intent=false;transport();
      }
      if(handle?.el){
        handle.el.addEventListener("error",reportError,{once:true});
        handle.el.addEventListener("ended",()=>{
          if(!dead && stream===handle && intent) select(current+1,true);
        },{once:true});
      }
      transport();
      if(!handle?.el){reportError();return;}
      if(play) start();
    }
    function start() {
      if(dead || !stream?.el) return;
      failed=false;intent=true;
      const handle=stream;
      transport();
      const playing=handle.play();
      playing?.catch?.(error=>{
        if(stream!==handle || dead || error.name==="AbortError") return;
        intent=false;failed=true;transport();
      });
    }
    previous.onclick=()=>select(current-1,intent);
    next.onclick=()=>select(current+1,intent);
    toggle.onclick=()=>{
      if(failed){select(current,true);return;}
      if(intent){intent=false;stream?.pause();transport();}
      else start();
      options.onIntent?.(intent);
    };
    function tick(now, reduced=false) {
      if(dead) return;
      root.classList.toggle("fm-still",reduced || !intent);
      const delta=lastTick===null ? 0 : Math.min(200,now-lastTick);
      lastTick=now;
      if(reduced) {paintPose("rest");cue=0;remaining=0;}
      else if(poses){
        remaining-=delta;
        if(remaining<=0){
          const seq=meta.loops[variant], step=seq[cue++];
          paintPose(step.frame);remaining=step.ms;
          if(cue===seq.length){cue=0;variant=(variant+1)%meta.loops.length;}
        }
      }
      const position=reduced ? 0 : stream?.el?.currentTime || 0;
      if(position!==displayedTime && now-waveAt>=30){
        paintWave(position,reduced);displayedTime=position;waveAt=now;
        const phase=position%scrollPeriod;
        const travel=Math.max(0,phase-1.35)/(scrollPeriod-1.35);
        tape.style.transform=travel ? "translateX("+(-50*travel)+"%)" : "";
      }
    }
    if(queue.length) select(0,!!options.autoplay);
    else {previous.disabled=toggle.disabled=next.disabled=true;transport();}
    return {tick, destroy(){
      dead=true;
      const old=stream;stream=null;old?.stop();
      previous.onclick=toggle.onclick=next.onclick=null;root.remove();
    }};
  }
  return {mount,skinFor};
})();
