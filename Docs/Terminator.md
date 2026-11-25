# The terminator on an airless body — why craters glowed

**Symptom.** Standing on Artemis in the day/night transition band, the landscape is
black but single craters still "light up": their sun-facing inner walls are as bright
as noon, and the effect does not follow the terrain's own horizon. In some places a
lit rim is correct (a peak that still catches the sun), but a crater floor 10 km
away cannot be lit by a sun that is 1° above the *point's* horizon.

Three independent defects added up. Fixing only one of them changes almost nothing,
which is why the bug looked like a shading-model problem rather than geometry.

## 1. The terrain never cast a shadow

`PlanetSurface.shader` had a forward pass and a depth-prepass, but **no
`ShadowCaster` pass**. `PlanetSurfaceRenderer` already set
`shadowCastingMode = On` for the near chunks (`TerrainShadowDepthBand = 3`), so the
renderer *asked* for shadows that the shader could not deliver. Only decor
(`GroundDecorSolid.shader`) had a caster pass, which is why rocks had shadows and
crater rims had none.

The shader now has a `ShadowCaster` pass: position only, `Cull Off` (the chunks are
open shells with skirts — a shadow ray must not slip through a chunk seam), and
`ColorMask 0`. It must match the forward pass's culling, or the shadow silhouette
disagrees with the visible surface at LOD junctions.

## 2. The shadow range was 250 m, sized for a rocket

`maxShadowDistance = 250 m` is right on Terra: the one shadow anyone reads is the
3 m rocket on the pad, and the docs in [Shadows.md](Shadows.md) explain why the
cascades must stay tight. On the Moon it is absurd: at a 1° sun a 50 m rim shadows
**kilometres** of ground, and a 250 m cascade map never contains the rim at all.

HDRP has exactly one global shadow distance (an `HDShadowSettings` volume override,
not a per-light field). `SkyEnvironment` now creates a runtime global volume
`AirlessShadowDistance` at **priority 999** and raises the distance to
`AirlessShadowDistanceMeters` (default **2000 m**) when the camera body has no
atmosphere. Priority 999 sits below `GameSettingsVolume` (1000), so a distance the
player chose in the settings still wins; on Terra (or any body with atmosphere) the
volume is disabled entirely and the scene's authored 250 m applies.

The cascade split shape is copied (5% / 18% / 45%): at 2 km the first cascade is
still ~100 m, so the rocket's shadow keeps 5 cm texels while the last cascade
reaches the crater rims. A 2 km shadow range covers the whole visible ground from
eye level on a 412 km body — the geometric horizon is `√(2Rh)` = 1.2 km at 1.7 m.

## 3. There was no terrain horizon at all

Even with a 2 km range, a rim 5–50 km away is outside the cascades, and a global
2048×1024 relief map cannot be ray-marched per pixel. Airless bodies therefore get
a **baked horizon map**: for every texel and 8 azimuth bins (45° each), the highest
terrain elevation angle seen from that point along that azimuth, out to 56 height-map
texels (~70 km on Artemis).

* `LunarMacroMapBaker` (menu `Tools/Galilego/Airless Bodies/Bake lunar macro maps`)
  already sampled `LunarRelief` at 2048×1024 for the albedo and normal maps; the
  horizon reuses that **same height array**, so the bake cannot drift from the mesh.
  The whole bake on Artemis (albedo + normal + horizon) took **6.5 s** on the dev
  machine, so this is a routine one-off, not a build step.
* **Bakes rebake themselves.** `LunarMacroMapAutoBaker` fingerprints the inputs
  (`TerrainProfile.MacroBakeHash`: every `LunarParams` field including seed and
  radius, the three palette colours the albedo derives from, and a baker version).
  When a profile, seed or map reference changes, it waits for the edit to settle
  (1.5 s after the last change), then computes on a worker (the editor stays
  responsive, cancel aborts and the bake is not retried until the next edit) and
  writes the PNGs on the main thread. It never runs in Play or batch mode. Menu:
  `Tools/Galilego/Airless Bodies/Auto-rebake macro maps` (toggle).
  One caveat: the PNG names are per **profile**, so two bodies sharing a profile with
  different seeds cannot both be represented — the first body owns the bake and the
  second gets one warning (give it its own profile).
* Stored as two RGBA PNGs, `_macro_horizon0/1` (azimuths 0–3 and 4–7), one 8-bit
  channel per azimuth. **The fourth bin of each map lives in the PNG's ALPHA
  channel**, so those two images must be imported with `alphaSource = FromInput`
  (`alphaUsage: 1` in the `.meta`). `WritePng` used to force
  `TextureImporterAlphaSource.None` for all four bakes: the GPU then read alpha as
  `1.0`, `DecodeHorizonAngle(1) = +90°`, and every pixel whose sun azimuth fell in
  bin 3 or 7 was horizon-shadowed. On screen this was **two dark quadrants with a
  straight edge through the sunlit disc** (half the azimuths), which reads exactly
  like a broken terminator. If the horizon ever darkens half the day side again,
  check `Shader.GetGlobalTexture("_TerrainMacroHorizonMapA")`'s alpha first: it
  must be the baked angles, not 255.
