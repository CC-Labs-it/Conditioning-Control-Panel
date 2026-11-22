# Doorways and rendering performance

Measured on 22 November 2026. At the Grand Hall, the desktop build now submits 338,864 triangles per frame, down from 980,151. Gentle submits 319,514, down from 953,941. The worst frame when first panning in the Hall fell from 853 to 51 ms on desktop. A first walk into the Hall still had a 141 ms frame.

## Doorways

- Both sides of all 15 shared swinging thresholds were checked before and after the neighboring room streamed in. Each of the 30 views used 6,400 surface samples on both desktop and Gentle geometry. The separate Grand Hall scan used 19,071 samples per side. All views retained their original visible surfaces.
- The openings now have a fixed reveal between their two wall finishes. The hinge pins, rail clearance and response to a visitor in the swing path were checked through all 22 swinging doors on both profiles. The Grand Hall plaque remains on its baked floor shadow. The upper inspection passage retains its own painted wall and collision when the atrium unloads.

Six rendered views of the closed, partly open and distant Grand Hall and portrait rotunda doors matched the reference with all furnishings visible, pixel for pixel. The right column shows the amplified difference. [Doorway comparison](doorway-comparison.jpg)

## Room measurements

Each pair reads from the previous build to the revised build. GPU is median graphics time. The frame interval at the 95th percentile includes the CPU, graphics, and wait for the next display refresh. All times are milliseconds.

| Room | Desktop draws | Desktop GPU | Desktop frame p95 | Gentle draws | Gentle GPU | Gentle frame p95 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Atrium | 660 to 413 | 11 to 11.34 | 17 to 17 | 80 to 85 | 2.47 to 2.53 | 33.6 to 33.5 |
| Grand Hall | 670 to 291 | 10.63 to 8.18 | 17.2 to 17.4 | 536 to 185 | 4 to 2.12 | 33.8 to 33.5 |
| Applied Hoof Sciences | 66 to 71 | 6.78 to 7.27 | 17 to 17 | 40 to 51 | 2.07 to 2.22 | 33.7 to 33.6 |
| Gift shop | 276 to 221 | 8.36 to 8.18 | 17.5 to 17.2 | 120 to 121 | 1.96 to 2.17 | 33.8 to 33.9 |
| Human Habitat | 80 to 89 | 10.28 to 9.35 | 17 to 17 | 52 to 61 | 1.93 to 2.58 | 33.6 to 33.7 |
| Hall of the Twelve | 220 to 222 | 7.63 to 7.66 | 17.1 to 17.2 | 109 to 111 | 2.19 to 2.15 | 33.9 to 33.6 |
| Service wing | 66 to 67 | 5.67 to 5.71 | 16.9 to 16.9 | 38 to 48 | 1.31 to 1.38 | 33.6 to 33.5 |
| Contract corridor | 49 to 48 | 5.06 to 5.38 | 16.8 to 16.9 | 34 to 34 | 1.06 to 1.06 | 33.5 to 33.5 |
| Catwalk | 54 to 55 | 8.41 to 8.92 | 16.9 to 16.9 | 34 to 34 | 1.73 to 1.85 | 33.6 to 33.5 |
| Chronology | 127 to 131 | 5.41 to 5.54 | 17 to 16.9 | 97 to 101 | 1.6 to 1.57 | 33.7 to 33.6 |
| Forum | 93 to 93 | 5.06 to 5.14 | 17 to 16.9 | 93 to 93 | 1.46 to 1.41 | 33.5 to 33.6 |
| Commons | 105 to 105 | 5.81 to 5.81 | 16.9 to 16.9 | 110 to 110 | 1.58 to 1.59 | 33.7 to 33.5 |
| Aquarium | 89 to 89 | 7.01 to 6.99 | 17 to 17 | 89 to 89 | 1.37 to 1.37 | 33.6 to 33.6 |
| Dome | 38 to 42 | 4.21 to 4.26 | 17 to 16.9 | 34 to 38 | 1.47 to 1.43 | 33.7 to 33.9 |

## Walking through the Grand Hall doors

| Profile | Pass | Walk frame p95 | Longest walk frame | Pan frame p95 | Longest pan frame |
| --- | --- | ---: | ---: | ---: | ---: |
| Desktop | First | 26 to 26.6 | 113.1 to 140.5 | 18.5 to 17.6 | 852.6 to 51.4 |
| Desktop | Repeat | 19.3 to 18.8 | 145 to 40.7 | 17.2 to 17.1 | 45.4 to 23.3 |
| Gentle | First | 50.1 to 42.3 | 130.4 to 78.9 | 33.8 to 34.4 | 34.9 to 49 |
| Gentle | Repeat | 42.3 to 33.8 | 74.2 to 50 | 33.6 to 34 | 35.2 to 50.1 |

The workstation used Chrome 157.0.8103.80 on Intel Graphics 0x7D67, verified through WebGL. Desktop ran at 1920 by 1080. Gentle used a 393 by 852 touch viewport on the same GPU. The route walked through the animated leaves and panned for three seconds. The room table used matched fixed viewpoints for about 2.2 seconds each after detail textures were ready. GPU timer queries sampled every tenth frame.

Some room GPU times increased. The largest increase on Gentle was in the Human Habitat, from 1.93 to 2.58 ms. Its frame p95 moved from 33.6 to 33.7 ms. Across all 14 stationary views, final frame p95 ranged from 16.9 to 17.4 ms on desktop and 33.5 to 33.9 ms on Gentle.

## Loading and interaction

- On the first Hall pan, the Habitat chair light used to appear as a new light halfway through a frame. That made existing fern leaves, metalwork, glass and wool wait for new shaders. The light is now present when the visit starts, and room shaders are prepared for the same render target that displays the game. One diagnostic run measured the blocking pan at 857 ms before and 53 ms after. The table gives the separate uninstrumented route timings with only the frame probes active.
- Across four fresh Chrome processes per build and profile, median time to the first frame was 1.774 to 1.954 seconds on desktop and 1.695 to 1.784 seconds on Gentle. The first walk still includes room assembly, decoding and texture uploads. On the normal-power staff route, desktop cold frame p95 was 18.5 to 19.0 ms and warm p95 was 17.1 to 17.0 ms.
- Furnishings behind closed doors stay loaded for interaction and return to the draw list before the door opens. The room shells, glazing and lights stay in view. Fixed projector casings share draws. The Habitat security feed skips its hidden camera render. On 1,086 rays through the shipped rooms, picking returned the same target and distance. Placement checks accounted for all 208 batched pieces.
- On 3,740 collision queries, the revised broad-phase checks returned the same results and reduced the candidate sector checks on the doorway route from 293,282 to 134,591. Geometry decoding produced byte-identical results in four rooms. In the Hall of the Twelve, the median main-thread decode interruption fell from 17.7 to 11.3 ms, while total decode time went from 17.7 to 19.7 ms.
- After unplugging the heater, the chair lamp dimmed from 17 to 0.85 during the blackout and settled at 6.29. It returned to 17 after power was restored. Leaving and re-entering the room during emergency power preserved its lighting.
- Desktop tests used keyboard input. Touch and standard-mapped controller checks used synthetic input. The Gentle figures describe the phone layout running on the verified Intel GPU.
