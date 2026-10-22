# Glassroad feel sheet

Every number here is a starting point for the owner's hands (plan section 6). Code reads them only
through `core/feel.js` (`feel.get('group.key')`, angles via `feel.rad(...)`); `?feel=1` opens the live
sheet and an edit applies next tick. Units sit in the key name. "(added)" = not in plan 6, a slice
default so the code has no hidden number.

## speed
- `speed.topMps` 30: class A cruise top speed (B 36 and C 42 come later).
- `speed.reverseMps` 6: top reverse speed.
- `speed.minMps` 3: crawl floor while throttle is held (plan "min").
- `speed.maxPct` 1.3: ceiling of speedPct (forward speed / topMps) with every boost stacked (added).

## engine
- `engine.bandGains` [0.9, 0.8, 0.55, 0.35, 0.25]: five equal bands over 0..top; engineV gains `gain * top` per second in its band. The last two bands are the weight.
- `engine.chaseTcS` 0.15: velocity chases engineV, `v += (engineV - v) * dt / 0.15`.
- `engine.brakeMps2` 22: engineV loss per second under full brake (added).
- `engine.coastMps2` 4: engineV loss per second with no throttle (added).

## steer
- `steer.slewCentreS` 0.06: stick slew time near centre.
- `steer.slewFullS` 0.14: stick slew time at full lock.
- `steer.yawRateDegps` 137.5: yaw rate at full lock and zero speed (2.4 rad/s); `yaw = steer * rate / (1 + v / yawSpeedRefMps)`.
- `steer.yawSpeedRefMps` 25: speed that halves the yaw rate.
- `steer.slipCostGripPerDegS` 0.009: engineV fraction lost per degree of slip per second in a grip turn.
- `steer.slipCostDriftPerDegS` 0.0035: the same while drifting.

## offroad
- `offroad.hazeThrustLoss` 0.35: thrust lost on `haze` surface; skipped during any boost.

## wall (railed edges)
- `wall.normalKeep` 0.4: share of the into-wall speed bounced back out; tangential speed kept; drift and boost cancelled.
- `wall.engineLoss` 0.12: engineV fraction lost per wall hit.
- `wall.cueFullDvMps` 12: delta-v that plays the wall cue at full volume (added).
- `wall.shakeFrac` 0.5: share of cueFullDvMps that shakes the camera (added).

## hop
- `hop.upMps` 5.5: hop up speed.
- `hop.gravityHopMps2` 18: gravity right after a hop.
- `hop.gravityMps2` 28: rest gravity (every other airborne moment).
- `hop.gravityEaseS` 0.3: time to ease from hop gravity to rest gravity.

## drift
- `drift.minSpeedMps` 8: a hop starts a drift only above this speed; dropping under it cancels.
- `drift.tightMin` 0.6: drift yaw share with the stick away from the turn.
- `drift.tightMax` 1.0: drift yaw share with the stick into the turn.
- `drift.yawMul` 1.6: max drift yaw as a multiple of grip yaw.
- `drift.cancelSlipDeg` 6: slip under this angle ...
- `drift.cancelSlipS` 0.2: ... for this long cancels the drift (also release, low speed, wall).
- `drift.tierS` [0.6, 1.4, 2.4]: charge seconds for tiers 1, 2, 3 (hold time is primary).
- `drift.softInMul` 1.4: charge rate while steering into the drift.
- `drift.softOutMul` 0.7: charge rate while counter-steering.
- `drift.beatBonusOn` false: beat release bonus, off in the slice.
- `drift.beatBonus` 0.25: extra boost for a release on the beat (music tracks only).
- `drift.beatWindowS` 0.08: how close to the beat counts.

## boost
- `boost.tierAmount` [0.12, 0.18, 0.24]: drift boost size per tier, fraction of top speed.
- `boost.tierHoldS` [0.5, 0.9, 1.4]: drift boost hold per tier.
- `boost.riseS` 0.1: envelope rise.
- `boost.tailTcS` 0.5: exponential tail time constant after the hold. Off-road loss and turn drag are skipped while any boost runs.
- `boost.launchTier` 2: tier a launch boost pays.
- `boost.launchWindowS` 0.15: a fresh throttle press in the last this-many seconds of the countdown launches.

## pad
- `pad.amount` 0.2: boost pad pins the engine at top + this share.
- `pad.holdS` 1.0: pad hold, same envelope as drift boosts.

