# T&C "Go Outside" office pull-back

The end of the Go Outside ending: the live meadow keeps playing on an office monitor while the camera pulls back from it into an empty, after-hours cubicle. A fork of `../breakout-office/` (same grey partitions, desk, props, Cycles, AgX), folded into one build script and changed: the room is dark, the monitor (emissive `meadow.jpg`) is the key light, the desk lamp is off, the chair is pushed back and empty, and there are T&C touches (llama plush with a pink scarf, pink sticky note on the bezel, spiral mug, headphones, a framed GO OUTSIDE poster, a wall clock at 21:47). No window. No character.

`breakout-office/` is left untouched.

## Build and render
Requires Blender 5.2 and FFmpeg. From this directory (Git Bash):

```bash
B="/c/Program Files/Blender Foundation/Blender 5.2/blender.exe"
"$B" --background --factory-startup --python-exit-code 1 --python build_tc_office.py           # -> tc-office.blend (~10 s)
"$B" --background --factory-startup --python-exit-code 1 --python render_tc_office.py -- check  # fit + occlusion + office-track.json
"$B" --background --factory-startup --python-exit-code 1 --python render_tc_office.py -- contact --pct 50   # 5 preview frames
"$B" --background --factory-startup --python-exit-code 1 --python render_tc_office.py -- final  # 150 frames, 1280x720
OUT=../../../ConditioningControlPanel/Resources/web/fuzz/assets/outside
ffmpeg -y -framerate 30 -i final-frames/%04d.png -an -c:v libx264 -preset slow -crf 20 -pix_fmt yuv420p -movflags +faststart $OUT/office-pull.mp4
ffmpeg -y -i final-frames/0149.png -q:v 4 $OUT/office-hold.jpg
cp office-track.json $OUT/office-track.json
```

Contact sheet (frames 0, 25%, 50%, 75%, last):
```bash
cd contact-frames && ffmpeg -y -i 0000.png -i 0037.png -i 0074.png -i 0112.png -i 0149.png -filter_complex "color=c=0x111111:s=640x360[b];[0][1][2][3][4][b]xstack=inputs=6:layout=0_0|w0_0|w0+w1_0|0_h0|w0_h0|w0+w1_h0" -frames:v 1 -update 1 contact.png
```

`render_tc_office.py` also takes `still F [F...] --pct N --samples N` for single frames. It picks OPTIX, then CUDA, then CPU. Every mode first asserts that frame 1's screen corners land on the frame corners within 0.001 and ray-casts a 13x8 grid over the screen from the camera on all 150 frames (nothing may sit in front of the screen), then writes `office-track.json`.

## Scene
- Metres, Cycles, AgX (Medium High Contrast look where available), exposure +0.2, 128 samples with adaptive threshold .02, OpenImageDenoise. About 2-3 s a frame at 1280x720 on an RTX 5080 (OptiX).
- Screen: `SCREEN_MEADOW`, a 0.616 x 0.3465 m quad at y=.131, the front-most surface of the monitor (bezel and sticky note sit behind its plane). Emission = `../../../ConditioningControlPanel/Resources/web/fuzz/assets/outside/meadow.jpg`, strength 1 for camera rays (true colours on screen) and 3.2 for every other ray, so it is the key light and the light it throws matches the meadow.
- Camera: 30 fps, 150 frames. Frame 1 is the screen fit (lens 39 mm, 36 mm horizontal sensor). Distance eases in log space (even perceived zoom, quintic smootherstep), the arc to the off-axis wide view and the 39 -> 34 mm widen start a beat later; arrival at frame 135, hold to 150.
- Generated textures (poster meadow painting, mug spiral) are drawn with numpy in the build into `gen/` and packed into the blend.

## Contract outputs
In `ConditioningControlPanel/Resources/web/fuzz/assets/outside/`: `office-pull.mp4` (1280x720, 30 fps, h264 yuv420p, faststart, no audio, 5 s), `office-hold.jpg` (last frame), `office-track.json` (`{version:1, fps:30, size:[1280,720], frames:[[TL,TR,BR,BL] x 150]}` in video pixels, y down). Blends, frames and intermediate files stay local (see `.gitignore`).

## Asset credits
Geometry, lighting, motion, the poster painting and the mug spiral are authored for this project. Lettering uses Blender's built-in font. The screen image is `meadow.jpg`, the game's own render (SHA-256 at build time `1ef385f53751058ac6802d7ca6fb46f8efc2b6cfde299e69f735d16a9abfc623`).

The partition fabric uses the diffuse map from [Poly Haven Rough Linen](https://polyhaven.com/a/rough_linen), [CC0](https://polyhaven.com/license), the same file as Breakout's office: `assets/rough-linen/rough_linen_diffuse_2k.jpg`, SHA-256 `e71016e08dfebea1dd4e8a2a675e3b617a889a782af782f1abb795906f4db1b4`. It is gitignored here (the tracked copy is the LFS file in `../breakout-office/assets/rough-linen/`); the build reads this folder first, then that one. To fill it: `git lfs checkout tools/blender/breakout-office/assets/rough-linen/rough_linen_diffuse_2k.jpg` or copy the object from the local LFS store.

No third-party models or photographs are included.
