using Galilego.Building;
using Galilego.Spacecraft;
using UnityEngine;

namespace Galilego.Effects
{
    /// <summary>
    /// Look of an exhaust plume for one propellant family. Colours are LINEAR RGB.
    /// Add a propellant = add one static field + one branch in PlumeStyles.For().
    /// </summary>
    public sealed class PlumeStyle
    {
        public string Id;
        /// <summary>Hot dense core colour.</summary>
        public Color Core;
        /// <summary>Cooler, thinner edge colour.</summary>
        public Color Edge;
        /// <summary>Afterburning sheath (exhaust burning with air); only in the atmosphere.</summary>
        public Color Afterburn;
        /// <summary>Radiance of the core seen through one exit radius, nits (before HDRP exposure).</summary>
        public float Nits = 8000f;
        /// <summary>Visible axial decay length in the atmosphere, exit radii.</summary>
        public float AtmosphereLengthRe = 14f;
        /// <summary>0 = vacuum plume dims as mass spreads, 1 = fully compensated. 0.6 keeps it readable.</summary>
        public float VacuumCompensation = 0.6f;
        public float DiamondGain = 1.6f;
        /// <summary>Shock-diamond cell length in exit DIAMETERS.</summary>
        public float CellSpacingDiameters = 1.5f;
        public float AfterburnGain = 0.35f;
        public float Turbulence = 0.35f;
        /// <summary>Soot-like absorption (0 = pure emission).</summary>
        public float Absorption = 0f;
        /// <summary>Boundary half-angle of a pressure-confined jet, degrees.</summary>
        public float ConfinedAngleDeg = 2.5f;
        /// <summary>Lumens per m^2 of nozzle exit area at full thrust (light cast on the surroundings).</summary>
        public float LightLumensPerM2 = 4.0e5f;
        public Color LightColor = Color.white;
    }

    public static class PlumeStyles
    {
        /// <summary>LOX/LH2: pale blue-violet (OH*/H2O continuum), almost no soot, faint orange afterburning in air.</summary>
        public static readonly PlumeStyle Hydrolox = new PlumeStyle
        {
            Id = "hydrolox",
            Core = new Color(0.55f, 0.70f, 1.00f), Edge = new Color(0.30f, 0.28f, 0.95f),
            Afterburn = new Color(1.00f, 0.55f, 0.30f),
            Nits = 400f, AtmosphereLengthRe = 14f, DiamondGain = 1.6f, CellSpacingDiameters = 1.5f,
            AfterburnGain = 0.35f, LightLumensPerM2 = 6.0e3f, LightColor = new Color(0.6f, 0.7f, 1f),
        };

        /// <summary>N2O4/Aerozine-50 (hydrazine/UDMH): faint peach-orange glow, wide and soft in vacuum.</summary>
        public static readonly PlumeStyle Hypergolic = new PlumeStyle
        {
            Id = "hypergolic",
            Core = new Color(1.00f, 0.78f, 0.55f), Edge = new Color(1.00f, 0.45f, 0.30f),
            Afterburn = new Color(1.00f, 0.55f, 0.25f),
            Nits = 800f, AtmosphereLengthRe = 20f, DiamondGain = 1.2f, CellSpacingDiameters = 1.7f,
            AfterburnGain = 0.25f, Turbulence = 0.4f, LightLumensPerM2 = 1.2e4f, LightColor = new Color(1f, 0.65f, 0.4f),
        };

        /// <summary>Monopropellant hydrazine RCS (NH3/N2/H2): very faint, short pulses.</summary>
        public static readonly PlumeStyle Monoprop = new PlumeStyle
        {
            Id = "monoprop",
            Core = new Color(1.00f, 0.72f, 0.60f), Edge = new Color(1.00f, 0.45f, 0.40f),
            Afterburn = new Color(1.00f, 0.60f, 0.35f),
            Nits = 200f, AtmosphereLengthRe = 12f, DiamondGain = 0.6f, AfterburnGain = 0.15f,
            LightLumensPerM2 = 4.0e3f, LightColor = new Color(1f, 0.7f, 0.5f),
        };

        public static PlumeStyle For(EngineSpec spec)
        {
            if (spec != null && spec.Mix != null && spec.Mix.Fuel == Propellants.Lh2) return Hydrolox;
            return Hypergolic;
        }
    }
}
