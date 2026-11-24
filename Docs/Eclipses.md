# Eclipses and planet shadows — one rule for every body

The game has one eclipse geometry, used by every light path:

| Step | Code |
|---|---|
| Angular radii and the uncovered disc area | `Core/Stars/EclipseGeometry.cs`, `StarPhysics.EclipseVisibleFraction` |
| Star visibility from a point (camera, ship, surface) | `StarSystem.StarVisibilityAt` |
| Star visibility for an extended body (centre + whole-body rule) | `StarSystem.StarVisibilityForBody` |
| Fallback for the editor / an unbuilt runner | `StarOcclusion` |

`StarVisibilityAt` loops **all** bodies of the system, skips the star and an optional
`ignoreBody` (the body the observer stands on), and multiplies the visible fraction of
the star's disc. There is no parent-only special case: Terra can shadow Artemis,
Artemis can shadow Terra, a second moon can shadow the first, and a future planet/moon
is included as soon as it is in the `StarSystem` / body tree.

`ignoreBody` exists because the body under the observer also has a horizon; that is
modelled smoothly by `SkyPhysics.SunOcclusion`. Counting the body's own sphere a second
time produced a hard step at sunset.

## Who consumes the factor

| Render path | Field |
|---|---|
| Sun disc, glare, corona, lens flare, HDRP Directional Light | `StarRenderer.EclipseVisibility` (disc/glare/corona/flare) and `StarRenderer.ApplyLight` (light) |
| Terrain, grass, decor, water, underwater fog — direct sun and sky ambient | `SkyEnvironment.EclipseVisibility`; it multiplies `_SunLightColor`, `DayFactor`, sky luminance and the ambient radiance |
| Atmosphere dome in-scatter | `PlanetAtmosphereView`: observer factor for the camera body, body factor for a foreign dome |
| Clouds | `SkyEnvironment.DayFactor`, already multiplied by the eclipse |
| Distant body disc (Moon/Terra far proxy) | `BodyFarDiscView`: `_BFD_Eclipse`, plus `_BFD_EclipseRed` for the atmospheric "blood moon" tint |
| Gas giant near sphere and far disc | `GasGiantView`: `_GG_Eclipse` |
| Moonlight on the ground | `MoonlightSystem`: `EclipseVisibility`, the same `StarVisibilityForBody` |

The copper lunar-eclipse tint is only enabled when the occluder has an atmosphere
**and** is at least as large as the eclipsed body (`EclipseGeometry.CoversBody`). An
airless occluder leaves a neutral dim disc; a small moon transiting a planet does not
turn the planet red.

## The whole-body rule — why a transit does not dim a planet

`StarVisibilityForBody` evaluates the star visibility at the body's centre. That is a
good proxy for a moon inside its planet's shadow, but it is wrong for a small moon
transiting a planet: the centre can be eclipsed while the rest of the planet is fully
lit. The returned factor is therefore used only when

```
angularRadius(occluder, bodyDistance) >= angularRadius(body, bodyDistance)
```

i.e. when the occluder can cover the whole disc (`EclipseGeometry.CoversBody`). Terra
seen from Artemis stays at `f = 1` during a solar eclipse; Artemis seen from Terra goes
to `f = 0.326` during the deepest lunar eclipse. The observer's own `StarVisibilityAt`
is not subject to this rule: a person in the umbra really does see the Sun covered.

## What the current rails actually produce

> **Stale after the 0.369-scale Sun (2026-11-24).** The table below was measured with
> the old `StarPreset_Sol` (R = 1 R☉, 1.444° disc) and the old 90 000 km Artemis orbit.
> The scene now uses a 0.3689 R☉ star (0.533° disc) and Artemis at 73 400 km
> (0.59–0.70°), so the Moon is **larger** than the Sun and a total solar eclipse is
> geometrically possible. Re-measure the times and `f` with `Tools/Eclipse/EclipseScan`
> before quoting them; do not reuse the numbers below.

Measured from `OutdoorsScene` (Sol R = 695 700 km, Terra R = 1143 km at
55.1876e6 km, Artemis R = 412 km at 90 000 km), with the Kepler rails and a 10-minute
scan:

| Event | Time | Deepest `f` |
|---|---|---|
| Solar eclipse on Terra (Artemis transits the Sun) | `t = 88.743 d` | **0.846** (≈ 15 % of the Sun's disc) |
| Lunar eclipse (Artemis in Terra's penumbra) | `t = 363.486 d` | **0.326** (≈ 67 % of the Sun hidden) |

A **total solar eclipse is impossible with the authored scale**: Artemis is 0.36 of
the Sun's angular diameter (0.525° vs 1.47° at 0.369 AU), so the best alignment is an
annular/partial eclipse. A **total lunar eclipse** needs the Moon's centre to pass
within the ~9 km umbra radius at 90 000 km (the Earth's shadow cone converges at
~90 700 km); the current scan never reaches it. The geometry is correct; the scene's Moon is simply
too small for the big local sun.

If the design later wants a dramatic total eclipse, the documented lever is the Moon's
orbit: ~32 100 km makes it exactly the Sun's angular size and is still 0.106 of Terra's
Hill radius (`Docs/LunarBodies.md`). Do not fake it with a shader threshold — the same
`f` drives the sun disc, the terrain, the atmosphere and the moonlight, and they must
agree.

## How to inspect it

* `StarRenderer` HUD: `f_vis` (the star disc's visible fraction).
* `SkyEnvironment` inspector: `EclipseVisibility`, `DayFactor`, `AmbientAmount`.
* `MoonlightSystem` inspector: `EclipseVisibility` for the active moon.
* `SkyEnvironment.SunsetDiagLog`: the `SUNSET` line now carries `ecl=…`.
* Time warp to `t = 88.743 d` (solar, from Terra) or `t = 363.486 d` (lunar, from
  Artemis). At the second one, stand on the sub-Earth point of Artemis; the Sun is
  high but blocked by Terra, and the ground, the HDRP light and the far disc all show
  the same 0.326.

## Rules for future bodies

1. Do not write a new eclipse formula. Call `StarSystem.StarVisibilityAt` /
   `StarVisibilityForBody` (or `StarOcclusion` in the editor).
2. Do not special-case a parent. Every body in the system is an occluder.
3. Do not apply a body-centre factor to a whole planet without the
   `CoversBody` check; that is how Terra once dimmed by 15 % from the Moon.
4. If a new render path lights a body, multiply its direct sun term by the same factor.
   If it has its own atmosphere, pass the body's factor to its in-scatter.
