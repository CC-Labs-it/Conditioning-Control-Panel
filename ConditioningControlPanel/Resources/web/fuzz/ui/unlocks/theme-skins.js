/* ui/unlocks/theme-skins.js - each theme's button look and picture niches, in one place, so Second Theme can bring along the
 * real ones. A theme lane's own unlock wears the same css from here. Infection has no button of its own: it gets a sick green one. */

export const THEME_TASTES = Object.freeze({
  trance: Object.freeze({ skin: 'tr-pendulum', niches: ['spirals', 'soft voices', 'pendulum', 'eyes closed'], css: `#btn[data-skin=tr-pendulum]{width:118px;height:54px;border-radius:50%;background:radial-gradient(circle at 36% 30%,color-mix(in srgb,var(--niche) 45%,#fff),var(--niche) 45%,color-mix(in srgb,var(--niche-2) 70%,#000));border:0;color:var(--btn-ink);box-shadow:none;transform-origin:50% -46px;}
#btn[data-skin=tr-pendulum]::before{content:'';position:absolute;left:50%;bottom:100%;width:2px;height:46px;margin-left:-1px;background:var(--niche-2);opacity:.8;}
#btn[data-skin=tr-pendulum].down{transform:none;filter:brightness(1.2);}` }),
  bimbo: Object.freeze({ skin: 'bi-lips', niches: ['pink', 'heels', 'gloss', 'curls', 'lashes'], css: `#btn[data-skin=bi-lips]{width:132px;height:84px;font-size:0;background:radial-gradient(circle at 34% 26%,#fff 0 6%,color-mix(in srgb,var(--niche) 55%,#fff) 24%,var(--niche) 62%,var(--niche-2));border:0;border-radius:42px;box-shadow:0 3px 0 var(--btn-edge);}
#btn[data-skin=bi-lips]::after{content:'👄';font-size:60px;line-height:84px;font-family:"Segoe UI Emoji","Apple Color Emoji","Noto Color Emoji",sans-serif;}` }),
  drone: Object.freeze({ skin: 'dr-hex', niches: ['shiny', 'chrome', 'visor', 'gloss', 'mirror', 'latex'], css: `#btn[data-skin=dr-hex]{width:112px;height:96px;font-size:14px;border:0;border-radius:0;box-shadow:none;clip-path:polygon(25% 0,75% 0,100% 50%,75% 100%,25% 100%,0 50%);color:var(--btn-ink);background:linear-gradient(135deg,color-mix(in srgb,var(--niche) 18%,#fff),var(--niche) 40%,color-mix(in srgb,var(--niche) 30%,#fff) 62%,var(--niche-2));}
#btn[data-skin=dr-hex].down{transform:scale(.93);filter:brightness(1.25);}
#btn-zone{min-height:104px;}` }),
  sissy: Object.freeze({ skin: 'si-bow', niches: ['lace', 'bows', 'pearls', 'frills', 'gloves', 'satin'], css: `#btn[data-skin=si-bow]{width:128px;height:90px;font-size:0;background:transparent;border:0;box-shadow:none;}
#btn[data-skin=si-bow]::after{content:'🎀';font-size:76px;line-height:90px;font-family:"Segoe UI Emoji","Apple Color Emoji","Noto Color Emoji",sans-serif;font-style:normal;filter:drop-shadow(0 0 7px var(--niche-2));}` }),
  goon: Object.freeze({ skin: 'worn', niches: ['goon'], css: `#btn[data-skin=worn]{background:radial-gradient(ellipse at 50% 55%,rgba(255,255,255,calc(.08 + var(--worn,0) * .6)) 0 14%,rgba(255,255,255,calc(var(--worn,0) * .25)) 38%,transparent 66%),color-mix(in srgb,var(--mix) 62%,#2a2622);border:2px solid var(--btn-edge);color:rgba(247,230,207,calc(1 - var(--worn,0) * .7));box-shadow:none;}` }),
  locked: Object.freeze({ skin: 'padlock', niches: ['chastity'], css: `#btn[data-skin=padlock]{position:relative;width:108px;height:70px;margin-top:24px;background:linear-gradient(color-mix(in srgb,var(--niche) 58%,#fff),var(--niche) 55%,var(--niche-2));color:var(--btn-ink);border:0;border-radius:10px;font:700 15px var(--font);overflow:visible;box-shadow:0 3px 0 var(--btn-edge);}
#btn[data-skin=padlock]::before{content:"";position:absolute;left:50%;top:-26px;width:58px;height:40px;margin-left:-29px;box-sizing:border-box;border:10px solid color-mix(in srgb,var(--niche) 70%,#fff);border-bottom:0;border-radius:30px 30px 0 0;transform:translateY(var(--shackle,0));transition:transform .1s ease-out;}
#btn[data-skin=padlock].down{transform:scale(.97);}` }),
  censored: Object.freeze({ skin: 'censorbar', niches: ['censored'], css: `#btn[data-skin=censorbar]{width:230px;height:40px;background:#000;color:#000;border:0;border-radius:0;box-shadow:0 0 0 2px var(--niche),0 0 0 4px var(--niche-2);font:700 15px var(--font);transition:color .08s ease-out;}
#btn[data-skin=censorbar].down{color:#fffdf6;transform:scale(.98);}
body.gentle #btn[data-skin=censorbar].down{color:#000;}` }),
  infection: Object.freeze({ skin: 'in-spot', niches: ['infection'], css: `#btn[data-skin=in-spot]{background:radial-gradient(circle at 30% 30%,color-mix(in srgb,var(--niche) 28%,#fff),var(--niche) 60%,var(--niche-2));color:var(--btn-ink);border:2px solid var(--btn-edge);border-radius:18px;font-weight:700;}` }),
});
