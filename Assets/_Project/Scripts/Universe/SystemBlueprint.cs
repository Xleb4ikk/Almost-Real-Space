using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Description of one system body: plain data without UnityEngine types
    /// (except Vector3d), serializable in the Unity inspector and in tests.
    /// The hierarchy is set by ParentIndex (-1 = the root star, exactly one).
    /// </summary>
    [Serializable]
    public sealed class BodyBlueprint
    {
        public string Name = "Body";
        public double StandardGravitationalParameter = 1d;
        public double Radius = 1d;
        public double CrashToleranceMps = 5d;

        /// <summary>Surface friction (Coulomb, see SurfaceMotion): sticks while |a_t| ≤ μ_s·|a_n|.</summary>
        public double SurfaceStaticFrictionMu = 0.7d;

        public double SurfaceKineticFrictionMu = 0.5d;

        public double SemiMajorAxis = 1d;
        public double Eccentricity;
        public double InclinationDegrees;
        public double LongitudeOfAscendingNodeDegrees;
        public double ArgumentOfPeriapsisDegrees;
        public double MeanAnomalyAtEpochDegrees;
        public double EpochTimeSeconds;

        public double RotationPeriodSeconds;
        public double PrimeMeridianOffsetDegrees;
        public double NorthPoleX;
        public double NorthPoleY = 1d;
        public double NorthPoleZ;

        /// <summary>1:1 tidal lock (see OrbitingBody.ApplyTidalLock): manual period/offset are ignored.</summary>
        public bool TidallyLocked;

        public AtmosphereProfile Atmosphere;

        public CloudProfile Clouds;

        /// <summary>Terrain: null means a smooth sphere. Shape/colour/palette preset; copied into HeightfieldTerrain at build.</summary>
        public TerrainProfile Terrain;

        /// <summary>Per-body noise seed: one profile preset, different seeds per body.</summary>
        public int TerrainSeed;

        /// <summary>
        /// Flattened pads at points of the body (like KSP PQS mods). Carried in the
        /// blueprint because the system is built both in Play and in the editor, and
        /// both builds must see the SAME pads — otherwise the preview lies.
        /// null/empty = terrain untouched.
        /// </summary>
        public TerrainModifier[] TerrainModifiers;

        /// <summary>Parent index in the list; -1 is the tree root (star).</summary>
        public int ParentIndex = -1;
    }

    /// <summary>
    /// System blueprint: builds a StarSystem from flat data. Kept free of the
    /// BodyAuthoring MonoBehaviour wrappers so the build logic is testable without
    /// the Unity API. Build() runs loud Hill validation, so unstable content fails
    /// at system creation.
    /// </summary>
    [Serializable]
    public sealed class SystemBlueprint
    {
        public List<BodyBlueprint> Bodies = new List<BodyBlueprint>();

        public StarSystem Build()
        {
            if (Bodies == null || Bodies.Count == 0)
            {
                throw new InvalidOperationException("Чертёж системы пуст: добавьте хотя бы звезду.");
            }

            int rootCount = 0;
            int rootIndex = -1;
            for (int i = 0; i < Bodies.Count; i++)
            {
                if (Bodies[i].ParentIndex < 0)
                {
                    rootCount++;
                    rootIndex = i;
                }
            }

            if (rootCount != 1)
            {
                throw new InvalidOperationException(
                    "Чертёж обязан содержать ровно одну звезду (тело без родителя); найдено " + rootCount + ".");
            }

            var created = new List<OrbitingBody>(Bodies.Count);
            for (int i = 0; i < Bodies.Count; i++)
            {
                BodyBlueprint bp = Bodies[i];
                var body = new OrbitingBody
                {
                    Name = string.IsNullOrEmpty(bp.Name) ? ("Body" + i) : bp.Name,
                    StandardGravitationalParameter = bp.StandardGravitationalParameter,
                    Radius = bp.Radius,
                    CrashToleranceMps = bp.CrashToleranceMps,
                    SurfaceStaticFrictionMu = bp.SurfaceStaticFrictionMu,
                    SurfaceKineticFrictionMu = bp.SurfaceKineticFrictionMu,
                    SemiMajorAxis = bp.SemiMajorAxis,
                    Eccentricity = bp.Eccentricity,
                    InclinationDegrees = bp.InclinationDegrees,
                    LongitudeOfAscendingNodeDegrees = bp.LongitudeOfAscendingNodeDegrees,
                    ArgumentOfPeriapsisDegrees = bp.ArgumentOfPeriapsisDegrees,
                    MeanAnomalyAtEpochDegrees = bp.MeanAnomalyAtEpochDegrees,
                    EpochTimeSeconds = bp.EpochTimeSeconds,
                    RotationPeriodSeconds = bp.RotationPeriodSeconds,
                    PrimeMeridianOffsetDegrees = bp.PrimeMeridianOffsetDegrees,
                    NorthPoleDirection = new Vector3d(bp.NorthPoleX, bp.NorthPoleY, bp.NorthPoleZ),
                    Atmosphere = bp.Atmosphere,
                    Clouds = bp.Clouds
                };
                if (bp.Terrain != null)
                {
                    body.Terrain = HeightfieldTerrain.FromProfile(bp.Terrain, bp.TerrainSeed);
                    // SetModifiers exists only on HeightfieldTerrain: a smooth
                    // sphere (SphericalTerrain) has no terrain that could be edited.
                    if (bp.TerrainModifiers != null
                        && bp.TerrainModifiers.Length > 0
                        && body.Terrain is HeightfieldTerrain heightfield)
                    {
                        heightfield.SetModifiers(bp.TerrainModifiers, body.Radius);
                    }
                }

                created.Add(body);
            }

            for (int i = 0; i < Bodies.Count; i++)
            {
                int p = Bodies[i].ParentIndex;
                if (p < 0)
                {
                    continue;
                }

                if (p >= Bodies.Count || p == i)
                {
                    throw new InvalidOperationException(
                        "Тело " + Bodies[i].Name + ": некорректный ParentIndex " + p + ".");
                }

                created[i].Parent = created[p];
                created[p].Children.Add(created[i]);
            }

            // Tidal lock runs AFTER the hierarchy is linked: μ_local needs the tree
            // (μ_parent + μ_own_subtree). A pure function of the element data.
            for (int i = 0; i < Bodies.Count; i++)
            {
                if (Bodies[i].TidallyLocked)
                {
                    created[i].ApplyTidalLock();
                }
            }

            return new StarSystem(created[rootIndex]);
        }
    }
}
