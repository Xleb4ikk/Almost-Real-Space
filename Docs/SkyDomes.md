# Sky domes at altitude — the "circular hole" bug

Short version: **never tie the radius of a camera-centred sky dome to `camera.farClipPlane`.**
Use `SkyDomeLayout.Radius(camera)`.

Affected: `PlanetAtmosphereView` (AtmosphereDome), `PlanetCloudsView` (CloudsDome),
`StarFieldView` (StarField) — all under `Assets/_Project/Scripts/Planet/`.

---

## Symptom (what the player sees)

Flying above roughly 100–200 km, a **circle centred on the view axis** appears in
which the atmosphere disappears (the raw planet/space shows through). The higher
the camera, the **bigger the circle**. Looking at the sky, the **stars** vanish in
the same circle, and the **clouds** do too. Above a certain altitude the effect
goes away — that is when the body stops being dominant and the views switch their
domes off (`Runner.DominantBody != body`), not because anything got fixed.

It looks like a shading bug, but it is not: the dome's fragments are never
rasterised in that region.

## Root cause

All three domes are **camera-centred screen-space proxies**: their shaders only use
`input.positionOS` as a ray direction and march the real geometry analytically.
The proxy's distance therefore has **no effect on the image** — only on the depth
at which it is drawn.

The old code scaled all three to `camera.farClipPlane * 0.5f` (the star dome ended up
at the same radius: a `CreatePrimitive(Sphere)` has radius 0.5, and it was scaled by
`far`). That is harmless on the ground, where `far` is 20–300 km.

At altitude it is not:

* `FirstPersonCamera.UpdateClipPlanes` derives `far` from the horizon
  (`SkyPhysics.MaxSightDistance`), so `far ≈ 1.4e6 m` at 400 km and `≈ 1.2e7 m` at
  10 000 km;
* `near` stays at **0.03 m** in the cabin and **0.1 m** in EVA
  (`SeatedNearClipMeters`, `EvaNearClipMeters`).

A near/far ratio of 1e7…1e8 leaves the depth buffer unable to separate two surfaces
at `0.5·far`. With the atmosphere and star domes at *exactly* the same radius, the
transparent pass loses fragments inside a cone around the view axis. Measured at
365 km altitude: hole radius ≈ 35°, and an A/B diff (dome on/off, same frozen frame)
showed the atmosphere contributed **exactly `(0,0,0)`** over the whole planet disc.

## Fix (what is in the code now)

`Assets/_Project/Scripts/Planet/Sky/SkyDomeLayout.cs`:

```csharp
float radius = SkyDomeLayout.Radius(camera);   // ~1 km, guarded
```

* fixed `MinRadiusMeters = 1000f`, raised to `near * 10` if the near plane is large;
* capped at `far * 0.25` so it can never approach the far plane.

At ~1 km the proxy is always in the well-resolved part of the depth range,
independent of altitude and of the near/far ratio.

Occlusion does not depend on the proxy distance, so nothing else had to change:

* **atmosphere / clouds** — `ZTest Always` plus their own analytic occlusion from
  `LoadCameraDepth`/`IsSky`;
* **stars** — the shader discards every pixel where `IsSky(pixel)` is false, so it
  does not need to sit behind the planet either.

Verified in play mode at 5 km, 400 km, ~1200 km and 10 000 km: the haze covers the
whole disc uniformly, the clouds render, and no stars appear over the planet.

## Same failure, same remedy: the sun disc

This is the second time this exact artifact bit us, and the first fix is the
precedent to copy:

* `279cb0a` — the sun disc was a billboard at `0.9·far`. With `near = 0.03` and
  `far ≈ 1e6` (ratio ≈ 3e7) the quad's clip-space z collapsed onto the far plane
  and the rasterizer dropped the whole quad: near a planet the sun vanished.
  Moving it to `0.25·far` kept the same angular size (the shader does its own
  depth test) and fixed it.
* `ac738a4` — the same disc's occlusion, moved from `ZTest LEqual` into the
  fragment shader.
* the glare and the corona had **no** occlusion at all (`ZTest Always`, no depth
  read): the disc was correctly hidden behind a hull while an additive Moffat halo
  and the corona kept painting the sun through it — with bloom on top that reads as
  a glowing sun inside the ship. All three proxies now share
  `GalilegoStarOcclusion.hlsl` (`StarPixelOccluded`), so "scene depth hides the
  star" has exactly one implementation.

`SkyDomeLayout.MaxFarFraction = 0.25f` follows that precedent: nothing that is
supposed to be visible may sit near the far plane when the near/far ratio is in
the 1e7 range.

## Checklist when adding a new body / planet / sky effect

1. Any new camera-centred dome or full-screen proxy: `SkyDomeLayout.Radius(camera)`.
   Do **not** copy `far * 0.5f` from older code.
2. Prefer `ZTest Always` + explicit occlusion (`IsSky`, analytic ray/sphere) over
   relying on the depth test at planetary distances. A new additive star/sky proxy
   needs `StarPixelOccluded` too — `ZTest Always` alone means *everything* in front
   of it is invisible.
3. If a new dome must sit farther away for some reason, keep it **well inside `far`**
   and **distinct** from the other domes' radii — two coincident transparent domes at
   the same depth is exactly what made this bug deterministic.
4. Watch the near/far ratio: anything above ~1e6 starts to be suspicious, above ~1e7
   is broken. `FirstPersonCamera.UpdateClipPlanes` owns `near`/`far`; the EVA/cabin
   overrides are the reason the ratio explodes at altitude.

## If a hole ever comes back (diagnosis recipe)

This is the procedure that actually found the bug; it works for any "something is
missing in a circle" report.

1. **Freeze the frame.** `SimulationRunner.Paused = true` (via `unity_eval`), then
   take screenshots with `unity_screenshot`. Without this the ship moves at km/s and
   A/B comparisons are meaningless.
2. **Toggle the suspect, not the GameObject.** The views re-activate their dome in
   `LateUpdate`, so `GameObject.SetActive(false)` is undone next frame. Disable the
   **component** (`PlanetAtmosphereView.enabled = false`) *and* the object.
3. **Diff two PNGs pixel by pixel** (load them into `Texture2D` in an eval). A large
   region where the delta is **exactly `(0,0,0)`** means the pass never ran there
   (a shading bug would still change some pixels).
4. **Bisect the depth range.** Set `camera.nearClipPlane = 10` or
   `camera.farClipPlane *= 10` (disable `FirstPersonCamera` first, it rewrites them
   every frame). If the hole disappears, it is a depth/precision artifact, not a
   shading or geometry bug.
5. **"Ping" the shader** if you need to separate "not rasterised" from "shader
   returned black": add a temporary debug mode that returns a constant colour at the
   very top of `Frag`, force a reimport
   (`AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate)`) — Unity does
   not always pick up shader edits while in play mode.
6. Remember the two depth guards in the shaders: `ATM_NEAR_OCCLUDER_M`
   (`PlanetAtmosphere.shader`) and `CLD_NEAR_OCCLUDER_M` (`PlanetClouds.shader`).
   Both replace a coarse/quantized screen-space depth with the analytic planet sphere
   when the camera is above the shell — if a *different* circular hole ever appears
   over the disc only, check those thresholds first (and `marchEnd <= marchStart`
   discards in the cloud march).
