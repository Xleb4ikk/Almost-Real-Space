# Almost Real Space — project notes for agents

Unity **6000.6.0f1**, HDRP, real-scale solar system. Absolute positions are
`Galilego.Core.Vector3d`; everything that is rendered goes through
`Galilego.Universe.FloatingOrigin.ToRender(...)` so Unity only ever sees small
coordinates. Do not put absolute astro positions into `Transform`s.

Full-space-scale rendering has its own class of bugs; the two that already bit us
are documented here:

## Sky domes (atmosphere / clouds / stars) — the "circular hole" at altitude

The atmosphere, clouds and stars are **camera-centred screen-space proxy spheres**.
Their radius must **never** be derived from `camera.farClipPlane` — at altitude
`far` grows to 1e6…1e7 m while `near` stays at 0.03–0.1 m, the depth buffer stops
resolving surfaces at `0.5·far`, and the transparent pass loses fragments inside a
circle around the view axis (atmosphere, clouds and stars disappear there; the
circle grows with altitude).

Always use:

```csharp
float radius = SkyDomeLayout.Radius(camera);   // ~1 km fixed, guarded
```

Full write-up, the checklist for new planets/bodies and the diagnosis recipe that
found it: **[`Docs/SkyDomes.md`](Docs/SkyDomes.md)**.

## Sun shadows — the rocket's shadow is a blob

HDRP directional shadows are PCSS on the "High Fidelity" asset, so the penumbra is
sized in **shadow-map texels** and in **the sun's angular diameter**. Two rules:

* `angularDiameter` is physics, not art: `SkyEnvironment` derives it from the star's
  apparent size every frame (`0.53°` = Sol at 1 AU). Never hand-tune it in the light
  inspector — a 2.5° sun smears a 10 m object into an unreadable halo.
* The per-light `m_ShadowResolution` **tier** decides the cascade resolution
  (`Low` = 512 texels/cascade, whatever the asset says). Keep the sun on `High`.

Full write-up, the symptom/root-cause split and the A/B recipe:
**[`Docs/Shadows.md`](Docs/Shadows.md)**.

## Moonlight — real phase and position on Terra

`MoonlightSystem` turns a moon into reflected star light: geometric albedo,
`(R/d)²`, Allen's lunar phase law (quarter = 1/11 of full) and the parent planet's
shadow; the Moon's own atmospheric transmittance and horizon are applied. The one
non-physical step is `NightAdaptationGainEv` (14 EV) plus
`SkyEnvironment.MoonlightExposureBoost` (0.5 EV) — without them the physical 2.9·10⁻⁶
moon/sun ratio is black under a day exposure. HDRP has **one** cascade atlas: the sun
owns it by day, the Moon by night (`_MoonShadowActive` selects the bias direction).
Full write-up: **[`Docs/Moonlight.md`](Docs/Moonlight.md)**.

## Planets between planets — the SOI is not a visibility boundary

A planet is drawn by its own chunks whenever the **camera** can rasterise it: it is the
camera body, or it is inside the far plane and in front of the eye (`BodyVisibility`).
Never hide a surface because `CameraBody != body` — that made Terra pop out at its SOI
(165 500 km) while still 0.8° across. The far disc (`BodyFarDiscView`) is a **fallback**
for the far-plane cut, not a replacement for a visible mesh (`OnlyWhenMeshMissing`); the
gas giant's disc/mesh switch is by angular size (`MeshHandoverPixels`), not by
`far·0.95` — during a transfer `far` is the Sun's horizon and the destination would be a
0.3 px mesh for the whole flight. The proxy is created automatically for every body by
`BodyView.EnsureFarProxy` (heightfield → far disc with a terrain-derived albedo, gas giant
→ `GasGiantView`); authored components win. Full write-up and the measured numbers:
**[`Docs/Interplanetary.md`](Docs/Interplanetary.md)**.

## The terminator — light must follow the terrain horizon, not just the normal

