# Glassroad art: Wave 1 EMI teacup kart

A porcelain cup and saucer carrying the existing EMI mascot.
Runtime: one skinned mesh, two material primitives, 13 rigid joints, 4,722 triangles.
Eight karts therefore contribute 16 material draws before engine-specific passes.

## Integration

Metres, Y up, forward -Z. Origin is the saucer's underside centre.
The saucer radius is 0.72 m; the handle extends the full width to about 1.528 m.
The model is about 1.263 m tall. Manifest radius is padded upward from measured decoded vertices.
Meshopt decode support and KTX2 Basis transcode support are required.
Position values remain full float under lossless meshopt compression to preserve the smallest inherited faces.

The blank screen is a separate material named emi_screen with a clean 0..1 UV island.
It has no image, no face atlas and no painted expression. Bind the runtime face texture there.
kart_surface uses two embedded 512-square UASTC atlases:
base colour (sRGB) and packed occlusion/roughness/metalness (linear), each with ten mip levels.
All visible colour and finish changes outside the screen share that surface material.
No separate external texture files need loading for this kart.

The model has a real rigid-weight skin: every vertex follows exactly one joint.
Named joints preserve the source control points:
kart_root, rider_mount, EMI_root, shoulderL, shoulderR, ant0, ant1, ant2,
ballpiv, button, footL, footR, EMI_glass.
Animate the named bone transforms for arms and antenna. Drive the whole kart from its scene root.
Keep the mesh and skeleton together; use SkeletonUtils.clone for independent kart instances.
The source's five selection/showcase clips were omitted because full-body hops and stretches
do not fit a seated rider. No new race animations are claimed or included.

## Sources and licence

EMI starts from ConditioningControlPanel/Resources/web/dtrh/race/assets/emi.glb.
Her approved body, screen, arms, antenna and identity are retained. The inverted outline hull
and fully concealed footwear geometry were removed to make room for the cup within the triangle budget.
Only zero-area faces were removed after the rigid bind; no visible character forms were redrawn.
The cup, saucer, connected handle, inlaid motifs and shared surface atlas are original work for CCP.
The existing EMI asset rights remain unchanged under the project's existing terms; no CC0 claim is made.
The supplied concepts guided porcelain, trim and composition only; no reference pixels were reused.
Private sources retain the accepted rigid version, .blend masters, atlas source and reproducible scripts.

## Validation and assumptions

Fresh import of the final decoded compressed GLB preserves one mesh, two primitives, 13 bones,
the named materials, full screen UV range and zero degenerate triangles.
Bind conversion moved vertices by at most 0.000000121 m.
A 25-degree shoulder test moves the arm while keeping the cup and screen fixed.
The contact sheet and private pose views were rendered from the actual decoded packed export.
Three.js r186 CPU skinning also confirms that translating, rotating and scaling the common root
moves all 3,063 vertices correctly. The validator non-root-skinned-mesh warning is accounted for:
mesh and joints share identity-transform parents, with no independent mesh offset.
The engine still needs to supply faces, race poses, floating motion and Gentle Visuals behaviour.
The original source's +Z facing was rotated to the required -Z direction.
The cup's 0.72 m saucer radius follows the current engine's visual footprint.

## Triangles and texture memory

| Asset | Tris | RGBA8 with all mips | Block16 with all mips | Draws |
|---|---:|---:|---:|---:|
| emi-teacup-kart | 4,722 | 2,796,200 bytes (2.667 MiB) | 699,104 bytes (0.667 MiB) | 2 |

texBytes uses conservative RGBA8 allocation including every mip, not compressed download size.
Textures can be shared by every kart instance; do not count their memory once per racer.
Review PNGs are evidence only and are not runtime texture inputs.
Each family branch starts directly from the same main snapshot. When integrating families,
append their distinct manifest rows and retain every family ART.md section. Palettes are identical.
