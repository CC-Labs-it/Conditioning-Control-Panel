// The question is a child of EMI, so both share the same position during a drag or a scripted visit.
export function attachQuestion(ctx, emi, spot, idle) {
  const root = spot.dock.root;
  const clamp = (value, max) => Math.max(4, Math.min(value, max));
  const align = () => {
    if (!spot.dock.visible) return;
    // The resize grip scales the shared sprite with the question, as it did for the original question window.
    const scale = root.offsetWidth / (spot.offsetWidth || root.offsetWidth || 1);
    emi.setQuizScale(scale);
    const { w, h } = ctx.bounds();
    const { x, y, s } = emi.pos;
    const size = emi.size * s, gap = 4;
    const bottom = h - (root.ownerDocument.getElementById('dock-strip')?.offsetHeight || 0) - gap;
    const width = idle.hidden ? root.offsetWidth : idle.offsetWidth * scale;
    const height = root.offsetHeight;
    const leftRoom = x - gap, rightRoom = w - (x + size + gap);
    let left, top, origin;
    if (Math.max(leftRoom, rightRoom) >= width + gap) {
      const right = rightRoom >= width + gap && (rightRoom >= leftRoom || leftRoom < width + gap);
      left = right ? x + size + gap : x - width - gap;
      top = y + (size - height) / 2;
      origin = right ? 'left center' : 'right center';
    } else {
      const above = y >= height + gap && y > bottom - y - size;
      const controls = emi.controls.offsetHeight + gap;
      left = x + (size - width) / 2;
      top = above ? y - height - controls : y + size + controls;
      origin = above ? 'center bottom' : 'center top';
    }
    root.style.setProperty('--question-x', `${(clamp(left, w - width - gap) - x) / s}px`);
    root.style.setProperty('--question-y', `${(clamp(top, bottom - height) - y) / s}px`);
    root.style.setProperty('--question-scale', String(1 / s));
    root.style.setProperty('--question-origin', origin);
  };
  const observer = new ResizeObserver(align);
  observer.observe(root);
  observer.observe(idle);
  const unbind = emi.onPlace(align);
  ctx.cleanup(() => { unbind(); observer.disconnect(); emi.setQuizScale(1); });
  return align;
}

export function createQuizReactions(ctx, emi) {
  const { el, sprite } = emi;
  ctx.css('.xe-quiz-star{position:absolute;left:50%;top:45%;color:#ffcf62;font:700 12px/1 sans-serif;pointer-events:none;opacity:0;}');
  const stars = Array.from({length:12}, () => {
    const star = ctx.el('span', '', '✦', sprite);
    star.className = 'xe-quiz-star';
    star.setAttribute('aria-hidden','true');
    return star;
  });
  let response = '', start = 0, duration = 0, asking = false;
  const idle = () => {
    response = '';
    el.dataset.reaction = asking ? 'thinking' : 'idle';
    sprite.style.transform = '';
    for (const star of stars) star.style.opacity = '0';
    if (!asking && emi.currentMood === 'thinking') emi.mood('happy');
  };
  idle();
  ctx.frame(() => {
    if (!response) return;
    const age = ctx.state.clock - start;
    if (age >= duration) { idle(); return; }
    const celebrate = response === 'celebrate', wrong = response === 'wrong';
    const calm = ctx.gentle || ctx.reduced || emi.napping || emi.blocking;
    const progress = Math.min(1, age / (celebrate ? 1600 : 900));
    const y = wrong ? 0 : -Math.abs(Math.sin(progress * Math.PI * (celebrate ? 3 : 1))) * (celebrate ? 10 : 5);
    const turn = Math.sin(age / (wrong ? 85 : 110)) * (celebrate ? 6 : 4) * (1 - progress);
    sprite.style.transform = calm ? '' : `translateY(${y}px) rotate(${turn}deg)`;
    stars.forEach((star, i) => {
      const t = (age - i % 3 * 90) / 1500;
      const visible = celebrate && !calm && t > 0 && t < 1;
      star.style.opacity = visible ? String(.9 * Math.sin(t * Math.PI)) : '0';
      if (!visible) return;
      const angle = i * 2.39996, r = emi.size * (.16 + t * .42);
      star.style.transform = `translate(${Math.cos(angle) * r}px,${Math.sin(angle) * r * .8 - t * 16}px) scale(${.4 + Math.sin(t * Math.PI) * .6})`;
    });
  });
  ctx.cleanup(() => {
    emi.asking = asking = false;
    idle();
    delete el.dataset.reaction;
  });
  return {
    ask() {
      emi.asking = asking = true;
      idle();
      if (!emi.napping && !emi.blocking) emi.mood('thinking');
    },
    idle() { emi.asking = asking = false; if (!response) idle(); },
    answer(right, complete) {
      start = ctx.state.clock;
      emi.lastTouch = start;
      duration = complete ? 2600 : 1700;
      response = complete ? 'celebrate' : right ? 'correct' : 'wrong';
      el.dataset.reaction = response;
      if (!emi.napping && !emi.blocking) emi.mood(right ? 'glee' : 'annoyed', duration);
      emi.say(complete ? 'Lesson complete!' : right ? 'Nice!' : 'Next one!', duration);
    },
  };
}
