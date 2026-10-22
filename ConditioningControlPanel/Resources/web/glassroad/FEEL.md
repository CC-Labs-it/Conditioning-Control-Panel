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
Polish 2 (owner: "the steering is too strong"): grip yaw = `steer * rate / (1 + v / yawSpeedRefMps) * calm(v) * userMul`
(`sim/steer.js`). Full-lock grip yaw, before / after: 8 m/s 104 / 104 deg/s, 20 m/s 76 / 51 (-33%), 30 m/s (top)
62.5 / 40 (-36%), 39 m/s (boosted top) 54 / 27 (-50%). Drift yaw keeps the uncalmed base law, so a drift is the strong
turn. -50% already at 30 m/s cost 5.8% lap time on Threshold (the 280 m banked sweeper at 1.2 deg/m); -36% costs 2.8%.
Polish 4 (owner: "it takes way too little to steer completely"): about another third off at cruise and top, so a held
key carves a wide arc and the tight bends ask for a hold, a lift or a drift. Full-lock grip yaw before / after: 20 m/s
51.2 / 35.1 deg/s (-31%), 30 m/s 40.0 / 28.1 (-30%), 39 m/s 26.9 / 18.8 (-30%); 8 m/s unchanged (104). Full lock at
top is now a 61 m arc. Threshold at top speed (30 m/s): the 55 to 58 m bends need 30 to 31 deg/s (a small lift or a
drift), the 38 to 49 m bends and the helix (r about 36 m on the road plane, 47 deg/s) need a drift (45 to 91 deg/s at
30 m/s) or grip at 22 to 26 m/s.
- `steer.slewCentreS` 0.09: stick slew time near centre (0.06 before polish 2: a tap is a small correction now).
- `steer.slewFullS` 0.18: stick slew time at full lock (0.14 before).
- `steer.yawRateDegps` 137.5: yaw rate at full lock and zero speed (2.4 rad/s), before the calm.
- `steer.yawSpeedRefMps` 25: speed that halves the base yaw rate.
- `steer.calmFromMps` 8: below this the base law is untouched (nimble when slow).
- `steer.cruiseMps` 20: cruise point of the calm ...
- `steer.cruiseYawMul` 0.46: ... grip yaw multiplier there (smoothstep from 1 at calmFromMps; 0.67 before polish 4).
- `steer.topCalmMps` 30: top-speed point of the calm ...
- `steer.topYawMul` 0.45: ... grip yaw multiplier there (0.64 before polish 4).
- `steer.boostCalmMps` 39: boosted-top point of the calm (speed.topMps x speed.maxPct) ...
- `steer.boostYawMul` 0.35: ... grip yaw multiplier there and above (0.5 before polish 4).
- `steer.userMul` 1: steering sensitivity, the player's Options `steerSens` (0.6 to 1.4 in 0.1 steps). The integrator
  wires it with `feel.set('steer.userMul', opts.steerSens)`. Scales grip yaw only (not drift yaw); bots hold it at 1
  (`sim/racers.js` overlay), and the bots' corner planning reads the same calmed law.
- `steer.slipCostGripPerDegS` 0.009: engineV fraction lost per degree of slip per second in a grip turn.
- `steer.slipCostDriftPerDegS` 0.0035: the same while drifting.

## input (human steering response, `input/steerCurve.js`; bots never pass here)
Each source tags its steer (`steerKind` key, stick or touch) and the action map shapes it before the merge. Pass the
feel to `createActionMap(sources, { items: true, feel })` for live edits; without it the same numbers are the defaults.
Polish 4: progressive. A held key reaches full input at 0.40 s (0.30 before) on an ease-in; the kart's slewed steer
passes 50% at 0.33 s (0.20 before), 90% at 0.60 s (0.47). Sticks and touch use a power curve: half stick is a quarter
of the lock (0.41 before).
- `input.keyStart` 0.12: a key press starts at this share of full lock (0.35 before) ...
- `input.keyRampS` 0.4: ... and builds to full over this long held (a release or a change of side starts over; 0.3 before) ...
- `input.keyRampPow` 1.6: ... on this ease-in, `start + (1 - start) t^pow` (1 = linear): a tap stays a small correction.
- `input.stickPow` 2: gamepad stick curve `sign(x) |x|^p`: gentle near centre, full at the edge (was expo 0.35).
- `input.touchPow` 1.8: the same for the touch steer drag (was expo 0.2).

## offroad
- `offroad.hazeThrustLoss` 0.35: thrust lost on `haze` surface; skipped during any boost.

## wall (railed edges)
- `wall.normalKeep` 0.4: share of the into-wall speed bounced back out; tangential speed kept; drift and boost cancelled.
- `wall.engineLoss` 0.12: engineV fraction lost per wall hit.
- `wall.cueFullDvMps` 12: delta-v that plays the wall cue at full volume (added).
- `wall.shakeFrac` 0.5: share of cueFullDvMps that shakes the camera (added).
- `wall.topM` 1.5: an airborne kart this far over the road plane, or already past the edge, clears a rail (the tube is 0.9 m; polish 4 drop shortcut).

