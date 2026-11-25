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
function the mesh and the physics use** — over an equirect grid into two 2048×1024 maps
(plus the horizon maps added later, see [Terminator.md](Terminator.md)). The bake is
kept current automatically by `LunarMacroMapAutoBaker`; the menu is the explicit path.

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
// The ALBEDO field is smooth and body-fixed: on an airless regolith body it is applied at
// full strength at EVERY distance, otherwise the maria vanish underfoot and the weight
// itself is a visible step while descending. Only the NORMAL needs the texel weight.
float albedoW = _TerrainDetailMean > 0 ? _TerrainMacroAlbedoStrength : macroW;
albedo *= lerp(1, tex2D(_TerrainMacroAlbedoMap, uv).r * 2, albedoW);
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
the dark half is black — measured at 848 km / 75° phase, the boundary is clean. (On Terra
that dark half is no longer black by day: the air in front of the disc lights it — see the
next section.)

## The daytime Moon looked like a Moon with no atmosphere

Report: *"the Moon in the daytime looks as if Terra had no atmosphere — it should be sort of
bluish because of the air, but it looks like night."* By day the disc was a white lamp with
a hard terminator on a blue sky; it looked the same at 44° solar elevation as at midnight.

**Root cause is draw order, not shading.** `Galilego/PlanetAtmosphere` is a screen-space
pass at queue `Transparent` that *replaces* the frame with `background·T + in-scatter`.
Everything that must appear *behind* the air but draws later is composited on top of a
finished sky: the sun disc (`Transparent+2`, which at least multiplies by
`_SunTransmittance`) and the Moon's far disc (`Transparent+4`, which knew nothing about the
air at all). At night nobody notices — the air between the eye and the Moon scatters almost
nothing once the Sun is down — and by day the Moon is painted as if seen in vacuum.

**Fix: one atmosphere for the dome and for the sky objects.**
`GalilegoAtmosphereAir.hlsl` now holds the raymarch (uniforms, both CPU LUTs, the march
itself); `PlanetAtmosphere.shader` and `BodyFarDisc.shader` both include it. The disc
composites as

```hlsl
float3 air = AtmSkyRadiance(normalize(-_BFD_ViewWS.xyz), atmosphereTransmittance);
color = (radiance * atmosphereTransmittance) + air;   // T·body + in-scatter
```

which is *the dome's own T and its own in-scatter* for that direction, not an approximation
of them — a second implementation that drifts by a few percent shows a circle where the
disc edge meets the sky. `PlanetAtmosphereView` publishes `_AtmActive` (1 only on a frame
where the dome really rendered, after its LUTs are this frame's), so in vacuum the disc
falls back to `T = 1`, no air.

`BodyFarDiscView.HazeStrength` (`_BFD_Haze`) scales that column from vacuum (0) to the
dome's own physics (1), applied to the veil and to the dimming alike. It is **1** in the
scene, and it must stay there: the veil in front of the Moon *is* the sky behind it, so
dimming it makes the unlit half darker than the sky and the disc starts to read as a night
moon again. The same reasoning rules out "just brighten the disc to make it less hazy": at
`Brightness` 1.6 the sky's share of the Moon's colour drops from 29 % to 20 %, but the disc
is then 60 % above its own physical radiance. `1` / `1` is the realistic composite.

**The disc's brightness had to become physical too.** `Brightness` used to be a raw
multiplier on an arbitrary albedo (`24` in the scene); at that value the disc was ~10×
brighter than the Moon's own terrain, so even with the air it stayed a lamp. It is now a
multiplier on the *scene's* sunlit-surface radiance —
`albedo · ndl · limb · _BFD_SunColor · _TerrainRadianceScale` — i.e. `1` is a sunlit airless
surface of that albedo, lit by the star (no observer air in the illumination) and sharing
the terrain's scale. That also makes the 4 R → 1.6 R hand-over match in brightness, which
within one scale it never did before. It stays at **1**: raising it (1.6 was tried, to make
the disc read harder at 22 px) is +0.7 EV above its own physical radiance and only pretends
to be "less haze" — the veil is the sky and does not shrink, the disc just gets brighter.

The night keeps the old, deliberately non-physical look through one explicit number,
`NightBrightness = 24`, faded in by the Sun's elevation (`_SkySunDir`·`_SkyUp`, full gain
below −12°): `MoonlightSystem` lifts the moonlit ground by 14 EV, so a strictly physical
disc would be the only object at its true brightness and would read as a dim coin. The
signal is the Sun, not the sky's brightness — on an airless body the sky is black at noon
too.