* **Encoding is nonlinear**: `u = sign(a)·√(|a|/90°)`, decoded as `a = sign(u)·u²·90`.
  A plain linear 0–255 byte over ±90° would quantise the horizon in 0.7° steps —
  the whole terminator — while this gives **0.0014°** at 0° and only degrades near
  ±90°, where nothing lives. The shader function is `DecodeHorizonAngle`.
* `PlanetSurface.shader` compares the sun's **local elevation** `asin(dot(sunBody, dir))`
  with the interpolated horizon in the sun's azimuth and attenuates the direct sun
  term. The edge is `smoothstep(h − r, h + r, elev)` where `r` is the star's angular
  radius (`_SunAngularRadiusRad`, 0.72° at 0.37 AU) — the shadow edge is exactly as
  soft as the solar disc, like HDRP's PCSS penumbra.
* The frame is the **body-fixed** one the macro maps live in (Z = pole, x = lon 0).
  `SkyEnvironment` writes `_TerrainSunDirBody = orientation⁻¹ · sunDirAstro` every
  frame; using the inertial direction would rotate the terminator with the body's
  spin.
* The two systems compose: the cascade map handles rims within 2 km (metre-scale),
  the horizon map handles everything beyond (kilometre-scale). `MacroHorizonStrength
  = 0` disables the map (A/B), and the map itself is optional (`_TerrainHorizonOn`).

## 4. The black/white static on a shadowed slope is the micro-normal

**Symptom.** The day/night edge of a dune is a smooth gradient at noon but a 1–2 px
black/white stipple where the slope itself turns away from the sun. It looks like a
broken shadow map, and the stipple survives `_ShadowStrength = 0` — that is the
quickest way to tell it apart from a cast shadow: **the shadow term is not involved.**

**Cause.** `microSunFade` (the guard that removes the regolith normal map at grazing
light, see the comment in `PlanetSurface.shader`) was keyed to
`dot(radialUp, sunDir)` — the sun's elevation above the **observer's** horizon. At a
25° sun a dune's lee face still reports 25°, while its own `N·L` is 0, so the 0.5
strength normal map kept flipping `N·L` from +ε to −ε texel by texel. The geometry is
clean: a screen-space derivative normal (`cross(ddx(positionWS), ddy(positionWS))`)
gives a smooth terminator at the same pixels, so the static comes entirely from the
shading normal.

**Fix.** Gate the micro-normal by the **local** incidence of the unperturbed (mesh)
normal: `microSunFade = smoothstep(0.12, 0.70, saturate(dot(normal, sunDir)))` with
`normal` read *before* the detail perturbation. The plain at a 25° sun keeps the same
0.53 weight it had; only slopes within ~44° of their own terminator lose the grain,
which is exactly where the texture aliases.

**How to check.** Debug the signed term as `saturate(dot(normal, sunDir) * 50 + 0.5)`:
with the micro-normal on, the boundary is speckled; with `DetailNormalStrength = 0`
or with the fix it is a clean curve. The same rule applies to any future body-level
detail normal — never gate it by the sun's elevation, always by the surface's own
incidence.

## What was measured

* A 5 m × 20 m wall with the terrain material cast a crisp shadow on the terrain:
  the new caster pass works.
* The horizon mask (`horizonVis` painted as grey) shows crater floors occluded and
  plains/rims visible at a 1° sun — the pattern follows the relief, not the mesh
  normals.
* A/B with `MacroHorizonStrength` 0 vs 1 at 4 km altitude changed **53 200 of
  921 600 pixels** (5.8%, max delta 104); at ground level on a 1.26 km/texel map the
  change is small (0.4%) because the near field belongs to the cascade map.

## How to check a change

1. `Tools/Galilego/Airless Bodies/Bake lunar macro maps` → the profile must get
   `MacroHorizonA/B` and the two PNGs must appear next to the albedo. For a single
   edit the automatic path does the same; watch the Console for
   `[LunarMacroAutoBake] … готово`.
2. Play, teleport to the terminator (`SimulationRunner.TeleportTo` +
   `SetPlayerOnSurface`), set `SimulationRunner.Paused = true` (the terminator moves).
3. `Shader.GetGlobalFloat("_TerrainHorizonOn")` must be 1 and
   `_TerrainSunDirBody` must be finite.
4. A/B: `HeightfieldTerrain.MacroHorizonStrength = 0` vs 1 and diff the frames.
   On the terrain the horizon must darken crater interiors and leave high rims lit.
5. The shadow range: `VolumeManager.instance.Update(stack, Camera.main.transform, 1)`
   then read `stack.GetComponent<HDShadowSettings>().maxShadowDistance.value` — it
   must be 2000 on Artemis and 250 on Terra (`GameSettingsVolume` still overrides if
   the player set a distance).
