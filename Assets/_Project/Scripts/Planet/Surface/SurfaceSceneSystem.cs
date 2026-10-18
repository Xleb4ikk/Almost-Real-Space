using System;
using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Access to the assembled SYSTEMS OF BODIES OUTSIDE Play mode: the authoring
    /// frame, the terrain preview and SurfacePlaced must know the same bodies, the
    /// same terrain and the same granularity as the runtime, or the preview lies.
    ///
    /// The path is exactly that of SimulationRunner.Awake: BuildBlueprint() (pure
    /// data from the scene hierarchy) → Build() (HeightfieldTerrain from profile+seed).
    /// There is deliberately no separate "editor" model: one truth, otherwise the
    /// scene and what the player falls onto would diverge.
    ///
    /// Cached by scene signature. The signature is a hash of the blueprint scalar
    /// fields plus the key terrain parameters: rebuilding every editor Update would
    /// be a hot loop, while with no cache a seed edit would not be picked up.
    /// </summary>
    public static class SurfaceSceneSystem
    {
        private static StarSystem cached;
        private static long cachedSignature;
        private static bool cachedValid;
        private static string lastError;
        private static double lastCheckTime = -1d;

        /// <summary>How often to check whether the cache is stale, seconds.</summary>
        private const double RevalidateIntervalSeconds = 0.5d;

        /// <summary>System assembled from the current scene; null on error (see LastError).</summary>
        public static StarSystem System
        {
            get
            {
                if (cached == null)
                {
                    Rebuild();
                    return cached;
                }

                // The signature is checked HERE, not only inside Rebuild: otherwise a
                // stale cache is forever.
                double now = Time.realtimeSinceStartup;
                if (now - lastCheckTime >= RevalidateIntervalSeconds)
                {
                    lastCheckTime = now;
                    if (Stale())
                    {
                        Rebuild();
                    }
                }

                return cached;
            }
        }

        /// <summary>Cache does not match the current scene (by body count and by signature).</summary>
        private static bool Stale()
        {
            BodyAuthoring[] found = UnityEngine.Object.FindObjectsByType<BodyAuthoring>(
                FindObjectsInactive.Include);
            if (found.Length != cached.AllBodies.Count)
            {
                return true;
            }

            StarSystemAuthoring[] roots = UnityEngine.Object.FindObjectsByType<StarSystemAuthoring>(
                FindObjectsInactive.Include);
            if (roots == null || roots.Length == 0)
            {
                return false;
            }

            try
            {
                return Signature(roots[0].BuildBlueprint()) != cachedSignature;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string LastError => lastError;

        /// <summary>Reset the cache (scene change, domain reload).</summary>
        public static void Invalidate()
        {
            cachedValid = false;
            cached = null;
            cachedSignature = 0L;
        }

        private static void Rebuild()
        {
            cachedValid = true;
            cached = null;
            lastError = null;

            StarSystemAuthoring[] roots = UnityEngine.Object.FindObjectsByType<StarSystemAuthoring>(FindObjectsInactive.Include);
            if (roots == null || roots.Length == 0)
            {
                lastError = "В сцене нет StarSystemAuthoring — нечего собирать.";
                return;
            }

            try
            {
                SystemBlueprint blueprint = roots[0].BuildBlueprint();
                long signature = Signature(blueprint);
                if (cached != null && signature == cachedSignature)
                {
                    return;
                }

                // The old system's site tables are Persistent — without releasing them
                // every rebuild in the editor (which happens on every terrain edit)
                // would leak a few kilobytes. Rebuilds are rare, so waiting until end
                // of frame is safer than keeping a second system: it is unused anyway.
                DisposeTerrainModifiers(cached);
                cached = blueprint.Build();
                cachedSignature = signature;
            }
            catch (Exception e)
            {
                cached = null;
                lastError = e.Message;
            }
        }

        /// <summary>Release the site tables of all bodies in the system (Persistent).</summary>
        private static void DisposeTerrainModifiers(StarSystem system)
        {
            if (system == null)
            {
                return;
            }

            for (int i = 0; i < system.AllBodies.Count; i++)
            {
                (system.AllBodies[i].Terrain as HeightfieldTerrain)?.DisposeModifiers();
            }
        }

        /// <summary>
        /// Hash of everything that affects the bodies and the terrain shape. The
        /// detailed TerrainProfile fields are not included: editing a
        /// TerrainProfileAsset changes the asset instance, not this blueprint, and
        /// that case triggers a rebuild explicitly (frame inspector button / rebuild preview).
        /// </summary>
        private static long Signature(SystemBlueprint blueprint)
        {
            unchecked
            {
                long hash = 17L;
                if (blueprint.Bodies == null)
                {
                    return hash;
                }

                hash = (hash * 31L) + blueprint.Bodies.Count;
                foreach (BodyBlueprint b in blueprint.Bodies)
                {
                    hash = Mix(hash, b.Name);
                    hash = Mix(hash, b.Radius);
                    hash = Mix(hash, b.StandardGravitationalParameter);
                    hash = Mix(hash, b.SemiMajorAxis);
                    hash = Mix(hash, b.Eccentricity);
                    hash = Mix(hash, b.InclinationDegrees);
                    hash = Mix(hash, b.LongitudeOfAscendingNodeDegrees);
                    hash = Mix(hash, b.ArgumentOfPeriapsisDegrees);
                    hash = Mix(hash, b.MeanAnomalyAtEpochDegrees);
                    hash = Mix(hash, b.EpochTimeSeconds);
                    hash = Mix(hash, b.RotationPeriodSeconds);
                    hash = Mix(hash, b.PrimeMeridianOffsetDegrees);
                    hash = Mix(hash, b.NorthPoleX);
                    hash = Mix(hash, b.NorthPoleY);
                    hash = Mix(hash, b.NorthPoleZ);
                    hash = Mix(hash, b.ParentIndex);
                    hash = Mix(hash, b.TidallyLocked);
                    hash = Mix(hash, b.TerrainSeed);
                    hash = Mix(hash, b.Terrain == null ? 0d : b.Terrain.AmplitudeMeters);
                    hash = Mix(hash, b.Terrain == null ? 0d : b.Terrain.BaseFrequency);
                    hash = Mix(hash, b.Terrain == null ? 0d : b.Terrain.Octaves);
                    hash = Mix(hash, b.Terrain == null ? 0d : b.Terrain.SeaLevelMeters);
                    hash = Mix(hash, b.Terrain == null ? 0d : b.Terrain.ContinentFrequency);
                    hash = Mix(hash, b.Terrain == null ? 0d : b.Terrain.WarpStrength);

                    // Sites must be in the signature: without them a change of a site
                    // radius or height would not rebuild the system, and the preview
                    // would keep showing the old ground while Play already computed
                    // the new one — a silent visual/physics divergence.
                    if (b.TerrainModifiers != null)
                    {
                        hash = Mix(hash, b.TerrainModifiers.Length);
                        for (int i = 0; i < b.TerrainModifiers.Length; i++)
                        {
                            TerrainModifier pad = b.TerrainModifiers[i];
                            hash = Mix(hash, pad.LatitudeDegrees);
                            hash = Mix(hash, pad.LongitudeDegrees);
                            hash = Mix(hash, pad.InnerRadiusMeters);
                            hash = Mix(hash, pad.OuterRadiusMeters);
                            hash = Mix(hash, pad.TargetHeightMeters);
                            hash = Mix(hash, pad.OverridesSeaLevel);
                        }
                    }
                }

                return hash;
            }
        }

        private static long Mix(long hash, string value)
        {
            if (value == null)
            {
                return (hash * 31L) + 1L;
            }

            foreach (char c in value)
            {
                hash = (hash * 31L) + c;
            }

            return (hash * 31L) + 2L;
        }

        private static long Mix(long hash, double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            if (bits == long.MinValue)
            {
                bits = 0L;
            }

            return (hash * 31L) + bits;
        }

        private static long Mix(long hash, bool value)
        {
            return (hash * 31L) + (value ? 3L : 4L);
        }

        /// <summary>
        /// Body by the authoring asset's GameObject. The GO name is the link key
        /// across the project (BodyView/PlanetSurfaceRenderer/PlanetCloudsView find
        /// the body by it), so here too only the name, no manual references.
        /// </summary>
        public static bool TryResolve(BodyAuthoring authoring, out OrbitingBody body, out HeightfieldTerrain terrain)
        {
            body = null;
            terrain = null;
            return authoring != null && TryResolve(authoring.gameObject.name, out body, out terrain);
        }

        /// <summary>Body by GameObject name (menus and preview work without an asset reference).</summary>
        public static bool TryResolve(string bodyName, out OrbitingBody body, out HeightfieldTerrain terrain)
        {
            body = null;
            terrain = null;
            if (string.IsNullOrEmpty(bodyName))
            {
                return false;
            }

            StarSystem system = System;
            if (system == null)
            {
                return false;
            }

            foreach (OrbitingBody candidate in system.AllBodies)
            {
                if (candidate.Name == bodyName)
                {
                    body = candidate;
                    terrain = candidate.Terrain as HeightfieldTerrain;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Terrain height at a point, metres above the radius, with sea-level clamp —
        /// the same as SimulationRunner.GroundHeight, so the preview and the player
        /// landing compute the same ground. null Terrain = smooth sphere.
        /// </summary>
        public static double GroundHeight(OrbitingBody body, double latitudeDegrees, double longitudeDegrees)
        {
            HeightfieldTerrain terrain = body?.Terrain as HeightfieldTerrain;
            if (terrain == null)
            {
                return 0d;
            }

            return terrain.GetHeightMeters(body, latitudeDegrees * (Math.PI / 180d), longitudeDegrees * (Math.PI / 180d));
        }

        /// <summary>All BodyAuthoring in the scene — for menus and body selection lists.</summary>
        public static List<BodyAuthoring> AllAuthorings()
        {
            var result = new List<BodyAuthoring>();
            result.AddRange(UnityEngine.Object.FindObjectsByType<BodyAuthoring>(FindObjectsInactive.Include));
            return result;
        }
    }
}
