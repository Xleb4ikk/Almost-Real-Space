# Regolith — the Moon's surface material, tracks and dust

Status 2026-11-25. The base material (colour, micro-relief, photometry) is in the
build and was inspected on the ground. The track map is wired end-to-end; the
remaining brief items are listed at the bottom.

## 1. The detail set is now albedo + normal + cavity

`Tools/Textures/GenerateRegolithTextures.py` no longer generates the old soft
1/f "dunes". It builds a **tileable 2048² set** at 25 m per tile (1.2 cm/texel):

| file | channels | use |
|---|---|---|
| `regolith_flat_clean.png` | RGB sRGB albedo, **A = cavity/AO** | dust / mare / highland floor |
| `regolith_rock_clean.png` | RGB sRGB albedo, A = cavity | scree, crater rims, steep blend |
| `regolith_flat_normal.png` | tangent-space normal, linear | `TextureDetailNormal` |
| `regolith_rock_normal.png` | tangent-space normal, linear | `TextureDetailNormalSteep` |
| `regolith_mean.txt` | linear mean | `TerrainProfile.DetailAlbedoMean` |

The generator is deliberately **anti-dune**:

* no low-frequency 1/f field (the old `beta_large = 1.40` was the sand-wave look);
* ridged high-frequency noise + two scales of jittered Voronoi clods;
* faceted 4-70 cm pebbles and a few 0.4-1.0 m boulders (sharp convex polygons,
  one tilted plane per rock). Sparse on the **flat** set (260 per 25 m tile: a mare
  is dust with pebbles, not a gravel pit) and dense on **rock** (scree/crater rims);
* micro-craterlets with a bowl + raised rim, radii 7 px to 180 px (0.1-2.2 m);
* a sparse set of bright glass beads and dark glassy impact-melt splashes;
* normal from a **blurred** height (sigma 0.7 flat / 0.5 rock), because a raw finite
  difference at 1.2 cm per texel makes every texel a 40° facet — the first iteration
  read as TV static under a low sun (measured x/y normal std 0.46; now ~0.2 with the
  features still coherent);
* AO in the albedo alpha: `blur(height) - height`, clamped to 0.55..1. The shader
  multiplies it in at `REGOLITH_AO_STRENGTH = 1.0` (mean 0.94, so the absolute
  level barely moves; the pits go dark).

Import settings are enforced by `GroundDecorTexturePostprocessor`: anything under
`Textures/Terrain/` whose name contains `normal` is **linear** (this was the bug:
the terrain branch forced sRGB on every terrain texture and the normals were
decoded as colour), lunar files are BC7 `CompressedHQ`, trilinear, aniso 4.

## 2. Shader: lunar photometry, not Lambert

`PlanetSurface.shader`, airless branch (`_TerrainDetailMean > 0`):

```hlsl
float ls = 2 * mu0 / max(mu0 + mu, 0.02);          // Lommel-Seeliger
float opposition = 1 + 0.45 * exp(-phase / 0.13);  // Hapke surge
regolithSun = lerp(mu0, min(ls, 1.7), 0.50) * opposition;
```

* **Lommel-Seeliger** is the flat full-Moon disk: no limb darkening and a brighter
  grazing view. The blend is 0.5 and the cap 1.7 — pure L-S runs away at the
  terminator, and the first version (0.62/2.0) made the horizon 2× the foreground.
* **Opposition** is `1 + 0.45` at phase 0, width 7.5°. This is the flare around
  the observer's shadow and the bright full Moon in one term.
* **Sun-angle tint**: `GalilegoRegolithTint` (in `GalilegoLighting.hlsl`), applied
  to the albedo at strength 0.8: high sun `(1.07, 1.00, 0.925)`, low sun
  `(0.975, 0.985, 1.0)`. Rocks and dust shift together.
* **Vacuum shadow floor**: 2 % cool parent-planet light (`earthshine`), no sky
  ambient. HDRP's one cascade belongs to the sun by day and the moon by night.
* **Far disc**: `BodyFarDisc.shader` now applies the same opposition to the scalar
  lunar map (`_BFD_MapColor < 0.5`), so the full Moon is bright from Terra and the
  4 R → 1.6 R hand-over does not step. Atmospheric bodies keep Lambert.

Micro-relief:

```hlsl
nObj = normalize(lerp(meshNormal, TriplanarNormal(detailSet), strength * fade));
```

The tangent-space triplanar normal is **swizzled into object axes** before the
three projections are blended (`(n.z, n.y, n.x)` for the x projection, etc.).
Averaging the raw tangent vectors turns every slope's normal 90° away from the
light. The fade is 35→220 m; beyond that the map is a flat mip and the fade only
removes fizz. `DetailNormalStrength = 0.5` on Artemis.

**Grazing sun.** A 1 cm facet that is correct at noon can flip from lit to black
when the sun is 10-25° above the horizon; at full strength the plain became
black-and-white static (reported on landing). The micro-normal is therefore
multiplied by `smoothstep(0.12, 0.70, sunElevation)`: 0 at the terminator, 1 above
~45°. The coherent macro/mesh normal is untouched, so crater rims still shade.
The detail and macro albedo modulations are clamped to `0.55..1.35` for the same
reason: a 1.6× bright grain × 1.8× macro ray × 2.5× sun scale clipped to pure
white and read as noise, not regolith.

