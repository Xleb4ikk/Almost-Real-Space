# Flying to a moon: three ways the body disappears

Artemis (Артемида, 412 km airless body in tidal lock over Terra) is the first body the
player can fly to by hand, and the approach exposed three independent bugs that all
looked like "the Moon vanishes when I get close". They are separate; fixing one does not
hide the others.

## 1. The view followed the SHIP, not the camera

`SimulationRunner.DominantBody` is the body of the **craft**: it frames drag, landing and
the contact zone, and it is recomputed from `Ship.Position`. With `Noclip` / free EVA the
player leaves the craft, so the two can be a whole SOI apart — 1 800 km over Artemis with
the ship parked 83 000 km away at Terra.

Every view asked `DominantBody` "which ground is under me":

* `PlanetSurfaceRenderer` hid **all** of its chunks when it was not the dominant body;
* `FirstPersonCamera` built near/far from Terra's radius and got Terra's horizon
  (89 500 km) as `far`;
* `SkyEnvironment` lit the lunar relief with Terra's centre (`_TerrainBodyCenterWS`) and
  Terra's atmosphere.

The far disc, meanwhile, hands the body over at 1.6 R — to chunks that were switched off.
The Moon disappeared at exactly 659 km.

Fix: `SimulationRunner.CameraBody` — the same deepest-SOI rule, on `PlayerPosition`, cached
per frame. Used by the surface renderer, the camera (clip planes and local up) and the sky.
`DominantBody` stays the physics frame.

## 2. A billboard cannot hand over to nothing, and a cut is visible

`BodyFarDiscView` used to cut at `nearSurface < 1.5 R`. Two problems:

* the cut is visible even when everything is built — the quad is flat and unlit-diffuse,
  the sphere is a real Lambert surface, and their brightness differed by 1.35× (measured on
  the same frame with the disc forced opaque and then hidden);
* when the terrain was still arriving (its first view is hundreds of tiles) the cut exposed
  a half-built sphere: a cratered near cap and a black far side.

Fix: the disc fades from `FadeStartRadii` (4 R) to `HandoverRadii` (1.6 R), and it is held
while `PlanetSurfaceRenderer.ViewCoverage` says the view is not covered. Coverage is
computed by the quadtree rule, not by area: a desired node is covered if **any ancestor is
built**, because the traversal already keeps parents as hole plugs. Distance/area weights
both lied here — the missing part was the far half of the visible cap, which is a large
share of the screen but a small share of the solid angle.

`Brightness` was raised 14 → 24 so the disc matches the terrain it becomes.

## 3. near = 0.1 m against far = 1 200 km renders NOTHING

In EVA the near plane is a fixed `EvaNearClipMeters` (0.1 m), and above the atmosphere
`far` is the physical horizon. At 660 km over Artemis that is `far = 1 223 km`, ratio 1.2e7,
and **the whole terrain failed to rasterise** — a black Moon at 660 km, a normal one at
700 km, with nothing in between but the far plane. Setting `near = 1 m` or more brought it
back; the stars were unaffected, which is why it read as "the planet disappeared".

Fix in `FirstPersonCamera.UpdateClipPlanes`: above 10 km altitude the near plane is at least
`far · 2.5e-6`. Below 10 km the mode's near is kept untouched (the cabin interior and the
ground at your feet need centimetres).

## 4. The player's physics ran in the CRAFT's frame

Same split as the view (section 1), but this one hits harder: the player's own step read
`DominantBody` — the craft's body — for everything.

* `LocalFrameVelocity` (the frame noclip and EVA station-keeping are relative to) was
  Terra's while the player flew over Artemis. The Moon moves ~1 km/s relative to Terra, so
  the cheat velocity was relative to the wrong ground: the Moon "ran away".
* `EvaFrameVelocity` used the craft's velocity at the player's point. With the craft parked
  83 000 km away that is a frame the player has nothing to do with — station-keeping held
  the player in it and dragged them off the Moon.
* `StepPlayerEva` tested the terrain contact against Terra's radius, so the lunar surface
  was not solid: 1 km/s into the ground and straight through it. `ShouldUseSurfaceController`
  (altitude < 3 km, < 200 m/s) also never fired, so the walking controller never took over.
* `PlayerUpAstro` / `OutwardFromBody` pointed away from Terra instead of up from the Moon.

Fix: `SimulationRunner.PlayerBody` (== `CameraBody`, the deepest SOI around
`PlayerPosition`) for the whole player path — local frame, EVA frame fallback, terrain
contact, landing, swimming, walking, jetpack normal, up vector. `DominantBody` stays the
craft's frame. `EvaFrameVelocity` falls back to `LocalFrameVelocity` when the craft is in a
different SOI, because "station-keeping beside the rocket" is meaningless from another moon.

Verified in Play: hovering 30 km over Artemis keeps the altitude to the millimetre (was
~1 km/s of drift), descending to the surface switches to the walking controller, and a
1.2 m drop at 20 m/s lands at altitude 0.00 in `OnSurface` instead of passing through.

