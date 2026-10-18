using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>Tank computation result: volumes, metal, propellant.</summary>
    public readonly struct TankMassInfo
    {
        public readonly PropellantLoad Load;

        /// <summary>Internal volume from geometry, m³.</summary>
        public readonly double VolumeM3;

        /// <summary>Volume of loaded propellant (excluding ullage gas), m³.</summary>
        public readonly double UsableVolumeM3;

        public readonly double ShellAreaM2;

        /// <summary>Wall metal, kg.</summary>
        public readonly double StructureMassKg;

        /// <summary>Insulation, kg.</summary>
        public readonly double InsulationMassKg;

        /// <summary>Propellant in the tank at current fill, kg.</summary>
        public readonly double PropellantMassKg;

        public TankMassInfo(
            PropellantLoad load, double volumeM3, double usableVolumeM3, double shellAreaM2,
            double structureMassKg, double insulationMassKg, double propellantMassKg)
        {
            Load = load;
            VolumeM3 = volumeM3;
            UsableVolumeM3 = usableVolumeM3;
            ShellAreaM2 = shellAreaM2;
            StructureMassKg = structureMassKg;
            InsulationMassKg = insulationMassKg;
            PropellantMassKg = propellantMassKg;
        }

        public double VolumeLiters { get { return VolumeM3 * 1000d; } }
        public double UsableLiters { get { return UsableVolumeM3 * 1000d; } }
        public double DryMassKg { get { return StructureMassKg + InsulationMassKg; } }
        public double TotalMassKg { get { return DryMassKg + PropellantMassKg; } }
    }

    /// <summary>
    /// Tank mass from size and propellant kind.
    ///   volume and area — ProceduralFuelTank formulas (single source);
    ///   metal = (shell + bulkhead) × structure kg/m² × ShellScale(radius);
    ///   fill kg/m² and insulation — volume-weighted average of components;
    ///   propellant = Σ volume × fraction × (1 − ullage) × density × fill.
    /// ShellScale: pressure tanks have wall thickness growing with radius
    /// (t = p·r/σ), so kg/m² is proportional to radius. Reference
    /// Propellant.TankStructureKgM2 values are given for radius 4.2 m (ET);
    /// a 1.5 m tank has a wall three times thinner. Clamped below by minimum thickness.
    /// </summary>
    public static class TankMassModel
    {
        public const double ReferenceRadiusM = 4.2d;

        /// <summary>Minimum fraction of the reference wall (manufacturing floor).</summary>
        public const double MinShellScale = 0.25d;

        public static double ShellScale(double meanRadiusM)
        {
            return System.Math.Max(MinShellScale, meanRadiusM / ReferenceRadiusM);
        }

        public static TankMassInfo Compute(
            float bottomRadius, float topRadius, float height, PropellantLoad load, double fill)
        {
            PropellantLoad l = load ?? PropellantLoads.Default;
            double volume = ProceduralFuelTank.Volume(bottomRadius, topRadius, height);
            double meanRadius = 0.5d * (bottomRadius + topRadius);

            // Shell + bulkhead between components (disc of mean cross-section).
            double area = ProceduralFuelTank.ShellArea(bottomRadius, topRadius, height);
            if (l.HasBulkhead)
            {
                area += System.Math.PI * meanRadius * meanRadius;
            }

            double structure = area * l.StructureKgM2 * ShellScale(meanRadius);
            double insulation = area * l.InsulationKgM2;

            double f = Mathf.Clamp01((float)fill);
            double usable = 0d;
            double propellantMass = 0d;
            for (int i = 0; i < l.Count; i++)
            {
                usable += l.UsableVolumeM3(i, volume);
                propellantMass += l.ComponentMassKg(i, volume, f);
            }

            return new TankMassInfo(l, volume, usable, area, structure, insulation, propellantMass);
        }
    }
}
