# Moonlight — real phase, real position, real shadow cascades

The Moon on Terra is not a decorative bluish directional light. Its light is the
star's light reflected from the lunar surface, so every dynamic term is geometry and
photometry:

```
E_moon / E_sun = p · (R_moon / d)² · Φ(α) · f_eclipse
```

| Term | What it is | Where |
|---|---|---|
| `p` | geometric albedo of regolith, **0.136** | `MoonlightPhysics.MoonGeometricAlbedo` |
| `(R/d)²` | the solid angle of the Moon's disc at the observer | `IrradianceRatioToSun` |
| `Φ(α)` | Allen's empirical lunar phase law: `10^(−0.4(0.026α + 4·10⁻⁹α⁴))`, α in degrees | `PhaseFunction` |
| `f_eclipse` | fraction of the Sun's disc still visible from the Moon (Terra's shadow) | `LunarEclipseVisibility` |

`Φ` is the important difference from a Lambert sphere: the regolith backscatters, so
the **quarter moon is ~1/11 of full**, not 1/π. At new moon the empirical fit is cut
smoothly to zero (the true flux is zero).

For Artemis (R = 412 km, a = 90 000 km) full moon is **2.85·10⁻⁶ of the star** — the
real Moon/Earth ratio. That ratio is independent of the star: a brighter star lights
both the Moon and Terra more.

## Night adaptation: the pupil, not a baked-in gain

A moonlit landscape is ~2.9·10⁻⁶ of daylight. On a display calibrated for day it is
black, so the night has to be lifted somewhere. The lift is **one system**: the
observer's pupil.

* `MoonlightSystem.NightAdaptationGainEv = 0` — the moon is **physical**:
  `_MoonLightColor` is the real `E_moon/E_sun` (2.9·10⁻⁶ at full), the light on the
  ground follows phase, distance, eclipse and the air column with no artistic scale.
* `SkyEnvironment` feeds that illuminance into the pupil (`MoonlightLevel`) and opens
  **fully** at night (`share = 1`), capped by `NightAdaptationMaxEv = 14 EV`. 14 EV is
  the authored night level (0.036 of noon ≈ 2.4 lx — the darkest that still reads as a
  lit landscape), so the pupil *is* at its ceiling on any moonlit night: "максимально
  открыт" is the normal state, not an exception. 13 EV is darker, 15–16 EV reads as a
  day-for-night grade.
* The consequence, and the point: the ground is `moon ratio × 2¹⁴`, so a full moon is
  ~11× a quarter moon — the phase stays visible in the ground as well as in the disc.
  A moonless night has nothing to scale and stays black.

Nothing else adds adaptation. `Exposure.limitMin` stays at −1 (a fixed-exposure
daylight camera, see [StarRender.md](StarRender.md)): with −8 the auto exposure lifted
the HDRP/Lit asphalt to 118/255 on the same frame where −1 gives ~10, which is the
"the night ground is insanely bright" bug of 2026-11-24.

Historical note: before the pupil existed the lift was baked into the moon
(`NightAdaptationGainEv = 14`, "the one non-physical step"), and `SkyEnvironment` had
`NightExposure`/`MoonlightExposureBoost` fields to open the eye a little on top. Two
adaptations stacked is exactly how the asphalt ended up looking daylit; the fields are
gone and the gain is 0. `NightAdaptationGainEv > 0` still restores the baked look (and
then the pupil barely opens), but do not mix the two.

Measured 2026-11-24 in Play (physical moon, midnight, `t = 32 400 s`):
`EyeEv = 14.00` and `SunEv = 0`, so `MoonlightGain = 16 384` while
`_TerrainSun = 1.03`. The night lift lives in the moon's own colour
(`_MoonLightColor` = 2.9e-6 × 16 384) and in the HDRP moon light, **not** in
`_TerrainSun`: the shaders multiply the moon term by `sun`, and putting the night
opening there bleached the sunlit half of Terra to a white disc when the camera sat
just past the terminator in orbit (`_TerrainSun` = 1 745 at 54 000 km, `sunEl`
= −5.5°). The star and the moon are two lights with two gains, and the night branch
additionally requires `SunOcclusion ≈ 0` — at altitude the sun is "below the local
horizon" while the planet in front of you is fully lit.