## 5. "Half the relief is cut off" on the ground

Two different things look the same in a screenshot:

* **The tiles are still arriving.** At ground level the LOD wants ~1 700 nodes
  (depth 6…14 rings around the camera). Each 129×129 upload costs ~2.8 ms of main-thread
  mesh work, and the steady finalize budget was 3 ms — i.e. 2-3 tiles per frame, tens of
  seconds for the set. Until the near ring lands the screen shows the coarse parents (a
  smooth "melted sheet", see the count/area discussion above) and a straight edge where
  the built region ends. Reproduce: `ClearChunkCache()` while standing.
  Fix: while less than 90 % of the desired nodes are built, the finalize budget is raised to
  `CatchUpBudgetMs = 25` (4× the upload rate) and the jobs-in-flight / queue-age fields on
  Artemis are 24 / 16 instead of 12 / 6. Measured: ~9 tiles per frame instead of 2-3, so the
  near field closes in about a second and the full ground view in ~3 s at 60 fps. The
  bottleneck is the upload, not the Burst jobs — more jobs alone did nothing.

* **A mare is genuinely flat.** At lat 0 / lon 180.6 the analytic field varies by 55 m over
  420 m (`min -646 m, max -591 m, sd 11 m`) and the profile's mare terms are
  `MareUndulationMeters = 35`, `RegolithMeters = 2.5`. From an eye 1.3 m above it the horizon
  is ~1 km away, so the converged view is a plain with a perfectly straight horizon; the
  cratered highland further out is drawn but sits below that horizon until you walk up.
  Verified that the mesh matches the physics: the nearest chunk's vertices span 40 m of
  height, exactly the analytic range. Nothing is being clipped.
  Knob if a denser ground is wanted: `MaxDepth` (15 → 19.7 m leaves, ~5.7 px per quad at eye
  height) or `TileResolution`; both cost upload time linearly.

## Diagnosing it again

* `SimulationRunner.Paused = true` first — the ship moves at km/s.
* `Runner.DominantBody` vs `Runner.CameraBody` tells the two frames apart.
* `PlanetSurfaceRenderer` private fields `chunks` / `desired` / `visibleChunks` and
  `ViewCoverage` say whether the geometry exists, is wanted, and is drawn.
* `Camera.nearClipPlane` / `farClipPlane` and the ratio between them: a planet that is
  absent while the stars are fine is a clipping/rasterisation problem, not a shading one.

## The dark shell around the limb — NOT fixed, reverted

`PlanetSurface.shader` is `Cull Off` (a chunk's skirt must be visible from both sides), so
a chunk that straddles the limb also paints its FAR half — beyond the silhouette, unlit —
as a dark shell around the body. With fine leaves it is a 1-2 px sliver; with a coarse
LOD at altitude a 20 px band.

Two per-fragment clips were tried to remove it (tangent plane through the camera, and plain
back-face semantics on the surface normal). **Both cut a razor-straight line across the
GROUND** — the same line the player reported as "half the relief is cut off". In this pass
`positionWS` is not in the space `_TerrainBodyCenterWS` lives in (camera-relative vs
floating-origin render space), so any absolute test derived from them is offset by the
camera delta and eats near-side terrain. The clip is removed; the shader is back to the
plain `Cull Off` behaviour. Doing it properly needs either one explicit space for both
globals, or double-sided skirts + `Cull Back`.

## Geometry budget over Artemis (measured)

Desired LOD nodes and their ideal triangles, `TileResolution 129 / MaxDepth 15 /
SplitFactor 5 / ResolutionHalveEveryLevels 3`:

| altitude | chunks | triangles | vertex data |
|---------:|-------:|----------:|------------:|
| 660 km   | 143    | 0.03 M    | ~5 MB       |
| 100 km   | 659    | 0.4 M     | ~60 MB      |
| 20 km    | 1201   | 1.8 M     | ~200 MB     |
| 2 km     | 2117   | 8.6 M     | ~600 MB     |
| 200 m    | 2861   | 27 M      | ~0.95 GB    |

The near ring (depth 13, resolution 129 — 444 nodes at 200 m altitude) alone is 54 % of
the vertex data: it is the price of ~1 px per quad under the camera. The whole front
hemisphere is built, of which the 83° frustum draws about a quarter; Unity's per-renderer
frustum culling already keeps the GPU cost to that quarter, so the rest is memory and
build time. `GameSettings.terrainDetail` scales `SplitFactor` (chunks ∝ scale²) and
`ResolutionHalveEveryLevels` sets how fast the far rings drop their grid — those two are
the quality/cost knobs.

Measured in the Editor (throttled to ~24 fps, so it bounds nothing): facing the Moon adds
~1.3 ms to the frame against facing empty sky at the same altitude — the terrain is not
the frame-time bottleneck there.