## air (landing tiers by airtime)
- `air.lightS` 0.25: under this, nothing.
- `air.mediumS` 0.6: light tier up to here (squash, dust).
- `air.heavyS` 1.0: medium tier up to here (squash, bounce, cue); above is heavy (squash, bounce, shake, loud cue).
- `air.squash` [0.04, 0.08, 0.12]: squash per tier light, medium, heavy.
- `air.bounceM` [0, 0.15, 0.3]: bounce height per tier.
- `air.keepDriftOnLand` true: landing with drift held keeps drift and charge.

## drop
- `drop.edgeGraceM` 1.2: distance past the edge before the drop timer runs (the sim reads this, not track limits).
- `drop.edgeGraceS` 0.8: seconds with no ground past that distance before a drop (the sim reads this, not track limits). `limits.fallY` drops at once.

## respawn
- `respawn.aheadNodes` 4: the rescued kart faces this many nodes past the latched one.
- `respawn.pullS` 0.5: tendril pull.
- `respawn.hoverS` 0.6: hover.
- `respawn.releaseS` 0.5: release.
- `respawn.releasePct` 0.6: release speed, share of top speed.
- `respawn.graceS` 1.5: grace after release (no second drop, no wall cancel).
- `respawn.pullWhite` 0.6: how far the pull fades toward white (Gentle rules apply).

## camera
- `camera.eyeUpM` 2.2: eye height above the kart.
- `camera.eyeBackM` 6.5: eye distance behind the kart.
- `camera.lookAheadM` 8: look point ahead of the kart.
- `camera.lookUpM` 0.6: look point above kart height.
- `camera.followHTcS` 0.08: horizontal follow time constant.
- `camera.followVTcS` 0.25: vertical follow time constant.
- `camera.followHopTcS` 0.6: both follow constants during a low-speed hop.
- `camera.lowHopMps` 8: a hop under this speed is a low-speed hop (added).
- `camera.yawCapDegps` 55: camera yaw rate cap.
- `camera.yawCapFarDegps` 160: cap when the yaw gap exceeds yawFarGapDeg.
- `camera.yawFarGapDeg` 70: gap that switches to the far cap.
- `camera.driftBiasDeg` 12: yaw bias into a drift.
- `camera.driftBiasOutDeg` 16: bias on the outside line.
- `camera.driftBiasEaseS` 0.15: bias ease time. Camera yaw is frozen during a stall.

## fov
- `fov.baseVDeg` 60: vertical fov at 16:9, through the lifted viewport law for other aspects.
- `fov.boostKickDeg` 8: boost kick (none under Gentle).
- `fov.kickInS` 0.12: kick in time.
- `fov.kickOutS` 0.45: kick out time.

## shake (off under Gentle)
- `shake.hitAmpM` 0.12: vertical hit amplitude.
- `shake.boostAmpM` 0.35: forward-axis boost amplitude.
- `shake.periodS` 0.1: one parabola `a*t - b*t^2` lasts this long (added).
- `shake.repeatMul` 0.85: each repeat is this much of the last.
- `shake.stopBelowM` 0.05: repeats stop under this amplitude.

## stall (item hit, unused until B1)
- `stall.durationS` 1.2: stall length; steering cleared, no camera yaw.
- `stall.yawSteps` 3: fixed yaw steps during a stall.
- `stall.thrustMul` 0.4: thrust share while stalled.

## juice (the 10 beats, plan 6)
- `juice.countdownPulses` 3: countdown pulses on the beat clock.
- `juice.countdownBloomStep` 0.1: bloom added per pulse.
- `juice.goReleaseS` 0.6: go pulse bloom release.
- `juice.launchCamSettleS` 0.5: camera settle after a launch boost.
- `juice.sparkColours` pink, white, gold: drift spark colour per tier.
- `juice.sparkDieS` 0.3: sparks burst and die on release.
- `juice.driftPitchSemis` 7: drift tone rises a fifth over three tiers.
- `juice.boostBandMul` 1.5: band speed multiplier while boosting.
- `juice.landGoneS` 0.25: landing squash and dust are gone in this.
- `juice.edgeGlowFadeS` 0.3: edge glow fade on return.
- `juice.duckLiftS` 0.5: audio duck lift after a rescue.
- `juice.paletteFadeS` 2.0: section palette crossfade.
- `juice.finishSwellS` 1.5: finish swell length.
- `juice.finishOrbitDeg` 100: camera orbit at the finish over finishSwellS (halved under Gentle) (added).
- `juice.riseS` 0.08: generic fast IN for fx envelopes (added).
- `juice.tailTcs` 3: fx tails after a boost last this many boost.tailTcS (added).
- `juice.launchPushM` 1.2: forward camera push on a launch boost (added).
- `juice.padBandPeak` 0.6: band flash on a pad, inside the photosafe budget (added).
- `juice.releasePulse` 0.25: bloom pulse on rescue release (added).
- `juice.edgeGlowRefused` 0.3: edge glow level when photosafe refuses the full glow (added).
- `juice.rumbleByTier` [0.35, 0.65, 1]: pad rumble per drift or landing tier (added).
- `juice.rumbleMs` 120: one rumble pulse; re-sent while the rumble holds, at its current level (added).

