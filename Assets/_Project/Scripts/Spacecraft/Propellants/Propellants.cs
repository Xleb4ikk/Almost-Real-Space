using System;
using System.Collections.Generic;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// One propellant (component). Immutable. Everything that depends on the
    /// propellant when sizing a tank lives here: density, ullage reserve and the
    /// metal/insulation mass per m² of wall. A new propellant is one line in
    /// Propellants (see below).
    /// </summary>
    public sealed class Propellant
    {
        public readonly string Id;
        public readonly string DisplayName;

        /// <summary>Liquid density at storage temperature, kg/m³.</summary>
        public readonly double DensityKgM3;

        /// <summary>
        /// Structure mass (wall metal) per m² of tank shell for a tank of radius
        /// TankMassModel.ReferenceRadiusM, kg/m². Scaled for other radii (see
        /// TankMassModel.ShellScale).
        /// </summary>
        public readonly double TankStructureKgM2;

        /// <summary>Thermal insulation per m² of shell, kg/m² (independent of radius).</summary>
        public readonly double TankInsulationKgM2;

        /// <summary>Fraction of tank volume not filled (ullage), 0..1.</summary>
        public readonly double UllageFraction;

        public Propellant(
            string id, string displayName, double densityKgM3,
            double tankStructureKgM2, double tankInsulationKgM2, double ullageFraction)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("Id топлива обязателен.", nameof(id));
            }

            if (!(densityKgM3 > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(densityKgM3), "Плотность должна быть > 0.");
            }

            Id = id;
            DisplayName = displayName ?? id;
            DensityKgM3 = densityKgM3;
            TankStructureKgM2 = tankStructureKgM2;
            TankInsulationKgM2 = tankInsulationKgM2;
            UllageFraction = ullageFraction;
        }

        public override string ToString()
        {
            return Id;
        }
    }

    /// <summary>
    /// Registry of propellant COMPONENTS (oxidiser or fuel separately) — exact data:
    /// density at storage temperature, tank wall and insulation, ullage. A NEW
    /// COMPONENT is one Register(...) line:
    ///   public static readonly Propellant Rp1 = Register(new Propellant(
    ///       "RP1", "Kerosene RP-1", 810d, 10.7d, 0d, 0.02d));
    /// A new propellant can reuse LOX's tank wall (both are stored at moderate
    /// pressure). A WHOLE PROPELLANT (a pair of components, O/F, mass flow and tank
    /// fill) is one line in PropellantPairs below.
    /// </summary>
    public static class Propellants
    {
        // The list is declared FIRST: static fields initialise in order.
        private static readonly List<Propellant> all = new List<Propellant>();

        // Data from the Space Shuttle ET tank (NASA KLASS): Ø 8.41 m.
        // Shell area is computed from the in-game tank shape (cylinder + flat caps).
        public static readonly Propellant Lox = Register(new Propellant(
            "LOX", "Жидкий кислород", 1141d, 10.7d, 0d, 0.02d));

        public static readonly Propellant Lh2 = Register(new Propellant(
            "LH2", "Жидкий водород", 70.8d, 14.8d, 1.5d, 0.02d));

        // Long-storable pair AJ10-118K: N2O4/Aerozine-50. Both components live at
        // ambient temperature in pressure-fed tanks — wall and insulation taken from
        // LOX (no insulation needed).
        public static readonly Propellant N2o4 = Register(new Propellant(
            "N2O4", "Азотный тетраоксид", 1443d, 10.7d, 0d, 0.02d));

        public static readonly Propellant Aerozine50 = Register(new Propellant(
            "AZ50", "Аэрозин-50", 903d, 10.7d, 0d, 0.02d));

        public static IReadOnlyList<Propellant> All
        {
            get { return all; }
        }

        /// <summary>Default propellant (when no type is set).</summary>
        public static Propellant Default
        {
            get { return Lox; }
        }

        /// <summary>Find by Id; unknown Id — Default.</summary>
        public static Propellant Get(string id)
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == id)
                {
                    return all[i];
                }
            }

            return Default;
        }

        private static Propellant Register(Propellant propellant)
        {
            all.Add(propellant);
            return propellant;
        }
    }

    /// <summary>
    /// An engine's "oxidiser + fuel" pair: the mass-flow ratio (O/F). From this follow
    /// how much larger the fuel tank must be than the oxidiser tank by volume and how
    /// much propellant actually burns if the tanks are not filled in proportion (the
    /// remainder is dead mass). Instances are created by the PropellantPairs registry
    /// (which also holds the exact O/F and engine mass flow).
    /// </summary>
    public sealed class BipropellantMix
    {
        public readonly Propellant Oxidizer;
        public readonly Propellant Fuel;

        /// <summary>Oxidiser-to-fuel mass flow ratio (O/F).</summary>
        public readonly double OxidizerToFuelMassRatio;

        /// <summary>Total mixture flow at full thrust, kg/s.</summary>
        public readonly double MassFlowKgS;

        public BipropellantMix(
            Propellant oxidizer, Propellant fuel, double oxidizerToFuelMassRatio, double massFlowKgS)
        {
            Oxidizer = oxidizer;
            Fuel = fuel;
            OxidizerToFuelMassRatio = oxidizerToFuelMassRatio;
            MassFlowKgS = massFlowKgS;
        }

        public double OxidizerFlowKgS { get { return MassFlowKgS * OxidizerMassFraction; } }
        public double FuelFlowKgS { get { return MassFlowKgS - OxidizerFlowKgS; } }

        public double OxidizerMassFraction
        {
            get { return OxidizerToFuelMassRatio / (1d + OxidizerToFuelMassRatio); }
        }

        /// <summary>Fuel volume per unit oxidiser volume (RS-25 ≈ 2.67).</summary>
        public double FuelToOxidizerVolumeRatio
        {
            get
            {
                return (1d / Fuel.DensityKgM3) / (OxidizerToFuelMassRatio / Oxidizer.DensityKgM3);
            }
        }

        /// <summary>
        /// How much mixture mass actually burns with the given reserves: limited by
        /// whichever component runs out first.
        /// </summary>
        public double BurnableMassKg(double oxidizerKg, double fuelKg)
        {
            double fromOxidizer = oxidizerKg / OxidizerMassFraction;
            double fromFuel = fuelKg / (1d - OxidizerMassFraction);
            return Math.Min(fromOxidizer, fromFuel);
        }
    }

    /// <summary>
    /// Tank fill: which components and in what VOLUME fractions it is filled with.
    /// An engine's tank holds oxidiser and fuel together (a bulkhead inside), the
    /// proportion comes from the engine's BipropellantMix. Single-component fill is
    /// PropellantLoad.Single.
    /// </summary>
    public sealed class PropellantLoad
    {
        public readonly string Id;
        public readonly string DisplayName;

        /// <summary>Engine pair this fill is sized for (or null).</summary>
        public readonly BipropellantMix Mix;

        private readonly Propellant[] components;
        private readonly double[] volumeFractions;

        private PropellantLoad(
            string id, string displayName, BipropellantMix mix, Propellant[] components, double[] volumeFractions)
        {
            Id = id;
            DisplayName = displayName ?? id;
            Mix = mix;
            this.components = components;
            this.volumeFractions = volumeFractions;
        }

        /// <summary>Fill for an engine: component volumes follow the engine's O/F.</summary>
        public static PropellantLoad ForMix(string id, string displayName, BipropellantMix mix)
        {
            double fuelPerOxidizer = mix.FuelToOxidizerVolumeRatio;
            double oxidizerShare = 1d / (1d + fuelPerOxidizer);
            return new PropellantLoad(
                id, displayName, mix,
                new[] { mix.Oxidizer, mix.Fuel },
                new[] { oxidizerShare, 1d - oxidizerShare });
        }

        /// <summary>Tank with one component (for future types: kerosene, methane, etc.).</summary>
        public static PropellantLoad Single(string id, string displayName, Propellant propellant)
        {
            return new PropellantLoad(id, displayName, null, new[] { propellant }, new[] { 1d });
        }

        public int Count { get { return components.Length; } }

        public Propellant Component(int index) { return components[index]; }

        public double VolumeFraction(int index) { return volumeFractions[index]; }

        /// <summary>Has a bulkhead between components (more than one component).</summary>
        public bool HasBulkhead { get { return components.Length > 1; } }

        /// <summary>Structure mass per m² of wall (reference radius): volume average.</summary>
        public double StructureKgM2
        {
            get
            {
                double sum = 0d;
                for (int i = 0; i < components.Length; i++)
                {
                    sum += volumeFractions[i] * components[i].TankStructureKgM2;
                }

                return sum;
            }
        }

        /// <summary>Insulation per m² of wall: volume average.</summary>
        public double InsulationKgM2
        {
            get
            {
                double sum = 0d;
                for (int i = 0; i < components.Length; i++)
                {
                    sum += volumeFractions[i] * components[i].TankInsulationKgM2;
                }

                return sum;
            }
        }

        /// <summary>Fillable volume of a component (excluding ullage), m³.</summary>
        public double UsableVolumeM3(int index, double tankVolumeM3)
        {
            return tankVolumeM3 * volumeFractions[index] * (1d - components[index].UllageFraction);
        }

        /// <summary>Mass of a component in a tank of the given volume at fill (0..1), kg.</summary>
        public double ComponentMassKg(int index, double tankVolumeM3, double fill)
        {
            return UsableVolumeM3(index, tankVolumeM3) * components[index].DensityKgM3 * fill;
        }
    }

    /// <summary>
    /// A WHOLE PROPELLANT — a pair of components for an engine group: exact
    /// characteristics (O/F and mass flow) and a tank fill built from them
    /// automatically. An engine references a pair in its spec (EngineSpecs):
    /// PropellantPairs.X.Mix — the tank card and fill button appear on their own.
    /// </summary>
    public sealed class PropellantPair
    {
        public readonly string Id;
        public readonly string DisplayName;

        /// <summary>Engine mix: components, O/F, mass flow.</summary>
        public readonly BipropellantMix Mix;

        /// <summary>Tank fill for the pair (proportion follows O/F).</summary>
        public readonly PropellantLoad Load;

        public PropellantPair(
            string id, string displayName, Propellant oxidizer, Propellant fuel,
            double oxidizerToFuelMassRatio, double massFlowKgS)
        {
            Id = id;
            DisplayName = displayName ?? id;
            Mix = new BipropellantMix(oxidizer, fuel, oxidizerToFuelMassRatio, massFlowKgS);
            Load = PropellantLoad.ForMix(id, displayName, Mix);
        }
    }

    /// <summary>
    /// Registry of propellants (pairs) — the SINGLE place to add: one Register(...)
    /// line with components, exact O/F and mass flow; the tank fill and the panel
    /// button are created automatically, and an engine references the pair in its
    /// spec (EngineSpecs):
    ///   public static readonly PropellantPair X = Register(new PropellantPair(
    ///       "X", "AJ10-118K · N2O4/Aerozine-50",
    ///       Propellants.N2o4, Propellants.Aerozine50, 1.9d, 13.8d));
    /// Components that don't exist yet must be added to Propellants above first.
    /// </summary>
    public static class PropellantPairs
    {
        // The list is declared FIRST: static fields initialise in order.
        private static readonly List<PropellantPair> all = new List<PropellantPair>();

        // RS-25: LOX/LH2, O/F = 6.03, flow 514.49 kg/s (at 2279 kN in vacuum):
        // LOX ≈ 441 kg/s, LH2 ≈ 73 kg/s.
        public static readonly PropellantPair Rs25 = Register(new PropellantPair(
            "RS25", "RS-25 · LOX/LH2", Propellants.Lox, Propellants.Lh2, 6.03d, 514.49d));

        // AJ10-118K (Delta II 2nd stage): N2O4/Aerozine-50, O/F = 1.9, flow 13.8 kg/s
        // at 43.4 kN in vacuum and Isp 320.5 s (43400 / (320.5 · 9.80665) = 13.81 kg/s).
        public static readonly PropellantPair Aj10 = Register(new PropellantPair(
            "AJ10", "AJ10-118K · N2O4/Аэрозин-50",
            Propellants.N2o4, Propellants.Aerozine50, 1.9d, 13.8d));

        public static IReadOnlyList<PropellantPair> All { get { return all; } }

        /// <summary>Default propellant (the registry's first entry).</summary>
        public static PropellantPair Default { get { return Rs25; } }

        /// <summary>Find by Id; unknown Id — Default.</summary>
        public static PropellantPair Get(string id)
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == id)
                {
                    return all[i];
                }
            }

            return Default;
        }

        /// <summary>
        /// Touch the registry so its static initialisation registers the fills in
        /// PropellantLoads: class static-initialisation order is not guaranteed, and
        /// PropellantLoads reads the list the pairs populate.
        /// </summary>
        public static void EnsureRegistered()
        {
        }

        private static PropellantPair Register(PropellantPair pair)
        {
            all.Add(pair);
            PropellantLoads.Register(pair.Load);   // the tank panel button comes for free
            return pair;
        }
    }

    /// <summary>
    /// Registry of tank fills: panel buttons are built from All. Fills for propellant
    /// pairs are registered by the PropellantPairs registry — a separate entry is needed
    /// only for single-component tanks (kerosene, etc.):
    ///   Register(PropellantLoad.Single("RP1", "Kerosene RP-1", Propellants.Rp1))
    /// </summary>
    public static class PropellantLoads
    {
        private static readonly List<PropellantLoad> all = new List<PropellantLoad>();

        public static IReadOnlyList<PropellantLoad> All
        {
            get
            {
                PropellantPairs.EnsureRegistered();
                return all;
            }
        }

        /// <summary>Default fill — the default propellant.</summary>
        public static PropellantLoad Default
        {
            get { return PropellantPairs.Default.Load; }
        }

        /// <summary>Find by Id; unknown Id — Default.</summary>
        public static PropellantLoad Get(string id)
        {
            PropellantPairs.EnsureRegistered();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == id)
                {
                    return all[i];
                }
            }

            return Default;
        }

        /// <summary>Register a fill (called by PropellantPairs.Register).</summary>
        public static PropellantLoad Register(PropellantLoad load)
        {
            all.Add(load);
            return load;
        }
    }
}
