using Galilego.Spacecraft;
using UnityEngine;

namespace Galilego.Building
{
    /// <summary>Part kind in the build catalog.</summary>
    public enum PartKind
    {
        Engine = 0,
        FuelTank = 1,
        Separator = 2,
        Capsule = 3,
        RcsBlock = 4
    }

    /// <summary>
    /// Fuel tank form: a straight cylinder or a truncated cone (an adapter tank
    /// between stages of different diameters: one radius at the bottom, another
    /// at the top, straight generatrix).
    /// </summary>
    public enum TankForm
    {
        Cylinder = 0,
        Cone = 1
    }

    /// <summary>
    /// Part preset for a panel slot: kind and sizes. Sizes are stored PER SLOT,
    /// not globally per kind: the panel can hold tanks of different forms and
    /// diameters (e.g. a 2.5 m cylinder and a 5→2.5 m cone) and place them in
    /// turn without reconfiguring sizes.
    /// </summary>
    public struct PartPreset
    {
        public PartKind Kind;

        /// <summary>Tank form (irrelevant for other parts).</summary>
        public TankForm Form;

        /// <summary>Tank — bottom radius; separator — radius; irrelevant for the engine.</summary>
        public float Radius;

        /// <summary>Tank top radius (equal to Radius — cylinder).</summary>
        public float TopRadius;

        /// <summary>Tank length, m.</summary>
        public float Height;

        /// <summary>Tank load: components and their volume fractions (null for other parts).</summary>
        public PropellantLoad Load;

        /// <summary>Engine (Id from EngineSpecs; irrelevant for other parts, null — default).</summary>
        public string EngineId;

        /// <summary>
        /// Ring size for a radial part (RCS block): how many blocks are placed evenly
        /// around the host part at once, 1/2/3/4/6/8. X grows it, Z shrinks it.
        /// Irrelevant for other parts.
        /// </summary>
        public int Count;
    }

    /// <summary>
    /// Part sizes and physics for this instance (radii/height are set by the
    /// player for the tank and separator; for the engine they come from the model
    /// bounds). All heights are relative to the part PIVOT along the site Y axis
    /// (zenith). Tank pivot — bottom, separator — ring middle, engine — bottom of
    /// the bounds on the axis center.
    /// </summary>
    public readonly struct PartMetrics
    {
        /// <summary>Bottom radius of the body of revolution, m (tank: bottom radius).</summary>
        public readonly float Radius;

        /// <summary>
        /// Top radius of the body of revolution, m. Equal to Radius — cylindrical
        /// part; different — truncated cone (for the separator and engine always
        /// equal to the bottom one: their shape is not conical).
        /// </summary>
        public readonly float TopRadius;

        /// <summary>Full height of the shape along the axis, m.</summary>
        public readonly float Height;

        /// <summary>Center of the shape relative to the pivot, m.</summary>
        public readonly float Center;

        /// <summary>Bottom of the shape relative to the pivot, m.</summary>
        public readonly float Bottom;

        /// <summary>Top of the shape relative to the pivot, m.</summary>
        public readonly float Top;

        public readonly double MassKg;
        public readonly double JointStrengthNewtons;
        public readonly double BreakTorqueNm;

        /// <summary>Dry mass (metal + insulation) without propellant, kg.</summary>
        public readonly double DryMassKg;

        /// <summary>Propellant mass in the tank, kg (0 for other parts).</summary>
        public readonly double PropellantMassKg;

        /// <summary>Internal volume of the shape, m³ (0 for other parts).</summary>
        public readonly double VolumeM3;

        /// <summary>Tank load (components and their volume fractions; null for other parts).</summary>
        public readonly PropellantLoad Load;

        public PartMetrics(float radius, float topRadius, float height, float center, float bottom, float top,
            double massKg, double jointStrengthNewtons, double breakTorqueNm)
            : this(radius, topRadius, height, center, bottom, top, massKg, jointStrengthNewtons, breakTorqueNm,
                massKg, 0d, 0d, null)
        {
        }

        public PartMetrics(float radius, float topRadius, float height, float center, float bottom, float top,
            double massKg, double jointStrengthNewtons, double breakTorqueNm,
            double dryMassKg, double propellantMassKg, double volumeM3, PropellantLoad load)
        {
            Radius = radius;
            TopRadius = topRadius > 0.01f ? topRadius : radius;
            Height = height;
            Center = center;
            Bottom = bottom;
            Top = top;
            MassKg = massKg;
            JointStrengthNewtons = jointStrengthNewtons;
            BreakTorqueNm = breakTorqueNm;
            DryMassKg = dryMassKg;
            PropellantMassKg = propellantMassKg;
            VolumeM3 = volumeM3;
            Load = load;
        }
    }

    /// <summary>
    /// A part placed by the player: kind, sizes, pivot position in SITE AXES
    /// (x — north, y — zenith, z — east), snap parent and node occupancy
    /// (only one part may sit on a node). Metrics are cached on placement:
    /// size parameters are frozen at placement time.
    /// </summary>
    public sealed class PlacedPart
    {
        public PartKind Kind;

        /// <summary>Tank form at placement time (irrelevant for other parts).</summary>
        public TankForm Form;

        public float SizeA;
        public float SizeB;

        /// <summary>
        /// Engine spec at placement time (null for other parts):
        /// part name for physics, propellant pair and characteristics.
        /// </summary>
        public EngineSpec EngineSpec;

        /// <summary>Tank top radius at placement time (equal to SizeA — cylinder).</summary>
        public float SizeTop;

        public PartMetrics Metrics;
        public Vector3 Pivot;
        public int ParentIndex = -1;

        /// <summary>
        /// Part frame rotation in the ship/site frame: radial parts (RCS blocks) sit
        /// with +Z along the hull normal; stack parts keep the identity.
        /// </summary>
        public Quaternion Rotation = Quaternion.identity;

        /// <summary>
        /// Physics body centre offset in the part frame, m: a radial part's body is
        /// not centred on its hull contact pivot. Zero — the centre is the pivot.
        /// </summary>
        public Vector3 PhysicsCenterLocal;

        /// <summary>
        /// Group id: parts placed together (one RCS ring) are removed together.
        /// 0 — a standalone part.
        /// </summary>
        public int Group;

        /// <summary>
        /// Radial part (mounted on a host's side): not a stack node (nothing snaps
        /// to it) and ignored by the stack overlap tests.
        /// </summary>
        public bool Radial;

        /// <summary>How the part is docked: true — by its bottom onto the parent's TOP.</summary>
        public bool ParentOnTop;

        /// <summary>Something already sits on the top node.</summary>
        public bool TopOccupied;

        /// <summary>Something already sits on the bottom node.</summary>
        public bool BottomOccupied;

        /// <summary>Part visual: lives under the ship transform, can turn red
        /// (aimed deletion) and restore its original materials.</summary>
        public PartCatalog.PartVisual Visual;
    }
}
