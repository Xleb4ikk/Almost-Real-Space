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
| Orbit | a = **73 400 km**, e = 0.0824 → 67 351 … 79 449 km |
| Inclination | 5.14° (the real Moon's, so eclipses are not monthly) |
| Spin | tidal lock: 12.647 d = the orbital period |
| Apparent size from Terra | **0.643°** mean, 0.594–0.701° over the orbit (**0.6725°** at 70 200 km). The real Moon is 0.518°, so Artemis is 24 % wider in diameter (54 % in area) — the price of a = 73 400 km at R = 412 km |

The originally requested 170 452–201 067 km is **0.69–0.82 of Terra's Hill radius**, and
`StarSystem.ValidateHillStability` throws above 0.30 (R_H(Terra) = 246 449 km →
a ≤ 73 935 km). Such a moon is not instantly unstable but chaotic on a ~year
timescale, which makes the baked ephemeris meaningless. 73 400 km is 0.298 R_H.

## Authoring rules that are easy to get wrong

* **`RadiusMeters` follows `BodyAuthoring.Radius` automatically** (`BodyAuthoring.ResolveTerrain`),
  because every crater formula is in metres.
* **`NormalEpsilonMeters = 2`** — the normal step used to be a fixed 1e−4 rad, which is
  170 m on the Moon and 41 m here: fine craters would vanish from the lighting.
* **Albedo rides in the colour-detail channel** (`Extra.w`) close up, and in the baked
  macro map (`TerrainProfile.MacroAlbedo`) from a few hundred metres up. The vertex channel
  is faded by pixel footprint (against shimmer) and the map takes over exactly where a
  pixel becomes wider than a map texel — see [MoonLook.md](MoonLook.md). Without the map the
  maria and ray systems wash out with altitude and the Moon looks painted on.
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
1 000 km. Artemis orbits at 73 400 km — **230× beyond the far plane** — so the cube-sphere
chunks are clipped away and the body is not in the frame at all. Raising `far` is not an
option (`near/far` would hit 10^8; see [SkyDomes.md](SkyDomes.md)).

`BodyFarDiscView` (`Scripts/Views/`) is the answer, the same one `GasGiantView` and
`SunBillboard` already use for the gas giant and the star: a camera-facing quad at
`0.28·far` (a quarter of the far plane keeps it ahead of the depth-precision cliff), sized
to the body's **exact angular radius**, shaded with a Lambert phase from the real sun
direction plus limb darkening. The real chunks take over as soon as the surface fits
inside `0.95·far`, so the switch is invisible.

At the camera's 83° vertical FOV, Artemis is **0.643°** in the mean (0.594–0.701° over the
orbit): **6.2 px** in a 983-px-tall view, 6.5 px at 70 200 km. The real Moon is 0.518°
(5.0 px), and the disc is small on purpose — the radius and the orbit are what they are.
For comparison the star is 0.533° at 0.369 AU after the 0.369-scale luminosity change —
smaller than the Moon (0.59–0.70° at 73 400 km), so eclipses are total rather than a
small moon crossing a big sun. A larger disc means a smaller orbit or a bigger body.

## Moonlight on Terra

The same body is a light source: `MoonlightSystem` turns its reflected sunlight into
a dynamic directional light with the real phase law, the observer's position on Terra
and the Moon's own atmospheric extinction. An eclipse dims both the light and the far
disc through the shared `EclipseGeometry`/`StarSystem` rule; the deepest one in the
current rails is `f = 0.326` at `t = 363.486 d`, because the Earth's umbra at 90 000 km
has a radius of only ~9 km and the Moon's centre never reaches totality. See
[Moonlight.md](Moonlight.md) and [Eclipses.md](Eclipses.md).

### The size floor (`MinScreenRadiusPixels`, `GroundScreenRadiusPixels`)

