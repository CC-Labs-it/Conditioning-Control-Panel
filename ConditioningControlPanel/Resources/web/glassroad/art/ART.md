# Glassroad art: Wave 1 sky set

Three palette families of static galaxy haze and separate sparse star fields, plus two reusable
grayscale spiral masks. All motion and spiral colouring remain in the engine shader.

## Sampling and composition

Galaxy textures are 2048 x 1024 equirectangular maps. Star fields are 1024 x 512 equirectangular maps.
U wraps longitude, V clamps latitude; the north pole is V=0. Use sphere direction to derive the UVs.
The separate star layer has black background and adds light after sRGB decoding.
Each palette has its own colour layers; only the active palette and a crossfade partner need residency.

The 1024-square masks use disc UVs 0..1, centre (0.5, 0.5), transparent-by-zero outside the disc.
Sample R, multiply by the selected palette colour and apply the engine's contrast/intensity controls.
open-mask has two broad arms; deep-mask has three. Both have a smooth core and edge.
They deliberately avoid fine high-contrast central rings. Rotate their UVs, never the static nebula.
If projected on a dome, keep angular extent and orientation under the sky shader's control.
The contact sheet combines these actual decoded layers with a static review recipe; it is not baked runtime art.

## Sources and licence

Original deterministic spherical cloud fields, spherical star sampling and spiral-mask construction for CCP.
No external sky photography, HDRI, generated image or third-party texture was used.
The supplied concepts informed mood and the shared sampled palettes only.
The private master archive retains generation inputs, source images and review composition source.
Rights follow the project's existing asset terms; these assets are not claimed to be CC0.

## Validation

All eight KTX2 files pass Khronos strict glTF BasisU validation and decode successfully.
Source longitude edges match exactly. Hero galaxy gradients use UASTC to avoid dark-field block banding.
The bulk star fields and neutral masks use ETC1S; decoded differences are recorded privately.
Mask borders decode to zero and can clamp without a visible rectangular edge.
The whole sky set's conservative memory is listed below, although normal play needs one palette at a time.

## Contract

Runtime assets only. No scene, renderer, physics, track or host code changes.
All geometry uses metres, Y up, base-centre origin, forward -Z.
Textures are KTX2 with complete mip chains. Mask and data textures are linear; colour layers are sRGB.
Read scalar masks from the red channel. They are not coloured pictures or final luminance.
Texture origin is top-left (KTX orientation rd); V increases forward for road surfaces.
Use the manifest sampler modes and scale metre UVs by uvSizeM before sampling.

texBytes is conservative RGBA8 resident memory, including every mip, not download size.
texBytesBlock16 is a 16-byte-per-4x4-block GPU allocation including all mips.
The actual Basis transcode target depends on the device. Budget against texBytes.
A texture row has tris 0 and boundRadiusM 0 because it contributes no geometry.
Every model/prop row carries a radius measured from the export origin including all its vertices.

Palettes contain five concept-sampled sRGB colours in this order:
background, primary, secondary, accent, pearl. Choose primary through pearl for road bands.
No motion or lighting changes are baked. Engine Gentle Visuals and flash limiting control
contrast, scroll, bloom and transitions. The contact sheet is static material review, not a game capture.
Review PNGs are evidence and are not runtime texture inputs.

Each family branch starts from the same main snapshot. When integrating multiple families,
append unique rows from each manifest and retain all family sections in ART.md.
The shared palettes.json is identical across branches.

## Triangles and texture memory

| Asset | Tris | RGBA8 with mips (MiB) | Block16 with mips (MiB) |
|---|---:|---:|---:|
| sky-dawn-galaxy | 0 | 10.667 | 2.667 |
| sky-dawn-stars | 0 | 2.667 | 0.667 |
| sky-deep-galaxy | 0 | 10.667 | 2.667 |
| sky-deep-stars | 0 | 2.667 | 0.667 |
| sky-afterglow-galaxy | 0 | 10.667 | 2.667 |
| sky-afterglow-stars | 0 | 2.667 | 0.667 |
| sky-spiral-open-mask | 0 | 5.333 | 1.333 |
| sky-spiral-deep-mask | 0 | 5.333 | 1.333 |
| Total | 0 | 50.667 | 12.667 |
