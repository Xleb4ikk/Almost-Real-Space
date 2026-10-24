using System;
using System.IO;
using Galilego.Core;
using Galilego.Events;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Runtime loader for baked ephemerides. Files live in
    /// StreamingAssets/&lt;folder&gt; (placed by inspector generation, shipped to the
    /// build automatically). No files — bodies stay on Keplerian rails: this is
    /// normal, not an error. Main thread only (uses UnityEngine APIs).
    /// </summary>
    public static class EphemerisRuntime
    {
        /// <summary>Default StreamingAssets subfolder name (matches inspector generation).</summary>
        public const string DefaultFolder = "Ephemerides";

        public static bool AttachFromStreamingAssets(StarSystem sys, string folder = DefaultFolder)
        {
            if (sys == null)
            {
                throw new ArgumentNullException(nameof(sys));
            }

            string dir = Path.Combine(Application.streamingAssetsPath, folder);
            if (!Directory.Exists(dir) || !File.Exists(Path.Combine(dir, "manifest.txt")))
            {
                Debug.LogWarning("EphemerisRuntime: испечённые эфемериды не найдены в " + dir +
                    " — тела остаются на кеплеровых рельсах. Сгенерируйте их в инспекторе StarSystemAuthoring.");
                return false;
            }

            EphemerisBaker.AttachFiles(sys, dir);

            string mismatch = sys.DescribeEphemerisRailMismatch();
            if (mismatch != null)
            {
                Debug.LogError("EphemerisRuntime: испечённая эфемерида не совпадает с рельсами на старте (" + mismatch +
                    ") — файлы испечены старым пекарем или из других элементов. ПЕРЕПЕКИТЕ эфемериду в инспекторе StarSystemAuthoring, " +
                    "иначе периоды и положения планет будут неверными.");
            }

            return true;
        }

        /// <summary>
        /// Accelerated-time decision for this frame: atmosphere cap ×3, effective factor,
        /// execution layer (physics vs long warp); the horizon is the end of the baked
        /// world. After it the caller runs either the long-warp path (warp.PrepareForLongWarp
        /// + LongWarpDriver.AdvanceToTarget) or the physics chain with EffectiveThrottle.
        /// Main thread only.
        /// </summary>
        public static WarpFrameDecision ComputeFrame(
            StarSystem sys, WarpController warp, Vector3d shipPosition, double timeSeconds, double realDtSeconds)
        {
            if (sys == null)
            {
                throw new ArgumentNullException(nameof(sys));
            }

            bool inAtmosphere = sys.IsInsideAtmosphere(shipPosition, timeSeconds);
            double horizon = double.PositiveInfinity;
            double bakedEnd = sys.BakedEndSeconds();
            if (double.IsFinite(bakedEnd))
            {
                horizon = bakedEnd - timeSeconds;
            }

            return WarpPlanner.ComputeFrame(warp, inAtmosphere, realDtSeconds, horizon);
        }
    }
}