## route (polish 4 drop shortcut, `sim/routes.js`; the catch-zone mesh in `track/mesh.js` repeats cleanLenM and cleanLatM)
- `route.catchPct` 0.25: a route landing keeps at most this share of top speed ("soft glass").
- `route.catchHoldS` 2.5: thrust stays capped there this long.
- `route.cleanLenM` 24: a clean landing is within this length of road around the pad.
- `route.cleanLatM` 3.5: and this far either side of the pad centre.
- `route.cleanDeg` 25: with the nose this close to the road heading.
- `route.cleanAmount` 0.15: the clean landing boost (share of top over top), paid when the catch ends.
- `route.cleanHoldS` 0.6: how long that boost holds.

## hop
Polish 2: the hop was floaty (0.69 m apex, 0.45 s air, a landing thud on every drift); now 0.40 m, 0.33 s, lands soft.
- `hop.upMps` 4.8: hop up speed (5.5 before).
- `hop.gravityHopMps2` 26: gravity right after a hop (18 before: the hang at the apex).
- `hop.gravityMps2` 28: rest gravity (every other airborne moment; unchanged so jump arcs stay the same).
- `hop.gravityEaseS` 0.25: time to ease from hop gravity to rest gravity (0.3 before).

## drift
Polish 4: a hop OR a brake press while steering at speed (throttle held: the power slide) starts the slide; the first
tier comes at 0.5 s (0.4 s steering into it), and every drift that charged 0.4 s pays at least the mini-turbo on
release. Drift yaw at 30 m/s: 45 to 91 deg/s (60 to 100 before), so the slide is the strong turn and still holdable.
Events: `drift-start { dir, via: 'hop' | 'brake' }`, `drift-tier { tier, dir }`, `drift-release { tier }`,
`drift-boost { tier, dir }` (every paid release, tier 0 = mini-turbo), `drift-cancel { reason }`; a bot's arrive as
`racer-event { racer, name, p }`. Skid flag: `skidOf(st)` in `sim/drift.js` (drifting and grounded).
- `drift.minSpeedMps` 8: a hop or a brake starts a drift only above this speed; dropping under it cancels.
- `drift.tightMin` 0.5: drift yaw share with the stick away from the turn (0.6 before polish 4).
- `drift.tightMax` 1.0: drift yaw share with the stick into the turn.
- `drift.yawMul` 1.45: max drift yaw as a multiple of the uncalmed base yaw (1.6 before polish 4).
- `drift.cancelSlipDeg` 6: slip under this angle ...
- `drift.cancelSlipS` 0.2: ... for this long ends the drift: one that charged `drift.miniMinS` pays like a release
  (the slide straightened out by itself), a shorter one cancels (also release, low speed, wall).
- `drift.tierS` [0.5, 1.2, 2.1]: charge seconds for tiers 1, 2, 3 ([0.6, 1.4, 2.4] before polish 4).
- `drift.softInMul` 1.25: charge rate while steering into the drift (1.4 before polish 4).
- `drift.softOutMul` 0.7: charge rate while counter-steering.
- `drift.miniMinS` 0.4: charge that pays the mini-turbo on release with no tier reached (boost.miniAmount).
- `drift.brakeMin` 0.6: brake share that starts a power slide ...
- `drift.brakeHoldMin` 0.3: ... and the share that keeps holding it (letting off under this releases and pays).
- `drift.brakeEntryS` 0.15: the brake press counts as fresh this long: steer within it to slide (a held brake brakes).
- `drift.brakeThrottleMin` 0.5: a power slide needs the throttle held; brake with no throttle is a real brake.
- `drift.brakeDragMps2` 3: engineV lost per second in a power slide with the throttle held (the brake does not brake).
- `drift.straightenMax` 0.12: a power slide with the stick under this share ...
- `drift.straightenS` 0.2: ... for this long counts as straightened: it releases and pays.
- `drift.beatBonusOn` false: beat release bonus, off in the slice.
- `drift.beatBonus` 0.25: extra boost for a release on the beat (music tracks only).
- `drift.beatWindowS` 0.08: how close to the beat counts.

## boost
- `boost.tierAmount` [0.12, 0.18, 0.24]: drift boost size per tier, fraction of top speed.
- `boost.tierHoldS` [0.5, 0.9, 1.4]: drift boost hold per tier.
- `boost.miniAmount` 0.06: the mini-turbo, paid by a drift released with no tier (kind 'drift', tier 0).
- `boost.miniHoldS` 0.3: the mini-turbo hold.
- `boost.riseS` 0.1: envelope rise.
- `boost.tailTcS` 0.5: exponential tail time constant after the hold. Off-road loss and turn drag are skipped while any boost runs.
- `boost.launchTier` 2: tier a launch boost pays.
- `boost.launchWindowS` 0.15: a fresh throttle press in the last this-many seconds of the countdown launches.