## What the 14 EV is measured against

| `NightAdaptationMaxEv` | ground, share of noon | reads as |
|---|---|---|
| 13 | 0.018 | dark night, asphalt at 0 |
| **14** | **0.036** | the authored night (2.4 lx) |
| 15 | 0.071 | day-for-night grade |
| 16 | 0.14 | overcast day |

## Sky position, atmosphere, horizon

The observer is the **camera eye** and the body is `Runner.CameraBody` (same rule as
`SkyEnvironment`), not the ship. For every moon of that body the component computes:

* the direction to the Moon (drives the directional light, the shader normal and the
  Moon's own far disc);
* `T_moon` through `AtmosphereOptics.TransmittanceToSpace` along the **Moon's** line
  of sight — a low moon reddens and dims at a different time of night than the sun;
* `SkyPhysics.SunOcclusion` at the observer's altitude — the Moon below the horizon
  contributes exactly zero.

The brightest moon wins (`ratio × visibility × sin(elevation)`); a system with two
moons does not add two directional lights, it picks the one that actually lights the
ground. `MoonName` pins a specific moon; empty means auto.

## Where the light goes

`MoonlightSystem` writes:

| Global | Consumer |
|---|---|
| `_TerrainMoonDir` | terrain, grass, decor, water |
| `_MoonLightColor` | direct moon term (already includes phase/eclipse/transmittance/gain) |
| `_MoonAmbientColor` | small night-sky fill so moon shadows are not pure black |
| `_MoonShadowActive` | selects the light that owns HDRP's single cascade map |

The custom shaders add the same Lambert term as the sun:

```hlsl
+ (_MoonLightColor * (sun * moonNdl * shadow * cloudShadow) * _TerrainRadianceScale)
```

`sun` is `_TerrainSun`, which carries the **star's** pupil (`SkyEnvironment.SunEv`,
0 EV when there is no star light at the eye). The moon's own night gain is folded
into `_MoonLightColor` by `MoonlightSystem` (`SkyEnvironment.MoonlightGain`), and
into the HDRP/Lit moon light in the same place — one exposure for the pad, the road
and the terrain, and the star term stays at 1x at night. Without that split a camera
just past the terminator in orbit drew a white planet: the ground's sun term was
multiplied by the 2¹⁴ night opening.

**The camera exposure must not chase the night.** `SkyEnvironment.ExposureVolume` is
a *fixed-exposure daylight camera* (`StarRenderer` keeps `Exposure.limitMin = −1`).
HDRP/Lit props are pre-exposed and the uber-post does not cancel it in Automatic
mode, so with `limitMin = −8` the auto exposure lifted the moonlit asphalt to
**118/255** on the same frame where `−1` gives **10/255** — the "the pupil made the
ground insanely bright" report. The night belongs to the pupil, never to the camera.

`GalilegoCloudField.SampleCloudShadowDirection(pos, dir)` samples clouds along the
**moon's** direction at night, otherwise a moonlit ground would lose its cloud
shadows. Water adds a moon diffuse term, a specular glitter path and a Fresnel mirror
streak (`WaterSurface.shader`), and the Snell window gets the real moon disc.

## Shadows: one cascade atlas, two owners

