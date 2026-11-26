# Flying between planets: the planet that disappeared at its SOI

Report: "перелёт между планетами ломает планеты" — during a flight from one body to
another the planet vanishes / appears as a broken sliver. There were three independent
defects in the same path. None of them is in the transfer math (`Porkchop`/`LambertSolver`
are not called by the game loop at all): the ship flies manually, and the view layer was
built around "the camera body is a planet".

## 1. The surface was gated on the SOI, which is a GRAVITY boundary

`PlanetSurfaceRenderer.LateUpdate` hid every chunk as soon as

```csharp
Runner.CameraBody != body && (PlayerPosition - bodyNow).Magnitude > body.Radius * 2d
```

`CameraBody` is the deepest SOI around the eye. Terra's SOI ends at **165 500 km**, where
Terra is still **0.79° across (~8 px at 983p)** — a clearly visible disc. Crossing that
line switched the whole planet off, and no far disc existed for Terra (only Artemis had
one). On the way back the planet popped in half-built.

Measured in Play (frozen runner, camera aimed at Terra):

| camera distance | `CameraBody` | visible chunks | `ViewCoverage` | frame |
|---|---|---|---|---|
| 161 100 km | Terra | 4 | 1.00 | Terra present |
| 171 100 km | **Sol** | **0** | **0.00** | Terra absent, only the Sun's glare |

Fix: ask the RENDER question instead (`BodyVisibility`):

* the body is the camera body, or the eye is within `2R` (cold-cache safety net), or
* the body is inside the camera far plane (`distance − R < far·0.98`), in front of the
  camera (its angular radius is the margin), and at least 0.5 px across.

The SOI no longer decides visibility. Terra now stays rasterised across its SOI; the
0.5 px floor hands a truly subpixel body to its far disc instead of aliasing a 4-chunk mesh.

## 2. A far disc must be a FALLBACK, not a replacement

`BodyFarDiscView` used to be opaque from 4R to infinity, because it was written for a body
whose chunks are only drawn while it is the camera body. With the gate fixed, Terra's real
mesh is drawn at 170 000 km and the disc would have painted over the better image.

New `OnlyWhenMeshMissing` (Terra): the disc is used only while `SurfaceVisible == false`
(the real LOD cannot be rasterised). When the mesh appears, the disc holds until
`ViewCoverage ≥ 0.98` and then fades over ~0.7 s — the same hand-over as the Moon, so an
async first view never shows a hole. Artemis keeps the old policy (disc up to 4R → 1.6R).

## 3. Terra's disc had no colour

An atmospheric planet has no baked macro map (the lunar baker is for airless bodies), so
the generic disc would have been a flat grey ball. `BakeTerrainAlbedo` now builds an
equirectangular **sRGB albedo** from the body's own `HeightfieldTerrain` + palette:

* `TerrainMacroAlbedo` — 512×256, `TerrainNoise.SampleHeight` + polar-ice weight, the
  same `TerrainPalette.SurfaceColor` the nav globe uses (extracted from `NavMapRenderer`,
  one definition now), `light = 1` so the disc shader owns the sun;
* built once on a worker, only the first frame the disc is actually needed;
* `BodyFarDisc.shader` gained `_BFD_MapColor` (1 = RGB albedo, 0 = the lunar scalar
  R×2 map) and repeat sampling for the 360° wrap.

Terra from Artemis is ~15 px: the oceans, land and ice caps are the real palette, and
Terra's cloud dome (queue 3050) composites over the disc. The ocean world no longer reads
as a grey coin.

## 4. The gas giant switched to a subpixel mesh for the whole cruise

`GasGiantView` chose the near sphere when `nearSurfaceDistance < far·0.95`. During an
interplanetary leg `far` is the **Sun's horizon** (≈1.6·10¹¹ m), so the sphere was chosen
for the whole flight and the far disc — with its `MinScreenRadiusPixels` floor — was off.
Measured at the Terra–Aeolus midpoint: distance 89.5 million km, **0.34 px**, `near=True`,
`far=False`.

Fix: `nearVisible` also requires `pixelRadius ≥ MeshHandoverPixels` (8). Above it the mesh
is the better picture; below it the disc is, at the true angular size with the floor.
The disc is placed at `min(0.25·far, 0.55·nearSurface)` so it stays in front of the body.
Brightness is matched at the hand-over: `FarDiscBrightness = 3` (= `SurfaceBrightness`).
Below the floor the disc is flux-conserving (`MinEnergyScale = 0`): the one-pixel
stand-in dims with the body's real area — the old `0.3` floor kept a 0.34-px Aeolus
brighter than physics, which is what made distant bodies read as lamps.

