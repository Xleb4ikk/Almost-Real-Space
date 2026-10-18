using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Galilego.Universe
{
    /// <summary>Base terrain noise primitive.</summary>
    public enum TerrainNoiseStyle
    {
        /// <summary>
        /// Value noise: scalars at the 8 cell corners, blended trilinearly.
        /// </summary>
        Value = 0,

        /// <summary>
        /// Perlin gradient noise: vectors at the 8 corners, dotted with the
        /// offset from the corner. Same lattice and hash as value noise, so all
        /// profile salts and frequencies stay valid — only the hash
        /// interpretation changes.
        /// </summary>
        Perlin = 1
    }

    /// <summary>How the ridged component of the terrain is assembled.</summary>
    public enum TerrainRidgedMode
    {
        /// <summary>
        /// Sum of (1 − |v|) octaves followed by a smoothstep. Gives parallel
        /// folds: each octave is independent, with no coupling between them.
        /// </summary>
        Legacy = 0,

        /// <summary>
        /// Ridged multifractal (Musgrave): each octave is weighted by a value
        /// inherited from the previous one. Ridges branch and merge into a
        /// network while valleys stay flat, instead of even ripple.
        /// </summary>
        Multifractal = 1
    }

    /// <summary>
    /// <summary>Slope source for the octave domain (SlopeDamp) used by the terrain shape.</summary>
    public enum TerrainSlopeDampMode
    {
        /// <summary>No domain: all octaves are equally weighted.</summary>
        Off = 0,

        /// <summary>
        /// Slope comes from the accumulated shape value. Cheap, but it measures
        /// HEIGHT, not steepness: detail leaves valleys and peaks while mid
        /// heights stay eroded.
        /// </summary>
        Accum = 1,

        /// <summary>
        /// Slope comes from the analytic octave derivative. Measures steepness
        /// properly: plateaus stay smooth, slopes and ridges stay eroded.
        /// Requires a gradient primitive (TerrainNoiseStyle.Perlin) — value
        /// noise has a discontinuous derivative at cell borders, which would
        /// make the domain jitter at seams. With Value this mode degenerates to Accum.
        /// </summary>
        Gradient = 2
    }

    /// <summary>
    /// Flat noise parameters for the Burst job: blittable fields only, no references.
    /// Everything is double (float casts are forbidden): the noise internals must be
    /// double — float cannot hold the offset scheme (seed·91.7 with warp offsets
    /// reaches ~5e6, ulp ~0.5). Float is used only for the output mask arrays
    /// (color does not affect physics).
    /// </summary>
    public struct TerrainNoiseParams
    {
        public int Seed;
        public double BaseFrequency;
        public int Octaves;
        public double Lacunarity;
        public double Gain;
        public double ContinentFrequency;
        public int ContinentOctaves;
        public double ContinentThreshold;
        public double ContinentSharpness;
        public double ContinentDepth;

        /// <summary>
        /// Continent mask amplitude falloff. ≤0 — follow the general Gain.
        /// </summary>
        public double ContinentGain;

        /// <summary>
        /// Strength of the separate continent-mask domain warp, in direction units.
        /// 0 = the mask uses the terrain warp.
        ///
        /// The source is the UNWARPED direction, otherwise a second warp would
        /// stack on the first and the result would depend on their product.
        /// </summary>
        public double ContinentWarpStrength;

        /// <summary>Continent mask warp frequency.</summary>
        public double ContinentWarpFrequency;

        /// <summary>Continent mask warp octaves.</summary>
        public int ContinentWarpOctaves;

        /// <summary>
        /// Ridge term of the continent mask: continentRaw +=
        /// ContinentRidgeMix·(1 − 2·|fBm|). 0 = off.
        ///
        /// The zero line |fBm| = 0 is an isoline along which the mask is maximal,
        /// so the mask gets STRETCHED continents and bays between them instead of
        /// round blobs.
        ///
        /// The term is not centered, so it also biases toward land; the bias is
        /// cancelled by ContinentThreshold.
        /// </summary>
        public double ContinentRidgeMix;

        /// <summary>Continent mask ridge term frequency.</summary>
        public double ContinentRidgeFrequency;

        /// <summary>Continent mask ridge term octaves.</summary>
        public int ContinentRidgeOctaves;

        /// <summary>
        /// Equatorial mask bias, continentRaw += ContinentLatitudeBias·
        /// (1 − 2·|sin φ|). 0 = off.
        ///
        /// Shifts continents toward the equator. |sin φ| has mean 0.5, so the
        /// term is centered and does not change the land fraction by itself —
        /// it only moves the distribution over latitude.
        ///
        /// Reads the UNWARPED direction: latitude must not depend on terrain noise.
        /// </summary>
        public double ContinentLatitudeBias;

        /// <summary>
        /// Abyssal floor depth in normalized units. ≤0 — the legacy path
        /// "minus (1−mask)·ContinentDepth"; &gt;0 — terrain blends with a floor
        /// whose depth grows away from the shore in mask units.
        /// </summary>
        public double OceanFloorDepth;

        /// <summary>
        /// Shelf depth near the shore, normalized units. Multiplied by ocean,
        /// so it is always zero on land. 0 = no shelf.
        /// </summary>
        public double OceanShelfDepth;

        /// <summary>
        /// Interior floor, normalized units. ≤0 — off. Land does not drop below
        /// continent·InteriorFloor: near the shore the floor is ≈ 0 (islands and
        /// bays remain), inside a continent it rises above the sea.
        /// </summary>
        public double InteriorFloor;

        public double OrogenyFrequency;
        public int OrogenyOctaves;
        public double OrogenyThreshold;
        public double OrogenySharpness;
        public double OrogenyFloor;
        public double OrogenyGain;
        public double RidgedMix;
        public int RidgedMode;
        public double RidgedSharpness;
        public double RidgedWeightGain;
        public double RidgedGamma;
        public double SlopeDamp;
        public int SlopeDampMode;
        public int NoiseStyle;

        /// <summary>
        /// Primitive for MASKS and warp, separate from the terrain shape. Follows
        /// NoiseStyle by default. May be set to Value to cut cost: the continent
        /// mask is read as a quantile, so the primitive does not affect its land
        /// fraction and is noticeably cheaper. The shape stays on Perlin.
        ///
        /// The sentinel "follow NoiseStyle" is minus one, resolved in FromTerrain.
        /// The field deliberately has NO initializer: the project is on C# 9.0,
        /// where struct field initializers are unavailable, so a direct
        /// `new TerrainNoiseParams()` yields 0 here, i.e. Value — parameters are
        /// therefore built through FromTerrain, as all production code does.
        /// </summary>
        public int MaskNoiseStyle;

        public double PlainMix;
        public double PlainFrequency;
        public int PlainOctaves;
        public double PlainThreshold;
        public double PlainSharpness;
        public double PlainElevation;

        /// <summary>
        /// Compression of the upper tail of the shape, in normalized units
        /// (like ContinentDepth, NOT meters). Applies to the far tail and barely
        /// touches the bulk of the distribution.
        ///
        /// TailKnee &lt;= 0 — compression off, shape unchanged. This is the
        /// default, on which the legacy profile stays bit-for-bit.
        /// </summary>
        public double TailKnee;

        /// <summary>Compression threshold in normalized units. Below it the shape is untouched.</summary>
        public double TailThreshold;

        /// <summary>
        /// Compression of the lower (ocean) tail, in normalized units, mirroring
        /// TailKnee. DepthKnee &lt;= 0 — off (default).
        ///
        /// Needed because the ridged profile's ocean tail is even heavier than
        /// the mountain one. Reducing ContinentDepth would raise both at once
        /// and drown the shelf, so it is more precise to cut only the tail.
        /// </summary>
        public double DepthKnee;

        /// <summary>Lower compression threshold, normalized units. Above it the
        /// shape is untouched (compression is one-sided and downwards).</summary>
        public double DepthThreshold;

        public double DetailMix;
        public double DetailFrequency;
        public int DetailOctaves;
        public double WarpStrength;
        public double WarpFrequency;
        public int WarpOctaves;
        public int WarpSeedOffset;

        /// <summary>Sea level (m) — for the beach shelf (height normalization).</summary>
        public double SeaLevelMeters;

        /// <summary>Terrain amplitude (m) — for the beach shelf (height normalization).</summary>
        public double AmplitudeMeters;

        /// <summary>Beach shelf height above the sea (m). 0 = shelf off.</summary>
        public double BeachShelfAltitudeMeters;

        /// <summary>Shelf half-width in continent-mask units. 0 = off.</summary>
        public double BeachShelfWidth;

        /// <summary>Max shelf width multiplier (shore noise). ≤1 = constant width.</summary>
        public double BeachShelfWidthMaxScale;

        /// <summary>Shelf width noise frequency (cycles per unit vector). 0 = off.</summary>
        public double BeachShelfWidthNoiseFrequency;

        /// <summary>Shelf width noise octaves.</summary>
        public int BeachShelfWidthNoiseOctaves;

        /// <summary>
        /// Width of the shelf's "seaward shoulder" in continent-mask units: the band
        /// BEYOND the mask threshold where the shelf weight fades to zero. ≤0 — auto
        /// (DefaultSeawardWidthFactor of the shelf width). It replaces the step at
        /// the threshold isoline with a continuous fade.
        /// </summary>
        public double BeachShelfSeawardWidth;
        public double ColorNoiseFrequency;
        public int ColorNoiseOctaves;
        public int ColorNoiseSeedOffset;
        public double ColorDetailFrequency;
        public int ColorDetailOctaves;
        public int ColorDetailSeedOffset;

        /// <summary>Whether to compute the mask in the job (otherwise writes 0). Saves ~15% when the mask is off.</summary>
        public bool ComputeMask;

        /// <summary>Whether to compute small-scale color detail (land mottling).</summary>
        public bool ComputeDetail;

        /// <summary>
        /// Flat pads at planet points (analogous to KSP PQS FlattenArea).
        /// NativeArray — blittable, so the table passes through this struct and
        /// further into the [BurstCompile] render job without managed references
        /// and without changing signatures: the job already receives all of Params.
        ///
        /// [ReadOnly] is NOT cosmetic but a safety requirement. The table is read
        /// and never written, yet without the attribute the job safety system
        /// treats the container field as WRITABLE. Decor builds are scheduled in
        /// batches: StepDecorBuilds schedules several GroundDecorCandidateJob in one
        /// frame with default(JobHandle), i.e. with no dependency between them, and
        /// all receive the same TerrainNoiseParams. Writing to a shared container
        /// without a dependency is a job safety exception and fires every frame.
        /// It also happened with an empty shared table: the check works from the
        /// field declaration, not from the array length.
        ///
        /// HeightfieldTerrain owns the table, not this method: FromTerrain is called
        /// on EVERY GetRawHeightMeters (thousands of times per second), so allocation
        /// here is not allowed — only the descriptor is copied.
        /// A zero-length table means no modifiers.
        /// </summary>
        [ReadOnly]
        public NativeArray<TerrainModifierData> Mods;

        /// <summary>
        /// Zones where decor does not grow (DecorExclusionTable). Same contract as
        /// Mods: HeightfieldTerrain owns the table, FromTerrain copies only the
        /// descriptor, and jobs read it in parallel.
        /// </summary>
        [ReadOnly]
        public NativeArray<DecorExclusionData> DecorExclusions;

        public static TerrainNoiseParams FromTerrain(HeightfieldTerrain terrain)
        {
            // The slope domain is calibrated for Gain*Lacunarity == 1. Otherwise
            // the windows are wrong and the domain silently degenerates to a no-op
            // or a constant, so the profile looks broken though the data is not.
            // The domain is therefore disabled EXPLICITLY, with one log per configuration.
            //
            // Windows are not recomputed on the fly: the correct windows depend on
            // the signal distribution over the whole sphere and require a search.
            // FromTerrain is called on every GetRawHeightMeters, thousands of times
            // per second. Disabling the domain is cheaper and more predictable than
            // silently retuning the terrain shape.
            double damp = terrain.SlopeDamp;
            int dampMode = terrain.SlopeDampMode;
            if (damp > 0d && dampMode != (int)TerrainSlopeDampMode.Off
                && !TerrainNoise.SlopeDampWindowsValid(terrain.Gain, terrain.Lacunarity))
            {
                TerrainNoise.LogDampWindowsOnce(terrain);
                damp = 0d;
                dampMode = (int)TerrainSlopeDampMode.Off;
            }

            return new TerrainNoiseParams
            {
                Seed = terrain.Seed,
                BaseFrequency = terrain.BaseFrequency,
                Octaves = terrain.Octaves,
                Lacunarity = terrain.Lacunarity,
                Gain = terrain.Gain,
                ContinentFrequency = terrain.ContinentFrequency,
                ContinentOctaves = terrain.ContinentOctaves,
                ContinentThreshold = terrain.ContinentThreshold,
                ContinentSharpness = terrain.ContinentSharpness,
                ContinentDepth = terrain.ContinentDepth,
                ContinentGain = terrain.ContinentGain,
                ContinentWarpStrength = terrain.ContinentWarpStrength,
                ContinentWarpFrequency = terrain.ContinentWarpFrequency,
                ContinentWarpOctaves = terrain.ContinentWarpOctaves,
                ContinentRidgeMix = terrain.ContinentRidgeMix,
                ContinentRidgeFrequency = terrain.ContinentRidgeFrequency,
                ContinentRidgeOctaves = terrain.ContinentRidgeOctaves,
                ContinentLatitudeBias = terrain.ContinentLatitudeBias,
                OceanFloorDepth = terrain.OceanFloorDepth,
                OceanShelfDepth = terrain.OceanShelfDepth,
                InteriorFloor = terrain.InteriorFloor,
                OrogenyFrequency = terrain.OrogenyFrequency,
                OrogenyOctaves = terrain.OrogenyOctaves,
                OrogenyThreshold = terrain.OrogenyThreshold,
                OrogenySharpness = terrain.OrogenySharpness,
                OrogenyFloor = terrain.OrogenyFloor,
                OrogenyGain = terrain.OrogenyGain,
                RidgedMix = terrain.RidgedMix,
                RidgedMode = terrain.RidgedMode,
                RidgedSharpness = terrain.RidgedSharpness,
                RidgedWeightGain = terrain.RidgedWeightGain,
                RidgedGamma = terrain.RidgedGamma,
                SlopeDamp = damp,
                SlopeDampMode = dampMode,
                NoiseStyle = terrain.NoiseStyle,
                MaskNoiseStyle = terrain.MaskNoiseStyle >= 0
                    ? terrain.MaskNoiseStyle
                    : terrain.NoiseStyle,
                PlainMix = terrain.PlainMix,
                PlainFrequency = terrain.PlainFrequency,
                PlainOctaves = terrain.PlainOctaves,
                PlainThreshold = terrain.PlainThreshold,
                PlainSharpness = terrain.PlainSharpness,
                PlainElevation = terrain.PlainElevation,
                TailKnee = terrain.TailKnee,
                TailThreshold = terrain.TailThreshold,
                DepthKnee = terrain.DepthKnee,
                DepthThreshold = terrain.DepthThreshold,
                DetailMix = terrain.DetailMix,
                DetailFrequency = terrain.DetailFrequency,
                DetailOctaves = terrain.DetailOctaves,
                WarpStrength = terrain.WarpStrength,
                WarpFrequency = terrain.WarpFrequency,
                WarpOctaves = terrain.WarpOctaves,
                WarpSeedOffset = terrain.WarpSeedOffset,
                SeaLevelMeters = terrain.SeaLevelMeters,
                AmplitudeMeters = terrain.AmplitudeMeters,
                BeachShelfAltitudeMeters = terrain.BeachShelfAltitudeMeters,
                BeachShelfWidth = terrain.BeachShelfWidth,
                BeachShelfWidthMaxScale = terrain.BeachShelfWidthMaxScale,
                BeachShelfWidthNoiseFrequency = terrain.BeachShelfWidthNoiseFrequency,
                BeachShelfWidthNoiseOctaves = terrain.BeachShelfWidthNoiseOctaves,
                BeachShelfSeawardWidth = terrain.BeachShelfSeawardWidth,
                ColorNoiseFrequency = terrain.ColorNoiseFrequency,
                ColorNoiseOctaves = terrain.ColorNoiseOctaves,
                ColorNoiseSeedOffset = terrain.ColorNoiseSeedOffset,
                ColorDetailFrequency = terrain.ColorDetailFrequency,
                ColorDetailOctaves = terrain.ColorDetailOctaves,
                ColorDetailSeedOffset = terrain.ColorDetailSeedOffset,
                ComputeMask = terrain.ColorNoiseFrequency > 0d && terrain.ColorNoiseStrength != 0d,
                ComputeDetail = terrain.ColorDetailFrequency > 0d && terrain.ColorDetailStrength != 0d,
                Mods = terrain.Mods,
                DecorExclusions = terrain.DecorExclusions
            };
        }
    }

    /// <summary>
    /// Burst-compatible heightfield shape: the same formula as HeightfieldTerrain
    /// (operation order mirrored — difference only in the last ulp of codegen).
    /// Pure statics on double3/math: no references, no allocations, no System.Math.
    /// HeightfieldTerrain delegates the shape here, so visual/physics parity is by
    /// construction, not by tolerance.
    /// </summary>
    public static class TerrainNoise
    {
        /// <summary>
        /// Plains' own noise amplitude as a fraction of PlainElevation. Without it,
        /// pulling toward a constant gives a perfectly flat table with sharp edges
        /// at the mask border; a small amplitude reads as a plain.
        /// </summary>
        private const double PlainRelief = 0.03d;

        /// <summary>Octaves in the plains noise.</summary>
        private const int PlainReliefOctaves = 4;

        /// <summary>
        /// Interior floor smoothing width as a fraction of the floor itself.
        ///
        /// The scale is not derived from InteriorFloor because what is needed here
        /// is the SMOOTHING scale, not the magnitude: it keeps the transition zone
        /// narrow so no inland lakes appear while the shore stays sharp.
        /// </summary>
        private const double InteriorFloorSoftness = 0.05d;

        /// <summary>
        /// Smooth maximum with a smooth transition band (Inigo Quilez, polynomial
        /// form). Returns a if a ≥ b + k and b if b ≥ a + k, i.e. OUTSIDE the
        /// transition it is an EXACT max; inside it is a bulge at most k/4 high.
        /// The first derivative is continuous, so normals have no crease.
        ///
        /// A plain math.max is not usable here: lakes appear exactly where the
        /// terrain crosses the floor, and each cut would crease the normals.
        /// </summary>
        internal static double SmoothMax(double a, double b, double k)
        {
            if (!(k > 0d))
            {
                return math.max(a, b);
            }

            double t = math.clamp(0.5d + (0.5d * (a - b) / k), 0d, 1d);
            return b + ((a - b) * t) + (k * t * (1d - t));
        }

        public static double SampleHeight(TerrainNoiseParams p, double3 direction)
        {
            double gain = EffectiveGain(p.Gain);
            double lacunarity = EffectiveLacunarity(p.Lacunarity);
            if (p.WarpStrength <= 0d && p.RidgedMix <= 0d && p.ContinentFrequency <= 0d
                && (p.PlainMix <= 0d || p.PlainFrequency <= 0d)
                && (p.DetailMix <= 0d || p.DetailFrequency <= 0d)
                && lacunarity == 2d && gain == 0.5d)
            {
                return SampleFbmLegacy(p, direction);
            }

            double3 q = direction;
            if (p.WarpStrength > 0d)
            {
                q = ApplyWarp(p, direction, p.WarpStrength, p.WarpFrequency, p.WarpOctaves,
                    p.Seed + (p.WarpSeedOffset * 7919), 100, gain, lacunarity);
            }

            double baseHeight = SampleShapeFbm(p, q, p.BaseFrequency, p.Octaves, 0, gain, lacunarity);

            double continent = 1d;
            double continentRaw = 0d;
            if (p.ContinentFrequency > 0d)
            {
                // The continent mask is computed on its OWN domain qc, not on the
                // terrain warp q, otherwise the shore scale is set by the mountain
                // warp scale.
                //
                // Without a separate warp the domain stays SHARED (legacy, bit-for-bit):
                // using `direction` here would silently remove the mountain warp from
                // the continent mask.
                //
                // The separate warp source is the original direction, not q: otherwise
                // a second warp would stack on the first and the mask domain would
                // depend on the product of two distortions instead of one.
                double3 qc = q;
                if (p.ContinentWarpStrength > 0d)
                {
                    qc = ApplyWarp(p, direction, p.ContinentWarpStrength, p.ContinentWarpFrequency,
                        p.ContinentWarpOctaves, p.Seed, 400, gain, lacunarity);
                }

                double cg = EffectiveGain(p.ContinentGain > 0d ? p.ContinentGain : gain);
                continentRaw = SampleFbmEx(
                    qc, p.ContinentFrequency, p.ContinentOctaves, p.Seed, 1, cg, lacunarity, p.MaskNoiseStyle);

                if (p.ContinentRidgeMix > 0d)
                {
                    // Continent elongation: the mask peaks along the fBm = 0
                    // isolines, i.e. along long chains instead of round blobs.
                    double cr = SampleFbmEx(
                        qc, p.ContinentRidgeFrequency, p.ContinentRidgeOctaves, p.Seed, 11,
                        cg, lacunarity, p.MaskNoiseStyle);
                    continentRaw += p.ContinentRidgeMix * (1d - (2d * math.abs(cr)));
                }

                if (p.ContinentLatitudeBias > 0d)
                {
                    // Pulls continents toward the equator. Reads the UNWARPED
                    // direction: latitude must not depend on terrain noise.
                    continentRaw += p.ContinentLatitudeBias * (1d - (2d * math.abs(direction.z)));
                }

                continent = Smoothstep01((continentRaw - (p.ContinentThreshold - p.ContinentSharpness))
                    / math.max(1e-9d, 2d * p.ContinentSharpness));
            }

            double h = baseHeight;

            // Mountain BELTS, not mountains on all land.
            //
            // The belt multiplies the RELIEF RANGE relative to the lowland level
            // (applied below, after the interior floor), while the ridged detail
            // stays everywhere: outside a belt its detail is pressed against that
            // shelf and reads as a rolling plain. The mask is low-frequency (own
            // stream, salt 10), so ridges gather into belts thousands of kilometers
            // wide.
            //
            // OrogenyFrequency ≤ 0 — off, shape bit-for-bit.
            double beltRelief = 1d;
            bool orogeny = p.OrogenyFrequency > 0d;
            if (orogeny)
            {
                double beltNoise = SampleFbmEx(
                    q, p.OrogenyFrequency, p.OrogenyOctaves, p.Seed, 10, gain, lacunarity, p.MaskNoiseStyle);
                double belt = Smoothstep01((beltNoise - (p.OrogenyThreshold - p.OrogenySharpness))
                    / math.max(1e-9d, 2d * p.OrogenySharpness));
                beltRelief = p.OrogenyFloor + (p.OrogenyGain * belt);
            }

            if (p.RidgedMix > 0d)
            {
                double ridged = (SampleRidged(p, q, gain, lacunarity) * 2d) - 1d;
                double k = math.min(1d, math.max(0d, p.RidgedMix)) * continent;
                h = baseHeight + (ridged - baseHeight) * k;
            }

            // Legacy ocean: a uniform downward offset wherever the mask is absent.
            // Used only when OceanFloorDepth ≤ 0, and then the behavior is bit-for-bit
            // unchanged.
            bool oceanModel = p.OceanFloorDepth > 0d;
            if (!oceanModel)
            {
                h -= (1d - continent) * math.max(0d, p.ContinentDepth);
            }

            double plainK = 0d;
            if (p.PlainMix > 0d && p.PlainFrequency > 0d)
            {
                // Plains: a low-frequency mask (own stream, salt 5) selects zones
                // where the terrain is pulled to a low plateau; ridges remain between
                // them. Multiplied by continent — plains only on land.
                double plainNoise = SampleFbmEx(q, p.PlainFrequency, p.PlainOctaves, p.Seed, 5, gain, lacunarity, p.MaskNoiseStyle);
                double plainMask = Smoothstep01((plainNoise - (p.PlainThreshold - p.PlainSharpness))
                    / math.max(1e-9d, 2d * p.PlainSharpness));
                plainK = math.min(1d, math.max(0d, p.PlainMix)) * plainMask * continent;
                // The pull target is low hills of its own, not a flat shelf: the first
                // PlainReliefOctaves octaves of the same shape (p.BaseFrequency,
                // salt 0) give a gentle slope instead of a table. Salt 0 is shared
                // with the base shape on purpose: the first octaves match, so the
                // shelves lie in terrain valleys rather than detached from them.
                double flat = p.PlainElevation + PlainRelief * SampleFbmEx(
                    q, p.BaseFrequency, PlainReliefOctaves, p.Seed, 0, gain, lacunarity, p.NoiseStyle);
                h = flat + ((h - flat) * (1d - plainK));
            }

            if (p.DetailMix > 0d && p.DetailFrequency > 0d)
            {
                // Small-scale detail (rock/scree): high-frequency own stream (salt 6),
                // absolute amplitude share. Land only, fades in plains — ridges get
                // cut up, plains stay smooth.
                double detail = SampleShapeFbm(p, q, p.DetailFrequency, p.DetailOctaves, 6, gain, lacunarity);
                double detailLand = continent * (1d - plainK);
                h += detail * p.DetailMix * detailLand;
            }

            // INTERIOR FLOOR. No land point drops below continent·InteriorFloor, so
            // there are no inland lakes inside continents. Smoothing instead of max:
            // a hard max creases the first derivative, and the crease shows as a
            // normal band on every lake.
            //
            // Positive ONLY WHERE continent = 1 — in the ocean the mask is zero and
            // so is the floor. The floor must not be applied AFTER the ocean blend:
            // max(depth, 0) = 0 would lift the whole ocean to sea level and it would
            // disappear.
            if (p.InteriorFloor > 0d)
            {
                double floorK = p.InteriorFloor * InteriorFloorSoftness;
                h = SmoothMax(h, continent * p.InteriorFloor, floorK);
            }

            // Relief range by belts (see above). The reference level is the interior
            // floor, the height below which land never drops. Scaling from it downward
            // is impossible (h ≥ ref), so this step creates no new lakes.
            //
            // Requires InteriorFloor > 0: with a zero floor the reference would be
            // sea level and all low land would go under water. Without a floor this
            // step is skipped and the shape is unchanged.
            if (orogeny && p.InteriorFloor > 0d)
            {
                double refLevel = continent * p.InteriorFloor;
                h = refLevel + ((h - refLevel) * beltRelief);
            }

            // OCEAN MODEL. Terrain is blended with the floor instead of subtracting
            // a constant.
            //
            // Shape: h·continent − ocean·(continent·shelf + depth·ocean²).
            // The ocean² multiplier gives an Earth-like profile — a gentle shelf
            // near the shore, a steep continental margin, a flat abyssal plain.
            // The shelf is also multiplied by ocean, otherwise at continent = 1 it
            // would pull land down by its depth. At continent = 1 the whole
            // expression is zero, so land is exactly untouched; at continent = 0
            // only the constant floor remains.
            if (oceanModel)
            {
                double ocean = 1d - continent;
                double shelf = math.min(math.max(0d, p.OceanShelfDepth), p.OceanFloorDepth) * continent;
                h = (h * continent) - (ocean * (shelf + (p.OceanFloorDepth * ocean * ocean)));
            }

            h = ApplyTailCompression(p, h);
            h = ApplyDepthCompression(p, h);
            h = ApplyBeachShelf(p, h, continentRaw, q);

            // Flat pads are applied LAST. Otherwise detail/plains noise would pull
            // the pad apron back and the flat core would stop being flat. The
            // modifier table uses its own direction, so it does not affect warp/masks.
            if (p.Mods.IsCreated && p.Mods.Length > 0)
            {
                h = ApplyModifiers(p, h, direction);
            }

            return h;
        }

        /// <summary>
        /// Soft compression of the far upper tail of the shape.
        ///
        /// Shape: rational (Cauchy) knee
        ///     e = x - t;   y = t + e / (1 + e / TailKnee)
        /// Strictly monotonic; the slope at the threshold is exactly 1, so the
        /// first derivative is continuous and no edge appears; as e → ∞, y → t +
        /// TailKnee, so the tail has a hard ceiling rather than merely growing slower.
        ///
        /// Applied BEFORE the beach and the pads: a pad sets its height absolutely,
        /// so compression after it would move the plate off its level. The ocean
        /// tail is untouched — the threshold is one-sided.
        /// </summary>
        internal static double ApplyTailCompression(TerrainNoiseParams p, double h)
        {
            if (!(p.TailKnee > 0d) || h <= p.TailThreshold)
            {
                return h;
            }

            double e = h - p.TailThreshold;
            return p.TailThreshold + (e / (1d + (e / p.TailKnee)));
        }

        /// <summary>
        /// Soft compression of the lower (ocean) tail — mirror of
        /// ApplyTailCompression. Same rational form, but downward:
        ///     u = t - x;   y = t - u / (1 + u / DepthKnee),  for x &lt; t
        /// Same properties: strictly monotonic, slope at the threshold exactly 1
        /// (first derivative continuous, no edge on the shelf), the floor
        /// asymptotically approaches t - DepthKnee.
        ///
        /// One-sided downwards: points above the threshold are untouched, so the
        /// shoreline, SeaLevelMeters and beach shelf do not change, and only the
        /// deep floor rises. Applied in the same place as the upper compression,
        /// before the beach and pads.
        ///
        /// DepthKnee &lt;= 0 — off, and this is the default: the legacy profile must
        /// stay bit-for-bit.
        /// </summary>
        internal static double ApplyDepthCompression(TerrainNoiseParams p, double h)
        {
            if (!(p.DepthKnee > 0d) || h >= p.DepthThreshold)
            {
                return h;
            }

            double u = p.DepthThreshold - h;
            return p.DepthThreshold - (u / (1d + (u / p.DepthKnee)));
        }

        /// <summary>
        /// Pads: a flat core and a smooth apron following the natural height.
        ///
        /// All in NORMALIZED space (SampleHeight returns shape ~[−1,1]; the caller
        /// applies the amplitude multiplier). Division by amplitude happens only for
        /// modifiers that actually touch the point — after the cosine early-out, so
        /// it costs no extra division in the hot loop.
        ///
        /// Overlaps: modifiers are applied in list order, each from the previous
        /// result. The order is deterministic, so the result is reproducible; the
        /// last one in the list wins on overlap.
        /// </summary>
        public static double ApplyModifiers(TerrainNoiseParams p, double h, double3 direction)
        {
            NativeArray<TerrainModifierData> mods = p.Mods;
            double amplitude = p.AmplitudeMeters;
            if (!(amplitude > 0d))
            {
                return h;
            }

            for (int i = 0; i < mods.Length; i++)
            {
                TerrainModifierData m = mods[i];

                // The dot product decreases with angular distance, so one comparison
                // rejects everything outside the pad, without acos.
                double dot = math.dot(direction, m.Direction);
                if (dot < m.CosOuter)
                {
                    continue;
                }

                // Core: t = 1. Apron: 0 at the outer edge → 1 at the inner edge.
                double t = 1d;
                if (dot < m.CosInner)
                {
                    double span = m.CosInner - m.CosOuter;
                    t = span > 1e-15d ? (dot - m.CosOuter) / span : 1d;
                    t = math.min(1d, math.max(0d, t));
                }

                // Smoothstep: zero derivatives at both apron ends, otherwise the
                // pad border creases the normals.
                t = t * t * (3d - (2d * t));

                double target = m.TargetHeightMeters / amplitude;
                h += (target - h) * t;
            }

            return h;
        }

        /// <summary>
        /// Whether a pad here is allowed to override the sea-level clamp. Needed
        /// exactly for digs below the sea: GetHeightMeters lifts everything below
        /// sea level back to the water, and without this check a dry dock would
        /// silently fill up.
        /// </summary>
        public static bool ModsOverrideSeaLevel(TerrainNoiseParams p, double3 direction)
        {
            NativeArray<TerrainModifierData> mods = p.Mods;
            if (!mods.IsCreated)
            {
                return false;
            }

            for (int i = 0; i < mods.Length; i++)
            {
                TerrainModifierData m = mods[i];
                if (m.OverridesSeaLevel == 0)
                {
                    continue;
                }

                if (math.dot(direction, m.Direction) >= m.CosOuter)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Beach shelf: near the waterline (raw continent mask just above the
        /// threshold on the land side) land is pulled to a gentle height above the
        /// sea — otherwise the shore jumps from water straight to a slope/plain
        /// with nowhere for a beach band. Land only (h above the sea): the ocean
        /// floor is untouched. The shelf is off without a sea (SeaLevel −∞) or
        /// without continents. All thresholds zero — return h unchanged (legacy
        /// bit-for-bit). The weight is a smooth dome with zero derivatives at the
        /// edges, so normals have no crease.
        /// </summary>
        public static double ApplyBeachShelf(TerrainNoiseParams p, double h, double continentRaw)
        {
            // Overload without shore noise: constant shelf width.
            return ApplyBeachShelfCore(p, h, continentRaw, false, default(double3));
        }

        /// <summary>
        /// Beach shelf with variable width: <paramref name="direction"/> is the
        /// point on the sphere (after warp), used to sample the width noise. If
        /// BeachShelfWidthMaxScale ≤ 1 — bit-for-bit as ApplyBeachShelf without direction.
        /// </summary>
        public static double ApplyBeachShelf(TerrainNoiseParams p, double h, double continentRaw, double3 direction)
        {
            return ApplyBeachShelfCore(p, h, continentRaw, true, direction);
        }

        /// <summary>Beach width noise contrast: n·2.2 stretches fBm (σ≈0.3) over almost the whole 1×..Max× range.</summary>
        private const double BeachWidthContrast = 2.2d;

        /// <summary>
        /// Fraction of the shelf width taken by the "seaward shoulder" when
        /// BeachShelfSeawardWidth is not set (≤0). Deliberately small: the shoulder
        /// exists only to remove the discontinuity, not to reshape the shore.
        /// </summary>
        private const double DefaultSeawardWidthFactor = 0.05d;

        /// <summary>Beach width noise gain: above the usual 0.5 so small octaves give noticeable local variation.</summary>
        private const double BeachWidthNoiseGain = 0.6d;

        /// <summary>
        /// Width multiplier at this point: 1 to BeachShelfWidthMaxScale.
        /// Interpolation is LOGARITHMIC (Max^x), so values are distributed
        /// geometrically. Smooth over direction — no creases.
        /// </summary>
        public static double BeachWidthScale(TerrainNoiseParams p, double3 direction)
        {
            double maxScale = p.BeachShelfWidthMaxScale;
            if (!(maxScale > 1d) || !(p.BeachShelfWidthNoiseFrequency > 0d))
            {
                return 1d;
            }

            int octaves = p.BeachShelfWidthNoiseOctaves < 1 ? 1 : p.BeachShelfWidthNoiseOctaves;
            double n = SampleFbmEx(
                direction, p.BeachShelfWidthNoiseFrequency, octaves, p.Seed, 9,
                BeachWidthNoiseGain, 2d, p.MaskNoiseStyle);
            double x = Smoothstep01(0.5d + (0.5d * math.clamp(n * BeachWidthContrast, -1d, 1d)));
            return math.exp(math.log(maxScale) * x);
        }

        private static double ApplyBeachShelfCore(
            TerrainNoiseParams p, double h, double continentRaw, bool varyWidth, double3 direction)
        {
            if (p.BeachShelfWidth <= 0d || p.BeachShelfAltitudeMeters <= 0d
                || p.ContinentFrequency <= 0d || p.SeaLevelMeters <= -1e29d)
            {
                return h;
            }

            double amp = math.max(1d, p.AmplitudeMeters);
            double seaN = p.SeaLevelMeters / amp;
            double shelf = seaN + (p.BeachShelfAltitudeMeters / amp);

            // The shelf only LOWERS terrain toward its height.
            //
            // The early-out threshold is on SHELF HEIGHT, not on "h > sea level":
            // at the boundary h == shelf and both branches return the same value,
            // so there is no step. It also stops the shelf from raising the floor:
            // shallows deeper than the shelf are not turned into land.
            if (!(h > shelf))
            {
                return h;
            }

            double above = continentRaw - p.ContinentThreshold;
            double w;
            if (above > 0d)
            {
                double width = math.max(1e-9d, p.BeachShelfWidth);
                if (varyWidth)
                {
                    // Width noise is sampled only on land at/above the shore (after the early outs).
                    width *= BeachWidthScale(p, direction);
                }

                double coastDist = above / width;
                double t = math.min(1d, coastDist * coastDist);
                w = (1d - t) * (1d - t);
            }
            else
            {
                // SEAWARD SHOULDER. Beyond the mask threshold the weight fades to
                // zero continuously: w = 1 at the threshold (same as on the land
                // side, so the seam has no step) and w = 0 with zero derivative at
                // the far edge of the shoulder. The shoulder depth is in mask units,
                // so in meters it is shoulder/|∇mask| — the same scale as the shelf
                // itself.
                double shoulder = p.BeachShelfSeawardWidth > 0d
                    ? p.BeachShelfSeawardWidth
                    : DefaultSeawardWidthFactor * math.max(1e-9d, p.BeachShelfWidth);
                double s = -above / shoulder;
                if (s >= 1d)
                {
                    return h;
                }

                w = 1d - Smoothstep01(s);
            }

            return h + ((shelf - h) * w);
        }

        public static double SampleColorNoise(TerrainNoiseParams p, double3 direction)
        {
            double gain = EffectiveGain(p.Gain);
            double lacunarity = EffectiveLacunarity(p.Lacunarity);
            return SampleFbmEx(
                direction, p.ColorNoiseFrequency, p.ColorNoiseOctaves,
                p.Seed + (p.ColorNoiseSeedOffset * 7919), 4, gain, lacunarity, p.MaskNoiseStyle);
        }

        /// <summary>
        /// Small-scale color detail ~[−1,1] (land mottling: soil/light patches),
        /// own stream (salt 7). Color does not affect physics.
        /// </summary>
        public static double SampleColorDetailNoise(TerrainNoiseParams p, double3 direction)
        {
            double gain = EffectiveGain(p.Gain);
            double lacunarity = EffectiveLacunarity(p.Lacunarity);
            return SampleFbmEx(
                direction, p.ColorDetailFrequency, p.ColorDetailOctaves,
                p.Seed + (p.ColorDetailSeedOffset * 7919), 7, gain, lacunarity, p.MaskNoiseStyle);
        }

        /// <summary>
        /// Decor distribution noise (clusters) ~[−1,1], own stream (salt 8): not
        /// correlated with shape or the color mask. The seed comes from the body's
        /// terrain seed + layer offset, so layers and planets do not coincide.
        /// </summary>
        public static double SampleDecorNoise(
            TerrainNoiseParams terrain, int seedOffset, double frequency, int octaves, double3 direction)
        {
            double gain = EffectiveGain(terrain.Gain);
            double lacunarity = EffectiveLacunarity(terrain.Lacunarity);
            return SampleFbmEx(
                direction, frequency, octaves,
                terrain.Seed + (seedOffset * 7919), 8, gain, lacunarity,
                terrain.MaskNoiseStyle >= 0 ? terrain.MaskNoiseStyle : terrain.NoiseStyle);
        }

        public static double EffectiveGain(double gain)
        {
            return gain > 0d && gain <= 1d ? gain : 0.5d;
        }

        public static double EffectiveLacunarity(double lacunarity)
        {
            return lacunarity >= 1d && lacunarity <= 8d ? lacunarity : 2d;
        }

        /// <summary>
        /// Whether the domain windows are valid for these Gain/Lacunarity.
        ///
        /// The domain signal is (slopeAccum/(o+1))/BaseFrequency. The (o+1)
        /// normalization is meaningful ONLY if every octave contributes equally to
        /// the accumulated slope: an octave's contribution is proportional to
        /// amplitude * frequency = gain^o * lacunarity^o = (gain*lacunarity)^o,
        /// constant exactly when Gain * Lacunarity == 1. The windows
        /// 1.35..2.05 / 0.05..0.45 are calibrated for that.
        ///
        /// If the product drifts, the contributions grow geometrically and the
        /// signal leaves the window — the domain silently degenerates to a no-op
        /// or a harmless constant. Neither an Assert nor an exception is possible
        /// here: the code goes into a Burst job, and Debug.Assert does not compile
        /// in Burst. The check therefore lives in the test
        /// (T131_DampWindowsValid) and this is only the predicate.
        /// </summary>
        public static bool SlopeDampWindowsValid(double gain, double lacunarity)
        {
            return math.abs((EffectiveGain(gain) * EffectiveLacunarity(lacunarity)) - 1d) < 1e-9d;
        }

        /// <summary>
        /// One log per encountered Gain/Lacunarity pair. FromTerrain is called
        /// thousands of times per second, so logging every time would itself stall
        /// a frame.
        ///
        /// Storage is a fixed array of 8 keys without allocations or hash
        /// structures: the project has only a few profiles alive at once, and 8 is
        /// more than enough. On overflow it logs again.
        ///
        /// The key is two integers at 1/1024 resolution, which is enough: profile
        /// values are round (0.5, 0.6, 2, 2.5), and 1/1024 tells them apart.
        /// </summary>
        private static readonly long[] dampWindowsLogged = new long[8];

        internal static void LogDampWindowsOnce(HeightfieldTerrain terrain)
        {
            long key = ((long)(int)System.Math.Round(EffectiveGain(terrain.Gain) * 1024d) << 32)
                | (uint)(int)System.Math.Round(EffectiveLacunarity(terrain.Lacunarity) * 1024d);

            for (int i = 0; i < dampWindowsLogged.Length; i++)
            {
                if (dampWindowsLogged[i] == key)
                {
                    return;
                }
            }

            for (int i = 0; i < dampWindowsLogged.Length; i++)
            {
                if (dampWindowsLogged[i] == 0L)
                {
                    dampWindowsLogged[i] = key;
                    break;
                }
            }

            UnityEngine.Debug.LogWarning(
                "[TerrainNoise] SlopeDamp отключён: Gain " + terrain.Gain.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + " * Lacunarity " + terrain.Lacunarity.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)
                + " = " + (EffectiveGain(terrain.Gain) * EffectiveLacunarity(terrain.Lacunarity)).ToString("F4", System.Globalization.CultureInfo.InvariantCulture)
                + " != 1, а окна домена откалиброваны под произведение 1.");
        }

        private static double SampleFbmLegacy(TerrainNoiseParams p, double3 direction)
        {
            int octaves = p.Octaves < 1 ? 1 : p.Octaves;
            double amplitude = 1d;
            double frequency = p.BaseFrequency < 1e-6d ? 1e-6d : p.BaseFrequency;
            double sum = 0d;
            double norm = 0d;
            double3 offset = new double3(p.Seed * 17.31d, p.Seed * 7.77d, p.Seed * 29.13d);
            for (int o = 0; o < octaves; o++)
            {
                sum += amplitude * Octave(p, (direction * frequency) + offset);
                norm += amplitude;
                amplitude *= 0.5d;
                frequency *= 2d;
                offset = new double3(offset.y + 19.19d, offset.z + 7.47d, offset.x + 3.13d);
            }

            return norm > 0d ? sum / norm : 0d;
        }

        private static double3 ApplyWarp(TerrainNoiseParams p, double3 source, double strength, double frequency,
            int octaves, int seed, int saltBase, double gain, double lacunarity)
        {
            double f = frequency < 1e-6d ? 1e-6d : frequency;
            int n = octaves < 1 ? 1 : octaves;
            double3 warp = new double3(
                SampleFbmEx(source, f, n, seed, saltBase, gain, lacunarity, p.MaskNoiseStyle),
                SampleFbmEx(source, f, n, seed, saltBase + 100, gain, lacunarity, p.MaskNoiseStyle),
                SampleFbmEx(source, f, n, seed, saltBase + 200, gain, lacunarity, p.MaskNoiseStyle));
            return source + (warp * strength);
        }

        /// <summary>
        /// Plain fBm, without a domain. Used for everything except the shape itself:
        /// continent and plains masks, warp, color mask and detail, decor noise.
        ///
        /// The octave domain is deliberately absent here: a continent mask with the
        /// domain would shift the shoreline and the land statistics (land fraction
        /// in 0.15..0.85, and the shelf at the waterline). A mask is not terrain; it
        /// needs even statistics, not eroded slopes.
        /// </summary>
        /// <summary>Wrapper: every mask except the shape calls the same function.</summary>
        private static double SampleFbmEx(double3 direction, double baseFrequency, int octaves, int seed, int salt, double gain, double lacunarity, int noiseStyle)
        {
            int n = octaves < 1 ? 1 : octaves;
            double amplitude = 1d;
            double frequency = baseFrequency < 1e-6d ? 1e-6d : baseFrequency;
            double sum = 0d;
            double norm = 0d;
            double3 offset = SaltOffset(seed, salt);
            for (int o = 0; o < n; o++)
            {
                sum += amplitude * (noiseStyle == (int)TerrainNoiseStyle.Perlin
                    ? GradientNoise((direction * frequency) + offset)
                    : ValueNoise((direction * frequency) + offset));
                norm += amplitude;
                amplitude *= gain;
                frequency *= lacunarity;
                offset = new double3(offset.y + 19.19d, offset.z + 7.47d, offset.x + 3.13d);
            }

            return norm > 0d ? sum / norm : 0d;
        }

        /// <summary>
        /// fBm for the terrain SHAPE itself, with an octave domain (SlopeDamp).
        ///
        /// Idea: the steeper the slope, the more high-frequency detail; on flat
        /// ground there is almost none. Each octave is weighted by the accumulated
        /// slope instead of 1, so fields and peaks come out smooth and slopes
        /// eroded, as in real terrain.
        ///
        /// Two slope sources, selected by SlopeDampMode (NOT by primitive):
        ///  • Gradient: analytic |∇| of the octave. Measures steepness properly,
        ///    but costs three extra trilinear blends per octave.
        ///  • Accum: accumulated value 1 − k·|sum|. Nearly free, but measures
        ///    HEIGHT, not slope — detail leaves peaks and valleys.
        ///
        /// The source is a separate parameter rather than "Perlin means derivative",
        /// so either mode can be tested with the other primitive.
        ///
        /// With SlopeDampMode = Off or SlopeDamp ≤ 0 there is no domain and the
        /// result is bit-for-bit SampleFbmEx.
        /// </summary>
        private static double SampleShapeFbm(TerrainNoiseParams p, double3 direction, double baseFrequency, int octaves, int salt, double gain, double lacunarity)
        {
            double damp = p.SlopeDamp;
            if (p.SlopeDampMode == (int)TerrainSlopeDampMode.Off || !(damp > 0d))
            {
                return SampleFbmEx(direction, baseFrequency, octaves, p.Seed, salt, gain, lacunarity, p.NoiseStyle);
            }

            // The derivative exists only for the gradient primitive; under Value
            // this mode silently degenerates to Accum.
            bool gradient = p.SlopeDampMode == (int)TerrainSlopeDampMode.Gradient
                && p.NoiseStyle == (int)TerrainNoiseStyle.Perlin;

            damp = math.min(1d, damp);
            int n = octaves < 1 ? 1 : octaves;
            double amplitude = 1d;
            double frequency = baseFrequency < 1e-6d ? 1e-6d : baseFrequency;
            double slopeAccum = 0d;
            double sum = 0d;
            double norm = 0d;
            double weight = 1d;
            double3 offset = SaltOffset(p.Seed, salt);
            for (int o = 0; o < n; o++)
            {
                double at = weight;
                double v;
                if (gradient)
                {
                    v = GradientNoiseWithDerivative((direction * frequency) + offset, out double3 d);
                    slopeAccum += amplitude * (math.length(d) * frequency);

                    // The domain signal is the average accumulated slope per octave,
                    // divided by the base frequency. Both normalizations are required:
                    //
                    //  • division by (o+1): every octave adds an equal contribution
                    //    to the slope (amplitude falls as 1/frequency), and without it
                    //    the signal grows linearly with the octave index;
                    //  • division by baseFrequency: slope is proportional to frequency,
                    //    and without it the saturation constant would depend on the
                    //    profile's BaseFrequency.
                    double signal = (slopeAccum / (o + 1)) / baseFrequency;
                    at = 1d - (damp * SlopeNorm(signal, SlopeWindowLo, SlopeWindowHi));
                }
                else
                {
                    v = p.NoiseStyle == (int)TerrainNoiseStyle.Perlin
                        ? GradientNoise((direction * frequency) + offset)
                        : ValueNoise((direction * frequency) + offset);
                }

                at = math.min(1d, math.max(0d, at));
                sum += amplitude * v * at;
                norm += amplitude * at;
                amplitude *= gain;
                frequency *= lacunarity;
                offset = new double3(offset.y + 19.19d, offset.z + 7.47d, offset.x + 3.13d);

                if (!gradient)
                {
                    // The accumulated value is a slope surrogate. Divide by the
                    // current norm, otherwise the weight drifts with Gain.
                    weight = 1d - (damp * SlopeNorm(norm > 0d ? math.abs(sum / norm) : 0d, AccumWindowLo, AccumWindowHi));
                }
            }

            return norm > 0d ? sum / norm : 0d;
        }

        /// <summary>
        /// How much "detail" corresponds to a given slope: 0 — kill the octave
        /// completely, 1 — pass it unchanged. Soft saturation with a shifted window.
        ///
        /// The windows DIFFER between the two modes: the signals live at different
        /// scales, and with a shared window one mode silently degenerates to a no-op.
        /// The accumulated value is always below the gradient mode's window.
        ///
        /// Gradient mode: the signal is normalized to the average accumulated slope.
        /// Accum mode: the signal is |accumulated shape| in its own [0,1] scale.
        ///
        /// The window is wider than the quantiles so the domain neither collapses to
        /// zero on gentle slopes nor already reaches one on cliffs.
        ///
        /// The shape is a smoothstep, not a hard threshold: a hard threshold would
        /// step the octave weight, hence the height, and the crease would show up in
        /// the normals.
        /// </summary>
        private static double SlopeNorm(double signal, double lo, double hi)
        {
            double s = math.min(1d, math.max(0d, (signal - lo) / (hi - lo)));
            return s * s * (3d - (2d * s));
        }

        /// <summary>Saturation window for the steepness domain signal.</summary>
        private const double SlopeWindowLo = 1.35d;

        private const double SlopeWindowHi = 2.05d;

        /// <summary>Saturation window for the height domain signal.</summary>
        private const double AccumWindowLo = 0.05d;

        private const double AccumWindowHi = 0.45d;

        internal static double SampleRidged(TerrainNoiseParams p, double3 direction, double gain, double lacunarity)
        {
            if (p.RidgedMode == (int)TerrainRidgedMode.Multifractal)
            {
                return SampleRidgedMultifractal(p, direction, gain, lacunarity);
            }

            int n = p.Octaves < 1 ? 1 : p.Octaves;
            double amplitude = 1d;
            double frequency = p.BaseFrequency < 1e-6d ? 1e-6d : p.BaseFrequency;
            double sum = 0d;
            double norm = 0d;
            double3 offset = SaltOffset(p.Seed, 2);
            for (int o = 0; o < n; o++)
            {
                double v = Octave(p, (direction * frequency) + offset);
                sum += amplitude * (1d - (v >= 0d ? v : -v));
                norm += amplitude;
                amplitude *= gain;
                frequency *= lacunarity;
                offset = new double3(offset.y + 19.19d, offset.z + 7.47d, offset.x + 3.13d);
            }

            // smoothstep keeps the 0.5 mean but sharpens contrast: narrow ridges
            // and flat valleys instead of even ripple.
            return norm > 0d ? Smoothstep01(sum / norm) : 0d;
        }

        /// <summary>
        /// Fold the octave into a ridge: 1 − |v| raised to a power.
        ///
        /// |v| is saturated at one BEFORE the subtraction. For the normalized
        /// gradient primitive |v| reaches 1.43; without saturation s would go to
        /// −0.43, the square would make it positive again, and a FALSE SECONDARY
        /// RIDGE would appear along |v| = 1 — exactly where a saddle should be.
        ///
        /// For value noise the saturation is an exact no-op: LatticeValue is in
        /// [−1,1] by construction and the trilinear blend stays inside the convex
        /// hull, so the legacy mode remains bit-for-bit.
        /// </summary>
        internal static double RidgeFold(double v, int sharpness)
        {
            double a = v >= 0d ? v : -v;
            double s = 1d - (a >= 1d ? 1d : a);
            for (int k = 1; k < sharpness; k++)
            {
                s *= s;
            }

            return s;
        }

        /// <summary>
        /// Ridged multifractal (Musgrave). The only difference from Legacy is that
        /// each octave is multiplied by a weight inherited from the previous one
        /// instead of a fixed amplitude. A ridge that dies on a lower octave also
        /// kills the higher ones, so instead of parallel folds you get a branching
        /// network visible from any height.
        ///
        /// The 0..1 range holds BY CONSTRUCTION: RidgeFold saturates |v| at one, and
        /// the weight is in [0,1]. This matters because SampleHeight mixes ridged
        /// into baseHeight as "both in the same range".
        ///
        /// `sum/norm` instead of dividing by gain geometry is deliberate: with the
        /// domain the denominator varies, and a fixed normalization would pull the
        /// mean level down.
        /// </summary>
        private static double SampleRidgedMultifractal(
            TerrainNoiseParams p, double3 direction, double gain, double lacunarity)
        {
            int n = p.Octaves < 1 ? 1 : p.Octaves;
            double amplitude = 1d;
            double frequency = p.BaseFrequency < 1e-6d ? 1e-6d : p.BaseFrequency;
            double sum = 0d;
            double norm = 0d;
            double weight = 1d;
            double weightGain = p.RidgedWeightGain > 0d ? p.RidgedWeightGain : 2d;
            int sharpness = SharpnessSteps(p.RidgedSharpness);
            double3 offset = SaltOffset(p.Seed, 2);
            for (int o = 0; o < n; o++)
            {
                double v = Octave(p, (direction * frequency) + offset);
                double s = RidgeFold(v, sharpness);

                // The first octave is not damped (weight = 1), otherwise the base
                // relief level would depend on how the hash happened to land on octave 0.
                s *= weight;
                sum += amplitude * s;
                norm += amplitude;
                double w = s * weightGain;
                weight = w >= 1d ? 1d : w;
                amplitude *= gain;
                frequency *= lacunarity;
                offset = new double3(offset.y + 19.19d, offset.z + 7.47d, offset.x + 3.13d);
            }

            if (!(norm > 0d))
            {
                return 0d;
            }

            return RidgeGamma(sum / norm, p.RidgedGamma);
        }

        /// <summary>
        /// Ridge level remap: s^γ for s in [0,1]. One parameter, monotonic, keeps
        /// the range by construction and does not change ridge topology — for γ > 0
        /// a monotonic transform preserves both order and the number of extrema.
        /// That is why γ rather than an affine remap over two parameters: an affine
        /// fit to mean and σ pushes the maximum past 1 and, at the ends of the range,
        /// drops the mean.
        ///
        /// γ = 1 — a no-op without calling pow; this is both the default and the
        /// legacy path. The remaining ridge height is handled by AmplitudeMeters in
        /// the preset, not by code.
        /// </summary>
        internal static double RidgeGamma(double s, double gamma)
        {
            if (!(s > 0d))
            {
                return s <= 0d ? 0d : s;
            }

            if (gamma > 0d && gamma < 1d)
            {
                return math.pow(s, gamma);
            }

            return s;
        }

        /// <summary>
        /// The exponent is quantized to an integer: pow() in the hot loop is not
        /// acceptable. 1 = no sharpening, 2 = classic signal², 3 and 4 — stronger.
        /// </summary>
        private static int SharpnessSteps(double sharpness)
        {
            if (!(sharpness > 1d))
            {
                return 1;
            }

            int n = (int)math.round(sharpness);
            return n > 4 ? 4 : n;
        }

        private static double3 SaltOffset(int seed, int salt)
        {
            switch (salt)
            {
                case 1:
                    return new double3(seed * 57.31d + 101.3d, seed * 43.77d + 67.9d, seed * 71.13d + 37.1d);
                case 2:
                    return new double3(seed * 23.17d + 503.7d, seed * 89.31d + 311.3d, seed * 11.71d + 877.9d);
                case 4:
                    return new double3(seed * 17.13d + 2100.7d, seed * 37.31d + 1900.4d, seed * 73.17d + 2300.8d);
                case 5:
                    // Own stream for the plains mask: NOT default (base salt 0 and
                    // warp-Z salt 300 already live there) — otherwise plains would
                    // correlate with the warp and, through it, with ridges.
                    return new double3(seed * 83.77d + 2600.3d, seed * 29.13d + 2900.7d, seed * 67.31d + 2700.1d);
                case 6:
                    // Small-scale detail stream (rock detail): own stream again, so
                    // it correlates with neither shape nor masks.
                    return new double3(seed * 53.19d + 3600.9d, seed * 97.31d + 3300.5d, seed * 41.77d + 3900.3d);
                case 7:
                    // Small-scale color detail stream (mottling).
                    return new double3(seed * 71.93d + 4600.1d, seed * 33.47d + 4900.7d, seed * 89.11d + 4300.5d);
                case 8:
                    // Decor distribution stream (grass/rock clusters).
                    return new double3(seed * 47.11d + 6100.3d, seed * 31.79d + 6400.7d, seed * 73.31d + 6700.1d);
                case 9:
                    // Beach width stream (uneven shore): own stream, so zones of
                    // wide/narrow sand correlate with neither shape nor masks.
                    return new double3(seed * 37.91d + 7300.7d, seed * 61.17d + 7600.3d, seed * 29.53d + 7900.9d);
                case 10:
                    // Mountain belt mask: own stream, so ridges gather into belts by
                    // their own geometry rather than where the continent or plains mask
                    // happens to lie (all three would otherwise get the same noise at
                    // the same frequency and look identical).
                    return new double3(seed * 34.67d + 9100.3d, seed * 87.13d + 9400.7d, seed * 56.29d + 9700.1d);
                case 11:
                    // Ridge term of the continent mask: own stream. It adds into
                    // continentRaw, so it must not share a stream with the main mask
                    // — otherwise continent elongation would match its shape.
                    return new double3(seed * 19.37d + 11200.7d, seed * 63.91d + 11500.3d, seed * 42.53d + 11800.9d);
                case 400:
                case 500:
                case 600:
                    // Continent mask warp. Separate 400/500/600 rather than the
                    // mountain warp's 100/200/300: sharing them would take continent
                    // shape and mountain folds from the same noise at the same
                    // frequency, so they would look identical, only at different scales.
                    int baseSalt = (salt / 100) * 100;
                    return new double3(
                        seed * 13.19d + (baseSalt * 0.7d) + 13100.3d,
                        seed * 71.53d + (baseSalt * 1.1d) + 13400.7d,
                        seed * 46.87d + (baseSalt * 1.9d) + 13700.1d);
                case 100:
                    return new double3(seed * 91.7d + 1000.3d, seed * 47.31d + 700.7d, seed * 13.17d + 400.9d);
                case 200:
                    return new double3(seed * 31.7d + 1400.1d, seed * 77.13d + 1100.5d, seed * 53.71d + 900.2d);
                default:
                    return new double3(seed * 61.3d + 1700.9d, seed * 19.77d + 1300.3d, seed * 83.31d + 1500.6d);
            }
        }

        private static double Smoothstep01(double t)
        {
            if (t <= 0d)
            {
                return 0d;
            }

            if (t >= 1d)
            {
                return 1d;
            }

            return t * t * (3d - (2d * t));
        }

        private static double Quintic(double t)
        {
            return t * t * t * (t * ((t * 6d) - 15d) + 10d);
        }

        internal static double ValueNoise(double3 p)
        {
            // floor separately: the fractional part p - floor(p) is always ∈ [0,1),
            // and the cell index is hashed via long→int. A direct (int)floor(p)
            // breaks when the seed offsets (seed·89.31 etc.) exceed int.MaxValue —
            // then p - (int)floor(p) ~ 4e9 and Quintic explodes to 1e298.
            double xf = math.floor(p.x);
            double yf = math.floor(p.y);
            double zf = math.floor(p.z);
            int ix = unchecked((int)(long)xf);
            int iy = unchecked((int)(long)yf);
            int iz = unchecked((int)(long)zf);
            double fx = p.x - xf;
            double fy = p.y - yf;
            double fz = p.z - zf;
            double ux = Quintic(fx);
            double uy = Quintic(fy);
            double uz = Quintic(fz);

            double c000 = LatticeValue(ix, iy, iz);
            double c100 = LatticeValue(ix + 1, iy, iz);
            double c010 = LatticeValue(ix, iy + 1, iz);
            double c110 = LatticeValue(ix + 1, iy + 1, iz);
            double c001 = LatticeValue(ix, iy, iz + 1);
            double c101 = LatticeValue(ix + 1, iy, iz + 1);
            double c011 = LatticeValue(ix, iy + 1, iz + 1);
            double c111 = LatticeValue(ix + 1, iy + 1, iz + 1);

            double x00 = c000 + (ux * (c100 - c000));
            double x10 = c010 + (ux * (c110 - c010));
            double x01 = c001 + (ux * (c101 - c001));
            double x11 = c011 + (ux * (c111 - c011));
            double y0 = x00 + (uy * (x10 - x00));
            double y1 = x01 + (uy * (x11 - x01));
            return y0 + (uz * (y1 - y0));
        }

        private static double LatticeValue(int x, int y, int z)
        {
            unchecked
            {
                int h = (x * 374761393) + (y * 668265263) + (z * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return ((h & 0xFFFF) / 32767.5d) - 1d;
            }
        }

        private const double Grad2 = 0.70710678118654752440d; // 1/sqrt(2)

        /// <summary>
        /// RMS normalization of the gradient primitive compared with value noise.
        ///
        /// Measured (4M points): RMS value 0.40038, RMS gradient 0.19475, ratio
        /// 2.05593. Without this normalization Perlin octaves would be far quieter
        /// and most of the profile amplitude would be lost: RMS is the average
        /// energy, while amplitude is calibrated by the peak. ContinentThreshold
        /// cannot be retuned to RMS — the threshold sits on the peak, and with
        /// ContinentDepth = 0.08 the land fraction shifts from 0.469 to 0.707.
        ///
        /// Normalization is by RMS, not by the maximum, and the two differ: the
        /// peak-to-peak limit is not reached in practice, whereas RMS is what
        /// actually contributes to variance. The upper bound of the shape comes
        /// from ShapeBound, not from here.
        /// </summary>
        private const double PerlinScale = 2.05593d;
        /// <summary>
        /// Limit of ONE normalized octave of the gradient primitive, not of the shape.
        ///
        /// An octave is bounded above by the sum of |g_i| over the three nearest
        /// nodes. With |g| = 1 and |f| ≤ 1 the sum does not exceed √3, and reaches
        /// that only at cell corner (1,1,1), where the interpolation gives 1, so the
        /// limit of one octave is √3/2 = 0.8660. The PerlinScale factor is already
        /// included.
        ///
        /// The WHOLE SHAPE is NOT ShapeBound. With ContinentDepth off, the weighted
        /// octave sum is normalized and stays within ±1. Enabling ContinentDepth
        /// lets the ContinentFrequency layer push the shape below the one-octave
        /// limit, so the low end cannot be obtained from ShapeBound.
        ///
        /// The real contract for a consumer of the Perlin profile:
        ///     |shape| ≤ ShapeBound + ContinentDepth
        /// and for legacy value noise the bound is indeed 1.
        ///
        /// The constant has no consumers in code and is not used as a claim about
        /// limits: chunk bounds are computed from actual vertices and hasWater uses
        /// SeaLevelMeters in meters. It exists as a checkable statement and as a
        /// bound on which an assert can be built if a consumer appears.
        /// </summary>
        public const double ShapeBound = 0.86602540378443864676d * PerlinScale;
        /// <summary>

        /// <summary>
        /// Lattice hash of the GRADIENT primitive. IMPORTANT: this is NOT the same
        /// hash as in LatticeValue, and it deliberately differs in the z-axis
        /// multiplier.
        ///
        /// In LatticeValue (value noise and all legacy paths) the z multiplier is
        /// 2147483647 = 2^31 - 1, so z*(2^31-1) = -z (mod 2^31): the z axis barely
        /// mixes. For a scalar hash this is harmless — the low 16 bits are random
        /// enough. For picking one of 12 gradient directions it produced measurable
        /// axial anisotropy of the field. Using the multiplier 2654435761 removes the
        /// skew while NOT touching value noise, its distribution or the legacy pins,
        /// because the hash copies are separate.
        ///
        /// The three copies (this one, plus the call sites in LatticeDot and
        /// LatticeGradientFast) must not be kept separately: they would diverge on
        /// the next edit. There is one function with an inline hint — it is on the
        /// hot path 8 times per octave.
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static int PerlinLatticeHash(int x, int y, int z)
        {
            unchecked
            {
                int h = (x * 374761393) + (y * 668265263) + (z * unchecked((int)2654435761u));
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return h;
            }
        }
        /// <summary>
        /// Dot product of the lattice node gradient and the offset — without
        /// materializing the vector itself.
        ///
        /// The vector is not built here. Eight cell corners would mean eight double3
        /// constructions with three stores each plus extra multiplies. Instead the
        /// coefficients of fx/fy/fz are computed directly from the index, leaving
        /// three multiplies and two adds per corner.
        ///
        /// There are no branches, and this is a correctness-by-cost requirement, not
        /// a speed optimization: the case index is random, so a branch predictor
        /// misses at each of the eight corners. A switch over 20 cases cost x8.1
        /// versus value noise, a branchy if — x3.7.
        ///
        /// The set is the 12 cube edges, 4 per plane, as in improved Perlin.
        /// With 12 edges the sum g gᵀ is exactly 4I, and the set is closed under
        /// axis permutations.
        ///
        /// About the index: h % 12, NOT the usual (ulong)h·12 >> 32. The hash has no
        /// live upper bit, so the shift scheme silently degenerated to a zero index
        /// and the yz plane never occurred. Remainder by a constant compiles to a
        /// multiply and shift; the spread over 12 buckets is 0.56%, which is the
        /// hash's own non-uniformity, not remainder quantization.
        /// </summary>
        internal static double LatticeDot(int x, int y, int z, double fx, double fy, double fz)
        {
            unchecked
            {
                int h = PerlinLatticeHash(x, y, z);

                // uint, not int: h >= 0 today, but if the hash's top bit comes alive,
                // a signed remainder would give a negative index.
                int idx = (int)((uint)h % 12u);
                int plane = idx >> 2; // 0 = xy plane, 1 = xz, 2 = yz
                int k = idx & 3;

                double sa = 1d - (2d * (k & 1));
                double sb = 1d - (2d * ((k >> 1) & 1));

                // Zero axis by plane: xy has z idle, xz has y, yz has x.
                double mx = 1d - (plane >> 1);
                double my = 1d - (plane & 1);
                double mz = (plane + 1) >> 1;

                // Of the two occupied axes (in increasing index order) the first gets
                // sa. For the xy and xz planes that is x, for yz it is y, hence the y
                // sign flips with bit plane>>1. The xz plane does not color y at all
                // (my = 0), so its value there does not matter.
                double sy = sb + ((plane >> 1) * (sa - sb));

                return ((sa * Grad2 * mx) * fx) + ((sy * Grad2 * my) * fy) + ((sb * Grad2 * mz) * fz);
            }
        }

        /// <summary>
        /// Lattice node gradient as a vector — the reference form from which the test
        /// takes the direction set and checks it against LatticeDot. Not used on the
        /// hot path: materializing the vector there is exactly the extra work avoided.
        /// </summary>
        internal static double3 LatticeGradientFast(int x, int y, int z)
        {
            unchecked
            {
                int h = PerlinLatticeHash(x, y, z);

                int idx = (int)((uint)h % 12u);
                int plane = idx >> 2;
                int k = idx & 3;
                double sa = 1d - (2d * (k & 1));
                double sb = 1d - (2d * ((k >> 1) & 1));
                double mx = 1d - (plane >> 1);
                double my = 1d - (plane & 1);
                double mz = (plane + 1) >> 1;
                double sy = sb + ((plane >> 1) * (sa - sb));
                return new double3(sa * Grad2 * mx, sy * Grad2 * my, sb * Grad2 * mz);
            }
        }


        /// <summary>
        /// One octave of the selected primitive. The single choice point is visible
        /// in one place instead of being spread over three call sites.
        /// </summary>
        private static double Octave(TerrainNoiseParams p, double3 at)
        {
            return p.NoiseStyle == (int)TerrainNoiseStyle.Perlin ? GradientNoise(at) : ValueNoise(at);
        }

        /// <summary>
        /// Gradient noise primitive: 8 cell corners, quintic interpolation, the
        /// direction set from the 12 cube edges. Returns the VALUE OF ONE OCTAVE,
        /// already multiplied by PerlinScale.
        ///
        /// IMPORTANT: the scale is NOT 1.0 and the function is NOT bounded by
        /// [-1, 1]. That is true for value noise, where SampleHeight normalizes the
        /// weighted octave sum to [-1, 1] and thus |H| <= Amplitude. Perlin has no
        /// such normalization — instead there is the RMS factor
        /// PerlinScale = 2.05593, and one octave reaches ShapeBound = 1.78.
        ///
        /// Therefore the SampleHeight consumer must distinguish two cases:
        ///   NoiseStyle = Value  ->  |H| <= Amplitude, as before;
        ///   NoiseStyle = Perlin ->  |H| <= (ShapeBound + ContinentDepth) * Amplitude.
        /// </summary>
        internal static double GradientNoise(double3 p)
        {
            double xf = math.floor(p.x);
            double yf = math.floor(p.y);
            double zf = math.floor(p.z);
            int ix = unchecked((int)(long)xf);
            int iy = unchecked((int)(long)yf);
            int iz = unchecked((int)(long)zf);
            double fx = p.x - xf;
            double fy = p.y - yf;
            double fz = p.z - zf;
            double ux = Quintic(fx);
            double uy = Quintic(fy);
            double uz = Quintic(fz);

            double n000 = LatticeDot(ix, iy, iz, fx, fy, fz);
            double n100 = LatticeDot(ix + 1, iy, iz, fx - 1d, fy, fz);
            double n010 = LatticeDot(ix, iy + 1, iz, fx, fy - 1d, fz);
            double n110 = LatticeDot(ix + 1, iy + 1, iz, fx - 1d, fy - 1d, fz);
            double n001 = LatticeDot(ix, iy, iz + 1, fx, fy, fz - 1d);
            double n101 = LatticeDot(ix + 1, iy, iz + 1, fx - 1d, fy, fz - 1d);
            double n011 = LatticeDot(ix, iy + 1, iz + 1, fx, fy - 1d, fz - 1d);
            double n111 = LatticeDot(ix + 1, iy + 1, iz + 1, fx - 1d, fy - 1d, fz - 1d);

            return TrilinearBlend(n000, n100, n010, n110, n001, n101, n011, n111, ux, uy, uz) * PerlinScale;
        }

        /// <summary>
        /// Perlin octave value together with its analytic derivative — needed by the
        /// slope domain (SampleShapeFbm). No new hashes: the 8 dot products are
        /// already computed for the value, and the derivative costs three more
        /// trilinear blends per axis.
        ///
        /// Deliberately NOT used for value noise: its derivative is discontinuous at
        /// cell borders (the corner value simply changes as a constant), and the
        /// domain would jitter at the seams.
        /// </summary>
        internal static double GradientNoiseWithDerivative(double3 p, out double3 derivative)
        {
            double xf = math.floor(p.x);
            double yf = math.floor(p.y);
            double zf = math.floor(p.z);
            int ix = unchecked((int)(long)xf);
            int iy = unchecked((int)(long)yf);
            int iz = unchecked((int)(long)zf);
            double fx = p.x - xf;
            double fy = p.y - yf;
            double fz = p.z - zf;
            double ux = Quintic(fx);
            double uy = Quintic(fy);
            double uz = Quintic(fz);

            // du/dt of the quintic smoothing: 30·t²(t−1)². Zero at cell borders,
            // so the derivative is continuous overall.
            double dudx = 30d * fx * fx * ((fx - 1d) * (fx - 1d));
            double dvdy = 30d * fy * fy * ((fy - 1d) * (fy - 1d));
            double dwdz = 30d * fz * fz * ((fz - 1d) * (fz - 1d));

            double3 g000 = LatticeGradientFast(ix, iy, iz);
            double3 g100 = LatticeGradientFast(ix + 1, iy, iz);
            double3 g010 = LatticeGradientFast(ix, iy + 1, iz);
            double3 g110 = LatticeGradientFast(ix + 1, iy + 1, iz);
            double3 g001 = LatticeGradientFast(ix, iy, iz + 1);
            double3 g101 = LatticeGradientFast(ix + 1, iy, iz + 1);
            double3 g011 = LatticeGradientFast(ix, iy + 1, iz + 1);
            double3 g111 = LatticeGradientFast(ix + 1, iy + 1, iz + 1);

            double n000 = LatticeDot(ix, iy, iz, fx, fy, fz);
            double n100 = LatticeDot(ix + 1, iy, iz, fx - 1d, fy, fz);
            double n010 = LatticeDot(ix, iy + 1, iz, fx, fy - 1d, fz);
            double n110 = LatticeDot(ix + 1, iy + 1, iz, fx - 1d, fy - 1d, fz);
            double n001 = LatticeDot(ix, iy, iz + 1, fx, fy, fz - 1d);
            double n101 = LatticeDot(ix + 1, iy, iz + 1, fx - 1d, fy, fz - 1d);
            double n011 = LatticeDot(ix, iy + 1, iz + 1, fx, fy - 1d, fz - 1d);
            double n111 = LatticeDot(ix + 1, iy + 1, iz + 1, fx - 1d, fy - 1d, fz - 1d);

            // ∂/∂x = trilinear blend of gx over (u,v,w) + du/dx · n differences
            // along x, blended only over (v,w). Similarly for y and z.
            double gx = TrilinearBlend(g000.x, g100.x, g010.x, g110.x, g001.x, g101.x, g011.x, g111.x, ux, uy, uz)
                + (dudx * Bilerp(n100 - n000, n110 - n010, n101 - n001, n111 - n011, uy, uz));
            double gy = TrilinearBlend(g000.y, g100.y, g010.y, g110.y, g001.y, g101.y, g011.y, g111.y, ux, uy, uz)
                + (dvdy * Bilerp(n010 - n000, n110 - n100, n011 - n001, n111 - n101, ux, uz));
            double gz = TrilinearBlend(g000.z, g100.z, g010.z, g110.z, g001.z, g101.z, g011.z, g111.z, ux, uy, uz)
                + (dwdz * Bilerp(n001 - n000, n101 - n100, n011 - n010, n111 - n110, ux, uy));

            // The derivative is scaled by the same coefficient as the value:
            // otherwise the slope domain would measure |∇| in different units, and
            // SlopeDamp would no longer mean the same thing across primitives.
            derivative = new double3(gx, gy, gz) * PerlinScale;
            return TrilinearBlend(n000, n100, n010, n110, n001, n101, n011, n111, ux, uy, uz) * PerlinScale;
        }

        private static double TrilinearBlend(
            double c000, double c100, double c010, double c110,
            double c001, double c101, double c011, double c111,
            double ux, double uy, double uz)
        {
            double x00 = c000 + (ux * (c100 - c000));
            double x10 = c010 + (ux * (c110 - c010));
            double x01 = c001 + (ux * (c101 - c001));
            double x11 = c011 + (ux * (c111 - c011));
            double y0 = x00 + (uy * (x10 - x00));
            double y1 = x01 + (uy * (x11 - x01));
            return y0 + (uz * (y1 - y0));
        }

        private static double Bilerp(double c00, double c10, double c01, double c11, double u, double v)
        {
            double a = c00 + (u * (c10 - c00));
            double b = c01 + (u * (c11 - c01));
            return a + (v * (b - a));
        }
    }

    /// <summary>
    /// A tile's vertex batch: directions (unit, body-fixed, double3) → shape
    /// (double) + color mask (float; color does not affect physics).
    /// Calls the same statics.
    /// </summary>
    [BurstCompile]
    public struct TerrainTileJob : IJobParallelFor
    {
        [ReadOnly]
        public TerrainNoiseParams Params;

        [ReadOnly]
        public NativeArray<double3> Directions;

        public NativeArray<double> Heights;
        public NativeArray<float> ColorMasks;
        public NativeArray<float> ColorDetails;

        public void Execute(int index)
        {
            double3 direction = Directions[index];
            Heights[index] = TerrainNoise.SampleHeight(Params, direction);
            ColorMasks[index] = Params.ComputeMask ? (float)TerrainNoise.SampleColorNoise(Params, direction) : 0f;
            if (ColorDetails.IsCreated)
            {
                ColorDetails[index] = Params.ComputeDetail
                    ? (float)TerrainNoise.SampleColorDetailNoise(Params, direction)
                    : 0f;
            }
        }
    }
}