## clock
- `clock.hz` 60: fixed sim rate.
- `clock.maxSubsteps` 2: most sim steps per frame; extra time is dropped.
- `clock.bpm` 96: beat clock tempo for the slice (no music yet) (added).

## photosafe
- `photosafe.windowS` 1: rolling window for luminance transitions.
- `photosafe.maxTransitions` 3: transitions allowed per window.
- `photosafe.redMax` 1: red swings allowed per window, inside maxTransitions (added).
- `photosafe.patternMaxFrac` 0.25: high-contrast band or spiral pattern screen share cap.

## gentle (set at boot from host `init.gentle` or `?gentle=1`, not a tuning knob)
- `gentle.on` false: Gentle visuals on.
- `gentle.fadeMs` 600: fade length under Gentle.
- `gentle.riseMs` 300: rise floor.
- `gentle.contrastMul` 0.5: band and spiral contrast share.
- `gentle.scrollMul` 0.5: band and spiral scroll share.

## items (B1 effect bubbles, design board section 03; read by sim/items.js, sim/itemTable.js, fx/effects.js)
Weights are `[front, middle, back]` relative draw weights per place bucket; 0 = never in that bucket.
- `items.boxRadiusM` 1.8: a kart within this (along and across) of a live item box draws from it.
- `items.pickHeightM` 2.5: a kart higher than this above the road (a jump arc) passes over boxes and bubbles.
- `items.boxRespawnS` 3: a taken box comes back after this.
- `items.rollS` 1.0: the slot spins this long before the drawn item shows and can be used.
- `items.bubbleRadiusM` 1.3: pickup-bubble pop distance.
- `items.bubbleRespawnS` 8: a popped pickup bubble comes back after this.
- `items.bubbleAmount` 0.05: boost size of a pickup bubble, share of top speed.
- `items.bubbleHoldS` 0.35: boost hold of a pickup bubble.
- `items.frontGapM` 25: the leader, and anyone within this of the leader's progress, draws from the front bucket.
- `items.backGapM` 120: this far or more behind the leader draws from the back bucket; between is middle.
- `items.flashWeights` [1, 3, 1]: Flash draw weights.
- `items.spiralWeights` [0, 3, 1]: Spiral draw weights.
- `items.brainDrainWeights` [0, 3, 1]: Brain Drain draw weights.
- `items.freezeWeights` [1, 3, 0]: Freeze draw weights.
- `items.blackoutWeights` [0, 0, 3]: Blackout draw weights (back only).
- `items.pinkWeights` [0, 1, 2]: Pink draw weights.
- `items.meltWeights` [4, 1, 0]: Melt draw weights (front trap).
- `items.shatterWeights` [0, 0, 0.6]: Shatter draw weights (rare, back only).
- `items.surgeWeights` [1, 2, 3]: Surge draw weights.
- `items.glassWeights` [4, 1, 0]: Glass draw weights (front shield).
- `items.echoWeights` [0, 0, 2]: Echo draw weights (back).
- `items.goldenWeights` [0, 0, 0.6]: Golden draw weights (rare, back only).
- `items.flashS` 1.2: Flash: a picture over flashCover of the screen, then it fades.
- `items.spiralS` 2.0: Spiral over the middle of the view, steering drifts with it.
- `items.brainDrainS` 2.5: Brain Drain: edges blur and go grey, the centre stays sharp.
- `items.freezeS` 2.0: Freeze: steering answers at freezeSteerMul, frost on the edges.
- `items.blackoutS` 2.0: Blackout: the view closes to a circle; rails and road lines keep glowing.
- `items.pinkS` 3.0: Pink: pink wash, low contrast.
- `items.meltS` 2.0: Melt hit: the whole view blurs and runs downward, the kart slides like on ice.
- `items.shatterS` 1.5: Shatter spin-out length (the one real crash).
- `items.surgeS` 1.2: Surge boost hold.
- `items.goldenS` 4.0: Golden: nothing sticks, bumps shove harder.
- `items.echoS` 6.0: Echo copies last this long (or until all are used).
- `items.glassMaxS` 20: a Glass bubble that soaks nothing pops by itself after this.
- `items.flashCover` 0.4: share of the screen a Flash picture covers.
- `items.spiralSteerAmp` 0.35: Spiral steering drift, stick share added at the peak.
- `items.spiralSteerHz` 0.4: Spiral steering drift rate (one slow sway per 1 / Hz s).
- `items.freezeSteerMul` 0.4: Freeze steering share.
- `items.meltGripMul` 6: Melt multiplies the grip bleed time (kart.gripTcS) by this: ice.
- `items.meltSlideMul` 2.5: Melt multiplies the drift slide time (drift.slideTcS) by this.
- `items.shatterSpeedKeep` 0.3: Shatter cuts engineV to this share of what it was.
- `items.shatterSpinTurns` 2: Shatter visual spin turns over shatterS (render only; the sim side is the stall).
- `items.shatterEdgeSafeM` 40: Shatter never fires, lands or is laid within this of an open segment.
- `items.pinkRangeM` 40: Pink hits every kart up to this far ahead of the user.
- `items.surgeAmount` [0.1, 0.16, 0.24]: Surge boost size by the user's bucket [front, middle, back].
- `items.goldenShoveMul` 2: a Golden kart's bumps shove this much harder.
- `items.echoCopies` 3: Echo copies; each decoyed throw uses one.
- `items.echoDecoyChance` 0.75: chance a throw or lob at an Echo kart goes for a copy.
- `items.throwExtraMps` 22: a throw flies at the user's speed plus this, along the driveline.
- `items.throwLifeS` 5: a throw that reaches nobody pops after this.
- `items.throwRangeM` 150: a throw locks on the kart ahead only within this; past it, it flies straight.
- `items.homeTcS` 0.25: a throw eases across to its target's lateral with this time constant.
- `items.hitRadiusM` 1.6: a throw or lob hits within this (along and across).
- `items.throwHeightM` 1.1: a throw flies this high above the road.
- `items.throwArcM` 0.5: extra height of the first arc out of the hand.
- `items.lobFlightS` 1.6: a lob reaches the leader in this, wherever they are.
- `items.lobHeightM` 14: lob arc peak height.
- `items.dropBehindM` 3.5: a dropped trap lands this far behind the user.
- `items.trapLifeS` 45: a trap no one drives over pops after this.
- `items.trapArmS` 0.6: a trap cannot hit its own layer for this long.
- `items.trapRadiusM` 1.5: trap hit distance.
- `items.meltRadiusM` 2.4: Melt puddle hit distance.
- `items.maxTraps` 12: the oldest trap pops when one more is laid.
- `items.mercyS` 2: after a screen effect ends, no screen effect lands on that kart for this.
- `items.viewBudgetS` 3: at most this much blocked view per viewWindowS (a hit is trimmed to fit).
- `items.viewWindowS` 10: the rolling window for viewBudgetS.
- `items.mildBlockMul` 0.5: Pink and Freeze count this share of their time against the budget.
- `items.minHitS` 0.4: a hit trimmed under this lands as mercy instead.
- `items.effectInS` 0.2: screen effect IN (Gentle raises it to the rise floor).
- `items.effectOutS` 0.4: screen effect OUT (Gentle raises it to the fade length).
- `items.photosafeInS` 0.5: IN used when the flash limiter refuses the step (the effect still lands, softer).
- `items.gentleFlashScale` 0.35: under Gentle a Flash card is drawn at this linear scale (sent as `params.scale`).
- `items.gentleSpiralDim` 0.35: under Gentle a Spiral is a soft dim this deep.
- `items.gentleBlurMul` 0.5: under Gentle every blur is this share.
- `items.popShowS` 0.6: a pop stays in the item view this long for its burst.

