# The Moon's surface, from orbit to the ground

Artemis looked like a blurred painted ball on approach: the maria were soft blobs, the
crater rays and fresh ejecta vanished with altitude, and from ~1 650 km up the body was a
flat billboard carrying a 512×256 albedo with **no relief at all**. The player could read
the LOD levels changing as they flew. Three independent layers caused it; each is now fed
by one baked pair of maps.

## 1. The albedo lived in a per-vertex channel and was faded out on purpose

`PlanetSurface.shader` gets its lunar albedo through `Extra.w` (colour detail), which is
**interpolated per vertex** — at 300 km the quads are ~1 km wide, so a mare is a blob. The
shader then fades that channel by pixel footprint against distance shimmer:

```hlsl
float footprint = angular * _ColorDetailFrequency;   // 600 for Artemis
detail *= saturate(1.0 - (footprint * 0.7));
```

At 300 km that factor is **0** (measured: `footprint ≈ 2.55`), so the only albedo left was
the low-frequency mare mask, itself vertex-interpolated. That is the "drawn-on" look.

## 2. Nothing said the body had relief

The mesh carries the craters, but a crater's *rim* is a shading feature long before it is a
silhouette: below the LOD that resolves it, the ground was lit by a single interpolated
normal per quad. From orbit the craters were invisible bumps.

## 3. The far disc could not carry the body's identity

`BodyFarDiscView` (used above ~1 650 km, where the terrain camera's far plane clips the
chunks) sampled `Artemis_disc.png` — 512×256, **no mipmaps**, no normal. A 30° disc of that
map is a smear, and the hand-over to the chunks was a change of material, not a refinement.

## The fix: one bake, two views

`Tools/Galilego/Airless Bodies/Bake lunar macro maps`
(`Scripts/Editor/AssetSetup/LunarMacroMapBaker.cs`) samples **`LunarRelief` — the same
function the mesh and the physics use** — over an equirect grid into two 2048×1024 maps:

| Map | Channels | Use |
|---|---|---|
| `<Profile>_macro_albedo.png` | R = albedo multiplier / 2 (the encoding the disc already used) | albedo from orbit down to ~1 texel/px |
| `<Profile>_macro_normal.png` | xyz = **body-fixed surface normal** `n·0.5+0.5` | shading relief at the same range |

Bake cost on this machine: **3.8 s** (2.1 M samples, `Parallel.For`; `LunarRelief.Sample`
is pure and runs at ~57 k samples/s single-threaded). Import settings are part of the bake:
mips on, trilinear, aniso 4, **BC7 (`CompressedHQ`)**.

> BC7 is not a taste: the default compressor (BC1) turned the body-space normal into a
> smear, and the bad samples drove `normalize()` to NaN. One NaN pixel poisons HDRP's
> auto-exposure, and the whole frame went teal — that is what "compressed normal" looked
> like. If a future bake starts showing a flat cyan/teal image, check the format first.

Both maps are referenced from `TerrainProfile` (`MacroAlbedo`, `MacroNormal`) and from
`BodyFarDiscView.SurfaceMap` / `NormalMap`, so one bake drives the disc and the terrain.

### The shader turns the map on by angle, not by distance

```hlsl
float angular = max(fwidth(dir.x), fwidth(dir.y), fwidth(dir.z)); // pixel angle
float macroW  = saturate(angular / _TerrainMacroTexelAngular);    // texel angle
mask   = extra.z * (1 - macroW);   // the interpolated copy fades OUT, it is not multiplied in
detail = extra.w * (1 - macroW);
albedo *= lerp(1, tex2D(_TerrainMacroAlbedoMap, uv).r * 2, macroW);
normal  = lerp(normal, mapNormal, macroW * strength);
```

`_TerrainMacroTexelAngular = 2π / width` is the exact LOD criterion in radians: a pixel
that covers a whole texel or more trusts the map, a pixel smaller than a texel keeps the
mesh. Nothing is hand-tuned per body, it follows the FOV, resolution and radius.

The same `macroW` also removes the seam between cube faces at altitude: the mesh normal is
computed per chunk by one-sided differences, so it **jumps** across a face boundary (a 1 px
line down the body). The baked normal is continuous, and at orbital ranges it is now the
only normal used.

### The normal must go object → world, not through `_TerrainWorldToBody`

The first version rotated the map normal with the `_TerrainWorldToBody` global. That matrix
answers for **camera-relative sim positions** (filled for the terrain *texture* triplanar
projection), not for object-space directions, and the mismatch turned every reconstructed
normal away from the sun: the Moon rendered black with a lit rim. The map and `dirOS` share
the chunk's **object** frame (that is the frame the bake is taken in), so the correct
rotation is the mesh transform:

