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
* **A body's scale is its DIAMETER and must never inherit its parent's.** The scene nests
  `Артемида` under `Terra` for organisation, and `Terra`'s transform is scaled by 2 286 000.
  `BodyView` reparents every body to a flat runtime root; keeping the *world* scale
  multiplied the moon by Terra (824 000 × 2 286 000 ≈ 1.9e12), and its `SphereCollider`
  with it — a sphere 1e12 m across, invisible from the inside, whose "penetration" pushed
  every PhysX body near the site out at `maxDepenetrationVelocity` (the rocket on the pad
  left at ≈10 m/s the moment physics was switched on). The view now restores the body's
  own authored scale after the reparent.

## Why you cannot see it: the far plane is 300 km

`FirstPersonCamera.UpdateClipPlanes` sets `far` from the physical horizon, not from the
content: standing on Terra that is **319 km**, and at 200 km altitude it is still under
1 000 km. Artemis orbits at 82 000 km — **262× beyond the far plane** — so the cube-sphere
chunks are clipped away and the body is not in the frame at all. Raising `far` is not an
option (`near/far` would hit 10^8; see [SkyDomes.md](SkyDomes.md)).

`BodyFarDiscView` (`Scripts/Views/`) is the answer, the same one `GasGiantView` and
`SunBillboard` already use for the gas giant and the star: a camera-facing quad at
`0.28·far` (a quarter of the far plane keeps it ahead of the depth-precision cliff), sized
to the body's **exact angular radius**, shaded with a Lambert phase from the real sun
direction plus limb darkening. The real chunks take over as soon as the surface fits
inside `0.95·far`, so the switch is invisible.

At the camera's 83° vertical FOV, 0.566° is **5.6 px**: that is the size of the real Moon
in our sky (0.52°), and it is small on purpose — the radius and the orbit are what they
are. For comparison the star is 1.47° at 0.369 AU, 2.6× wider, which is why the moon reads
as a dot next to a big sun. A larger disc means a smaller orbit (32 100 km would make it
exactly the sun's size, still 0.106 R_H) or a bigger body.

## Moonlight on Terra

The same body is a light source: `MoonlightSystem` turns its reflected sunlight into
a dynamic directional light with the real phase law, the observer's position on Terra
and the Moon's own atmospheric extinction. A total lunar eclipse dims both the light
and the far disc. See [Moonlight.md](Moonlight.md).

### The size floor (`MinScreenRadiusPixels`)

5.6 px reads as a star, so the far disc is drawn with a floor of **11 px radius** (22 px
across, ≈2.5° at this FOV). That is a deliberate lie of 4× in size, and it is the only
honest place for it: the *body* keeps its real radius and orbit, only the stand-in is
scaled. The floor is blended by distance (`nearSurface / far`), so as the real chunks come
within reach the disc shrinks back to its true angular size and the switch stays invisible.
`MinEnergyScale = 1` keeps the surface brightness (the disc gets bigger, not dimmer);
`MinScreenRadiusPixels = 0` restores the strictly physical size. Whether the disc lands as
white or mid-grey is settled by HDRP auto-exposure, not by `Brightness` — the shader value
only decides it relative to the rest of the frame.


