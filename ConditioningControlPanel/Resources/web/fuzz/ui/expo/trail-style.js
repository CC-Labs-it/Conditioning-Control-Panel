export const TRAIL_STYLE = `
.expo-route-guide,.expo-trail-card,.expo-stamp-moment,.expo-deviation-slip{box-sizing:border-box;color:#233f34;font-family:"Segoe UI",system-ui,sans-serif}
.expo-route-guide{position:absolute;top:max(12px,env(safe-area-inset-top));left:max(12px,env(safe-area-inset-left));width:max-content;max-width:min(33vw,310px);z-index:2;pointer-events:none;text-align:left}
.expo-trail-open,.expo-trail-task{border:1px solid #afa88d;background:#f4efdd;box-shadow:1px 2px 6px #17291a46}
.expo-trail-open{display:grid;grid-template-columns:1fr auto;align-items:baseline;column-gap:16px;padding:6px 10px;pointer-events:auto;text-align:left;font:inherit;color:inherit;cursor:pointer;width:100%;border-left:4px double #48634c}
.expo-trail-mobile-label,.expo-trail-mobile-count{display:none}
.expo-trail-open b,.expo-trail-task small,.expo-trail-card header small,.expo-trail-stop small{font-weight:700;font-size:10px;letter-spacing:.09em;text-transform:uppercase}
.expo-trail-open strong{font:bold 16px Georgia,serif;text-align:right}.expo-trail-open small{font-size:11px}
.expo-trail-open kbd{font:600 11px system-ui;color:#345442;text-align:right}
.expo-trail-pips{grid-column:1/-1;display:flex;gap:3px;align-items:center;margin:4px 0}
.expo-trail-pips i{display:block;width:12px;height:8px;border:1px solid #8b8a73;background:#e4dfcd;box-sizing:border-box}
.expo-trail-pips i:nth-child(8){margin-right:5px;position:relative}
.expo-trail-pips i:nth-child(8)::after{content:"";position:absolute;right:-6px;top:-3px;height:13px;border-right:2px solid #936429}
.expo-trail-pips i[data-ink=full]{background:#38664f;border-color:#38664f}.expo-trail-pips i[data-ink=partial]{background:linear-gradient(90deg,#38664f 50%,#e4dfcd 50%)}
.expo-trail-task{display:grid;gap:2px;padding:6px 10px;margin-top:3px;background:#173e36f2;border-color:#507364;color:#fff7e4;border-left:3px solid #c19b53}
.expo-trail-task small{color:#ddc596}.expo-trail-task strong{font:600 15px/1.2 "Segoe UI",sans-serif}.expo-trail-task span{font:12px/1.3 "Segoe UI",sans-serif;color:#eee9d8}.expo-task-arrow{display:inline-block;margin-right:.5em;font-style:normal}
.expo-trail-task .expo-range-hint{font-weight:650;color:#f1d99e}
.expo-root .expo-caption:not(:empty){left:auto;right:12px;max-width:min(45%,430px)}
.expo-trail-card{position:absolute;z-index:7;left:50%;top:50%;transform:translate(-50%,-50%);width:min(720px,calc(100% - 28px));max-height:calc(100% - 58px);overflow:auto;overscroll-behavior:contain;touch-action:pan-y;padding:18px 20px;background:#f5efdb;border:1px solid #aba17e;border-top:5px double #476752;box-shadow:0 10px 50px #081d1eb0;text-align:left}
.expo-trail-card header{border-bottom:1px solid #a3a58c;padding-right:60px}.expo-trail-card h2{font:bold clamp(23px,4vw,34px)/1.12 Georgia,serif;margin:4px 0}.expo-trail-card p{font-size:14px;line-height:1.35;margin:6px 0}
.expo-trail-card button{cursor:pointer;background:#254c3d;color:#fff4d9;border:1px solid #224c38;font:600 13px system-ui;padding:6px 12px}
.expo-trail-close{position:absolute;right:14px;top:12px}.expo-trail-card :focus-visible,.expo-trail-open:focus-visible{outline:3px solid #c57232;outline-offset:2px}
.expo-trail-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));list-style:none;padding:0;margin:10px 0;gap:5px}
.expo-trail-stop{position:relative;border:1px solid #a6aa91;padding:6px 7px;background:#fffae955;min-width:0;overflow:hidden}
.expo-trail-stop small{display:block;color:#64715d;font-size:9px}.expo-trail-stop strong{display:block;font:700 16px/1.12 Georgia,serif;margin:2px 0}.expo-trail-stop span,.expo-trail-stop p{font-size:12px;line-height:1.22;margin:2px 0}.expo-trail-stop span{color:#526152}.expo-trail-stop em,.expo-stamp-moment strong{display:inline-block;font:700 15px Georgia,serif;border:2px double #36664d;color:#36664d;padding:1px 5px;transform:rotate(-3deg);text-transform:uppercase}
.expo-trail-stop[data-sealed=true]{background:repeating-linear-gradient(130deg,#ebe5d4,#ebe5d4 5px,#e7e1cf 5px,#e7e1cf 6px)}
.expo-trail-card footer{border-top:1px solid #a3a58c;padding-top:7px;display:flex;justify-content:space-between;align-items:center;gap:8px}
.expo-trail-card footer p{font-size:12px}.expo-ledger-list{list-style:none;margin:10px 0;padding:0}.expo-ledger-list li{display:flex;justify-content:space-between;gap:10px;padding:6px 0;border-bottom:1px dotted #a4a890;font-size:14px}
.expo-stamp-moment,.expo-deviation-slip{position:absolute;left:50%;top:15%;transform:translateX(-50%);z-index:3;width:max-content;max-width:min(72%,430px);padding:10px 18px;background:#f6efda;border:1px solid #a9a285;text-align:center;box-shadow:1px 3px 9px #13291f54;pointer-events:none}
.expo-stamp-moment strong{font-size:24px}.expo-stamp-moment span,.expo-stamp-moment small{display:block;margin-top:4px;font-size:13px}
.expo-deviation-slip{top:auto;bottom:22%;padding:5px 11px;font-size:13px;border-left:3px solid #9b6a3f}
.expo-stamp-moment[data-new]{animation:expo-trail-ink .25s ease-out}.expo-deviation-slip[data-new],.expo-root[data-calm] .expo-stamp-moment[data-new]{animation:expo-trail-fade .2s ease-out}
@keyframes expo-trail-ink{from{opacity:0;translate:0 -8px;scale:1.08}to{opacity:1;translate:0 0;scale:1}}
@keyframes expo-trail-fade{from{opacity:0}to{opacity:1}}
@media(prefers-reduced-motion:reduce){.expo-stamp-moment[data-new]{animation:expo-trail-fade .2s ease-out}}
.expo-prompt[data-loan-card]{top:66%}
.expo-root[data-hoof-board] .expo-guide{display:none}
@media(pointer:coarse) and (orientation:landscape){
  .expo-root[data-hoof-board] .expo-narration{left:48%;bottom:max(6px,env(safe-area-inset-bottom));width:calc(100% - 310px)}
  .expo-root[data-hoof-board] .expo-narration p{font-size:16px}
  .expo-root[data-hoof-board] .expo-prompt{top:auto;bottom:15%;font-size:12px}
  .expo-root[data-hoof-board] .expo-deviation-slip{top:auto;bottom:23%;left:auto;right:8px;transform:none;font-size:11px;max-width:24%}
}
.expo-root .expo-route-guide [hidden],.expo-root .expo-route-guide[hidden],.expo-root .expo-trail-card [hidden],.expo-root .expo-trail-card[hidden],.expo-root .expo-stamp-moment[hidden],.expo-root .expo-deviation-slip[hidden],.expo-leaving .expo-route-guide,.expo-leaving .expo-trail-card,.expo-leaving .expo-stamp-moment,.expo-leaving .expo-deviation-slip{display:none}
.expo-root:has(.expo-trail-card:not([hidden])) :is(.expo-route-guide,.expo-touch,.expo-prompt,.expo-aim){visibility:hidden}
@media(max-width:660px){.expo-route-guide{top:max(51px,calc(env(safe-area-inset-top) + 43px));left:8px;max-width:min(76vw,300px)}.expo-trail-card{padding:12px;max-height:calc(100% - 80px)}.expo-trail-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.expo-root .expo-caption:not(:empty){top:auto;bottom:calc(38% + 12px);right:8px;max-width:79%}.expo-stamp-moment{top:33%}.expo-deviation-slip{bottom:30%}}
@media(max-height:480px){.expo-route-guide{top:4px;max-width:32vw}.expo-trail-task{padding:3px 6px}.expo-trail-open{padding:3px 6px}.expo-trail-card{max-height:92%}}

/* The ticket takes the spare space opposite the Expo's sound and exit controls. */
.expo-root .expo-route-guide[data-compact]:not([hidden]){
  top:max(8px,env(safe-area-inset-top));left:max(8px,env(safe-area-inset-left));right:max(8px,env(safe-area-inset-right));
  width:auto;max-width:none;display:grid;justify-items:start;align-items:start;gap:4px}
.expo-route-guide[data-compact] .expo-trail-open{
  grid-area:1/1;display:flex;align-items:center;gap:4px;width:max-content;min-height:44px;margin:0;padding:0 6px;
  border:1px solid #aaa78e;border-radius:6px;background:#f4efddf5;color:#213e32;box-shadow:0 1px 4px #10231d40}
.expo-route-guide[data-compact] .expo-trail-open>:not(.expo-trail-mobile-label,.expo-trail-mobile-count){display:none}
.expo-route-guide[data-compact] .expo-trail-mobile-label{display:block;font:650 13px/1 system-ui;letter-spacing:0;text-transform:none}
.expo-route-guide[data-compact] .expo-trail-mobile-count{display:block;padding-left:4px;border-left:1px dashed #89977f;font:bold 15px/1.15 Georgia,serif}
.expo-route-guide[data-compact] :is(.expo-trail-task,.expo-stamp-moment,.expo-deviation-slip){
  grid-area:2/1;position:static;transform:none;box-sizing:border-box;width:fit-content;max-width:min(100%,345px);
  margin:0;padding:5px 9px;box-shadow:0 1px 4px #10231d35;text-align:left;border-radius:4px}
.expo-route-guide[data-compact] .expo-trail-task{gap:1px}
.expo-route-guide[data-compact] .expo-trail-task small{display:none}
.expo-route-guide[data-compact] .expo-trail-task strong{font-size:14px;line-height:1.22}
.expo-route-guide[data-compact] .expo-trail-task span{font-size:12px;line-height:1.3}
.expo-route-guide[data-compact] .expo-stamp-moment strong{font-size:16px;padding:0 4px}
.expo-route-guide[data-compact] .expo-stamp-moment small{display:inline;margin-left:6px;font-size:12px}
.expo-route-guide[data-compact] .expo-stamp-moment span{margin-top:2px;line-height:1.25}
.expo-route-guide[data-compact]:has(:is(.expo-stamp-moment,.expo-deviation-slip):not([hidden])) .expo-trail-task,
.expo-root[data-hoof-target] .expo-route-guide[data-compact] .expo-trail-task{display:none}
@media(orientation:landscape) and (min-width:560px){
  .expo-root .expo-route-guide[data-compact]:not([hidden]){right:auto;width:max-content;max-width:calc(100% - max(225px,calc(env(safe-area-inset-right) + 217px)) - max(8px,env(safe-area-inset-left)));grid-template-columns:max-content minmax(0,1fr);column-gap:6px}
  .expo-route-guide[data-compact] :is(.expo-trail-task,.expo-stamp-moment,.expo-deviation-slip){grid-area:1/2;max-width:min(100%,345px)}
}
.expo-root .expo-trail-card[data-compact]:not([hidden]){
  left:max(6px,env(safe-area-inset-left));right:max(6px,env(safe-area-inset-right));margin-inline:auto;
  top:calc(max(8px,env(safe-area-inset-top)) + 50px);bottom:max(8px,env(safe-area-inset-bottom));transform:none;
  width:auto;max-width:690px;height:auto;max-height:calc(100% - max(8px,env(safe-area-inset-top)) - 50px - max(8px,env(safe-area-inset-bottom)));
  display:flex;flex-direction:column;padding:0;overflow:hidden;border-radius:5px}
.expo-trail-card[data-compact] header{position:relative;box-sizing:border-box;min-height:57px;flex:none;padding:9px 66px 8px 12px}
.expo-trail-card[data-compact] header small,.expo-trail-card[data-compact] footer p,.expo-trail-card[data-compact] .expo-trail-flip-label{display:none}
.expo-trail-card[data-compact] h2{font-size:23px;margin:1px 0 3px}
.expo-trail-card[data-compact] header p{font-size:13px;line-height:1.3;margin:0}
.expo-trail-card[data-compact] button{min-height:44px;border-radius:4px}
.expo-trail-card[data-compact] .expo-trail-close{right:8px;top:7px;padding:4px 9px}
.expo-trail-card[data-compact] .expo-trail-pages{flex:1 1 auto;min-height:0;overflow:auto;overscroll-behavior:contain;touch-action:pan-y;padding:8px 12px}
.expo-trail-card[data-compact] .expo-trail-front:not([hidden]){display:flex;flex-direction:column}
.expo-trail-card[data-compact] .expo-trail-front>.expo-trail-pips{order:-2}
.expo-trail-card[data-compact] .expo-trail-meter{order:-1;margin:2px 0}
.expo-trail-card[data-compact] .expo-trail-grid{margin:6px 0 2px}
.expo-trail-card[data-compact] footer{flex:none;padding:5px 12px;justify-content:flex-end}
.expo-trail-card[data-compact] .expo-trail-mobile-label{display:inline}
.expo-root:is(.expo-leaving,:has(.expo-reading-card:not([hidden]))) .expo-route-guide[data-compact]{display:none}
.expo-root:has(.expo-trail-card[data-compact]:not([hidden])) :is(.expo-guide,.expo-caption,.expo-narration){visibility:hidden}
`;
