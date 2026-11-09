/* Five local Racing Thoughts captures with a separate bonus button. The video never
 * owns money or a game clock, and no remote media or account is involved. */
import { restoreRace, advanceRace, takeRaceBonus, raceCanPlay } from './race-model.js';
import { sfx } from '../basics/fx.js';
import { autopilot } from './autopilot.js';  // prestige hook: Autopilot (idle_6)

export const RACE_COURSES = Object.freeze(['threshold','mainframe','boudoir','keep','goonworks']);
const courseTitles = {threshold:'Threshold', mainframe:'Mainframe', boudoir:'Boudoir', keep:'The Keep', goonworks:'GOONWORKS'};

export function mountRace(ctx) {
  ctx.css(`
    [data-arcade=mini_race]{width:100%;max-width:300px;margin:auto;display:grid;gap:3px;color:var(--ink);font:13px Arial,sans-serif}
    .mini-race-film{position:relative;aspect-ratio:16/9;width:100%;overflow:hidden;border-radius:8px;background:#15151d}
    .mini-race-film video,.mini-race-film img{position:absolute;inset:0;width:100%;height:100%;object-fit:cover;pointer-events:none}
    .mini-race-collect{position:absolute;right:5px;bottom:5px;min-height:44px;padding:6px 9px;border:1px solid #ffffff45;border-radius:6px;background:#161424df;color:#fff;font:700 12px Arial,sans-serif;touch-action:manipulation;cursor:pointer}
    html[data-bubble-type] [data-arcade=mini_race],html[data-bubble-type] .mini-race-collect{font-family:var(--game-bubble-font)}
    .mini-race-collect:disabled{cursor:default;opacity:.7}
    .mini-race-collect:not(:disabled){border-color:var(--niche-text);box-shadow:inset 0 -3px 0 var(--niche)}
    .mini-race-caption{position:absolute;left:6px;top:5px;font-size:10px;color:#fff;text-shadow:0 1px 3px #000;pointer-events:none}
    .mini-race-receipt{text-align:center;font-variant-numeric:tabular-nums;font-weight:700}
    .mini-race-receipt:empty{display:none}
    .mini-race-caption[hidden],.mini-race-sketch[hidden]{display:none}
    .mini-race-sketch{position:absolute;inset:0;overflow:hidden;pointer-events:none;--rs:var(--niche,#ff5fa2);background:linear-gradient(#251a3a,#15151d 46%,#0d0d13 46%)}
    .mini-race-sketch[data-course=threshold]{--rs:#8fd3ff}.mini-race-sketch[data-course=mainframe]{--rs:#3dff8e}
    .mini-race-sketch[data-course=boudoir]{--rs:#ff7ab8}.mini-race-sketch[data-course=keep]{--rs:#e0b45c}.mini-race-sketch[data-course=goonworks]{--rs:#ff4d6d}
    .rs-glow{position:absolute;left:50%;top:46%;width:60%;aspect-ratio:2;transform:translate(-50%,-50%);border-radius:50%;background:radial-gradient(closest-side,var(--rs),transparent);opacity:.35}
    .rs-road{position:absolute;left:0;right:0;top:46%;bottom:0;background:var(--rs);clip-path:polygon(47% 0,53% 0,108% 100%,-8% 100%)}
    .rs-road::before{content:'';position:absolute;inset:0;background:linear-gradient(#1c1b26,#2e2c3b);clip-path:polygon(47.6% 0,52.4% 0,102% 100%,-2% 100%)}
    .rs-lane{position:absolute;inset:0;background:repeating-linear-gradient(#fffc 0 9px,transparent 9px 20px);clip-path:polygon(49.8% 0,50.2% 0,51.6% 100%,48.4% 100%);animation:rs-lane .4s linear infinite}
    @keyframes rs-lane{to{background-position:0 20px}}
    .rs-streak{position:absolute;left:50%;top:46%;width:75%;height:1px;background:linear-gradient(90deg,transparent,var(--rs));transform-origin:0 50%;opacity:0;animation:rs-streak 1.1s linear infinite}
    .rs-streak:nth-child(3){--a:-168deg}.rs-streak:nth-child(4){--a:-12deg;animation-delay:-.35s}
    .rs-streak:nth-child(5){--a:-150deg;animation-delay:-.7s}.rs-streak:nth-child(6){--a:-30deg;animation-delay:-.9s}
    @keyframes rs-streak{0%{transform:rotate(var(--a,0deg)) translateX(8%) scaleX(.15);opacity:0}30%{opacity:.7}100%{transform:rotate(var(--a,0deg)) translateX(55%) scaleX(.55);opacity:0}}
    .rs-kart{position:absolute;left:50%;bottom:9%;width:26px;height:11px;margin-left:-13px;border-radius:6px 6px 3px 3px;background:var(--rs);box-shadow:-9px 6px 0 -3px #0d0d13,9px 6px 0 -3px #0d0d13,0 3px 6px #000a;animation:rs-kart .5s ease-in-out infinite alternate}
    @keyframes rs-kart{to{transform:translate(-3px,-1px)}}
    .rs-name{position:absolute;left:6px;right:6px;top:12%;text-align:center;font:900 19px/1.05 Arial,sans-serif;color:#fff;letter-spacing:.02em;text-shadow:0 2px 0 #000b,0 0 10px var(--rs)}
    html[data-bubble-type] .rs-name{font-family:var(--game-bubble-font)}
    .mini-race-sketch:not([data-run]) *{animation-play-state:paused}
  `);
  const dock = ctx.dock({ title: 'Racing Thoughts', width: 248 });
  const root = ctx.el('div', '', null, dock);
  root.dataset.dockSurface = '';
  root.dataset.arcade = 'mini_race';
  const film = ctx.el('div', '', null, root);
  film.className = 'mini-race-film';
  const doc = root.ownerDocument;
  // Drawn road under the footage: the web build ships no recorded clips, so this is what plays there.
  const sketch = ctx.el('div', '', null, film);
  sketch.className = 'mini-race-sketch';
  sketch.setAttribute('aria-hidden', 'true');
  for (const part of ['rs-glow', 'rs-road', 'rs-streak', 'rs-streak', 'rs-streak', 'rs-streak', 'rs-kart', 'rs-name']) ctx.el('div', '', null, sketch).className = part;
  ctx.el('div', '', null, sketch.querySelector('.rs-road')).className = 'rs-lane';
  const sketchName = sketch.querySelector('.rs-name');
  const poster = ctx.el('img', '', null, film);
  // A missing poster stays hidden: never a broken-image icon or its alt text over the card.
  poster.addEventListener('load', () => { if (!dead) paintMedia(); });
  poster.addEventListener('error', () => { if (!dead) paintMedia(); });
  const caption = ctx.el('small', '', '', film);
  caption.className = 'mini-race-caption';
  const collect = ctx.el('button', '', '', film);
  collect.type = 'button';
  collect.className = 'mini-race-collect';
  const receipt = ctx.el('div', '', '', root);
  receipt.className = 'mini-race-receipt';
  receipt.setAttribute('aria-live', 'polite');

  const model = restoreRace(ctx.store.get('race'));
  const persist = () => ctx.store.set('race', { ...model });
  const retryAfter = new Map();
  let courseIndex = Math.max(0,RACE_COURSES.indexOf(ctx.store.get('course')));
  let video = null, dead = false, ended = false, visible = false;
  let pendingPlay = false, hasFrame = false, needsNext = false;
  let lastTime = 0, furthestTime = 0, stalledAt = ctx.state.clock, recoveries = 0;
  let lastClock = ctx.state.clock, lastSecond = -1, wasGentle = ctx.gentle;
  const canPlay = () => raceCanPlay({ paused:ctx.paused || ended || !!ctx.state.ended,
    hidden:doc.hidden, gentle:ctx.gentle, visible, dead, failed:false });

  // With no footage at all the drawn road takes a new course name every SKETCH_LAP_MS of play.
  const SKETCH_LAP_MS = 14000;
  let sketchMode = false, showIndex = courseIndex, showSince = ctx.state.clock;
  function paintSketch() {
    const id = RACE_COURSES[showIndex];
    sketch.dataset.course = id;
    sketchName.textContent = courseTitles[id];
  }
  function paintMedia() {
    if (!video) return;
    const posterOk = poster.complete && poster.naturalWidth > 0;
    poster.hidden = !posterOk || (hasFrame && !ctx.gentle && !needsNext);
    video.hidden = ctx.gentle || needsNext;
    const covered = !poster.hidden || (hasFrame && !video.hidden);
    sketch.hidden = covered;
    caption.hidden = !covered; // the sketch prints the course name itself
    sketch.toggleAttribute('data-run', !covered && !ctx.reduced && canPlay());
  }
  function releaseVideo() {
    if (!video) return;
    const previous = video;
    video = null;
    previous.pause();
    previous.removeAttribute('src');
    previous.load();
    previous.remove();
  }
  function loadCourse(index) {
    releaseVideo();
    courseIndex = index;
    const id = RACE_COURSES[index], name = courseTitles[id];
    const path = 'assets/race/'+id;
    ctx.store.set('course', id);
    poster.src = path+'-poster.jpg';
    poster.alt = 'Racing Thoughts on the '+name+' course';
    caption.textContent = name;
    if (!sketchMode) { showIndex = index; showSince = ctx.state.clock; paintSketch(); }
    pendingPlay = hasFrame = needsNext = false;
    lastTime = furthestTime = recoveries = 0;
    stalledAt = ctx.state.clock;
    const player = doc.createElement('video');
    video = player;
    video.muted = true;
    video.defaultMuted = true;
    video.setAttribute('muted','');
    video.playsInline = true;
    video.setAttribute('playsinline','');
    video.disablePictureInPicture = true;
    video.preload = 'metadata';
    // No video.poster: the <img> above it is the poster, and a missing file would be fetched twice.
    // Only the active clip is attached. Late events from an old decoder
    // cannot advance the next clip or clear its playback promise.
    function on(type,fn) {
      player.addEventListener(type,()=>{ if (!dead && video===player) fn(); });
    }
    on('play',()=>{ if (!canPlay()) player.pause(); });
    on('playing',()=>{
      if (!canPlay()) { player.pause(); return; }
      hasFrame = player.readyState>=2;
      sketchMode = false;
      paintMedia();
    });
    for (const event of ['pause','waiting','emptied']) on(event,()=>{ hasFrame=false; paintMedia(); });
    on('volumechange',()=>{ if (!player.muted) player.muted=true; });
    on('ended',()=>{ if (player.ended) { needsNext=true; sync(); } });
    on('error',skipBroken);
    film.insertBefore(player,poster);
    player.src = path+'-loop.mp4';
    paintMedia();
  }
  function skipBroken() {
    retryAfter.set(courseIndex,ctx.state.clock+60000);
    hasFrame=false;needsNext=true;sync();
  }
  function sync() {
    if (dead) return;
    paintMedia();
    if (!canPlay()) {
      video.pause();
      stalledAt = ctx.state.clock;
      return;
    }
    if (needsNext || video.ended) {
      let next = -1;
      for (let n=1;n<=RACE_COURSES.length;n++) {
        const i=(courseIndex+n)%RACE_COURSES.length;
        if ((retryAfter.get(i)||0)<=ctx.state.clock) { next=i;break; }
      }
      if (next<0) {
        // Every clip failed (the web build ships none): the drawn road keeps racing instead.
        video.pause();
        if (!sketchMode) { sketchMode = true; showSince = ctx.state.clock; }
        else if (ctx.state.clock-showSince >= SKETCH_LAP_MS) {
          showIndex = (showIndex+1)%RACE_COURSES.length; showSince = ctx.state.clock; paintSketch();
        }
        return;
      }
      loadCourse(next);
    }
    if (!video.paused || pendingPlay) return;
    const player = video;
    pendingPlay = true;
    Promise.resolve(player.play()).catch(()=>{
      if (!dead && video===player) { hasFrame=false;paintMedia(); }
    }).finally(()=>{ if(video===player)pendingPlay=false; });
  }
  function measure() {
    const r = film.getBoundingClientRect();
    visible = r.width>0 && r.height>0 && r.bottom>0 && r.right>0 && r.top<doc.defaultView.innerHeight && r.left<doc.defaultView.innerWidth;
    sync();
    if (!canPlay() || needsNext) return;
    const time = video.currentTime;
    if (time>lastTime+.001) {
      stalledAt=ctx.state.clock;
      if(time>furthestTime+.05){furthestTime=time;recoveries=0;}
    }
    lastTime=time;
    if (ctx.state.clock-stalledAt<4000) return;
    hasFrame=false;paintMedia();stalledAt=ctx.state.clock;
    if (recoveries<2) { recoveries++;video.load(); }
    else skipBroken();
  }
  loadCourse(courseIndex);

  const observer = doc.defaultView.IntersectionObserver ? new doc.defaultView.IntersectionObserver((entries) => {
    visible = entries.some((e) => e.isIntersecting && e.intersectionRect.width > 0 && e.intersectionRect.height > 0);
    sync();
  }) : null;
  observer?.observe(film);
  const onDockMove = (event) => { if (event.detail?.root?.contains(film)) measure(); };
  doc.addEventListener('fz-dock-move', onDockMove);
  doc.addEventListener('visibilitychange', sync);
  ctx.on('pause', sync);
  ctx.on('resume', () => { lastClock = ctx.state.clock; measure(); });
  ctx.on('suspend', sync);
  ctx.on('ending', () => { ended = true; sync(); });
  ctx.on('buy', sync); // An ending purchase stops footage before the ending fade.
  ctx.every(500, measure); // Covers display:none trays and collapsed dock bodies too.
  ctx.frame(() => {
    const now = ctx.state.clock, step = Math.max(0, now - lastClock);
    if (!doc.hidden) advanceRace(model, step);
    lastClock = now;
    if (auto.step(doc.hidden ? 0 : step, model.leftMs === 0) && takeRaceBonus(model)) {  // prestige hook: Autopilot (idle_6), half an untaken bonus
      persist();
      const rates = ctx.rates();
      receipt.textContent = `Autopilot +${ctx.fmt(ctx.pay(Math.max(20, rates.fps * 12 + rates.click * 20) * auto.share, 'mini_race'))} ${ctx.brand.currencyPlural}`;
    }
    if (wasGentle !== ctx.gentle) { wasGentle = ctx.gentle; sync(); }
    const seconds = Math.ceil(model.leftMs / 1000);
    dock.dock?.setReady(model.leftMs === 0, 'Race bonus ready');
    if (seconds === lastSecond) return;
    lastSecond = seconds;
    collect.disabled = seconds > 0;
    collect.textContent = seconds ? `Bonus in ${seconds}s` : 'Collect bonus';
    persist();
  });
  dock.dock?.setReady(model.leftMs === 0, 'Race bonus ready');
  collect.disabled = model.leftMs > 0;
  collect.textContent = model.leftMs ? `Bonus in ${Math.ceil(model.leftMs / 1000)}s` : 'Collect bonus';
  const auto = autopilot(ctx, { dueMs: 35000 });  // prestige hook: Autopilot (idle_6), RACE_BONUS_MS left untaken
  collect.addEventListener('click', () => {
    if (dead || ended || ctx.paused || !takeRaceBonus(model)) return;
    auto.reset();
    dock.dock?.setReady(false);
    persist(); // Consume readiness before crediting: a second click cannot pay again.
    collect.disabled = true;
    collect.textContent = 'Bonus in 35s';
    const r = collect.getBoundingClientRect(), rates = ctx.rates();
    const got = ctx.pay(Math.max(20, rates.fps * 12 + rates.click * 20), 'mini_race', { x: r.left + r.width / 2, y: r.top });
    receipt.textContent = `+${ctx.fmt(got)} ${ctx.brand.currencyPlural}`;
    sfx(ctx, 'pluck/wood_07.wav', .16);
    if (!ctx.reduced) ctx.anim(receipt, [{ opacity: .35 }, { opacity: 1 }], { duration: ctx.gentle ? 1400 : 350 });
  });
  ctx.after(0, measure);
  ctx.cleanup(() => {
    dead = true;
    dock.dock?.setReady(false);
    observer?.disconnect();
    doc.removeEventListener('visibilitychange', sync);
    doc.removeEventListener('fz-dock-move', onDockMove);
    releaseVideo();
  });
  return { root, show() { dock.dock?.setVisible(true);dock.dock?.bringToFront();measure(); } };
}
