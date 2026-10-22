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
