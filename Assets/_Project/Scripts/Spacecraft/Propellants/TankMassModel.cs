using UnityEngine;

namespace Galilego.Spacecraft
{
    /// <summary>Итог расчёта бака: объёмы, металл, топливо.</summary>
    public readonly struct TankMassInfo
    {
        public readonly PropellantLoad Load;

        /// <summary>Внутренний объём по геометрии, м³.</summary>
        public readonly double VolumeM3;

        /// <summary>Объём заливаемого топлива (без газовой подушки), м³.</summary>
        public readonly double UsableVolumeM3;

        public readonly double ShellAreaM2;

        /// <summary>Металл стенки, кг.</summary>
        public readonly double StructureMassKg;

        /// <summary>Теплоизоляция, кг.</summary>
        public readonly double InsulationMassKg;

        /// <summary>Топливо в баке при текущем заполнении, кг.</summary>
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
    /// Масса бака от размера и вида топлива.
    ///   объём и площадь — формулы ProceduralFuelTank (единый источник);
    ///   металл = (оболочка + перегородка) × кг/м² наполнения × ShellScale(радиус);
    ///   кг/м² и изоляция наполнения — среднее компонентов по объёму;
    ///   топливо = Σ объём × доля × (1 − подушка) × плотность × заполнение.
    /// ShellScale: у баков под давлением толщина стенки растёт с радиусом
    /// (t = p·r/σ), поэтому кг/м² пропорциональны радиусу. Опорные значения
    /// Propellant.TankStructureKgM2 заданы для радиуса 4.2 м (ET); у бака
    /// 1.5 м стенка втрое тоньше. Снизу ограничено минимальной толщиной.
    /// </summary>
    public static class TankMassModel
    {
        public const double ReferenceRadiusM = 4.2d;

        /// <summary>Минимальная доля опорной стенки (технологический минимум).</summary>
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

            // Оболочка + перегородка между компонентами (диск среднего сечения).
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
