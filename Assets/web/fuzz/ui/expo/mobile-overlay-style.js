// Keep the scene readable while reserving a separate bottom band for the visitor's thumbs.
export const MOBILE_OVERLAY_STYLE=`
.expo-visitor-messages{display:contents}
.expo-root[data-expo-touch] .expo-visitor-messages{display:flex;position:absolute;z-index:2;bottom:calc(env(safe-area-inset-bottom) + 104px);left:max(10px,env(safe-area-inset-left));right:max(10px,env(safe-area-inset-right));flex-direction:column;align-items:center;gap:4px;pointer-events:none}
.expo-root[data-expo-touch] .expo-visitor-messages :is(.expo-caption,.expo-narration){position:static;transform:none;margin:0;width:auto;max-width:min(100%,600px)}
.expo-root[data-expo-touch] .expo-narration{display:grid;justify-items:center}
.expo-root[data-expo-touch] .expo-narration p{padding:5px 10px;border-radius:5px;background:#102521ed;font:550 15px/1.32 system-ui;text-wrap:pretty}
.expo-root[data-expo-touch] .expo-full-line{position:absolute;width:1px;height:1px;overflow:hidden;clip-path:inset(50%);white-space:nowrap}
.expo-root[data-expo-touch] .expo-narration p[data-phrase]::after{content:attr(data-phrase);content:attr(data-phrase) / ""}
.expo-root[data-expo-touch] .expo-visitor-messages .expo-caption{max-width:min(100%,42ch);border:0;border-radius:5px;padding:5px 10px;background:#f4efde;color:#17382d;font:600 14px/1.3 system-ui;text-align:center;overflow-wrap:anywhere}
.expo-root[data-expo-touch] .expo-guide{font:500 13px/1.3 system-ui;background:#152f27e6;color:#fff9e8}
.expo-root[data-expo-touch] .expo-visitor-messages:has(:is(.expo-caption,.expo-narration p):not(:empty)) .expo-guide{display:none}
.expo-root[data-expo-touch]:has(.expo-trail-card:not([hidden])) .expo-visitor-messages{visibility:hidden}
body:has(#pause:not([hidden])) .expo-root[data-expo-touch] :is(.expo-route-guide,.expo-trail-card,.expo-touch,.expo-visitor-messages,.expo-aim,.expo-step-down,.expo-reading-card,.expo-shop-receipt){visibility:hidden}
#game-controls[data-expo-compact] > #dock-lock{display:none}
.expo-root[data-expo-touch] .expo-reading-card{top:calc(max(8px,env(safe-area-inset-top)) + 50px);max-height:calc(100% - 170px)}
.expo-root[data-expo-touch] .expo-reading-card button{min-height:44px}
@media (orientation:landscape) and (max-height:540px){
 .expo-root[data-expo-touch] .expo-visitor-messages{bottom:max(7px,env(safe-area-inset-bottom));left:calc(105px + env(safe-area-inset-left));right:calc(150px + env(safe-area-inset-right))}
 .expo-root[data-expo-touch] .expo-visitor-messages .expo-narration{width:auto}
 .expo-root[data-expo-touch] .expo-narration p{font-size:14px}
 .expo-root[data-expo-touch] .expo-reading-card{max-height:calc(100% - 76px)}
}
`;
