# Sun shadows — a crisp rocket instead of a blob

Short version: a directional shadow in HDRP has three independent knobs that decide
whether a 3 m rocket reads as a shadow or as a smear, and the scene had all three
pointed the wrong way:

| Knob | Was | Now | Where |
|---|---|---|---|
| Per-cascade resolution | **512** (`m_Level: 0`) | **2048** (`m_Level: 2`) | the `Sun` light, `HDAdditionalLightData.m_ShadowResolution` |
| Sun angular diameter (penumbra) | **2.5°** | **the star's real size** (1.47° at 0.37 AU) | `SkyEnvironment.ApplyGlobalsNow` |
| PCSS samples / minimum penumbra | 4 + 8 taps, 1.5 texels | 16 + 24 taps, **2 texels** | the `Sun` light |
| Bias | 0.2 / 0.15 | 0.75 / 0.5 | the `Sun` light |

The useful mental model: `shadowDistance = 250 m` is split into 3 cascades, and the
whole cascade has to fit in one shadow map. At 512 texels the far cascade spends
**44 cm per texel**, so a 3 m rocket is 7 texels wide and the PCSS kernel (which is
sized in *texels*) turns it into a blob. At 2048 the same cascade is 11 cm/texel.

**The penumbra must never be narrower than 2 texels.** That is what
`dirLightPCSSMinFilterSizeTexels = 2` buys: the shadow edge is always a gradient over
at least two texels instead of the texel-aligned staircase that a 1-texel edge turns
into whenever the physical penumbra is small (a high sun) or the cascade is coarse.


## Symptom

On the launch pad the rocket's shadow is a huge, soft, brown-mud blob with faint
concentric rings; the real umbra is a small black core inside it. It does not follow
the rocket's silhouette and it grows/shrinks with the time of day.

Two things were happening at once:

1. **The umbra was quarter-resolution.** `SkyEnvironment.Start()` deliberately
   clears the per-light resolution override (`SetShadowResolutionOverride(false)`) so
   the HDRP asset decides — but the asset only holds a *scalable* array
   `[512, 1024, 2048]`, and the light's tier was `Low` (`m_Level: 0`). The single
   project quality level ("High Fidelity" at index 0) does **not** raise it: the tier
   is a per-light field, not a quality-level field.

2. **The penumbra was five times the sun.** HDRP's `DIRECTIONAL_SHADOW_HIGH` filter is
   PCSS: `filterSize = blockerDistance * tan(angularDiameter/2) * depth2RadialScale`.
   With `angularDiameter = 2.5°` a point 10 m above the pad has a ~1 m penumbra, and
   the *blocker search* cone (`dirLightPCSSBlockerSearchAngularDiameter = 12°`) smears
   it further. Combined with 4 filter taps the smear bands into the rings.

At a grazing sun both effects multiply: the depth gradient over one texel exceeds the
normal bias, so the receiver starts shadowing itself, and PCSS smooths that acne into
the wide brown halo. That halo is what looks "ugly" — it is not the rocket's shadow.

## The rule

* **Never author `angularDiameter` by hand.** It is physics: the star's apparent size.
  `SkyEnvironment` now derives it from `starRadius / starDistance` (wrapped in a
  `SunShadowMaxAngularDiameterDeg` cap, default 2°, only to stop the shadow from
  dissolving when the ship dives at the star). Sol at 1 AU = 0.53°, at 0.37 AU =
  1.44°. The visible sun disc and the penumbra then agree.
* **Keep the per-light `m_ShadowResolution` tier at `High`** on the sun. `Low` is
  512/cascade no matter which pipeline asset is active.
* **HDRP's `normalBias`/`slopeBias` are in texels / normalised units, not metres.**
  0.2 texels (the old value) buys ~3 cm of offset at 1024 texels — nothing against a
  17 cm depth step at a 20° sun. The defaults (0.75 / 0.5) are the sane floor.
* The terrain shader (`GalilegoLighting.hlsl`, `GalilegoSunShadow`) picks the stock
  HDRP filter inside `_ShadowHighDistance`, then GATHER, then one tap. The near field
  — the only place a rocket shadow is readable — therefore pays the PCSS cost and
  benefits from all of the above.

## Night: the Moon owns the cascades

HDRP stores **one** set of directional cascades (`HDShadowManager`:
`m_CachedDirectionalShadowData = new HDDirectionalShadowData[1]`). `MoonlightSystem`
therefore transfers shadow ownership: the sun casts by day, the Moon by night, with a
switch while the sun is below −2° (and back at −0.5°, when the sun's own intensity is
still zero). `Shader.SetGlobalFloat("_MoonShadowActive", 1)` makes
`GalilegoSunShadow` bias the one map with the Moon's direction, so terrain, grass and
decor receive the Moon's shadows. The Moon's `angularDiameter` is its real 0.53°, so
its PCSS penumbra is not the sun's 1.47°. See [Moonlight.md](Moonlight.md).

## Cost

3 cascades at 2048 texels need a 4096x4096 cascade atlas (16-bit): **+30 MB of VRAM**
and 16x more shadow-map texels to rasterise (4x wider per cascade), both only on the
"High Fidelity" asset. If that ever hurts, lower `maxShadowDistance` in the scene's
`SkyAndFog` profile (250 m) *first*: a shorter range shrinks every cascade's texel
instead of blurring it.

## How to check a change

`SimulationRunner` + the pad make this hard to A/B in play mode (the world streams and
the light is driven by the clock). The reliable recipe is a throwaway scene that copies
the *numbers*: a 90 m slab, a 3 m x 20 m cylinder 10 m above it, the same
`HDShadowSettings` (250 m, 3 cascades), `_SunLightColor`/`_TerrainSunDir` set by hand,
a camera with `far = 318686` (the flight camera's far plane!) and `Camera.Render()` into
a RenderTexture. At 512 texels + 2.5° the frame comes out 100% black (the slab
self-shadows); at 2048 + the star's real diameter the same frame is a clean shadow.
