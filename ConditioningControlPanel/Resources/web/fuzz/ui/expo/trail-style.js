export const TRAIL_STYLE = `
.expo-route-guide,.expo-trail-card,.expo-stamp-moment,.expo-deviation-slip{box-sizing:border-box;color:#233f34;font-family:"Segoe UI",system-ui,sans-serif}
.expo-route-guide{position:absolute;top:max(12px,env(safe-area-inset-top));left:max(12px,env(safe-area-inset-left));width:max-content;max-width:min(33vw,310px);z-index:2;pointer-events:none;text-align:left}
.expo-trail-open,.expo-trail-task{border:1px solid #afa88d;background:#f4efdd;box-shadow:1px 2px 6px #17291a46}
.expo-trail-open{display:grid;grid-template-columns:1fr auto;align-items:baseline;column-gap:16px;padding:6px 10px;pointer-events:auto;text-align:left;font:inherit;color:inherit;cursor:pointer;width:100%;border-left:4px double #48634c}
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
.expo-root .expo-route-guide[hidden],.expo-root .expo-trail-card [hidden],.expo-root .expo-trail-card[hidden],.expo-root .expo-stamp-moment[hidden],.expo-root .expo-deviation-slip[hidden],.expo-leaving .expo-route-guide,.expo-leaving .expo-trail-card,.expo-leaving .expo-stamp-moment,.expo-leaving .expo-deviation-slip{display:none}
.expo-root:has(.expo-trail-card:not([hidden])) :is(.expo-route-guide,.expo-touch,.expo-prompt,.expo-aim){visibility:hidden}
@media(max-width:660px){.expo-route-guide{top:max(51px,calc(env(safe-area-inset-top) + 43px));left:8px;max-width:min(76vw,300px)}.expo-trail-card{padding:12px;max-height:calc(100% - 80px)}.expo-trail-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.expo-root .expo-caption:not(:empty){top:auto;bottom:calc(38% + 12px);right:8px;max-width:79%}.expo-stamp-moment{top:33%}.expo-deviation-slip{bottom:30%}}
@media(max-height:480px){.expo-route-guide{top:4px;max-width:32vw}.expo-trail-task{padding:3px 6px}.expo-trail-open{padding:3px 6px}.expo-trail-card{max-height:92%}}
`;