### The Moon's ground was 4× darker than its own disc (the albedo path)

> **Status 2026-11-25: solved — and the albedo path was NOT the cause.** Everything below
> (the regolith set, the relative modulation, the full-strength macro albedo) is in the
> build and is correct. There were TWO defects outside it:
>
> 1. `_BFD_Albedo` was declared `Color`, so Unity decoded the ALREADY-LINEAR palette value
>    a second time on upload (0.214 → 0.0366, every far disc 5.85× dark).
> 2. `TerrainTextureDeband` overwrote Artemis' regolith set with the Earth photo set, and
>    the shader then divided the grass photo by the regolith mean — the GROUND came out
>    dark olive at low altitude. See
>    [LunarBodies.md](LunarBodies.md#the-ground-went-dark-brown-on-approach-the-regolith-set-was-overwritten).
>
> `Material.SetVector`/`GetVector` both round-trip the stored (gamma) value, so the
> material readback looked perfect and three sessions of albedo-source work could not move
> the number. The property is now a `Vector` (uploaded verbatim), and the disc measures
> `0.675–0.682` sRGB against the terrain's `0.584–0.598` at the same point, same sun, same
> session. The per-frame tables below are still worth reading as "what the shader does",
> but ignore their diagnosis of the symptom.

Report: *"in space the Moon is some grey, very dark; when I arrive it is clearly
brighter"*, then *"up close it is kind of grey — shouldn't it be bright?"*.

Measured in Play at 910 km (the disc's opacity is 0.25 there, the terrain owns the rest):
the billboard's luminance at the same screen point came out **2.5× below the terrain's** —
the same body, the same sun, the same `albedo·ndl·_TerrainRadianceScale` in both shaders.
That ratio is not a property of the body: it flips sign with the ground's albedo source.
Once the Earth photo set was stamped into Artemis (below) the ground fell to **0.051**
linear and a `Brightness = 1` disc was ~4× *above* it. The scene carried `2.5` to make the
hand-over look right, which is a calibration of a symptom: two shaders taking their albedo
from two different places.

The two views did not take their albedo from the same place:

* `PlanetSurface.shader` → `TerrainTextureAlbedo()`: the **Earth photo textures**
  (`terrain_low/mid/high/steep_clean.jpg`, the same files Terra uses — `TerrainTextureSetup`
  stamped them into *every* profile, Artemis included) × AO × biome tint. Mean linear
  albedo of that set is 0.075–0.089, the grey palette tint then multiplied it by 0.60, so
  the ground came out at **0.051** instead of the lunar highlands' ~0.2. That is 4× below
  the palette the disc uses, and olive-brown because the photo is grass.
* `BodyFarDisc.shader` → the **palette** colour (`Palette.SnowLinear` = 0.214, the linear
  value of the authored 0.5 grey) × the baked macro albedo map (a multiplier around 1).

The scene had compensated with **`Brightness = 2.5`** on the Artemis `BodyFarDiscView` — a
calibration for the dark ground, not physics. A single multiplier cannot make two shaders
agree while they take their albedo from two different places, so the Moon still changed
tone as you approached and its colour was Terra's grass at a lunar exposure.

**Fix: the ground uses the same albedo source as the disc.**

* Artemis owns a tileable regolith set (`Tools/Textures/GenerateRegolithTextures.py`,
  `Assets/_Project/Textures/Terrain/Lunar/`): neutral grey with the lunar red slope
  (1 : 0.975 : 0.945), mean linear **0.292**, std/mean 0.14 (dust) / 0.20 (scree),
  seamless (FFT noise + wrapped stamps). It carries dust, pebbles and small rocks only —
  craters are geometry and the macro normal; baking craters here would repeat every 25 m.
  Measured on the imported texture: the stored sRGB mean 0.575 decodes to 0.292 linear,
  exactly the profile value the shader divides by.
* `TerrainProfile.DetailAlbedoMean = 0.292` switches the shader to a **relative
  modulation**: `albedo *= sample / mean`. The palette and the macro albedo keep the
  absolute brightness (maria 0.07, highlands 0.21 linear), the map only adds sub-metre
  grain. With mean 0 the photo set still *replaces* the colour — Terra is untouched.
* The macro albedo now applies at full strength at **every** distance (`macroAlbedoW =
  MacroAlbedoStrength` instead of `macroW`), so the maria/highlands survive on the ground.
  The macro *normal* keeps the pixel-vs-texel weight: relief read from a kilometre-sized
  texel is wrong underfoot, a smooth albedo field is not.
* `Brightness` is back to **1**: both views are palette × macro map, so the hand-over
  cross-fades equal surface brightness.
* `TerrainTextureSetup` skips `Lunar.Enabled` profiles (its auto-run was what put Earth's
  grass on the Moon), and `Assign lunar regolith textures` assigns the set with the mean
  read from `regolith_mean.txt`.
* `_TerrainDetailMean` is written by `SkyEnvironment` for the **camera body**, like the
  baked horizon map: `ApplyTerrainGlobals` runs for every rasterisable body, so a
  per-renderer write would let the last one win and flip the Moon's ground between the
  modulation and the photo replacement as Terra entered the frustum.

### Why nothing pops while approaching

The Moon's hand-over is a cross-fade (opacity 1 → 0 between 4 R and 1.6 R), so a *step* in
brightness was never going to be hidden by it — it has to be zero by construction:

| source of a step | why it cannot appear |
|---|---|
| disc ↔ ground albedo | both are `SnowLinear` × the same macro map; `Brightness` 1 |
| detail ↔ macro albedo weight | the regolith weight is constant at every distance — nothing to switch |
| detail texture ← disk | 1024², mips on, **`streamingMipmaps: 0`** (no mip pop), BC7 |
| texture set ← profile | assigned in `ApplyProfile` before the first chunk is drawn |
| disc ← chunks | `ViewCoverage` hold + opacity fade (see above) |

The visible result is that the Moon keeps its maria all the way down and the ground is
neutral grey, not olive; its absolute level is the lunar albedo (maria 0.07, highlands
0.21 linear — the real values), i.e. 1.9–3.5 stops below white paper under the same sun,
like sunlit asphalt. Auto exposure still puts a ground-filled frame at mid-grey; that part
is the camera, not the albedo.

Verified in Play (`CheatMenu.FlyToBody` on Артемида and a noclip descent):

| view | result |
|---|---|
| 494 km / 800 km (chunks) | light grey highlands + dark maria, correct phase |
| 3.8 × 10⁶ m (far disc) | the same maria, no brown/olive cast |
| 2 km / 150 m (ground) | light grey cratered regolith, black sky, no green |
| A/B at 800 km (`BodyFarDiscView` on/off) | the frame does not change to the eye |

The far disc was *believed* to be darker in an isolated frame because of "exposure/tonemapping
of a mostly-black frame". It was not: the disc's albedo was gamma-decoded twice (see the
status block above), so its radiance really was 5.85× below the terrain's. Measured with
HDRP's exposure forced to Fixed 0 EV — the same numbers — and with the ACES fit, which maps
the measured `0.0946` to `0.048` and `0.553` to `0.410` exactly: `0.0366·2.585` vs
`0.214·2.585`, i.e. the albedo, not the exposure. `Brightness` is not the place to
compensate: raising it above 1 puts the disc above the terrain again and the 4 R → 1.6 R
cross-fade becomes a step. If the Moon has to read brighter from Terra, the
lever is an exposure bias in space, not the disc's albedo.

Re-check on the ground: the mare/highland boundary must still be readable (the macro map is
`2π·412 km / 2048 ≈ 1.3 km` per texel, so land on the boundary, not in the middle of a mare),
and the colour must be neutral grey with a slight warm cast, with no green.




### …but `_SkyUp` is the CAMERA BODY's up, so the Moon flashed on approach

Report: *"the Moon changes colour very strangely and wrongly while flying to it — it
changes them abruptly"*. Screenshots from one flight: a white lamp at 466 / 2 069 km, a
grey crescent at 1 730 km, the real maria at 637 km.

`_SkyUp` is set by `SkyEnvironment.ApplyGlobalsNow(Runner.CameraBody, …)`, and the camera
body changes at an SOI boundary. For the Artemis approach the observer leaves **Terra's
night side** but arrives on **Artemis' day side**, so the same physical point gives

| camera body | `_SkySunDir·_SkyUp` | `NightGain` |
|---|---|---|
| Terra (farther than Artemis' SOI, 19 391 km) | −0.28 | **24×** (4.6 EV) |
| Artemis (inside the SOI) | +0.25 | 1× |

The disc is therefore a saturating white ball for the whole flight and snaps to its true
grey in the last few thousand kilometres — and 24× is exactly the brightness the
4 R → 1.6 R hand-over is *not* tuned for (the terrain it hands over to is physical, see
`Brightness`).

The gain compensates a **night-adapted landscape**, and there is no landscape in space.
`BodyFarDiscView.LandscapeFactor()` now gates it by the observer's altitude over the
current body: full below `NightBoostFullAltitudeMeters` (100 km, the top of an atmosphere),
zero above `NightBoostFadeAltitudeMeters` (2 000 km). On the ground nothing changes; the
altitude is large on both sides of the SOI boundary, so the value cannot jump there.
Measured in Play on Terra's night side: 24× at 50 km, 19× at 500 km, 7× at 1 500 km, 1× at
3 000 km — while the camera body was still Terra and `sinEl` was −1.0.

### Measured: the daytime disc

Measured on the pad at sun 43.9°, Moon at 29.3° / phase 73°, Game view sRGB:

| | disc (lit) | sky beside it | veil share of the disc's luminance |
|---|---|---|---|
| before (`Brightness` 24) | 1.00 white, bloom halo | 0.086, 0.176, 0.314 | — (no air at all) |
| **shipped (`Brightness` 1)** | **0.216, 0.259, 0.357** | **0.086, 0.176, 0.314** | **29 %** |
| brighter trial (`Brightness` 1.6) | 0.290, 0.314, 0.392 | 0.086, 0.176, 0.314 | 20 % |

The lit disc is ~1.5–2.5× the sky (red channel 2.5×, blue 1.1×) — a pale, *warmer than
the sky* disc, which is what a real daytime Moon looks like; the unlit half is exactly the
sky colour, so the body has no visible edge there. In linear radiance (read back with the
tonemapper off and a fixed exposure) the disc's brightest point is **0.128** against
**0.076–0.119** for the sky around it — a ratio of **1.1–1.7**, which is the low contrast a
photograph of a gibbous Moon actually shows. That faintness is the result, not a bug:
`Brightness` 1.6 gives 1.8–2.7 and is still inside the range real photographs cover, but it
is no longer the body's radiance.

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

## The Moon went black, and it was only visible up close

Report: *"I arrived at the Moon and everything was fine; I landed in noclip, took off, and
it went completely black. You only see it when you are next to it, even though this is the
sunlit side — like the Moon's visibility range was cut off."*

Two independent defects, both only visible away from the ground, and both the same mistake
in a different place: a value that the C# sets but the GPU never receives, so it reads 0.

**1. The far disc had zero radiance — the billboard was simply not drawn.**
`BodyFarDisc.shader` declared `_BFD_SunColor`, `_BFD_SunScale` and `_BFD_Haze` in HLSL but
not in the ShaderLab `Properties` block. A Unity material only carries the properties its
shader declares: `Material.SetVector/SetFloat` on an undeclared uniform is silently
dropped, so `sunlit = _BFD_SunColor * _BFD_SunScale` was `0 * 0` and
`radiance = albedo * … * sunlit` was exactly zero for every pixel. The quad still drew —
black on black space — which is why it looked like the body had been deleted rather than
dimmed. Beyond the camera's far plane (~2 000 km over Artemis) the chunks are clipped, so
the Moon had no representation at all; up close the chunks take over and the surface looks
fine. Verified by disabling the chunk renderers at 3 000 km: before the fix the frame was
pure starfield, after it is a lit disc with maria. `_BFD_Haze` had the same hole, so the
disc's aerial perspective had never been applied since it was introduced.

**2. The macro normal map was transformed with the wrong axis frame.**
`SurfaceDir` (`dirOS` in the vertex) is stored in **astro** body axes (Z = pole — it is the
frame `macroUv` is built in, `asin(dir.z)` = latitude), while the chunk mesh lives in **sim**
axes (Y = up; `SurfaceChunkBuilder` writes `(x, z, −y)`). The macro-normal branch did
`TransformObjectToWorldDir(mapN)` on the astro triple with the sim chunk matrix, which
rotates every reconstructed normal by ~90°: the terminator of a *fully sunlit* hemisphere
went straight through its middle. The effect is invisible near the ground, where
`macroW → 0` and the mesh normal dominates, and total at altitude, where the map owns the
normal — the "sunlit side is black, but only from a distance" signature. Fixed by bridging
the axes before the transform: `float3(mapN.x, mapN.z, -mapN.y)` (the same permutation the
chunk builder uses). Measured at 700 km, 9° from the sub-solar point: before, a 60 %-lit
gibbous; after, the whole disc lit.

Both are cheap to re-check in the editor: at 3 000 km the disc alone must cover the body
(hide the `PlanetChunk_*` renderers), and at 700 km `ndl` must be ~1 across the whole disc
when the camera sits on the sub-solar radial.


## Known remaining artifact

A **1 px dashed line** along a cube-face boundary is still visible at some angles. It is not
the macro map (it survives with the map disabled), not the skirt (it survives with
`SkirtFactor = 0`), not the shadow (it survives with `ShadowStrength = 0`), and it disappears
when the quadtree is forced to depth 0 (all faces equal LOD). So it is a **LOD junction
between faces** — most likely the two edge surfaces meeting within a pixel of depth and
alternating in the depth test. Removing it properly needs either a stitched edge (parent
edge rendered as a hole plug under the children) or a shared normal rule across faces.

The gas giant far disc (`GasGiantDisc.shader`, also `Transparent+N`) has the same draw-order
problem this write-up fixes for the Moon, and does not use `GalilegoAtmosphereAir.hlsl` yet.
From Terra it is a 2 px dot, so it is not visible; near Aeolus the same `T·body + air`
composite is what it needs.

## Measured result

| Altitude | Before | After |
|---|---|---|
| 5 000–1 650 km (disc) | 512×256, no mips, no relief | 2048×1024 BC7 + relief normal |
| 1 000–300 km | maria = vertex blobs, detail factor 0, no relief | map albedo + map normal, full weight |
| 30 km | unchanged (map fades out below ~1 texel/px) | unchanged |
| 700 km | black crack hairlines at LOD junctions | surface-coloured (unshadowed skirt) |
| from Terra, day | white lamp, no air | `T·body + in-scatter`, pale warm-grey disc |

Memory: two 2048×1024 BC7 maps ≈ 5.3 MB + mips each (~7 MB together).