## ai (B1 bots, sim/ai.js and sim/racers.js)
- `ai.playerSlot` 5: grid slot of the local player (0 = front); bots fill the rest.
- `ai.difficulty` 1: index into difficultyMul (0 easy, 1 normal, 2 hard).
- `ai.difficultyMul` [0.92, 1.0, 1.06]: bot top speed share per difficulty.
- `ai.paceMul` [0.9, 0.92, 0.94, 0.96, 0.97, 0.98, 1.0]: per-bot top speed share (bot 1..7), times difficultyMul.
- `ai.lookAheadS` 0.55: steering aim point this many seconds ahead at the bot's speed.
- `ai.lookAheadMinM` 8: the aim point is never nearer than this.
- `ai.steerGain` 2.2: stick = clamp(atan2(right, ahead) x gain).
- `ai.lineSpreadM` 2.5: each bot keeps its own lateral line within +- this.
- `ai.lineWanderM` 1.0: slow wander around that line.
- `ai.lineWanderS` 7: wander period.
- `ai.cornerCutM` 2.0: lateral pull toward the inside of a bend at full curvature.
- `ai.openEdgeMarginM` 3.5: bots keep at least this inside an open edge.
- `ai.driftCurveDegPerM` 0.5: heading change per metre over the look-ahead that starts a hop drift.
- `ai.driftMinS` 0.9: a bot drift is held at least this (a tier 1 release).
- `ai.rubberBehindM` 60: a bot this far behind the player runs rubberUpMul.
- `ai.rubberAheadM` 40: a bot this far ahead of the player runs rubberDownMul (eased in between).
- `ai.rubberUpMul` 1.08: top speed share when far behind.
- `ai.rubberDownMul` 0.94: top speed share when far ahead.
- `ai.launchChance` 0.35: chance a bot gets the launch boost at go.
- `ai.avoidM` 10: look this far ahead for karts and traps to steer around.
- `ai.avoidShiftM` 2.5: lateral shift used to go around one.
- `ai.blindSteerNoise` 0.35: steering noise while a bot is under a view effect (mods.blind).
- `ai.blindThrottle` 0.75: throttle share while blind.
- `ai.itemUseChancePerS` 0.3: chance per second a bot uses a ready item (bots use items less often than people).
- `ai.itemHoldMinS` 1.5: a bot holds a drawn item at least this.
- `ai.itemCooldownS` 4: no bot item use for this after one.
- `ai.itemDropChance` 0.5: chance a front bot drops a throw behind as a trap instead.
- `ai.itemRangeM` 60: a bot throws only when its target is within this.
- `ai.resultsWaitS` 10: after the player finishes, results wait at most this for the bots (the rest are placed by distance).
- `ai.audibleKarts` 3: the nearest this many bot engines are heard.