## pad
- `pad.amount` 0.2: boost pad pins the engine at top + this share.
- `pad.holdS` 1.0: pad hold, same envelope as drift boosts.

## trick (polish 4, `sim/trick.js`)
A hop pressed in a window around a lip pays a trick boost on landing: in the air up to lateS after a lip or crest
take-off, on a pad up to padLateS after entering it, or on the ground up to earlyS before a jump, crest or pad ahead
(the early side needs kart.js to pass the track to `hop()`). Outside the window a press is a plain hop. Events:
`trick { grade: 'good' | 'great', airS, at: 'lip' | 'crest' | 'pad' }` when it registers (airS = air so far) and
`trick-land { grade, airS, amount, holdS }` on touch-down; boost-start kind 'trick'. A drop or rescue loses it.
- `trick.earlyS` 0.12: a ground press this long before the lip still counts (at the kart's speed).
- `trick.lateS` 0.2: an air press this long after a lip or crest take-off still counts.
- `trick.greatS` 0.06: within this of the lip moment the grade is 'great', else 'good'.
- `trick.padLateS` 0.25: a hop this long after entering a pad is a pad trick.
- `trick.crestDeg` 8: a road pitch drop this big just ahead reads as a crest lip (early side).
- `trick.amount` [0.12, 0.18]: trick boost size for good, great (fraction of top speed).
- `trick.holdS` [0.35, 0.55]: trick boost hold for good, great ...
- `trick.holdPerAirS` 0.5: ... plus this per second of air (bigger air pays longer) ...
- `trick.holdMaxS` 1.3: ... capped here.
- `trick.stackAdd` 0.05: over a running boost the trick pays at least that amount plus this, and the longer hold.
- `trick.botChance` 0.35: chance a bot tricks at a lip (read by sim/ai.js once it presses hop after a lip take-off).

## air (landing tiers by airtime)
- `air.lightS` 0.35: under this, nothing (0.25 before; a plain hop, 0.33 s, now lands soft).
- `air.mediumS` 0.6: light tier up to here (squash, dust).
- `air.heavyS` 1.0: medium tier up to here (squash, bounce, cue); above is heavy (squash, bounce, shake, loud cue).
- `air.squash` [0.04, 0.08, 0.12]: squash per tier light, medium, heavy.
- `air.bounceM` [0, 0.1, 0.2]: bounce height per tier ([0, 0.15, 0.3] before: the jump landing was a second hop).
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
- `camera.followHopTcS` 0.6: both follow constants (and the heading follow) during a low-speed hop.
- `camera.yawFollowTcS` 0.16: heading follow time constant (was followHTcS 0.08): a steering correction turns the kart on screen first and the world swings after, never exaggerating the steering. The horizon stays level (no roll).
- `camera.lowHopMps` 8: a hop under this speed is a low-speed hop (added).
- `camera.yawCapDegps` 55: camera yaw rate cap.
- `camera.yawCapFarDegps` 160: cap when the yaw gap exceeds yawFarGapDeg.
- `camera.yawFarGapDeg` 70: gap that switches to the far cap.
- `camera.driftBiasDeg` 8: yaw bias into a drift (12 before polish 2: the lateral swing was too big).
- `camera.driftBiasOutDeg` 11: bias on the outside line (16 before).
- `camera.driftBiasEaseS` 0.3: bias ease time (0.15 before). Camera yaw is frozen during a stall.

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
- `juice.sparkColours` blue, orange, pink: drift spark colour per tier (HUD pips; sparks, skid marks and ribbons use the same, render/atlas.js).
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
- `items.freezeSteerMul` 0.55: Freeze steering share (0.4 before polish 2; raised so Freeze on the calmed steer law bites about as hard at top speed as before).
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
- `ai.steerGain` 3: stick = clamp(atan2(right, ahead) x gain).
- `ai.lineSpreadM` 2.5: each bot keeps its own lateral line within +- this.
- `ai.lineWanderM` 1.0: slow wander around that line.
- `ai.lineWanderS` 7: wander period.
- `ai.cornerCutM` 2.0: lateral pull toward the inside of a bend at full curvature.
- `ai.openEdgeMarginM` 3.5: bots keep at least this inside an open edge.
- `ai.routeChance` 0.2: chance per lap a tier 2 bot tries a drop shortcut.
- `ai.routeLowMul` 0: tier 1 bots roll routeChance times this (0: never).
- `ai.routeLeadM` 70: a bot starts lining up for the open edge this far before it.
- `ai.routeAimAheadM` 25: it aims at a point this far past the landing pad (a straight line off the edge).
- `ai.routeHopEdgeM` 2.2: it hops the kerb once this close to the open edge.
- `ai.routeHopLeadM` 3: it hops this far before the opening, once the line has it at the kerb.
- `ai.routeLineM` 2.8: the line runs this far to the open side of the landing pad.
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
- `ai.cornerMargin` 0.9: share of the kart's calmed full-lock grip yaw a bot plans a bend with (was a feel gap in sim/ai.js).

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