## 5. The star was treated as ground

When `CameraBody` is the root, `up` points away from the star and `sunDir` at it, so
`sinEl = −1` and `SkyPhysics.SunOcclusion` returned **0.496**: the HDRP sun was at half
intensity for the whole leg. `MoonlightSystem` also picked the nearest planet as the
star's "moon" (measured `ActiveMoon = Aeolus`), stealing the shadow cascade.

Fix: `body.Parent == null` means "this is the star, not a ground" —
occlusion = 1, and no moon candidates. Also `PlanetAtmosphereView` / `PlanetCloudsView`
now follow `CameraBody` (the documented eye rule), not the ship's `DominantBody`; a body's
own atmosphere is enabled while the body is visible, so it no longer pops at the SOI. A
foreign body's cloud deck is lit by direct sunlight (`transmittance = 1`, no observer
ambient): Terra's clouds no longer go black from Artemis at night.

## Adding a planet — the proxy is automatic

`BodyView.EnsureFarProxy` runs at body start and fills in what a body is missing
(this is the rule for every existing and future body, not per-body scene wiring):

* a **heightfield body** gets a `BodyFarDiscView` configured from its own
  `TerrainProfile`: `MacroAlbedo` (the lunar-style scalar R×2 map) if baked, otherwise
  `BakeTerrainAlbedo` (colour from the palette/noise, 512×256, lazy + worker);
  `MacroNormal` for relief; `LimbDarkening` 0.3 airless / 0.1 atmospheric;
  `OnlyWhenMeshMissing` (the real mesh wins whenever it can be rasterised);
  2 px floor; `NightBrightness 24` for the 14 EV moonlit scene;
* a **gas giant** (`BodyAuthoring.GasGiantPreset != null`) gets a `GasGiantView` built
  from the preset, with `SurfaceBrightness`/`FarDiscBrightness = 3`, `MinEnergyScale 0.3`
  and the 8 px mesh/disc hand-over.

Authored components always win: Terra, Artemis and Aeolus keep their tuned scene values,
and the auto path only creates a component that is not there. A new body needs
`BodyAuthoring` + `BodyView` (plus `PlanetSurfaceRenderer` for terrain or a
`GasGiantPreset` for a gas giant) and nothing else — it is visible and physically sized
from any other body's surface the moment it is added.

## Verification

Frozen runner (`Paused = true`), teleport + `SnapLookDirection`, 1954×983 game view:

* 161 100 km / 171 100 km from Terra: `Terra.SurfaceVisible` true on both sides of the
  SOI, 4–5 chunks, coverage 1.00; the frame is identical apart from the perspective.
* Terra from Artemis (+500 km): disc active, `opacity = 1`, baked map 512×256, a
  blue/white disc with the real palette; no grey ball.
* Terra–Aeolus midpoint: `camBody = Sol`, Aeolus `near=False far=True`, disc intensity
  0.90, a stable floored dot instead of a 0.34 px mesh.
* 3.05 million km from Aeolus: `pxR = 10.05`, `near=True far=False` — the mesh takes over
  above 8 px.
* No shader errors, no exceptions in the console.

## Still open

* The in-ship/EVA near plane is now the mode's near (`SeatedNearClipMeters` 0.03 m,
  `EvaNearClipMeters` 0.1 m); the old `far·2.5e-6` floor was removed. It was added for a
  black Moon at 660 km that turned out to be the far-disc hand-over, and at 200 km it was
  2.7 m — enough to cut the whole cabin out of the frame. The cookie-cutter probe at Terra
  200 km and Artemis 660 km renders the terrain at 0.03 m exactly like at 3 m
  (`Docs/MoonApproach.md` §3). The remaining limit is the **far disc** at extreme
  distances: it sits at `0.25·far`, so at tens of millions of km its clip-space depth can
  still collapse; that is the case to test if a planet ever vanishes while the cabin is
  visible.
* A far disc of an atmospheric body gets the body's own atmosphere only through the
  foreign dome at queue 3005; the disc's own baked map has no cloud layer, so at > ~30 px
  the real mesh (with clouds) is still the better representation — which is why the disc
  is a fallback.