The **lunar albedo** (maria dark, fresh ejecta/rays bright) rides in the vertex
detail channel (`Extra.w`). It used to be faded out by pixel footprint, which
erased it underfoot; on airless bodies the macro map is the LOD authority
(`vertexDetail = 1 - macroW`) and the footprint fade is skipped. Artemis now sets
`ColorDetailStrength = 0.85` (it was 0, which made the ground one flat grey).

## 3. Tracks — permanent, crisp, local

`RegolithTracks.cs` + `Resources/RegolithTrackStamp.shader` +
`PlanetSurface.shader`:

* one **4096² RG16F** map over a **256 m** tangent patch anchored at the first
  grounded position (6.25 cm/texel);
* R = depression depth, G = compaction. The stamp shader draws a small quad per
  print (boot, wheel rut, landing blast) through a `CommandBuffer`; no full-screen
  pass. A boot is 0.48 m quad with a sole+heel shape, 1.4 cm deep, 0.10 compaction;
* the terrain shader samples the centre + two neighbours and turns the height
  gradient into a **surface normal**: `n += right·dh/dx + forward·dh/dy`. A 1.4 cm
  print is a real slope, so it keeps a crisp sunlit edge; nothing erodes it;
* compacted prints are 18 % darker; the landing site is a 55 m pale compacted
  circle with a 2 mm shallow depression — **no crater under the engine**;
* physics is untouched: the print is millimetres of visual relief, the player
  cannot sink (the brief's invariant);
* the map is camera-body-owned (`SkyEnvironment` calls `Tick` every frame) and is
  re-anchored on a body change or a teleport beyond 154 m. `Ensure()` reuses the
  first runtime instance and destroys duplicates left by a domain reload — three
  managers publishing three origins was why the first version sampled the pad's
  patch while standing 1 000 km away;
* `WheelRut(from, to, halfWidth)` is the rover API. No rover exists yet, so only
  the player's stride and the landing blast are wired.

Known limit: a 256 m patch is a landing-site patch, not a planet-wide trail
database. Walking past ~150 m re-anchors and the old prints are outside the map.
A persistent per-site store (or a ring of patches) is the next step if the play
area grows.

## 4. Profile changes on Artemis

| field | value | why |
|---|---|---|
| `TextureDetailNormal` | `regolith_flat_normal.png` | micro-relief |
| `TextureDetailNormalSteep` | `regolith_rock_normal.png` | scree on steep blend |
| `DetailNormalStrength` | 0.5 | tuned on the ground at 45° sun; the shader fades it at grazing sun |
| `ColorDetailStrength` | 0.85 | maria/rays underfoot |
| `DetailAlbedoMean` | 0.2903 | measured by the generator |
| `MinCraterDiameterMeters` | 12 | foreground craters: 24 m left a smooth plain |
| `Palette.Snow` | 0.30 sRGB (linear 0.073) | the brief's "dark as asphalt". The physical highland 0.5 is 0.214 linear, but this scene's sun term is `×2.5` and ACES lifts a 0.5 highland to near-white (measured), which is the "bright sand" the brief forbids. 0.30 with the 1.0 cavity and the normal set reads as regolith; the far disc uses the same palette, so the 4 R → 1.6 R hand-over stays matched. |
| `Palette.Soil/Lush` | 0.21 / 0.68 | mare / fresh ejecta |
| `MacroBakeHash` | rebaked after the palette change | the macro albedo factors are relative to `Snow`; a stale bake leaves maria/rays at the old ratio. `Tools ▸ Galilego ▸ Airless Bodies ▸ Bake lunar macro maps` |

`Tools/Galilego/Assign lunar regolith textures` sets all of the above; it is the
single place that must change if the set is regenerated.

## 5. What is still open from the brief

1. **Dust particles** (step 3): no ballistic emitter yet. The brief's flat 1-3°
   fan, parabola in vacuum, fall-and-lie, and the suit/vehicle dirt mask are not
   implemented. `PlumeEmitter` only draws the exhaust volume + light.
2. **Adhesion mask** on the suit/vehicle: none.
3. **Terrain types** beyond mare / highland / fresh ejecta: lunar swirls,
   pyroclastic (orange glass under a dark surface), dry pole with a 30 cm loose
   layer, PSR ice crust. The generator/profile has no masks for these yet; the
   brief's table is the spec for the next pass.
4. **Footprint depth per terrain type**: 1-2 cm normal / 3-5 cm polar / <1 cm PSR.
   The map has one `PrintDepthMeters = 0.02`; it must become a per-site value.
5. **Engine blast from the actual ship**: the landing site is stamped at the
   first grounded frame, but the blast is not driven by the thruster's proximity.
   `RegolithTracks` needs a ship-altitude/engine hook.
6. **Verification of the normal-depth footprint, walking in game**: the stamp
   pipeline writes (RT read-back: R = 1.0 at a boot) and the shader sampling path
   is in place; a normal-depth walk test still needs a human eye, because the
   headless probe kept teleporting the player away from the patch.

## 6. How to reproduce

```
python Tools/Textures/GenerateRegolithTextures.py
# Unity: Tools ▸ Galilego ▸ Assign lunar regolith textures
```

Then on the Moon: 45° sun, eye height → the ground must show angular grain,
a flat bright horizon (opposition), dark maria and bright rays, and prints that
keep their edges after any amount of time.
