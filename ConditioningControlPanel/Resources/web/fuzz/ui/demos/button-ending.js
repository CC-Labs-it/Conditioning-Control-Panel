import './routes.js';
import { DEMOS } from './kit.js';

// The shop shows the approach to the pink light. The final viewpoint is discovered during play.
DEMOS.o_out = k => {
  k.page('dark');
  k.add(0, 0, 'width:240px;height:150px;background:radial-gradient(ellipse at 48% 47%,#fff1fb 0%,#f983ce 3%,#62327e 16%,#220e31 50%,#110916 80%);');
  const rings = Array.from({ length: 9 }, (_, i) => k.add(120, 72,
    'width:144px;height:98px;margin:-49px -72px;border-radius:42%;border:2px solid ' +
    (i % 2 ? '#fa80ca' : '#87d4dd') + ';'));
  const slot = k.add(22, 45, 'width:69px;height:67px;background:url("assets/arcade/button-slot-cabinet.webp") center/contain no-repeat');
  const toyBox = k.add(158, 36, 'width:55px;height:44px;background:url("assets/glossy/toy-box-lid.webp") center/contain no-repeat');
  const calm = matchMedia('(prefers-reduced-motion: reduce)').matches || document.body.classList.contains('gentle');
  k.frame((_dt, t) => {
    slot.style.transform = calm ? 'none' : 'translateY(' + (2 * Math.sin(t * .55)) + 'px)';
    toyBox.style.transform = calm ? 'none' : 'translateY(' + (2 * Math.cos(t * .48)) + 'px)';
    for (let i = 0; i < rings.length; i++) {
      const depth = ((i / rings.length + (calm ? 0 : t * .12)) % 1);
      rings[i].style.transform = 'rotate(' + (i * 13 + (calm ? 0 : t * 6)) + 'deg) scale(' + (.08 + 2.4 * depth * depth) + ')';
      rings[i].style.opacity = String(Math.sin(depth * Math.PI) * .7);
    }
  });
};