```hlsl
float3 nWorld = TransformObjectToWorldDir(mapN);
```

Followed by a `tilt2 > 1e-8` guard: a degenerate direction must never reach `normalize()`.

## The far disc

`BodyFarDisc.shader` samples the same pair by lat/lon, rebuilds the sphere normal from the
map and lights it with the same Lambert term. The disc is now a *rendered* Moon (maria,
rays, crater rims with real shading), not a painted circle, which is what makes the
4 R → 1.6 R hand-over read as "more detail arriving" instead of "a different object".

## Holding the disc until the sphere is worth showing

`PlanetSurfaceRenderer.ViewCoverage` used to count a desired node as covered if **any**
ancestor was built. Right after arrival only the six depth-0 faces exist, the metric said
`1.0`, the disc dropped, and the player watched a smooth low-poly ball sharpen. It now
requires the built ancestor to be at most **one** level coarser (`MaxCoverageCoarsenLevels`)
and gates on the near band (`CoverageNearBandLevels = 4` depths) separately: a sharp far
ring over a melted near cap still reads as "loading".

## Skirts are drawn unshadowed

A chunk skirt is the edge duplicated one node **below** the surface. The shadow map has it
inside the terrain, so at a LOD junction — where the two edges cross and a pixel-wide sliver
of the wall shows — it rendered as a black hairline ("cracked Moon"). `Extra2.y` now marks
skirt vertices and the shader gives them the unshadowed light: the wall is exactly the
colour of the ground it hides behind.

## Bright craters on the unlit side (the disc's ambient)

Between 4 R and 1.6 R the far disc is *blended* over the real terrain (`_BFD_Opacity`
19 % at 850 km). The disc shader carried `_BFD_Ambient = 0.015` — a constant floor on the
Lambert term, multiplied by `_BFD_IntensityScale` (24) — so on the unlit half it painted
`01.5 % × 24 = 0.36` of the crater albedo onto a surface that receives no light at all.
That is the "the sun does not reach it but it is bright" report, and it only appears on
approach because that is where the disc and the terrain overlap.

For an airless body the floor is wrong: there is no atmosphere to scatter light into the
shadow. `BodyFarDiscView.Ambient` is now exposed and **0**, the shader default matches, and
the dark half is black — measured at 848 km / 75° phase, the boundary is clean.

## The Moon that was not in the sky

Artemis' scene elements put `MeanAnomalyAtEpochDegrees = 0`, and at t = 0 that is
**exactly behind Terra as seen from the Sun** (measured separation 0.06°): a full moon
inside Terra's umbra. The far disc therefore drew at `eclipse = 0` — gain 0.002, i.e. a
22 px disc at 0.2 % brightness, invisible next to the daylight scene — and, being the
anti-solar point, it was also *below the horizon* at the moment the player spawns. From
the ground the Moon simply was not there, and the night had no moonlight either
(`MoonlightSystem` multiplies by the same eclipse term).

`MeanAnomalyAtEpochDegrees = 295` moves it a quarter orbit round: phase angle 74° (63 %
lit, a gibbous moon), 29° above the horizon at spawn, no eclipse, and ~15 % of full
moonlight at night. The eclipse term itself is untouched — a real eclipse still darkens
the disc on purpose.

## Known remaining artifact

A **1 px dashed line** along a cube-face boundary is still visible at some angles. It is not
the macro map (it survives with the map disabled), not the skirt (it survives with
`SkirtFactor = 0`), not the shadow (it survives with `ShadowStrength = 0`), and it disappears
when the quadtree is forced to depth 0 (all faces equal LOD). So it is a **LOD junction
between faces** — most likely the two edge surfaces meeting within a pixel of depth and
alternating in the depth test. Removing it properly needs either a stitched edge (parent
edge rendered as a hole plug under the children) or a shared normal rule across faces.

## Measured result

| Altitude | Before | After |
|---|---|---|
| 5 000–1 650 km (disc) | 512×256, no mips, no relief | 2048×1024 BC7 + relief normal |
| 1 000–300 km | maria = vertex blobs, detail factor 0, no relief | map albedo + map normal, full weight |
| 30 km | unchanged (map fades out below ~1 texel/px) | unchanged |
| 700 km | black crack hairlines at LOD junctions | surface-coloured (unshadowed skirt) |

Memory: two 2048×1024 BC7 maps ≈ 5.3 MB + mips each (~7 MB together).
