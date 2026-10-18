using System;
using Galilego.Core;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Procedural relief: fBm value-noise on a BODY-FIXED unit direction
    /// (not a lat/lon texture — no seams or pole degeneracies). Deterministic by
    /// seed: physics and rendering read the same function.
    ///   H(lat,lon) = shape(dir(lat,lon))·Amplitude, clamped below at sea level.
    /// The sea is a hard clamp: water is physically flat, landing on water = landing.
    /// Continent mask (low-frequency fBm → smoothstep ocean/land + basin depth),
    /// ridged term (mountain ridges, land only) and domain-warp on INDEPENDENT
    /// noise (its own seed stream — otherwise folds would correlate with the
    /// continent mask). All new parameters at zero/default give bit-for-bit legacy
    /// fBm.
    /// The normal comes from the same H function via neighbouring points, but the
    /// neighbours' world positions come from body.GetSurfaceState (direct lat/lon→
    /// world mapping: tilt+spin handled by the CORE, not a duplicate of the math):
    /// parity by construction.
    /// </summary>
    public sealed class HeightfieldTerrain : ITerrainModel
    {
        /// <summary>Noise seed: same seed = same relief.</summary>
        public int Seed;

        /// <summary>Relief amplitude above/below Radius (m). Nominal: with continent
        /// depth enabled max |H| <= Amplitude·(1 + ContinentDepth).</summary>
        public double AmplitudeMeters = 1000d;

        /// <summary>Base frequency (cycles per unit direction vector).</summary>
        public double BaseFrequency = 3d;

        /// <summary>Octave count (each ×Lacunarity frequency, ×Gain amplitude).</summary>
        public int Octaves = 5;

        /// <summary>Sea level above Radius (m). −∞ = no sea.</summary>
        public double SeaLevelMeters = double.NegativeInfinity;

        /// <summary>fBm lacunarity (frequency multiplier per octave). 2 = legacy; outside [1, 8] = legacy.</summary>
        public double Lacunarity = 2d;

        /// <summary>Amplitude falloff per octave. 0.5 = legacy; <=0 or &gt;1 = legacy.</summary>
        public double Gain = 0.5d;
        /// <summary>Continent mask frequency. <=0 = off (mask ≡ 1, legacy).</summary>
        public double ContinentFrequency = 0d;

        /// <summary>Continent mask octave count.</summary>
        public int ContinentOctaves = 3;

        /// <summary>Mask threshold: above — land, below — ocean.</summary>
        public double ContinentThreshold = 0d;

        /// <summary>Half-width of the mask smoothstep transition.</summary>
        public double ContinentSharpness = 0.25d;

        /// <summary>Ocean basin depth as a fraction of amplitude (subtracted where the mask is absent).</summary>
        public double ContinentDepth = 0.75d;

        /// <summary>Continent mask amplitude falloff. <=0 — follow the global Gain (legacy).</summary>
        public double ContinentGain = 0d;

        /// <summary>
        /// Strength of a SEPARATE domain-warp of the continent mask. 0 = the mask uses
        /// the relief's global warp (legacy). The separate warp keeps continents from
        /// inheriting the mountain warp's scale (weaker and higher-frequency, too fine
        /// for ocean basins). Source is the unwarped direction, so the second warp does
        /// not stack over the first.
        /// </summary>
        public double ContinentWarpStrength = 0d;

        /// <summary>Continent mask warp frequency.</summary>
        public double ContinentWarpFrequency = 0d;

        /// <summary>Continent mask warp octave count.</summary>
        public int ContinentWarpOctaves = 3;

        /// <summary>
        /// Contribution of the mask's "ridge" term: continentRaw += ContinentRidgeMix·
        /// (1 − 2·|fBm|). 0 = off (legacy). The zero line |fBm| = 0 is the isoline where
        /// the mask is maximal; it runs in long chains, so continents come out elongated
        /// rather than round. The term is not centred — a bias toward land, cancelled by
        /// ContinentThreshold.
        /// </summary>
        public double ContinentRidgeMix = 0d;

        /// <summary>Continent mask ridge term frequency.</summary>
        public double ContinentRidgeFrequency = 0d;

        /// <summary>Continent mask ridge term octave count.</summary>
        public int ContinentRidgeOctaves = 2;

        /// <summary>
        /// Equatorial mask bias: continentRaw += ContinentLatitudeBias·
        /// (1 − 2·|sin φ|). 0 = off (legacy). The mean of |sin φ| is exactly 0.5,
        /// so the term is centred and does not change the land fraction by itself.
        /// </summary>
        public double ContinentLatitudeBias = 0d;

        /// <summary>
        /// Abyssal floor depth, normalized units.
        /// &lt;= 0 (default) — no distinct ocean: only the
        /// "minus (1−mask)·ContinentDepth" term applies, so the "ocean" is wherever
        /// the high-frequency shape happens to fall below zero.
        ///
        /// &gt; 0 — the relief is blended with a floor whose depth grows with distance
        /// from shore in mask units. The ocean is then guaranteed below sea level, the
        /// floor flat and the margin gentle. ContinentDepth is unused in this mode.
        /// </summary>
        public double OceanFloorDepth = 0d;

        /// <summary>
        /// Shelf depth at the shore, normalized units. Multiplied by ocean, so it is
        /// always zero on land and in the ocean gives a gentle shallow shelf hundreds
        /// of kilometres wide in front of the continental margin. 0 = none.
        /// </summary>
        public double OceanShelfDepth = 0d;

        /// <summary>
        /// Interior floor, normalized units. &lt;= 0 — off.
        ///
        /// Land does not drop below continent·InteriorFloor. Near the shore the mask
        /// is ≈ 0 and so is the floor, preserving islands, bays and beaches; inland
        /// the floor rises above the sea, so there are no inland lakes.
        /// </summary>
        public double InteriorFloor = 0d;

        /// <summary>Mountain belt mask frequency. <=0 = off (relief range is uniform everywhere).</summary>
        public double OrogenyFrequency = 0d;

        /// <summary>Mountain belt mask octave count.</summary>
        public int OrogenyOctaves = 3;

        /// <summary>Belt mask threshold: above — mountains.</summary>
        public double OrogenyThreshold = 0d;

        /// <summary>Half-width of the belt mask smoothstep transition.</summary>
        public double OrogenySharpness = 0.3d;

        /// <summary>Relief range fraction outside belts (0..1). 1 = no belts (legacy).</summary>
        public double OrogenyFloor = 1d;

        /// <summary>Range fraction added inside a belt on top of OrogenyFloor.</summary>
        public double OrogenyGain = 0d;

        /// <summary>Ridged noise fraction 0..1 (mountain ridges, land only). 0 = off (legacy).</summary>
        public double RidgedMix = 0d;

        /// <summary>Ridged term assembly: 0 = sum of octaves (legacy), 1 = multifractal (branching ridges).</summary>
        public int RidgedMode = (int)TerrainRidgedMode.Legacy;

        /// <summary>Ridge sharpening in multifractal, quantized to an integer 1..4. 2 = classic.</summary>
        public double RidgedSharpness = 2d;

        /// <summary>How strongly the upper octave weight depends on the lower one (ridged multifractal).</summary>
        public double RidgedWeightGain = 2d;

        /// <summary>Ridge level remap in multifractal: s^γ. 1 = no remap. γ &lt; 1 lifts
        /// ridges toward the top of [0,1], γ &gt; 1 presses them down.</summary>
        public double RidgedGamma = 1d;

        /// <summary>Per-octave domain strength 0..1 for the relief SHAPE. 0 = off.</summary>
        public double SlopeDamp = 0d;

        /// <summary>Slope source for the domain: Accum (by height) or Gradient (by steepness).</summary>
        public int SlopeDampMode = (int)TerrainSlopeDampMode.Off;

        /// <summary>Base primitive: 0 = value noise (legacy), 1 = gradient Perlin.
        /// The default is legacy; Perlin is enabled explicitly in the preset.</summary>
        public int NoiseStyle = (int)TerrainNoiseStyle.Value;


        /// <summary>Primitive for MASKS and warp, separate from the shape. 0 = follows
        /// NoiseStyle. Value is cheaper: value noise is about three times faster than
        /// gradient noise, and the continent mask is read as a quantile, so after RMS
        /// normalization it does not depend on the primitive.</summary>
        public int MaskNoiseStyle = -1;
        /// <summary>Plain strength 0..1: in mask zones the relief is pulled toward a
        /// low plateau. 0 = off (legacy, bit-for-bit). Plains only on land.</summary>
        public double PlainMix = 0d;

        /// <summary>Low-frequency plain mask frequency. ≤0 = off (legacy).</summary>
        public double PlainFrequency = 0d;

        /// <summary>Plain mask octave count.</summary>
        public int PlainOctaves = 2;

        /// <summary>Plain mask threshold: above — plain.</summary>
        public double PlainThreshold = 0d;

        /// <summary>Half-width of the plain mask smoothstep transition.</summary>
        public double PlainSharpness = 0.3d;

        /// <summary>Normalized height of the plain plateau (fraction of AmplitudeMeters).</summary>
        public double PlainElevation = 0.1d;

        /// <summary>Soft compression of the upper tail of the shape, normalized units.
        /// TailKnee <= 0 (default) — off. Cuts only the tail above TailThreshold.</summary>
        public double TailKnee = 0d;

        /// <summary>Upper tail compression threshold (normalized units).</summary>
        public double TailThreshold = 0d;

        /// <summary>Soft compression of the lower (ocean) tail, normalized units.
        /// DepthKnee <= 0 (default) — off. Mirror of TailKnee: cuts the deep floor
        /// without touching shore or shelf.</summary>
        public double DepthKnee = 0d;

        /// <summary>Lower compression threshold (normalized units).</summary>
        public double DepthThreshold = 0d;

        /// <summary>Beach height above the sea (m): below it is sand and no spawning.
        /// 0 = no beach (legacy).</summary>
        public double BeachHeightMeters = 0d;

        /// <summary>Beach shelf altitude above the sea (m): coastal relief is pulled
        /// toward it at the water line (land only, ocean untouched).
        /// 0 = shelf off (legacy).</summary>
        public double BeachShelfAltitudeMeters = 0d;

        /// <summary>Shelf half-width in continent-mask units. 0 = off (legacy).</summary>
        public double BeachShelfWidth = 0d;

        /// <summary>Maximum beach shelf width multiplier. The width varies smoothly
        /// along the shore from 1× up to this value (noise). ≤1 = uniform everywhere (legacy).</summary>
        public double BeachShelfWidthMaxScale = 1d;

        /// <summary>Beach width noise frequency (cycles per unit vector). 0 = off.</summary>
        public double BeachShelfWidthNoiseFrequency = 0d;

        /// <summary>Beach width noise octave count.</summary>
        public int BeachShelfWidthNoiseOctaves = 4;

        /// <summary>
        /// Width of the shelf's "seaward shoulder", continent-mask units: a band BEYOND
        /// the mask threshold over which the shelf weight fades to zero, replacing a
        /// step on the threshold isoline (which would render as a picket fence of
        /// vertical triangles) with a continuous falloff. In metres it equals
        /// shoulder/|∇mask|, the same scale as the shelf. ≤0 — auto:
        /// 5 % of BeachShelfWidth.
        /// </summary>
        public double BeachShelfSeawardWidth = 0d;

        /// <summary>Small-scale detail (rocks/scree) as a fraction of AmplitudeMeters.
        /// Added by a high-frequency stream on land only, fades out on plains.
        /// 0 = off (legacy).</summary>
        public double DetailMix = 0d;

        /// <summary>Detail base frequency (cycles per unit vector). ≤0 = off.</summary>
        public double DetailFrequency = 0d;

        /// <summary>Detail octave count.</summary>
        public int DetailOctaves = 5;

        /// <summary>Domain-warp strength in direction units (typically 0.05..0.3). 0 = off (legacy).</summary>
        public double WarpStrength = 0d;

        /// <summary>Warp noise base frequency.</summary>
        public double WarpFrequency = 1d;

        /// <summary>Warp noise octave count.</summary>
        public int WarpOctaves = 2;

        /// <summary>Warp stream offset (integer). Warp always uses independent noise:
        /// other seed constants plus this offset — no correlation with the base fBm by construction.</summary>
        public int WarpSeedOffset = 0;

        /// <summary>
        /// Rock-override threshold in slope-angle tangent: steeper — rock at any land
        /// height (beach included). ≤0 = off (legacy: rock by height only). It is a
        /// guard, not a literal comparison — at 0 any non-zero slope would pass the
        /// "≥ 0" threshold almost everywhere.
        /// </summary>
        public double ColorRockSlopeTan = 0d;

        /// <summary>Half-width of the smoothstep blend into rock (in tan units).</summary>
        public double ColorRockSlopeWidth = 0.1d;

        /// <summary>
        /// Minimum normalized height t=(h−sea)/amp for rock-override: rock only on major
        /// mountains, beach and lowlands stay green. ≤0 = no height threshold
        /// (legacy: rock by slope alone).
        /// </summary>
        public double ColorRockHeightMin = 0d;

        /// <summary>Max slope for snow (tan): steeper — rock instead of snow. ≤0 = off
        /// (legacy: snow by height only). Guard symmetric with rock: at 0 the
        /// "slope ≤ 0" condition would pass almost nowhere.</summary>
        public double ColorSnowSlopeTan = 0d;

        /// <summary>Color mask noise frequency (band break-up). ≤0 = off. Independent
        /// seed stream (salt 4) — color breaks correlate with neither ridges nor warp.</summary>
        public double ColorNoiseFrequency = 0d;

        /// <summary>Mask noise octave count.</summary>
        public int ColorNoiseOctaves = 3;

        /// <summary>Mask strength: shifts the normalized height t before the bands.
        /// 0 = off. The mask is evaluated BEFORE the layers (it shifts thresholds
        /// rather than painting over).</summary>
        public double ColorNoiseStrength = 0d;

        /// <summary>Mask stream offset (integer).</summary>
        public int ColorNoiseSeedOffset = 0;

        /// <summary>Small-scale color detail frequency (ground mottling). ≤0 = off.
        /// Separate stream (salt 7): soil patches on the ground.</summary>
        public double ColorDetailFrequency = 0d;

        /// <summary>Color detail octave count.</summary>
        public int ColorDetailOctaves = 3;

        /// <summary>Mottling strength 0..1 (0 = off).</summary>
        public double ColorDetailStrength = 0d;

        /// <summary>Color detail stream offset (integer).</summary>
        public int ColorDetailSeedOffset = 0;

        /// <summary>Surface palette (sRGB). The renderer converts it to Linear on the
        /// fly; the preset lives in TerrainProfile.</summary>
        public TerrainPaletteData Palette = new TerrainPaletteData();

        /// <summary>Relief textures from the profile (null = procedural palette).</summary>
        public Texture2D TextureLow;
        public Texture2D TextureMid;
        public Texture2D TextureHigh;
        public Texture2D TextureSteep;
        public Texture2D TextureOcclusion;
        public double TextureScale = 0.04d;
        public double LowMidBlendStart = 30d;
        public double LowMidBlendEnd = 60d;
        public double MidHighBlendStart = 2500d;
        public double MidHighBlendEnd = 3500d;
        public double SteepBlendStart = 0.7d;
        public double SteepBlendEnd = 1.4d;

        /// <summary>Copy the profile into the live runtime representation. The SINGLE
        /// copy site. Seed is a parameter: it is per-body, the profile is a preset.</summary>
        public void ApplyProfile(TerrainProfile profile, int seed)
        {
            if (profile == null)
            {
                return;
            }

            Seed = seed;
            AmplitudeMeters = profile.AmplitudeMeters;
            BaseFrequency = profile.BaseFrequency;
            Octaves = profile.Octaves;
            SeaLevelMeters = profile.SeaLevelMeters;
            Lacunarity = profile.Lacunarity;
            Gain = profile.Gain;
            ContinentFrequency = profile.ContinentFrequency;
            ContinentOctaves = profile.ContinentOctaves;
            ContinentThreshold = profile.ContinentThreshold;
            ContinentSharpness = profile.ContinentSharpness;
            ContinentDepth = profile.ContinentDepth;
            ContinentGain = profile.ContinentGain;
            ContinentWarpStrength = profile.ContinentWarpStrength;
            ContinentWarpFrequency = profile.ContinentWarpFrequency;
            ContinentWarpOctaves = profile.ContinentWarpOctaves;
            ContinentRidgeMix = profile.ContinentRidgeMix;
            ContinentRidgeFrequency = profile.ContinentRidgeFrequency;
            ContinentRidgeOctaves = profile.ContinentRidgeOctaves;
            ContinentLatitudeBias = profile.ContinentLatitudeBias;
            OceanFloorDepth = profile.OceanFloorDepth;
            OceanShelfDepth = profile.OceanShelfDepth;
            InteriorFloor = profile.InteriorFloor;
            OrogenyFrequency = profile.OrogenyFrequency;
            OrogenyOctaves = profile.OrogenyOctaves;
            OrogenyThreshold = profile.OrogenyThreshold;
            OrogenySharpness = profile.OrogenySharpness;
            OrogenyFloor = profile.OrogenyFloor;
            OrogenyGain = profile.OrogenyGain;
            RidgedMix = profile.RidgedMix;
            RidgedMode = profile.RidgedMode;
            RidgedSharpness = profile.RidgedSharpness;
            RidgedWeightGain = profile.RidgedWeightGain;
            RidgedGamma = profile.RidgedGamma;
            SlopeDamp = profile.SlopeDamp;
            SlopeDampMode = profile.SlopeDampMode;
            NoiseStyle = profile.NoiseStyle;
            MaskNoiseStyle = profile.MaskNoiseStyle;
            PlainMix = profile.PlainMix;
            PlainFrequency = profile.PlainFrequency;
            PlainOctaves = profile.PlainOctaves;
            PlainThreshold = profile.PlainThreshold;
            PlainSharpness = profile.PlainSharpness;
            PlainElevation = profile.PlainElevation;
            TailKnee = profile.TailKnee;
            TailThreshold = profile.TailThreshold;
            DepthKnee = profile.DepthKnee;
            DepthThreshold = profile.DepthThreshold;
            BeachHeightMeters = profile.BeachHeightMeters;
            BeachShelfAltitudeMeters = profile.BeachShelfAltitudeMeters;
            BeachShelfWidth = profile.BeachShelfWidth;
            BeachShelfWidthMaxScale = profile.BeachShelfWidthMaxScale;
            BeachShelfWidthNoiseFrequency = profile.BeachShelfWidthNoiseFrequency;
            BeachShelfWidthNoiseOctaves = profile.BeachShelfWidthNoiseOctaves;
            BeachShelfSeawardWidth = profile.BeachShelfSeawardWidth;
            DetailMix = profile.DetailMix;
            DetailFrequency = profile.DetailFrequency;
            DetailOctaves = profile.DetailOctaves;
            WarpStrength = profile.WarpStrength;
            WarpFrequency = profile.WarpFrequency;
            WarpOctaves = profile.WarpOctaves;
            WarpSeedOffset = profile.WarpSeedOffset;
            ColorRockSlopeTan = profile.ColorRockSlopeTan;
            ColorRockSlopeWidth = profile.ColorRockSlopeWidth;
            ColorRockHeightMin = profile.ColorRockHeightMin;
            ColorSnowSlopeTan = profile.ColorSnowSlopeTan;
            ColorNoiseFrequency = profile.ColorNoiseFrequency;
            ColorNoiseOctaves = profile.ColorNoiseOctaves;
            ColorNoiseStrength = profile.ColorNoiseStrength;
            ColorNoiseSeedOffset = profile.ColorNoiseSeedOffset;
            ColorDetailFrequency = profile.ColorDetailFrequency;
            ColorDetailOctaves = profile.ColorDetailOctaves;
            ColorDetailStrength = profile.ColorDetailStrength;
            ColorDetailSeedOffset = profile.ColorDetailSeedOffset;
            Palette = profile.Palette ?? new TerrainPaletteData();
            TextureLow = profile.TextureLow;
            TextureMid = profile.TextureMid;
            TextureHigh = profile.TextureHigh;
            TextureSteep = profile.TextureSteep;
            TextureOcclusion = profile.TextureOcclusion;
            TextureScale = profile.TextureScale;
            LowMidBlendStart = profile.LowMidBlendStart;
            LowMidBlendEnd = profile.LowMidBlendEnd;
            MidHighBlendStart = profile.MidHighBlendStart;
            MidHighBlendEnd = profile.MidHighBlendEnd;
            SteepBlendStart = profile.SteepBlendStart;
            SteepBlendEnd = profile.SteepBlendEnd;
        }

        /// <summary>Build a runtime relief from the profile (the palette is cloned — the asset is not mutated).</summary>
        public static HeightfieldTerrain FromProfile(TerrainProfile profile, int seed)
        {
            var terrain = new HeightfieldTerrain();
            terrain.ApplyProfile(profile, seed);
            terrain.Palette = terrain.Palette.Clone();
            return terrain;
        }

        /// <summary>
        /// Table of flat pads (PQS mods). Always VALID, even when there are no pads: the
        /// initializer sets the shared empty table TerrainModifiers.Empty, because the
        /// renderer passes the whole TerrainNoiseParams through a Burst job, and the Unity
        /// scheduler requires valid containers in every field of the job struct.
        ///
        /// Owned here, not in TerrainNoiseParams.FromTerrain: the latter is called on
        /// every height query and must not allocate.
        /// </summary>
        public NativeArray<TerrainModifierData> Mods = TerrainModifiers.Empty;

        /// <summary>
        /// Decor exclusion zones: no trees, grass or rocks grow under them. Built by
        /// DecorExclusionTable from site objects; the table is owned here, by the same
        /// rule as Mods: a rebuild every half second must not allocate on every height query.
        /// </summary>
        public NativeArray<DecorExclusionData> DecorExclusions = DecorExclusionTable.Empty;

        /// <summary>Angular neighbour step for the normal (rad): resolves 5 octaves with margin.</summary>
        private const double NormalEpsilonRadians = 1e-4d;

        /// <summary>
        /// Rebuild the pad table. The old one is released: the table is Persistent and
        /// the system is rebuilt on every scene edit.
        ///
        /// Order matters: build the new table, install it, and only then release the old
        /// one. The renderer keeps its own copy of the descriptor and updates it in
        /// Update(), while jobs are built in LateUpdate() — between those points nobody
        /// can schedule the freed array. Releasing before installing would leave a
        /// pointer to freed memory in the renderer's cache.
        /// </summary>
        public void SetModifiers(System.Collections.Generic.IList<TerrainModifier> source, double bodyRadiusMeters)
        {
            NativeArray<TerrainModifierData> built = TerrainModifiers.Build(source, bodyRadiusMeters);
            NativeArray<TerrainModifierData> previous = Mods;
            Mods = built;
            TerrainModifiers.Release(ref previous);
        }

        /// <summary>Release the pad table (on system teardown).</summary>
        public void DisposeModifiers()
        {
            NativeArray<TerrainModifierData> current = Mods;
            Mods = TerrainModifiers.Empty;
            TerrainModifiers.Release(ref current);
            DecorExclusionTable.Release(ref DecorExclusions);
        }

        public double GetHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians)
        {
            // The formula is evaluated directly, not via GetRawHeightMeters: here we
            // need to know whether a pad overrode the sea clamp. Otherwise an excavation
            // below sea level would silently fill with water.
            Vector3d direction = LatLonToDirection(latitudeRadians, longitudeRadians);
            var dir = new double3(direction.X, direction.Y, direction.Z);
            TerrainNoiseParams p = TerrainNoiseParams.FromTerrain(this);
            double height = TerrainNoise.SampleHeight(p, dir) * AmplitudeMeters;
            if (height < SeaLevelMeters && !TerrainNoise.ModsOverrideSeaLevel(p, dir))
            {
                height = SeaLevelMeters;
            }

            return height;
        }

        public double GetRawHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians)
        {
            // The shape is evaluated by the SINGLE implementation — TerrainNoise (Burst):
            // physics and rendering read one function, no duplication.
            // NO sea clamp: the ocean floor lies below sea level.
            Vector3d direction = LatLonToDirection(latitudeRadians, longitudeRadians);
            return TerrainNoise.SampleHeight(
                TerrainNoiseParams.FromTerrain(this),
                new double3(direction.X, direction.Y, direction.Z)) * AmplitudeMeters;
        }

        public double GetSeaLevelMeters()
        {
            return SeaLevelMeters;
        }

        public Vector3d GetOutwardNormal(OrbitingBody body, Vector3d relativePosition, double timeSeconds)
        {
            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
            body.SurfaceLatLonAt(bodyPosition + relativePosition, timeSeconds, out double latDeg, out double lonDeg);
            double lat = latDeg * (Math.PI / 180d);
            double lon = lonDeg * (Math.PI / 180d);

            double latUp = Math.Min(lat + NormalEpsilonRadians, 1.5707963267948966d - 1e-9d);
            double h0 = GetHeightMeters(body, lat, lon);
            double hUp = GetHeightMeters(body, latUp, lon);
            double hEast = GetHeightMeters(body, lat, lon + NormalEpsilonRadians);

            body.GetSurfaceState(lat * (180d / Math.PI), lon * (180d / Math.PI), h0, timeSeconds, out Vector3d p0, out _);
            body.GetSurfaceState(latUp * (180d / Math.PI), lon * (180d / Math.PI), hUp, timeSeconds, out Vector3d p1, out _);
            body.GetSurfaceState(lat * (180d / Math.PI), (lon + NormalEpsilonRadians) * (180d / Math.PI), hEast, timeSeconds, out Vector3d p2, out _);

            Vector3d normal = Vector3d.Cross(p1 - p0, p2 - p0).Normalized;
            if (Vector3d.Dot(normal, relativePosition) < 0d)
            {
                normal = -normal;
            }

            return normal;
        }

        /// <summary>
        /// Color mask noise in ~[−1, 1] over lat/lon (radians): shifts the height band
        /// boundaries, breaking up uniform stripes. A pure function of direction —
        /// deterministic by seed, called by the renderer per vertex (not physics).
        /// </summary>
        public double SampleColorNoise(double latitudeRadians, double longitudeRadians)
        {
            Vector3d direction = LatLonToDirection(latitudeRadians, longitudeRadians);
            return TerrainNoise.SampleColorNoise(
                TerrainNoiseParams.FromTerrain(this),
                new double3(direction.X, direction.Y, direction.Z));
        }

        /// <summary>Small-scale color detail (mottling) in ~[−1, 1] over lat/lon.</summary>
        public double SampleColorDetailNoise(double latitudeRadians, double longitudeRadians)
        {
            Vector3d direction = LatLonToDirection(latitudeRadians, longitudeRadians);
            return TerrainNoise.SampleColorDetailNoise(
                TerrainNoiseParams.FromTerrain(this),
                new double3(direction.X, direction.Y, direction.Z));
        }

        /// <summary>Body-fixed direction from lat/lon (radians) — the same basis as in GetSurfaceState.</summary>
        private static Vector3d LatLonToDirection(double lat, double lon)
        {
            double cosLat = Math.Cos(lat);
            return new Vector3d(cosLat * Math.Cos(lon), cosLat * Math.Sin(lon), Math.Sin(lat));
        }

    }
}