**In vacuum the disc is drawn at the body's true angular radius.** The only floor is the
display's own limit — **0.5 px radius** (one pixel across): a smaller quad is not
representable, so a sub-pixel body becomes a point. `MinEnergyScale = 0` keeps the *total
flux*: when the disc is lifted to that one-pixel floor its surface brightness is multiplied
by `(trueRadius / 0.5 px)²`, so a 5-px Moon is untouched, a 0.2-px one is a dim dot, and
nothing is painted larger than it is. That is what leaves Aeolus (0.044 px true radius) as
one dim pixel instead of a 0.25° black disc that covered the 0.048° Sun.
`MinScreenRadiusPixels = 0` is also available (strictly physical: a sub-pixel quad may
vanish). `Brightness` is a multiplier on the scene's sunlit-surface scale (1 = the Moon's
own terrain, which makes the hand-over seamless); by day the disc then goes through the same
air the sky does, so it is the atmosphere — not the exposure — that decides how washed out
it looks. See [MoonLook.md](MoonLook.md#the-daytime-moon-looked-like-a-moon-with-no-atmosphere).

**The disc is strictly physical: 0.5 px floor, flux-conserving.** `MinScreenRadiusPixels`
(0.5 px = the display limit) is the only floor; `MinEnergyScale = 0` keeps the total flux, so
a sub-pixel body is a faint one-pixel dot, never a lamp. From the pad the Moon is 0.32° =
**6–7 px** and grows monotonically as you approach: 9 px at 50 000 km from it, 44 px at
10 000 km, 110 px at 3 800 km, the mesh below 4 R. Nothing shrinks as you fly away from Terra
and nothing is drawn larger than it is.

`GroundScreenRadiusPixels` (the "readable landmark" floor) exists in the component but is
**0 on every body**, and that is the decision, not an oversight. It was tried twice:

* 11 px at EVERY distance with `MinEnergyScale = 1` — the Moon was 22 px from the pad, 22 px
  at 50 000 km and 22 px at 50 million km: *"the Moon is the same size at 50 000 km as from
  Earth"*;
* 11 px gated to the landscape (100 km → 20 000 km) — *"it shrinks abruptly on a short
  flight"*: 22 px at the pad, 13 px at 9 500 km, 9 px at 20 000 km.

A floor larger than the true size always produces a shrink somewhere, because the physical
Moon from the pad (6.5 px) is smaller than any readable marker. The player chose the physical
size; a navigational marker belongs in the nav tablet, not in the body's angular size. The
fields remain for a future body that wants one (`GroundScreenRadiusPixels > 0`), with the
lift keeping SURFACE brightness so the concession is not immediately undone by flux
conservation.

### The ground went dark brown on approach: the regolith set was overwritten

Report: *"when I get close it changes colour to a dark one"* — the far disc was grey, the
terrain below the 4 R hand-over was dark olive/brown, and the terrain's measured luminance was
0.316 sRGB against the disc's 0.675 at the same phase.

`TerrainTextureDeband.AssignToProfiles` (the clean-texture tool, auto-run keyed by
`Temp/terrain-textures-clean-v1.done`) stamped the Earth **grass/soil** photo set into
**every** `TerrainProfileAsset` with no airless guard. `Temp/` is wiped routinely (Unity does
it on restart, the pipeline uses it for screenshots), so the tool re-ran on later domain
reloads and overwrote Artemis' regolith set. `DetailAlbedoMean` stayed `0.292` (the regolith
mean), so the shader divided the grass photo by the regolith mean — the relative-modulation
path with the wrong set. Result: the ground was ~4× darker than its own palette and green-brown,
while the disc (palette × macro map) stayed grey. The hand-over at 4 R then read as a colour
change, not as more detail.

Two fixes: the tool now **skips `Lunar.Enabled` profiles** (the same guard
`TerrainTextureSetup.Run` has), and Artemis' profile was restored with
`Tools/Galilego/Assign lunar regolith textures`. Measured after: **0.584** sRGB for the ground
at 312 km against **0.675** for the disc, both neutral grey. If a future bake or tool turns the
Moon's ground olive again, check `Artemis.asset`'s `TextureLow/Mid/High/Steep` GUIDs first —
they must be `regolith_flat_clean.png` / `regolith_rock_clean.png`, not `terrain_*_clean`.

`Brightness` is a multiplier on the scene's sunlit-surface scale (1 = the Moon's own
terrain, which makes the hand-over seamless): raising it above 1 is the only "make the Moon
read harder" lever, and it breaks the disc/terrain match by construction.
