
## Flash exit and stay completion checks

1. WPF `Services/Compositor/FlashLayer.cs:DrawGlitch`: enable clickable flashes, disable Shatter, select Glitch and pop. Check seven displaced strips with red ghost left and cyan right. Repeat with Motion Off/Reduced; Stop must remove a leaving image. Rows: `feat-flash-exit-stay`
2. WPF `Services/Compositor/FlashLayer.cs:Draw/DrawShards`: disable glow and show a transparent PNG. Transparent pixels must show black backing. Enable owned Shatter and pop: pieces retain black backing; rounded corners still clip the picture. Rows: `feat-flash-exit-stay`
3. WPF `Services/Progression/SkillTreeService.cs:RollLuckyBubble`: unlock Lucky Bubbles and pop until lucky. Check the reused 20x toast with the active mod label, including after the XP bucket is exhausted; the dry bucket pays zero. Suppress perk notices: no toast. Rows: `feat-flash-exit-stay`
4. WPF `Services/Flash/FlashService.cs:TriggerMultiplication`: enable Corruption, pop a flash with local images available, and check two children on its monitor. Stop immediately after a pop: queued children must stay closed. Rows: `feat-flash-exit-stay`
