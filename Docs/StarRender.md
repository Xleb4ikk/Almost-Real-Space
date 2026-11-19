# Physically based star render

One star = one `StarPreset` asset. The component `StarRenderer` derives every
photometric value from `massSolar` through `Galilego.Core.StarPhysics` (pure C#,
double, no UnityEngine). No appearance value is hand-tuned in the renderer.

## What is wired in `OutdoorsScene`

* `Sol/SunVisual ` — `StarRenderer` with `StarPreset_Sol` (**M = 1.0 M☉**,
  a solar analogue: R = 1 R☉, Teff = 5772 K, L = 1 L☉). This is the explicit
  exception for the scene's star; `StarPreset_Default` (M = 0.2 M☉) remains the
  reference preset used by the checks, not by the scene.
  `Runner`, `PlayerCamera`, `SunLight`, `SkyEnvironment`.
* `Star Volume` — global Volume (priority 100) with `StarVolumeProfile`:
  ACES tonemapping, bloom (threshold 1, scatter 0.7), automatic exposure with
  asymmetric adaptation speeds and `exposureBias`.
* `SkyEnvironment.Star` — the same `StarRenderer`, so the Directional Light
  colour/intensity come from the preset (the atmosphere only multiplies its
  transmittance).
* `StarRenderer` applies the derived radius to the system's root body. Gravity
  (μ) and the orbits are untouched.

Menu helpers:

* `Tools/Galilego/Stars/Create Or Update Default Preset` (0.2 M☉ reference)
* `Tools/Galilego/Stars/Create Or Update Solar Preset` (1 M☉ scene preset)
* `Tools/Galilego/Stars/Setup Star Renderer In Open Scene`
* `Tools/Galilego/Stars/Validate Star Physics` (logs M = 0.1…10 M☉)

## Formulas

The full formula set lives in `Assets/_Project/Scripts/Core/Stars/StarPhysics.cs`;
the important pieces:

| Quantity | Formula |
|---|---|
| L(M) | `0.23 M^2.3 / M^4 / 1.4 M^3.5 / 32000 M` |
| R(M) | `M^0.9 / M^0.8 / M^0.57` |
| Teff | `Tsun (L/R²)^0.25` |
| g | `g_sun M/R²` |
| cell | `1000 (Teff/Tsun)/(g/g_sun)` km |
| N | `max(3, 2 R Rsun / cell)` |
| V | `(σ Teff⁴ / E_ref)(η/η_sun)` |
| V' | `K (1 + ln(V/K))` above K = 8 |
| E | `1361 L / d_AU²` W/m² |
| m | `−26.74 − 2.5 log10(E/1361)` |

`η` is the CIE 1931 `ybar`-weighted fraction of the Planck spectrum (Wyman
analytic fit), integrated 380–780 nm over 100–3000 nm; `η_sun` is computed by the
same code. The black-body chromaticity is Kim et al. (1667–25000 K), converted to
linear sRGB, negative channels clipped, luminance normalized to 1, then blended
25 % toward white (`adaptation = 0.25` by default).

The `M = 0.2 M☉` reference (adaptation 0, adaptation only affects the rendered
colour, not the photometry):

* L = 5.6767e-3 L☉, R = 0.23492 R☉ = 163 436 km, Teff = 3268.8 K
* g = 992.9 m/s², cell = 156.3 km, N = 2092
* cT (max-normalized) = (1.000, 0.529, 0.209), xy = (0.4189, 0.3972)
* V = 1636, V' = 50.56
* 0.075 AU: diameter 1.67°, E = 1373.5 W/m² ≈ 1.01 E_sun, m = −26.75,
  30 px at 60° FOV / 1080p
* 1 AU: diameter 7.51′, m = −21.13
* 5.2 AU: diameter 1.44′, m = −17.55, sub-pixel

## Renderer

The disc is a camera-facing quad at `0.25 × far clip` (250 m with the scene's
1 km far plane, 25 km when the space camera extends far). The fragment shader
treats it as an analytic sphere impostor: `μ = sqrt(1 − r²)`, per-channel linear
limb darkening with `I0 = V' c/(1 − u/3)`, 3D Worley granulation anchored to the
star's body frame, threshold spots with Planck-derived umbra/penumbra colours,
and limb faculae. The edge is smoothed by `fwidth` only.