## contact (B1 kart contact and slipstream, sim/contact.js)
- `contact.radiusM` 0.8: kart contact radius (two karts touch under 2x this).
- `contact.maxHeightM` 1.5: karts further apart than this along the road normal never touch.
- `contact.pushMps` 3.5: least sideways speed a touch gives.
- `contact.pushPerDv` 0.5: extra sideways speed per m/s of closing speed.
- `contact.maxPushMps` 7: cap on the sideways speed of one bump.
- `contact.speedRefMps` 12: weight = 1 + speed / this (the faster kart moves less).
- `contact.boostShoveMul` 1.6: a boosting kart weighs this much more.
- `contact.stalledShoveMul` 1.6: a stalled kart weighs this much less (divides).
- `contact.engineLoss` 0.03: engineV share both karts lose on a bump.
- `contact.ghostS` 1.5: after go a kart is a ghost (no contact) this long; a rescue's own grace does the same.
- `contact.cueMinDvMps` 1.5: a bump under this closing speed emits no bump event.
- `contact.cooldownS` 0.25: one bump event per pair per this.
- `contact.slipMinM` 2: the wake starts this far behind a kart.
- `contact.slipMaxM` 14: the wake ends this far behind it.
- `contact.slipWidthM` 1.6: the wake is this wide either side.
- `contact.slipMinPct` 0.6: both karts above this speedPct.
- `contact.slipChargeS` 0.8: time in the wake before the slingshot.
- `contact.slipAmount` 0.1: slingshot boost size, share of top speed.
- `contact.slipHoldS` 0.6: slingshot boost hold.
- `contact.slipCooldownS` 2.5: no second slingshot for this.
