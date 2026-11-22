# Airless bodies — the lunar relief generator, and Artemis

Short version: the game can now build cratered airless bodies (Ceres/Moon/Rhea class)
from a single profile, and Terra has a moon built that way. Three integration details
matter and none of them are visible from the generator's own code:

| Where | What was wrong | What it is now |
|---|---|---|
| `LunarProfileFactory` | the giant basin was an **absolute** 2.4 Mm — on a 412 km body its cavity wrapped the whole sphere | scaled by radius (`1.38 R`, the Moon's own ratio) |
| `PlanetSurface.shader` | `SeaLevel = -1e30` (the "no water" sentinel) fed `t = (h − sea)/amp` into `lerp(rock, snow, (t−0.7)/0.3)` → **HDR white disc + bloom halo** | `t` is clamped to `[0,1]` |
| `LunarProfileFactory` | the "near" hemisphere was `+X`, but `OrbitingBody.ApplyTidalLock` pins body **longitude 180** at the parent → maria on the far side | near axis is `-X`; the basin goes to `+X` |

## Files

| File | Role |
|---|---|
| `Scripts/Planet/Terrain/LunarRelief.cs` | the generator: craters, basins, maria, rays, albedo (Burst, no allocations) |
| `Scripts/Planet/Terrain/LunarReliefSettings.cs` | authoring fields → `LunarParams` for the job; `ContentHash` for live edits |
| `Scripts/Planet/Terrain/LunarProfileFactory.cs` | 4 body styles and the mass → radius → g → crater-morphology physics |
| `Scripts/Editor/AssetSetup/LunarBodyCreator.cs` | window `Tools ▸ Galilego ▸ Airless Bodies ▸ Lunar Body Creator…` |
| `Profiles/Terrain/Lunar/Artemis.asset` | Terra's moon, every parameter editable in the inspector ("Лунный рельеф") |

The window creates the profile asset and the scene body (`BodyAuthoring` + `BodyView` +
`PlanetSurfaceRenderer`, no atmosphere, no water) in one click.

## Artemis

| | |
|---|---|
| Radius | **412 km** |
| Surface gravity | **1.6249 m/s²** — the real Moon's |
| μ / mass | 2.75817e11 m³/s² / **4.1325e21 kg** |
| Density | **14 107 kg/m³** (falls out of R and g; no rock is this dense) |
| Orbit | a = **90 000 km**, e = 0.0824 → 82 584 … 97 416 km |
| Inclination | 5.14° (the real Moon's, so eclipses are not monthly) |
| Spin | tidal lock: 17.171 d = the orbital period |
| Apparent size from Terra | **0.525°** — the real Moon seen from Earth |

The originally requested 170 452–201 067 km is **0.62 of Terra's Hill radius**, and
`StarSystem.ValidateHillStability` throws above 0.30 (R_H(Terra) = 302 208 km →
a ≤ 90 662 km). Such a moon is not instantly unstable but chaotic on a ~year
timescale, which makes the baked ephemeris meaningless. 90 000 km is 0.298 R_H.

## Authoring rules that are easy to get wrong

* **`RadiusMeters` follows `BodyAuthoring.Radius` automatically** (`BodyAuthoring.ResolveTerrain`),
  because every crater formula is in metres.
* **`NormalEpsilonMeters = 2`** — the normal step used to be a fixed 1e−4 rad, which is
  170 m on the Moon and 41 m here: fine craters would vanish from the lighting.
* **Albedo rides in the colour-detail channel** (`Extra.w`). `PlanetSurface.shader` fades
  that channel by pixel footprint, so maria and ray systems wash out at a distance — by
  design, against shimmer.
* **No textures.** The profile leaves `TextureLow…Steep` empty, so the shader uses the
  procedural palette; the lunar field itself comes from the vertex data.
* **`MinCraterDiameterMeters = 24`**: smaller craters exist in physics but alias in the
  mesh at maximum LOD. Each halving adds a crater level — the expensive knob.