Sub-pixel discs are drawn at half a pixel of radius with the surface brightness
lowered by the area ratio, so the integrated flux is preserved. `f_vis` from the
circle-overlap formula scales the disc and glare; the corona is always additive
and becomes visible when the glare disappears.

Glare is a Moffat profile with `θ0 = 0.15°`, `β = 1.8`, peak radiance
`V' · (E/E_ref) · (η/η_sun) · glareGain · f_vis` (peak-normalized profile, so the
default 8° quad does not become an unphysically bright wall of light). Corona is
`Ic (0.7 ρ⁻⁷ + 0.3 ρ⁻²·⁵)` with `Ic = 1.5e-5 V' coronaGain (0.3 + 0.7 activity)`
and polar noise.

The Directional Light is the physical irradiance in scene units,
`(E/E_ref)(η/η_sun)`, multiplied by `π` for HDRP lux. Its colour is `cT'` with
atmospheric transmittance. The celestial eclipse and the local horizon model are
the same event, so the stricter of the two is used rather than multiplying both.

## Assumptions

1. **HDRP 17.6 / Unity 6000.6.** The custom shaders are HDRP `ForwardOnly`
   transparent passes; the volume uses HDRP `Bloom`, `Tonemapping`, `Exposure`.
2. **Scale is 1 unit = 1 m.** All physics is double in km/au; only the proxy
   position and quad scale are float.
3. **One star per system**, at `StarSystem.Root`. Multiple stars would need one
   `StarRenderer`/preset pair per root.
4. **The star is a sphere impostor, not a real mesh.** A real mesh at the true
   distance would lose float precision; the impostor is exact for the small
   angular radii used by the game (up to a few degrees). Beyond ~60° the flat
   impostor is not used in normal play.
5. **The preset radius is applied to the root body** (for SOI/eclipses) but the
   standard gravitational parameter is not changed. Orbits/baked ephemerides are
   unchanged.
6. **Overrides are allowed but warned**: radius/luminosity/Teff overrides leave
   the mass–luminosity relation and log
   `«вне зависимости масса–светимость»`.
7. **The existing planet shaders keep their normalized solar scale.** The star's
   own Directional Light follows `E/E_ref`; the custom terrain/atmosphere
   shaders still use `_TerrainSun`/`_TerrainRadianceScale` as before, and the
   atmosphere/cloud scattering source remains the existing 5772 K photosphere.
   Re-lighting the whole planet from the preset is a separate change.
8. **Glare is an optical halo**, so it is drawn without depth testing and uses
   `f_vis` to vanish behind a celestial occluder. The corona also is not masked
   per-fragment by the occluder silhouette; the eclipse fraction handles the
   photometry.
9. **HDRP clamps `Bloom.intensity` to 1.0.** `bloomGain > 1` is reported and
   clamped; the remaining energy goes through the glare quad and tonemapping.
10. **Auto exposure is HDRP's histogram/automatic mode**, configured with
    `adaptationSpeedLightToDark = 3`, `adaptationSpeedDarkToLight = 0.7` and
    `exposureBias`. On the first 30 visible frames the renderer adds a temporary
    `−log2((E/E_ref)(η/η_sun))` EV bias and runs both adaptation speeds at 50,
    so a solar star at the scene's close orbit does not flash the first frames;
    afterwards the artistic asymmetric speeds are restored.
11. **TAA** is the camera's existing AA (`antialiasing = TAA`); MSAA is off in
    the HDRP asset. The disc edge uses `fwidth`, so it stays one pixel wide.
12. **Activity defaults to 1** (the task's artistic multiplier range). Spot
    coverage is `activity · fMax`, so activity 0 removes spots and faculae.
13. **The scene deliberately uses a solar analogue.** `OutdoorsScene` is wired to
    `StarPreset_Sol` (M = 1 M☉, R = 1 R☉, Teff = 5772 K), not to the 0.2 M☉
    reference preset. Terra still orbits at 0.368 AU, where a solar star gives
    ≈7.4 E_sun; HDRP auto exposure compensates. Orbits were not changed.

## Tests

```powershell
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "FullyQualifiedName~StarPhysicsTests"
```

Unity EditMode: `Window > General > Test Runner > EditMode`, filter
`StarPhysics` (11 tests), or `unity run_tests --mode editor --filter StarPhysics`.

The Core-only xUnit project compiles `Assets/_Project/Scripts/Core/**` by glob and
deliberately has no UnityEngine reference; `StarPhysics` therefore cannot grow a
Unity dependency unnoticed.
