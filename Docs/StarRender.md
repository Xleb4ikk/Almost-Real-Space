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
  ACES tonemapping, subtle bloom (threshold 1, scatter 0.5, gain from the preset,
  0.1 for Sol), automatic exposure with
  asymmetric adaptation speeds and `exposureBias`.
* `SkyEnvironment.Star` — the same `StarRenderer`, so the Directional Light
  colour/intensity come from the preset (the atmosphere only multiplies its
  transmittance).
* `StarLensFlare` — the **observer's** optics on the same `SunVisual ` object
  (created automatically, `StarRenderer.OpticalFlare`). Veiling glare or, in
  `CameraLens` mode, aperture ghosts and the diffraction star. See “Observer
  glare” below.
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
toward white by `adaptation` (default 0.25; `StarPreset_Sol` uses **1.0** — a
fully white-balanced eye, so the Sun is white in vacuum and only the atmosphere
can tint it).

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
circle-overlap formula scales the disc and glare.

Glare is a Moffat profile with `θ0 = 0.15°`, `β = 1.8`, peak radiance
`V' · (E/E_ref) · (η/η_sun) · glareGain · f_vis · aureole` (peak-normalized
profile, so the default 8° quad does not become an unphysically bright wall of
light). `aureole = clamp(−ln T_luminance / 0.15, 0, 1)` is the air column in front
of the star: the halo around the Sun is scattered sunlight, so it fades with the
atmosphere and is exactly zero in vacuum (sharp disc). Corona is
`Ic (0.7 ρ⁻⁷ + 0.3 ρ⁻²·⁵)` with
`Ic = 1.5e-5 V' coronaGain (0.3 + 0.7 activity) (1 − f_vis)³` and polar noise:
the photosphere outshines the corona by orders of magnitude, so it only appears
when an eclipse has hidden the disc.

The Directional Light is the physical irradiance in scene units,
`(E/E_ref)(η/η_sun)`, multiplied by `π` for HDRP lux. Its colour is `cT'` with
atmospheric transmittance. The celestial eclipse and the local horizon model are
the same event, so the stricter of the two is used rather than multiplying both.

The star's own radiation — disc, chromosphere, glare and corona — is attenuated
by the same `SkyEnvironment.Transmittance` (`AtmosphereOptics.TransmittanceToSpace`
for the eye altitude and sun elevation): at the ground the photosphere takes the
warm Rayleigh cast, above the atmosphere `T = (1,1,1)` and the disc is its true
white. Without it the disc kept its vacuum colour at every altitude and the sun
never reddened at sunset. The volume Bloom keeps a neutral tint and a low gain
(Sol preset 0.1, scatter 0.5): a wide halo reads as atmospheric diffusion, and in
vacuum there is nothing to diffuse — only a thin glow around the disc should
remain.

## Observer glare (`StarLensFlare`)

The disc, the atmospheric aureole and the stellar corona belong to the star. What
the **observer's optics** add is a different physical system, so it lives in
`Assets/_Project/Scripts/Planet/Sky/StarLensFlare.cs` (created automatically by
`StarRenderer`; one full-screen additive custom pass at `BeforePostProcess`, i.e.
scene radiance that exposure, bloom and tonemapping handle exactly like the disc).

`Mode` has two values because the observers are not interchangeable:

| | `HumanEye` (default) | `CameraLens` |
|---|---|---|
| pupil | round | N-blade polygonal iris |
| internal reflections | none | aperture ghosts |
| star shape | none — no sun star | λ/D spikes, N-fold |
| veil normalised to | fraction of the source **radiance** | fraction of the source **flux** |

### Human eye

Intraocular stray light dominates: a healthy young eye scatters ≈10 % of the
source's radiance at 1° and ≈1 % at 10° (van den Berg's glare spread function).
The veil is a two-term Moffat normalised to the star's rendered radiance,

```
L_veil(θ) = L·g·(1−w)·(1+(θ/θ0)²)^−β  +  L·g·w·(1+(θ/θ0w)²)^−βw · air
```

with `g = EyeGlare` = 0.07, core θ0 = 0.8°, β = 2.4, and a 30 % wide share at
θ0w = 4°, βw = 1.6. The **core** is the eye's own forward scatter: it is present in
vacuum, so the Sun in space keeps a compact 2–3° glow. The **wide** term is the
column of air in front of the eye — the same forward-scattered aureole the star's
own `Galilego/StarGlare` models — and it is multiplied by the air column

```
air = clamp(−ln T_luminance / 0.15, 0, 1)
```

which is exactly 1 at the ground and exactly 0 above the atmosphere. That split is
what keeps the sun in space a hard, blazing disc instead of a fog ball: a wide
haze around the Sun is an *atmospheric* phenomenon, not an intraocular one.

