using System;
using Galilego.Core;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Galilego.Universe
{
    /// <summary>
    /// Flat decor placement parameters for the Burst job: blittable only,
    /// no references. Built from GroundDecorLayer + HeightfieldTerrain
    /// (terrain shape and sea level are the same as in physics/rendering).
    /// </summary>
    public struct GroundDecorPlacementParams
    {
        /// <summary>Terrain amplitude (m) — from HeightfieldTerrain.</summary>
        public double AmplitudeMeters;

        /// <summary>Sea level (m) — from HeightfieldTerrain.</summary>
        public double SeaLevelMeters;

        /// <summary>Body radius (m): converts the slope's angular step into meters.</summary>
        public double RadiusMeters;

        /// <summary>Finite-difference base for the slope (m).</summary>
        public double SampleStepMeters;

        public double MinAltitudeMeters;
        public double MaxAltitudeMeters;

        /// <summary>Min. normalized palette height (sandy beach band).</summary>
        public double MinNormalizedHeight;

        /// <summary>Max. normalized palette height (rocks/snow). &lt;=0 = disabled.</summary>
        public double MaxNormalizedHeight;

        /// <summary>Beach height above sea (m) — from HeightfieldTerrain: below it
        /// NOTHING is placed (neither grass nor rocks). 0 = no beach.</summary>
        public double BeachHeightMeters;

        /// <summary>Terrain color-mask strength (same as in the palette shader).</summary>
        public double ColorNoiseStrength;

        public double MaxSlopeTan;
        public bool AvoidWater;
        public double WetMin;
        public double WetMax;
        public double WetFade;

        /// <summary>Do not apply polar fade (tundra/snow): rocks lie on snow too.</summary>
        public bool IgnoreLatitude;

        /// <summary>Soft road exclusion zone: grass is measured with a small rectangle
        /// (0.3 m from the edge), trees/rocks with the full one (1 m). See GroundDecorLayer.SoftExclusion.</summary>
        public bool SoftExclusion;

        public double Density;
        public double DistributionFrequency;
        public int DistributionOctaves;
        public int DistributionSeedOffset;
        public double ClusterThreshold;
        public double ClusterFade;
        public double MinScale;
        public double MaxScale;
        public double SteepPower;

        /// <summary>Axial offset (m) along the radial: the model pivot is not at the base.</summary>
        public double GroundOffsetMeters;

        /// <summary>Sink into the ground: fraction of the instance scale.</summary>
        public double GroundSinkFactor;

        /// <summary>Density falloff by distance to the CAMERA (within the chunk):
        /// enabled by the renderer. Keeps islands dense up close and sparse in
        /// the distance instead of smearing the whole chunk by the instance limit.</summary>
        public bool UseCameraFalloff;

        /// <summary>Camera in chunk-local coordinates (sim, m).</summary>
        public double CameraLocalX;
        public double CameraLocalY;
        public double CameraLocalZ;

        /// <summary>Distance (m) from which thinning begins.</summary>
        public double FalloffNearMeters;

        /// <summary>Distance (m) of full thinning (FarDensity).</summary>
        public double FalloffFarMeters;

        /// <summary>Density at the far boundary (0..1).</summary>
        public double FarDensity;

        /// <summary>Exponential falloff scale (m); 0 = linear.</summary>
        public double FalloffScaleMeters;

        /// <summary>Falloff power (0 = exponential, 2 = coverage law).</summary>
        public double DensityFalloffPower;

        /// <summary>Radius of the flat core (m): within it the density is full.</summary>
        public double DensityCoreMeters;

        /// <summary>Compute density PER INSTANCE (sub-tuft), not per cell:
        /// the cell only selects candidates, while falloff/density/random are
        /// applied in the renderer to each sub-tuft.</summary>
        public bool PerInstanceDensity;

        /// <summary>Zone wind direction (rad): set by the caller after FromLayer.</summary>
        public double WindAzimuthRad;
        public double WindJitterRad;
        public double WindLeanMinDegrees;
        public double WindLeanMaxDegrees;
        public double ResolvedMinSink;
        public double ResolvedMaxSink;

        /// <summary>Number of near-LOD meshes in the layer (for random variant selection).</summary>
        public int NearMeshCount;

        /// <summary>Chunk center (body-fixed, m): instance positions are local to it (float precision).</summary>
        public double ChunkCenterX;
        public double ChunkCenterY;
        public double ChunkCenterZ;

        public static GroundDecorPlacementParams FromLayer(
            GroundDecorLayer layer, HeightfieldTerrain terrain, double radiusMeters, Vector3d chunkCenter)
        {
            return new GroundDecorPlacementParams
            {
                AmplitudeMeters = terrain.AmplitudeMeters,
                SeaLevelMeters = terrain.SeaLevelMeters,
                RadiusMeters = radiusMeters,
                SampleStepMeters = 1d,
                NearMeshCount = layer.NearMeshes != null ? layer.NearMeshes.Length : 0,
                MinAltitudeMeters = layer.MinAltitudeMeters,
                MaxAltitudeMeters = layer.MaxAltitudeMeters,
                MinNormalizedHeight = layer.MinNormalizedHeight,
                MaxNormalizedHeight = layer.MaxNormalizedHeight,
                BeachHeightMeters = terrain.BeachHeightMeters,
                ColorNoiseStrength = terrain.ColorNoiseStrength,
                MaxSlopeTan = layer.MaxSlopeTan,
                AvoidWater = layer.AvoidWater,
                WetMin = layer.WetMin,
                WetMax = layer.WetMax,
                WetFade = layer.WetFade,
                IgnoreLatitude = layer.IgnoreLatitude,
                SoftExclusion = layer.SoftExclusion,
                Density = layer.Density,
                // The patch is given in meters — the frequency is derived from the body
                // radius: one noise period ≈ R/frequency meters along the surface.
                DistributionFrequency = layer.ClusterPatchMeters > 0d
                    ? radiusMeters / layer.ClusterPatchMeters
                    : layer.DistributionFrequency,
                DistributionOctaves = layer.DistributionOctaves,
                DistributionSeedOffset = layer.DistributionSeedOffset,
                ClusterThreshold = layer.ClusterThreshold,
                ClusterFade = layer.ClusterFade,
                MinScale = layer.MinScale,
                MaxScale = layer.MaxScale,
                SteepPower = layer.SteepPower,
                GroundOffsetMeters = layer.GroundOffsetMeters,
                GroundSinkFactor = layer.GroundSinkFactor,
                FalloffNearMeters = layer.NearDistanceMeters,
                FalloffFarMeters = Math.Max(layer.NearDistanceMeters + 1d, layer.MaxDistanceMeters),
                FarDensity = Math.Max(0d, Math.Min(1d, layer.FarDensity)),
                FalloffScaleMeters = layer.DensityFalloffMeters,
                DensityFalloffPower = Math.Max(0d, layer.DensityFalloffPower),
                DensityCoreMeters = Math.Max(0d, layer.DensityCoreMeters),
                WindJitterRad = layer.WindJitterDegrees * 0.017453292519943295d,
                WindLeanMinDegrees = layer.WindLeanMinDegrees,
                WindLeanMaxDegrees = layer.WindLeanMaxDegrees,
                ResolvedMinSink = layer.MinGroundSinkFactor >= 0d ? layer.MinGroundSinkFactor : layer.GroundSinkFactor,
                ResolvedMaxSink = layer.MaxGroundSinkFactor >= 0d ? layer.MaxGroundSinkFactor : layer.GroundSinkFactor,
                ChunkCenterX = chunkCenter.X,
                ChunkCenterY = chunkCenter.Y,
                ChunkCenterZ = chunkCenter.Z
            };
        }
    }

    /// <summary>One decor instance: position/normal is in the sim frame (Y-up), chunk-local.</summary>
    public struct GroundDecorInstance
    {
        public float3 Position;
        public float3 Normal;
        public float Scale;
        public float Yaw;
        public float SinkFactor;
        public float LeanDegrees;

        /// <summary>Surface weight (cluster×biome×slope) 0..1 — for PerInstanceDensity
        /// mode: the renderer rolls acceptance per sub-tuft.</summary>
        public float DensityWeight;

        /// <summary>Mesh variant index of the near LOD (for species variety).</summary>
        public int MeshIndex;

        /// <summary>When the blade appeared (Time.time + jitter). The matrix
        /// job grows it via scale over DecorBladeFadeSeconds: new blades appear
        /// one by one and smoothly, not as a band. When the pool is rebuilt old blades
        /// are carried over with their BirthTime — already grown ones do not «sprout» again.</summary>
        public float BirthTime;

        /// <summary>When the instance started to sink under the ground (Time.time + jitter),
        /// 0 = alive. Set on blades dropped from the new selection when the pool is
        /// rebuilt. The matrix job fades them out via scale over DecorBladeSinkSeconds —
        /// mirroring the appearance.</summary>
        public float DeathTime;
    }

    /// <summary>
    /// Deterministic placement: a pure function of direction. Height/slope are
    /// computed by the SAME TerrainNoise as terrain physics and rendering (parity
    /// by construction); the biome uses the same wet mask SampleColorNoise that colors
    /// the surface. No runtime RNG: seed + direction hash.
    /// </summary>
    public static class GroundDecorDistribution
    {
        /// <summary>
        /// Biome wetness wet01 from the terrain color mask — the SINGLE source
        /// for painting (TerrainPalette) and placement (here), mirrored in
        /// PlanetSurface.shader (TerrainAlbedo). Any divergence = «green terrain
        /// with no grass/trees». tanh keeps the middle linear without saturating
        /// to 0/1, unlike a clamp.
        /// </summary>
        public static double BiomeWetness(double colorMask)
        {
            double mask = colorMask < -1d ? -1d : (colorMask > 1d ? 1d : colorMask);
            return 0.5d + (0.5d * Math.Tanh(1.6d * mask));
        }

        /// <summary>Hash in [0,1) — cell jitter/scale/rotation (deterministic).</summary>
        public static double Hash01(int a, int b, int c, int d, int salt)
        {
            unchecked
            {
                int h = (a * 374761393) + (b * 668265263) + (c * 1274126177) + (d * 2147483647) + (salt * 7919);
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216d;
            }
        }

        /// <summary>
        /// Normalized palette height: t = h/amp + mask·strength. The same
        /// quantity that colors the terrain (TerrainPalette) with the same mask shift.
        /// </summary>
        public static double NormalizedHeight(GroundDecorPlacementParams p, double aboveSeaMeters, double colorNoise)
        {
            return (aboveSeaMeters / Math.Max(1d, p.AmplitudeMeters))
                + (colorNoise * p.ColorNoiseStrength);
        }

        /// <summary>
        /// Uniform vegetation density across the whole planet (instead of polar
        /// falloff). The constant sets the density everywhere: 0.06 — polar tundra
        /// look, 0.15..0.3 — noticeably sparser than the equator, 1.0 — current
        /// equatorial density planet-wide.
        /// </summary>
        public const double UniformGreenWeight = 0.15d;

        /// <summary>
        /// Density multiplier by latitude. Layers with IgnoreLatitude (rocks) are
        /// unlimited, all vegetation gets UniformGreenWeight at any
        /// latitude, including the polar cap.
        /// </summary>
        public static double LatitudeGreenWeight(GroundDecorPlacementParams p, double3 direction)
        {
            return p.IgnoreLatitude ? 1d : UniformGreenWeight;
        }

        /// <summary>
        /// Exclusion-zone check taking the layer type into account: grass (SoftExclusion)
        /// is measured with the soft rectangle (0.3 m at roads), the rest with the full one
        /// (1 m at roads, 20 m at buildings).
        /// </summary>
        public static bool IsExcludedFor(GroundDecorPlacementParams p, TerrainNoiseParams terrain, double3 direction)
        {
            return p.SoftExclusion
                ? DecorExclusionTable.IsExcludedSoft(terrain.DecorExclusions, direction)
                : DecorExclusionTable.IsExcluded(terrain.DecorExclusions, direction);
        }

        /// <summary>
        /// Cheap hard surface filters (water/heights/beach/green top/wet biome/
        /// polar cap) — without slope, clusters or density. For re-checking
        /// EVERY sub-tuft on expand paths: the base candidate passes the full
        /// TryEvaluate at the cell center, while sub-tufts are scattered across the cell and
        /// on coarse LOD fall into water, sand and mountains. Any accepted
        /// TryEvaluate must also pass here (a necessary condition).
        /// Slope deliberately stays at cell level: SlopeTan costs 3 height
        /// samples per point, too expensive per sub-tuft, while biome boundaries
        /// (water/sand/rocks) are exactly what the eye sees.
        /// </summary>
        public static bool IsSurfaceAllowed(
            GroundDecorPlacementParams p, TerrainNoiseParams terrain, double3 direction)
        {
            // Building/road zone: nothing grows under it, including grass sub-tufts.
            if (IsExcludedFor(p, terrain, direction))
            {
                return false;
            }

            double rawHeight = TerrainNoise.SampleHeight(terrain, direction) * p.AmplitudeMeters;
            if (p.AvoidWater && rawHeight <= p.SeaLevelMeters + (p.AmplitudeMeters * 0.001d))
            {
                return false;
            }

            double aboveSea = rawHeight - p.SeaLevelMeters;
            if (aboveSea < p.MinAltitudeMeters || aboveSea > p.MaxAltitudeMeters)
            {
                return false;
            }

            // Beach: below its upper boundary NOTHING is placed — neither grass,
            // nor trees, nor rocks (single source — the terrain profile).
            if (p.BeachHeightMeters > 0d && aboveSea < p.BeachHeightMeters)
            {
                return false;
            }

            double colorNoise = Clamp(TerrainNoise.SampleColorNoise(terrain, direction), -1d, 1d);
            if (p.MinNormalizedHeight > 0d || p.MaxNormalizedHeight > 0d)
            {
                double normalized = NormalizedHeight(p, aboveSea, colorNoise);
                if ((p.MinNormalizedHeight > 0d && normalized < p.MinNormalizedHeight)
                    || (p.MaxNormalizedHeight > 0d && normalized > p.MaxNormalizedHeight))
                {
                    return false;
                }
            }

            double wet = BiomeWetness(colorNoise);
            if (wet < p.WetMin || wet > p.WetMax)
            {
                return false;
            }

            // Polar cap: with UniformGreenWeight > 0 vegetation is not cut here;
            // the green top/height/wet filters remain the limiters. The check stays
            // for the case UniformGreenWeight = 0 (a layer disabled by weight).
            if (LatitudeGreenWeight(p, direction) <= 0d)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Cube-sphere face direction from (u,v) to double3 — a Burst-compatible
        /// twin of CubeSphere.Direction (which uses Vector3d and lives only on the
        /// main thread). The face formula must mirror CubeSphere:
        /// if they diverge, sub-tuft re-checking looks in the wrong place.
        /// </summary>
        public static double3 CubeFaceDirection(int face, double u, double v)
        {
            double a = (u * 2d) - 1d;
            double b = (v * 2d) - 1d;
            double x;
            double y;
            double z;
            switch (face)
            {
                case 0: x = 1d; y = b; z = -a; break;
                case 1: x = -1d; y = b; z = a; break;
                case 2: x = a; y = 1d; z = -b; break;
                case 3: x = a; y = -1d; z = b; break;
                case 4: x = a; y = b; z = 1d; break;
                default: x = -a; y = b; z = -1d; break;
            }

            return math.normalize(new double3(x, y, z));
        }

        /// <summary>
        /// Radial height of a render-mesh point (chunk-local sim coordinates)
        /// above the body radius. Reverse sim→astro bridge (sim = (x, z, −y) from
        /// astro-rel, see AstroFrame): no UnityEngine dependency,
        /// works in Burst too. Needed because the filter uses the
        /// analytic height while the position snaps to the coarse chunk mesh:
        /// at coarse LOD the mesh dips below the sea where the analytic height sees land.
        /// </summary>
        public static double MeshPointHeightAboveRadius(GroundDecorPlacementParams p, float3 simPoint)
        {
            double ax = p.ChunkCenterX + simPoint.x;
            double ay = p.ChunkCenterY - simPoint.z;
            double az = p.ChunkCenterZ + simPoint.y;
            return Math.Sqrt((ax * ax) + (ay * ay) + (az * az)) - p.RadiusMeters;
        }

        /// <summary>
        /// Cheap guard for mesh snapping (no noise samples): a render-mesh point
        /// must be above water and within the layer altitude range. Catches trees and
        /// rocks «drowned» because the coarse mesh deviates from the analytic
        /// height. Biome/sand/rocks are not checked here — IsSurfaceAllowed
        /// catches them by direction.
        /// </summary>
        public static bool IsMeshPointAboveWater(GroundDecorPlacementParams p, float3 simPoint)
        {
            double height = MeshPointHeightAboveRadius(p, simPoint);
            if (p.AvoidWater && height <= p.SeaLevelMeters + (p.AmplitudeMeters * 0.001d))
            {
                return false;
            }

            double aboveSea = height - p.SeaLevelMeters;
            return aboveSea >= p.MinAltitudeMeters && aboveSea <= p.MaxAltitudeMeters;
        }

        /// <summary>
        /// Attempt to place an instance. random.x — acceptance roll, random.y — scale,
        /// random.z — rotation, meshPick — mesh variant selection [0,1). Filters:
        /// water, altitude, slope, wet biome, polar latitude, cluster noise; density is
        /// additionally damped by slope (SteepPower).
        /// </summary>
        public static bool TryEvaluate(
            GroundDecorPlacementParams p, TerrainNoiseParams terrain, double3 direction, double3 random, double meshPick,
            double buryRandom, double leanRandom,
            out GroundDecorInstance instance)
        {
            instance = default;

            // Building/road zone: a cell entirely inside the zone gets no
            // instance at all (IsSurfaceAllowed also checks sub-tufts).
            if (IsExcludedFor(p, terrain, direction))
            {
                return false;
            }

            double rawHeight = TerrainNoise.SampleHeight(terrain, direction) * p.AmplitudeMeters;
            if (p.AvoidWater && rawHeight <= p.SeaLevelMeters + (p.AmplitudeMeters * 0.001d))
            {
                return false;
            }

            double aboveSea = rawHeight - p.SeaLevelMeters;
            if (aboveSea < p.MinAltitudeMeters || aboveSea > p.MaxAltitudeMeters)
            {
                return false;
            }

            // Beach: below its upper boundary NOTHING is placed — neither grass,
            // nor trees, nor rocks (single source — the terrain profile).
            if (p.BeachHeightMeters > 0d && aboveSea < p.BeachHeightMeters)
            {
                return false;
            }

            double colorNoise = Clamp(TerrainNoise.SampleColorNoise(terrain, direction), -1d, 1d);

            // Same normalized height as the terrain palette:
            // t = h/amp + mask·strength. The lower bound cuts off the beach
            // band (colored Sand), the upper one rocks/snow (rocks only there).
            if (p.MinNormalizedHeight > 0d || p.MaxNormalizedHeight > 0d)
            {
                double normalized = NormalizedHeight(p, aboveSea, colorNoise);
                if ((p.MinNormalizedHeight > 0d && normalized < p.MinNormalizedHeight)
                    || (p.MaxNormalizedHeight > 0d && normalized > p.MaxNormalizedHeight))
                {
                    return false;
                }
            }

            double slope = SlopeTan(terrain, p, direction, out float3 surfaceNormal);
            if (slope > p.MaxSlopeTan)
            {
                return false;
            }

            double cluster = Clamp01((TerrainNoise.SampleDecorNoise(
                terrain, p.DistributionSeedOffset, p.DistributionFrequency, p.DistributionOctaves, direction) + 1d) * 0.5d);
            if (cluster < p.ClusterThreshold)
            {
                return false;
            }

            double wet = BiomeWetness(colorNoise);
            if (wet < p.WetMin || wet > p.WetMax)
            {
                return false;
            }

            // Soft edges: for islands (ClusterFade) and for the biome (WetFade).
            // 0 = hard threshold.
            double clusterWeight;
            if (p.ClusterFade > 1e-9d)
            {
                clusterWeight = Smoothstep01((cluster - p.ClusterThreshold) / p.ClusterFade);
                if (clusterWeight <= 0d)
                {
                    return false;
                }
            }
            else
            {
                clusterWeight = (cluster - p.ClusterThreshold) / Math.Max(1e-9d, 1d - p.ClusterThreshold);
            }

            double wetWeight = 1d;
            if (p.WetFade > 1e-9d)
            {
                // The ramp edge is only at boundaries INSIDE the range: wet never
                // goes above 1 or below 0, and a fade at WetMax=1 would zero out the
                // wettest biome (where wet is exactly 1).
                if (p.WetMin > 0d)
                {
                    wetWeight *= Smoothstep01((wet - p.WetMin) / p.WetFade);
                }

                if (p.WetMax < 1d)
                {
                    wetWeight *= Smoothstep01((p.WetMax - wet) / p.WetFade);
                }

                if (wetWeight <= 0d)
                {
                    return false;
                }
            }

            // Polar fade: tundra thins the density, solid ice forbids it
            // (rocks use IgnoreLatitude and don't see it).
            double latWeight = LatitudeGreenWeight(p, direction);
            if (latWeight <= 0d)
            {
                return false;
            }

            double slopeWeight = 1d - (slope / Math.Max(1e-9d, p.MaxSlopeTan));
            slopeWeight = Clamp01(slopeWeight);
            if (p.SteepPower > 0d)
            {
                slopeWeight = Math.Pow(slopeWeight, p.SteepPower);
            }

            double radius = p.MinScale + (random.y * Math.Max(0d, p.MaxScale - p.MinScale));
            double sinkFactor = p.ResolvedMinSink + (buryRandom * Math.Max(0d, p.ResolvedMaxSink - p.ResolvedMinSink));
            double offset = p.GroundOffsetMeters - (radius * sinkFactor);
            double3 astro = (direction * (p.RadiusMeters + rawHeight))
                + (new double3(surfaceNormal.x, -surfaceNormal.z, surfaceNormal.y) * offset);
            double3 rel = astro - new double3(p.ChunkCenterX, p.ChunkCenterY, p.ChunkCenterZ);

            // sim = (x, z, −y) — the same bridge as AstroFrame (without referencing UnityEngine).
            float3 simPosition = new float3((float)rel.x, (float)rel.z, (float)-rel.y);

            double accept = p.Density * clusterWeight * wetWeight * slopeWeight * latWeight;
            if (!p.PerInstanceDensity)
            {
                // Falloff by TANGENTIAL distance to the camera WITHIN the chunk:
                // flight altitude does not thin the grass beneath, while walking
                // still thins smoothly toward the boundary.
                if (p.UseCameraFalloff && p.FarDensity < 1d)
                {
                    double ddx = simPosition.x - p.CameraLocalX;
                    double ddy = simPosition.y - p.CameraLocalY;
                    double ddz = simPosition.z - p.CameraLocalZ;
                    double along = (ddx * surfaceNormal.x) + (ddy * surfaceNormal.y) + (ddz * surfaceNormal.z);
                    double tx = ddx - (surfaceNormal.x * along);
                    double ty = ddy - (surfaceNormal.y * along);
                    double tz = ddz - (surfaceNormal.z * along);
                    double distance = Math.Sqrt((tx * tx) + (ty * ty) + (tz * tz));
                    accept *= Falloff(p, distance);
                }

                if (random.x >= accept)
                {
                    return false;
                }
            }

            instance.Position = simPosition;

            // PerInstanceDensity mode: the island shape is the cell weight, while
            // the camera-distance falloff is computed by the RENDERER (where both points
            // are guaranteed to be in one coordinate system — the chunk mesh frame).
            instance.DensityWeight = (float)(clusterWeight * wetWeight * slopeWeight * latWeight);
            instance.Normal = surfaceNormal;
            instance.Scale = (float)radius;
            instance.SinkFactor = (float)sinkFactor;
            double jitter = (random.z - 0.5d) * 2d * p.WindJitterRad;
            // Wind azimuth ± jitter can go negative: matrices tolerate it, but the
            // instance contract is Yaw in [0, 2π), which the tests rely on.
            double turn = Math.PI * 2d;
            double yaw = p.WindAzimuthRad + jitter;
            yaw -= Math.Floor(yaw / turn) * turn;
            instance.Yaw = (float)yaw;
            instance.LeanDegrees = (float)(p.WindLeanMinDegrees + (leanRandom * Math.Max(0d, p.WindLeanMaxDegrees - p.WindLeanMinDegrees)));
            instance.MeshIndex = p.NearMeshCount > 1
                ? Math.Min(p.NearMeshCount - 1, (int)(meshPick * p.NearMeshCount))
                : 0;
            return true;
        }

        /// <summary>
        /// Slope tangent via central height differences in the tangent plane
        /// + the surface normal from the same three points (body-fixed formula
        /// p = dir·(R+h), cross of the differences) — cards/trees are placed along the slope
        /// rather than the radial (otherwise grass «lies flat» on slopes).
        /// </summary>
        public static double SlopeTan(
            TerrainNoiseParams terrain, GroundDecorPlacementParams p, double3 direction, out float3 surfaceNormal)
        {
            double epsilon = Math.Max(1e-7d, p.SampleStepMeters / Math.Max(1d, p.RadiusMeters));
            double3 axis = math.abs(direction.y) < 0.9d ? new double3(0d, 1d, 0d) : new double3(1d, 0d, 0d);
            double3 tangent = math.normalize(math.cross(axis, direction));
            double3 bitangent = math.cross(direction, tangent);
            double3 dir1 = math.normalize(direction + (tangent * epsilon));
            double3 dir2 = math.normalize(direction + (bitangent * epsilon));

            double h0 = TerrainNoise.SampleHeight(terrain, direction);
            double h1 = TerrainNoise.SampleHeight(terrain, dir1);
            double h2 = TerrainNoise.SampleHeight(terrain, dir2);
            double dh1 = (h1 - h0) * p.AmplitudeMeters;
            double dh2 = (h2 - h0) * p.AmplitudeMeters;

            double3 p0 = direction * (p.RadiusMeters + (h0 * p.AmplitudeMeters));
            double3 p1 = dir1 * (p.RadiusMeters + (h1 * p.AmplitudeMeters));
            double3 p2 = dir2 * (p.RadiusMeters + (h2 * p.AmplitudeMeters));
            double3 normal = math.normalize(math.cross(p1 - p0, p2 - p0));
            if (math.dot(normal, direction) < 0d)
            {
                normal = -normal;
            }

            surfaceNormal = new float3((float)normal.x, (float)normal.z, (float)-normal.y);
            return Math.Sqrt((dh1 * dh1) + (dh2 * dh2)) / p.SampleStepMeters;
        }

        private static double Clamp(double v, double min, double max)
        {
            return v < min ? min : (v > max ? max : v);
        }

        private static double Clamp01(double v)
        {
            return v < 0d ? 0d : (v > 1d ? 1d : v);
        }

        private static double Smoothstep01(double t)
        {
            t = Clamp01(t);
            return t * t * (3d - (2d * t));
        }

        /// <summary>Density falloff by distance (shared by cell and sub-tufts).
        /// Up to the flat core (DensityCoreMeters) the density is full — the island edge
        /// does not «breathe» on rebuilds and does not thin near the player.</summary>
        public static double Falloff(GroundDecorPlacementParams p, double distance)
        {
            if (p.FarDensity >= 1d && p.DensityCoreMeters <= 0d)
            {
                return 1d;
            }

            double d = Math.Max(0d, distance) - Math.Max(0d, p.DensityCoreMeters);
            if (d <= 0d)
            {
                return 1d;
            }

            if (p.FarDensity >= 1d)
            {
                return 1d;
            }

            // Coverage power law: density ∝ (R/(R+d))^p, where R is the
            // flat-core radius (density at its edge is exactly 1). With p = 2 and linear
            // far LOD growth (GroundDecorMatrixJob: size ∝ d) screen coverage
            // n·g² = (R/d)²·(d/R)² = const, so the far zone does not thin out
            // and the instance count grows as ln(R) rather than R².
            if (p.DensityFalloffPower > 0d)
            {
                double r = Math.Max(1d, Math.Max(0d, p.DensityCoreMeters));
                double k = r / (r + d);
                double shaped = p.DensityFalloffPower == 2d ? k * k : Math.Pow(k, p.DensityFalloffPower);
                return p.FarDensity + ((1d - p.FarDensity) * shaped);
            }

            if (p.FalloffScaleMeters > 0d)
            {
                return p.FarDensity
                    + ((1d - p.FarDensity) * Math.Exp(-d / p.FalloffScaleMeters));
            }

            double span = Math.Max(1d, p.FalloffFarMeters - p.FalloffNearMeters);
            double t = Clamp01(d / span);
            return 1d + ((p.FarDensity - 1d) * t);
        }
    }

    /// <summary>
    /// Burst pass over the chunk candidates: directions are prepared by the main
    /// thread (cell jitter), here — filters and instance output. Compaction happens on the
    /// main thread using Accepted (parallel queue writes are unnecessary: the number of
    /// candidates per chunk is bounded).
    /// </summary>
    [BurstCompile]
    public struct GroundDecorCandidateJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<double3> Directions;

        [ReadOnly]
        public NativeArray<double3> Randoms;

        /// <summary>Mesh variant selection [0,1) per candidate (deterministic hash).</summary>
        [ReadOnly]
        public NativeArray<double> MeshPicks;

        [ReadOnly]
        public NativeArray<double> BuryRandoms;

        [ReadOnly]
        public NativeArray<double> LeanRandoms;

        public TerrainNoiseParams Terrain;

        public GroundDecorPlacementParams Placement;

        public NativeArray<int> Accepted;

        public NativeArray<GroundDecorInstance> Instances;

        public void Execute(int index)
        {
            if (GroundDecorDistribution.TryEvaluate(
                Placement, Terrain, Directions[index], Randoms[index], MeshPicks[index], BuryRandoms[index], LeanRandoms[index], out GroundDecorInstance instance))
            {
                Accepted[index] = 1;
                Instances[index] = instance;
            }
            else
            {
                // The arrays come from a pool (DecorArrayPool) with stale data:
                // without an explicit reset a stale Accepted=1 would draw another chunk's
                // decor (grass in the sky). Instances with Accepted=0 are never read.
                Accepted[index] = 0;
            }
        }
    }
}