HDRP carries exactly **one** cascaded directional shadow set
(`HDShadowManager`: *"we only support directional light shadow"*). The sun owns it by
day, the Moon by night, switched with hysteresis while the sun is below −2° and back
at −0.5° (the sun's own light is already zero there). `_MoonShadowActive` tells
`GalilegoSunShadow` which direction to bias the one map with; the same map then
shadows the Moon's direct term on terrain, grass and decor.

The Moon's `HDAdditionalLightData.angularDiameter` is set from its real angular size
(≈0.53° from Terra), so its PCSS penumbra is the Moon's, not the enlarged sun's.

## Lunar eclipse

`MoonlightPhysics.LunarEclipseVisibility` is now a thin wrapper over the shared
`EclipseGeometry` / `StarSystem` rule ([Eclipses.md](Eclipses.md)), so any body — not
just the parent — can shadow the Moon. `MoonlightSystem` uses
`StarVisibilityForBody`, and `BodyFarDiscView` passes the same factor to
`BodyFarDisc.shader`, which dims the visible Moon to a copper disc (`_BFD_Eclipse`,
floor 0.002) instead of showing a full bright moon over a black ground.

With the authored rails the deepest lunar eclipse in a two-year scan is `f = 0.326`
at `t = 363.486 d` (the Earth's umbra at 90 000 km has a radius of only ~9 km, so the
Moon's centre never reaches totality). The copper tint is enabled only when the occluder has
an atmosphere and is at least as large as the Moon (`_BFD_EclipseRed`).

## Scene wiring

* `/Moonlight` — a duplicate of `/Sun` (Directional Light + HDAdditionalLightData,
  High cascade tier), intensity 0 in the scene. `SkyEnvironment.Start` clears the
  shadow-resolution override on the sun; the duplicate keeps the same asset tier.
* `/Sol/SunVisual ` — `MoonlightSystem` with `Runner`, `Star`, `Sky`, `SunLight`,
  `MoonLight`.
* `MoonlightSystem` is `[DefaultExecutionOrder(-50)]`: it runs before `SkyEnvironment`
  so `Sky.MoonlightLevel` is ready for the exposure term.

## Verification (Play)

At `t = 32 400 s` the pad is at local midnight and Artemis is high:

```
teleport to SurfaceFrame (lat, lon, ground + 1.6 m) after SetTime(32400)
```

Expected diagnostics (the moon's position depends on the authored orbit — pick a night
when Artemis is actually up; at `t = 32 400 s` in the current scene it sits on the
horizon and the night is moonless):

| | |
|---|---|
| `ActiveMoon` | Артемида |
| `MoonElevationDeg` | 81.3° |
| `PhaseAngleDeg` | 8.6° (99.4 % lit) |
| `EclipseVisibility` | 1 |
| `IrradianceRatioToSun` | 2.82·10⁻⁶ |
| `SceneIntensity` | 2.8·10⁻⁶ (physical: no gain) |
| `IlluminanceLux` | 2.4 lx |
| `SkyEnvironment.EyeEv` | 14.0 (the night ceiling) |
| `SunElevationDeg` | −88.7° |

A/B test: set `MoonlightSystem.MoonlightScale = 0`; the terrain and the VAB go
**completely black** while a self-lit object does not. That is the whole feature in
one frame.

At the launch site the gas giant **Aeolus** far disc (`GasGiantView`) may cover a large
part of the sky near the Moon; it is a separate artistic proxy with its own screen-size
floor, not a moonlight artifact. The Moon itself is 0.525° — 22 px at 720p/83° FOV, or
the 11 px `BodyFarDiscView` floor at long range.

## Tunables

| Field | Default | Effect |
|---|---|---|
| `GeometricAlbedo` | 0.136 | real regolith; 0 disables the light |
| `NightAdaptationGainEv` | **0** | physical moon; >0 bakes an adaptation into the light (14 = the old look, then the pupil barely opens) |
| `MoonlightScale` | 1 | global artistic multiplier (it also drives the pupil, so the night stays put) |
| `MoonlightTint` | (0.78, 0.88, 1) | slight Purkinje blue; the reflected spectrum is nearly neutral |
| `MoonSkyAmbient` | 0.01 | fraction of direct light used as sky fill |
| `ShareShadowCascades` | true | sun by day / moon by night cascade ownership |
| `SkyEnvironment.NightAdaptationMaxEv` | 14 EV | the night ceiling of the pupil: the authored night level (the pupil is at this ceiling all night) |