At 1° sun the *mesh normal* says a crater's inner wall is lit; the real world says the
opposite rim blocks it. Three things must all hold, or craters glow in the dark:
`PlanetSurface.shader` keeps its **`ShadowCaster` pass**, `maxShadowDistance` is
**2000 m on airless bodies** (250 m on Terra) via the `AirlessShadowDistance` volume,
and the terrain samples the **baked horizon map** beyond the cascades. The horizon is
baked by `LunarMacroMapBaker` into `MacroHorizonA/B` (8 azimuth bins, nonlinear 8-bit
angles) and kept current automatically by `LunarMacroMapAutoBaker` (fingerprint +
1.5 s debounce + background worker), so a profile edit or seed change rebakes itself.
Full write-up: **[`Docs/Terminator.md`](Docs/Terminator.md)**.

## Nav tablet — the trail is the selected time window, not a whole orbit

The map draws **every** body's history/forecast over the slider windows, in the
selected ISK: `point(t) = body(t) − frame(t)` for `t ∈ [now − history, now + forecast]`.
It is not just the centred body's trail (that was the "the Moon has no orbit" bug), and
it is **not** a full-period rail: a complete ellipse at T+00:43 ignores the sliders and
is the "green phantom" the player complained about twice. Short-period moons are capped
to three revolutions. The per-body dot in the frames table toggles that body's trail;
the frame body has no trail (its path in its own frame is zero). An `ИСК` press must
not reset the wheel zoom. Dim is not used for trails: history is bright green, forecast
amber dashed, the manoeuvre path white. Full write-up and the headless render recipe:
**[`Docs/NavOrbits.md`](Docs/NavOrbits.md)**.

### How this was got wrong twice — do not repeat

* **"Orbit" in this UI means the trail over the selected window.** The column is
  `СЛЕД · ТЕЛО`, the bottom sliders are `ДЛИНА ИСТОРИИ / ПРОГНОЗА`, and the dots under
  `СЛЕД` toggle the trail. Never draw a full-period conic as the thing those dots turn
  on, and never label it `ОРБИТА`: at T+00:43 with a 14 h slider a whole 17-day lunar
  ellipse is a phantom that ignores the sliders.
* **The original "the Moon has no orbit" bug was a missing loop, not a missing renderer
  pass.** `BuildBodyTrails` built only the centred body's path; with Terra centred the
  Moon's row toggled nothing. Fix the model loop (`SampleBodyPath` per body), not the
  renderer.
* **A parent-anchored conic is frame-independent, so it is wrong here.** The camera
  subtracts the same `frame(now)` offset, so a circle pasted on the parent looks
  identical in every ISK and makes the `ИСК` buttons feel broken. The frame's motion
  must be inside the sampled path: `body(t) − frame(t)`.
* **Reproduce the reporter's exact state before changing semantics**: frame, centre,
  mission time, both slider values, forecast on/off. It usually makes the bug obvious
  (T+00:43 + 14 h + forecast off cannot produce a 17-day ellipse). One clarifying
  question is cheaper than two wrong semantic rewrites.
* **Do not recompile scripts while the user is in Play mode.** Runtime-created UI
  Toolkit objects lose their references on a domain reload; the tablet then throws
  `NullReferenceException` every frame (`NavTabletView.TickCardSliders`,
  `ShipTablet.UpdateWorldPointer`). Stop Play (or wait for the user) before
  `AssetDatabase.Refresh`; after a reload the session is already broken, so stopping it
  is the honest move.
* `Tools/P1bTests` currently does not compile for unrelated reasons; `Test572` is the
  contract, but validate with the standalone probe until the harness is repaired.

## Debugging rendering at altitude

* Freeze the frame first: `SimulationRunner.Paused = true` (the ship moves at km/s).
* Toggle the **component** (`PlanetAtmosphereView.enabled = false`), not just the
  GameObject — the views re-activate their dome in `LateUpdate`.
* A pixel diff of two frozen frames showing a region with delta exactly `(0,0,0)`
  means the pass never ran there, not that it painted black.
* `CheatMenu.OrbitAltitudeMeters` + `CheatMenu.OrbitCapsule()` is the quickest way to
  get to orbit; `SimulationRunner.TeleportTo` + `SetNoclip(true, up)` for a specific
  altitude.