A source smaller than the eye's PSF core (`EyePointSpreadDegrees`, 0.1°) only
delivers its flux, so the veil is multiplied by `(α/α_psf)²`
(`LensOptics.PointSourceFactor`). The Sun at 0.37 AU (α = 0.73°) is resolved; the
same star at 5 AU is down to ≈0.3.

In EVA the veil is multiplied by `HelmetGlareMultiplier` (1.6): the observer looks
through a scratched, dusty visor, which scatters more than the cornea.

**The eye has no hexagonal ghosts, no diffraction spikes and no sun star**,
and nothing draws any:

* aperture ghosts are reflections between the coated surfaces of a multi-element
  objective — a biological eye has no such surfaces;
* the spikes are the Fourier transform of a polygonal iris — the pupil is round,
  and even if it were not, λ/D at a 3–5 mm pupil is ≈0.01°, ~70× smaller than the
  solar disc, so a resolved disc cannot show them;
* the ciliary corona (the soft rays the literature describes around a very bright
  point light) is not drawn either: a 12-ray starburst is a lens/iris artefact, not
  something the Sun shows at any distance in this game, and at Mars or the gas
  giant it read as exactly the old “rays” the realistic star was supposed to
  remove.

So the HumanEye flare is a smooth two-term Moffat veil only — no radial structure
at any distance. Only `CameraLens` mode, which is a view through an instrument,
draws the λ/D diffraction star.

### Camera lens

Only for a view through an instrument. The veil is flux-conserving (a Moffat
normalised so the scattered flux is exactly `VeilingGlare` of the star's flux),
aperture ghosts sit at `g = −k·s` (a reflection through the optical axis, NDC) with
the aperture polygon shape, chromatics from lateral aberration and the coating
tint, and the diffraction star has `N/2` lines whose width is λ/D (floored at one
pixel, `SpikeDrawWidthRad`) and whose number follows the blade count.

Both observers share the same occlusion and exposure wiring: amplitudes scale with
the star's rendered disc radiance, the atmosphere's per-channel transmittance, the
celestial eclipse (`f_vis`), the cos⁴ field falloff and the frame-edge fade, and a
five-tap depth test over the disc footprint (`GalilegoStarOcclusion.hlsl` contract)
kills the glare when a hull — or a strut across the Sun — blocks the entrance
pupil. `Scale` on the rendered disc matters here: the disc is log-compressed to
V′ ≈ 77 instead of σT⁴/E_ref ≈ 4.6e4, so the **absolute** glare cannot match a
photometer. The eye's veil is normalised to V′ (the only brightness the observer
sees), and the camera's ghost peaks carry `GhostGain`, which exists solely to
compensate that compression.

Debug: `DebugMode` 0 normal, 1 depth coverage, 2 veil, 3 ghosts, 4 spikes;
`DebugHud` prints the mode, the scatter and the disc radiance.

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
8. **Glare is an atmospheric aureole**, so it is drawn without depth testing,
   uses `f_vis` to vanish behind a celestial occluder and fades out with the air
   column (`aureole`), leaving a sharp disc in vacuum. The corona also is not
   masked per-fragment by the occluder silhouette; the eclipse fraction handles
   the photometry and gates the corona itself.
9. **HDRP clamps `Bloom.intensity` to 1.0.** `bloomGain > 1` is reported and
   clamped; the sun's disc stays sharp in vacuum (Sol preset gain 0.1) because
   the halo is the atmosphere's job, not bloom's.
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
14. **The observer is a living eye by default.** `StarLensFlare.Mode =
    HumanEye`; `CameraLens` is for views through an instrument. The two modes are
    physically different systems and are not blended.
15. **Observer glare is additive scene radiance before post-processing**, not a
    post-effect: it goes through the same exposure, bloom and ACES tonemap as the
    disc, and it is occluded by the scene depth (five taps over the disc
    footprint), not by the frame buffer.
16. **Absolute glare is calibrated to the rendered disc**, not to σT⁴/E_ref: the
    disc is log-compressed (V′ ≈ 77), so the eye's veil is normalised to V′ and
    the camera's ghosts carry `GhostGain`. The *shape*, *position*, *occlusion*
    and *scaling with altitude/distance/transmittance* are physical; the absolute
    photometric ratio to a real photometer is not, by construction of V′.
17. **`HumanEye` has no aperture ghosts and no sun star**, and that is a feature,
    not an omission — see “Observer glare”. The veil is smooth at every distance;
    nothing draws radial rays around the Sun.

## Tests

```powershell
dotnet test tests/GalilegoPhysicsTests/GalilegoPhysicsTests.csproj --filter "FullyQualifiedName~StarPhysicsTests"
```

Unity EditMode: `Window > General > Test Runner > EditMode`, filter
`StarPhysics` (11 tests), or `unity run_tests --mode editor --filter StarPhysics`.

The Core-only xUnit project compiles `Assets/_Project/Scripts/Core/**` by glob and
deliberately has no UnityEngine reference; `StarPhysics` therefore cannot grow a
Unity dependency unnoticed.
