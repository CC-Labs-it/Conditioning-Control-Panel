# Glassroad art: Wave 1 road set

Six texture inputs for the engine-generated road, rails, boost pads and jump markers.
These are texture assets, so no GLB or extra road triangles are shipped.

## Sampling

| Asset | Tile in metres (U x V) | Sampler | Interpretation |
|---|---:|---|---|
| road-bands-mask | 12 x 16 | repeat both | Three broad lateral crests with a slow along-track warp. Map R through the palette. |
| road-glass-roughness | 4 x 4 | repeat both | Subtle roughness, about 0.18-0.25. |
| road-glass-normal | 4 x 4 | repeat both | OpenGL tangent-space normal, shallow glass surface. |
| rail-glow-mask | 0.9 x 8 | clamp U, repeat V | U follows rail height; one broad crest and a weaker low edge. |
| boost-chevron-mask | 4 x 6 | clamp U, repeat V | Add 0.5 to U after dividing lateral metres by 4. Tip points along +V. |
| jump-lip-mask | 12 x 3 | repeat U, clamp V | Last 3 m before the jump lip, with a broad paired curl. |

For road U=0 at the centre, repeating U supports either signed metre values or a half-tile phase shift.
Rail U=0 at the bottom. Boost V=0 at the pad start. Jump V=0 starts the lip patch.
The normal and roughness maps describe the glass; band, rail and marker masks describe shader inputs.

## Sources and licence

Original deterministic procedural work created for CCP. No third-party pixels or geometry.
The supplied concept images informed mood and palette only. They are not redistributed or traced.
Dawn was sampled from 01-key-art_1, deep from 04-rabbit-hole_2, afterglow from 11-afterglow-finish_1.
The private master archive retains exact sampling coordinates, source PNGs, scene and generation scripts.
Rights follow the project's existing asset terms; these assets are not claimed to be CC0.

## Validation

All six packaged KTX2 files pass Khronos strict glTF BasisU validation and decode successfully.
All tileable source edges match exactly. ETC1S introduces small block differences at some edges;
review uses the decoded runtime files, not the source PNGs. Normal map uses UASTC.
The road shader must own intensity and contrast; do not display a raw black/white mask as lighting.
Front swatches, oblique material views and a sky-coloured scene are in review/road-contact.png.

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
| road-bands-mask | 0 | 5.333 | 1.333 |
| road-glass-roughness | 0 | 1.333 | 0.333 |
| road-glass-normal | 0 | 1.333 | 0.333 |
| rail-glow-mask | 0 | 0.667 | 0.167 |
| boost-chevron-mask | 0 | 1.333 | 0.333 |
| jump-lip-mask | 0 | 0.667 | 0.167 |
| Total | 0 | 10.667 | 2.667 |
