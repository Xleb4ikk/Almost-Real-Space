using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Planet water queries — the SINGLE source of truth about the sea.
    /// Separates two concepts that used to be merged by clamping:
    ///   - water surface (flat, Radius + SeaLevel) — for swimming, splashdown,
    ///     underwater effects;
    ///   - seabed (raw height WITHOUT clamping) — for depth, ocean geometry,
    ///     landing on the bottom while diving.
    /// GetHeightMeters (clamped) stays for ships/detectors: landing on water = landing
    /// on a flat sea-level solid (soft splashdown is a future stage, see GetSplashdownInfo).
    /// Pure functions without UnityEngine — covered by the P1bTests stand.
    /// </summary>
    public static class WaterQuery
    {
        /// <summary>
        /// "No sea" threshold: sea level at or below this means no ocean.
        /// The profile uses −1e30, the runtime NegativeInfinity.
        /// </summary>
        public const double NoOceanSentinel = -1e29d;

        /// <summary>Whether the body has an ocean (sea level is set).</summary>
        public static bool HasOcean(OrbitingBody body)
        {
            return body != null && body.Terrain != null
                && body.Terrain.GetSeaLevelMeters() > NoOceanSentinel;
        }

        /// <summary>Sea level above Radius (m). false — no sea.</summary>
        public static bool TryGetSeaLevel(OrbitingBody body, out double seaLevelMeters)
        {
            if (HasOcean(body))
            {
                seaLevelMeters = body.Terrain.GetSeaLevelMeters();
                return true;
            }

            seaLevelMeters = double.NaN;
            return false;
        }

        /// <summary>
        /// Raw seabed/land height above Radius (m) WITHOUT sea clamping.
        /// NaN — no terrain.
        /// </summary>
        public static double RawSeabedHeightAt(OrbitingBody body, double latitudeDegrees, double longitudeDegrees)
        {
            if (body == null || body.Terrain == null)
            {
                return double.NaN;
            }

            return body.Terrain.GetRawHeightMeters(
                body,
                latitudeDegrees * (Math.PI / 180d),
                longitudeDegrees * (Math.PI / 180d));
        }

        /// <summary>
        /// Water depth at a point (m, ≥ 0): sea level minus raw seabed.
        /// 0 — land, shallow water near shore, or no sea.
        /// </summary>
        public static double WaterDepthAt(OrbitingBody body, double latitudeDegrees, double longitudeDegrees)
        {
            if (!TryGetSeaLevel(body, out double sea))
            {
                return 0d;
            }

            double raw = RawSeabedHeightAt(body, latitudeDegrees, longitudeDegrees);
            if (double.IsNaN(raw))
            {
                return 0d;
            }

            return Math.Max(0d, sea - raw);
        }

        /// <summary>
        /// Whether there is water at a point (raw seabed below sea level). Strict
        /// comparison: rendering adds a visual epsilon (amp·0.001) for edge smoothing,
        /// physics does not.
        /// </summary>
        public static bool IsWaterAt(OrbitingBody body, double latitudeDegrees, double longitudeDegrees)
        {
            return WaterDepthAt(body, latitudeDegrees, longitudeDegrees) > 0d;
        }

        /// <summary>
        /// Radius of the water surface from the body center (m). NaN — no sea.
        /// </summary>
        public static double SeaSurfaceRadius(OrbitingBody body)
        {
            if (!TryGetSeaLevel(body, out double sea))
            {
                return double.NaN;
            }

            return body.Radius + sea;
        }

        /// <summary>
        /// Submersion of a world point under water (m): surface radius minus the
        /// distance to the body center. &gt; 0 — underwater, ≤ 0 — above water.
        /// NaN — no sea.
        /// </summary>
        public static double SubmersionDepthAt(OrbitingBody body, Vector3d worldPosition, double timeSeconds)
        {
            double surface = SeaSurfaceRadius(body);
            if (double.IsNaN(surface))
            {
                return double.NaN;
            }

            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPos, out _);
            return surface - (worldPosition - bodyPos).Magnitude;
        }

        public static bool IsSubmergedAt(
            OrbitingBody body,
            Vector3d worldPosition,
            double timeSeconds,
            out double depthMeters)
        {
            depthMeters = SubmersionDepthAt(body, worldPosition, timeSeconds);
            if (double.IsNaN(depthMeters) || depthMeters <= 0d)
            {
                return false;
            }

            body.SurfaceLatLonAt(
                worldPosition, timeSeconds, out double latitudeDegrees, out double longitudeDegrees);
            return IsWaterAt(body, latitudeDegrees, longitudeDegrees);
        }

        /// <summary>
        /// Height of a world point above the RAW seabed (m): distance to the center
        /// minus the seabed radius at this lat/lon. Used for diving clearance (unlike
        /// the clamped height, which in the ocean is always "sea level").
        /// NaN — no terrain.
        /// </summary>
        public static double ClearanceAboveSeabedAt(OrbitingBody body, Vector3d worldPosition, double timeSeconds)
        {
            if (body == null || body.Terrain == null)
            {
                return double.NaN;
            }

            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPos, out _);
            body.SurfaceLatLonAt(worldPosition, timeSeconds, out double latDeg, out double lonDeg);
            double raw = RawSeabedHeightAt(body, latDeg, lonDeg);
            if (double.IsNaN(raw))
            {
                return double.NaN;
            }

            return (worldPosition - bodyPos).Magnitude - (body.Radius + raw);
        }

        /// <summary>
        /// Information for future soft splashdown (data only, no behaviour change
        /// yet: landing uses the clamped height).
        /// </summary>
        public struct SplashdownInfo
        {
            /// <summary>Point over water (raw seabed below sea).</summary>
            public bool IsWater;

            /// <summary>Water depth (m, ≥ 0).</summary>
            public double WaterDepthMeters;

            /// <summary>Sea level above Radius (m, NaN — no sea).</summary>
            public double SeaLevelMeters;

            /// <summary>Raw seabed above Radius (m, NaN — no terrain).</summary>
            public double SeabedHeightMeters;
        }

        /// <summary>Water summary at a point for splashdown logic.</summary>
        public static SplashdownInfo GetSplashdownInfo(OrbitingBody body, double latitudeDegrees, double longitudeDegrees)
        {
            var info = new SplashdownInfo
            {
                IsWater = false,
                WaterDepthMeters = 0d,
                SeaLevelMeters = double.NaN,
                SeabedHeightMeters = RawSeabedHeightAt(body, latitudeDegrees, longitudeDegrees)
            };
            if (TryGetSeaLevel(body, out double sea) && !double.IsNaN(info.SeabedHeightMeters))
            {
                info.SeaLevelMeters = sea;
                info.WaterDepthMeters = Math.Max(0d, sea - info.SeabedHeightMeters);
                info.IsWater = info.WaterDepthMeters > 0d;
            }

            return info;
        }
    }
}
