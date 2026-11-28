import { DEMOS } from './kit.js';

export function registerBubbleDemo() {
  const swallow = DEMOS.bi_end;
  DEMOS.bi_end = k => {
    swallow(k);
    const sky = k.add(-8, -5, 'width:calc(100% + 16px);height:calc(100% + 10px);opacity:0;background:url("assets/img/bambi-sky.webp") 50% 44%/cover;');
    const still = matchMedia('(prefers-reduced-motion: reduce)').matches || document.body.classList.contains('gentle');
    k.frame((_dt,t) => {
      const beat = t % 6;
      sky.style.opacity = String(Math.max(0,Math.min(1,(beat-2.3)/.55,(6-beat)/.4)));
      if (!still) sky.style.transform = `translate(${5*Math.sin(t*.31)}px,${2*Math.sin(t*.21)}px) scale(1.03)`;
    });
  };
}
