using System;

namespace Galilego.Effects
{
    /// <summary>
    /// Pure plume math (no UnityEngine): nozzle flow, Prandtl-Meyer turning, a Simons-type
    /// source-flow plume and the blend between a pressure-confined jet (atmosphere) and a
    /// free expansion (vacuum). Everything is driven by ONE number: ambient pressure p_a
    /// against the nozzle exit pressure p_e.
    /// </summary>
    public static class PlumePhysics
    {
        /// <summary>A/A* for Mach M (isentropic nozzle).</summary>
        public static double AreaRatio(double m, double g)
        {
            double t = (2d / (g + 1d)) * (1d + (0.5d * (g - 1d) * m * m));
            return Math.Pow(t, (g + 1d) / (2d * (g - 1d))) / m;
        }

        /// <summary>Exit Mach for expansion ratio eps (supersonic branch).</summary>
        public static double MachFromAreaRatio(double eps, double g)
        {
            double lo = 1.0001d, hi = 40d;
            for (int i = 0; i < 80; i++)
            {
                double mid = 0.5d * (lo + hi);
                if (AreaRatio(mid, g) < eps) lo = mid; else hi = mid;
            }

            return 0.5d * (lo + hi);
        }

        /// <summary>p / p_chamber at Mach M.</summary>
        public static double PressureRatio(double m, double g)
        {
            return Math.Pow(1d + (0.5d * (g - 1d) * m * m), -g / (g - 1d));
        }

        /// <summary>Mach at which the isentropic flow reaches static pressure p (pcOverP = p_c / p >= 1).</summary>
        public static double MachFromPressureRatio(double pcOverP, double g)
        {
            double x = Math.Pow(Math.Max(1d, pcOverP), (g - 1d) / g) - 1d;
            return Math.Sqrt(Math.Max(0d, 2d / (g - 1d) * x));
        }

        /// <summary>Prandtl-Meyer function nu(M), radians.</summary>
        public static double Nu(double m, double g)
        {
            if (m <= 1d) return 0d;
            double a = Math.Sqrt((g + 1d) / (g - 1d));
            double b = Math.Sqrt((g - 1d) / (g + 1d) * (m * m - 1d));
            return (a * Math.Atan(b)) - Math.Atan(Math.Sqrt((m * m) - 1d));
        }

        /// <summary>Maximum turning angle of the jet boundary (expansion into vacuum), radians.</summary>
        public static double NuMax(double g)
        {
            return 0.5d * Math.PI * (Math.Sqrt((g + 1d) / (g - 1d)) - 1d);
        }

        public static double SmoothStep(double a, double b, double x)
        {
            double t = Math.Min(1d, Math.Max(0d, (x - a) / (b - a)));
            return t * t * (3d - (2d * t));
        }

        /// <summary>Integral of cos^n(pi x / (2 thetaB)) * sin x over [0, thetaB]: mass flux through the cone (Simpson).</summary>
        public static double ConeFlux(double thetaB, double n)
        {
            const int Steps = 48;
            double h = thetaB / Steps, s = 0d;
            for (int i = 0; i <= Steps; i++)
            {
                double x = i * h;
                double c = Math.Max(0d, Math.Cos(Math.PI * x / (2d * thetaB)));
                double f = Math.Pow(c, n) * Math.Sin(x);
                double w = (i == 0 || i == Steps) ? 1d : (i % 2 == 1 ? 4d : 2d);
                s += w * f;
            }

            return s * h / 3d;
        }
    }

    /// <summary>Nozzle data the plume needs (taken from EngineSpec / model bounds).</summary>
    public sealed class PlumeNozzle
    {
        public double ExpansionRatio = 40d;
        public double Gamma = 1.2d;
        public double ChamberPressurePa = 5.0e6d;
        public double ExitHalfAngleDeg = 9d;
        /// <summary>Nozzle exit radius, m.</summary>
        public double ExitRadiusM = 0.5d;
    }

    /// <summary>Everything the shader and the light need for one frame. Lengths are in nozzle-exit radii.</summary>
    public struct PlumeParams
    {
        public float ThetaB;        // boundary half-angle, rad
        public float N;             // angular profile exponent
        public float SourceDist;    // virtual source distance behind the exit plane
        public float Intensity;     // relative emission scale (x style nits in the emitter)
        public float EnvLength;     // axial decay length
        public float DiamondGain;   // brightness of the shock diamonds
        public float CellLength;    // diamond cell length
        public float BarrelAmp;     // radial breathing of the barrel
        public float AfterburnK;    // air afterburning sheath
        public float Air;           // 0..1 air density factor
        public float Free;          // 0 = confined jet, 1 = free expansion (vacuum)
        public float Turbulence;
        public float BoxLength;     // bounding volume, in r_e
        public float BoxHalfWidth;
        public float Ratio;         // p_e / p_a (diagnostics)
    }

    /// <summary>Per-engine model: constant part computed once, Evaluate() is cheap (per frame).</summary>
    public sealed class PlumeModel
    {
        private readonly PlumeNozzle nozzle;
        private readonly PlumeStyle style;
        private readonly double exitMach, exitPressurePa, nuExit, thetaExit, fluxRef;

        public PlumeModel(PlumeNozzle nozzle, PlumeStyle style)
        {
            this.nozzle = nozzle;
            this.style = style;
            double g = nozzle.Gamma;
            exitMach = PlumePhysics.MachFromAreaRatio(nozzle.ExpansionRatio, g);
            exitPressurePa = PlumePhysics.PressureRatio(exitMach, g) * nozzle.ChamberPressurePa;
            nuExit = PlumePhysics.Nu(exitMach, g);
            thetaExit = nozzle.ExitHalfAngleDeg * Math.PI / 180d;
            fluxRef = PlumePhysics.ConeFlux(6d * Math.PI / 180d, 2d);
        }

        public double ExitPressurePa { get { return exitPressurePa; } }

        public PlumeParams Evaluate(double level, double ambientPa)
        {
            double g = nozzle.Gamma;
            level = Math.Max(0.01d, Math.Min(1d, level));
            double pe = exitPressurePa * level;        // chamber pressure ~ throttle
            double pc = nozzle.ChamberPressurePa * level;
            double ratio = pe / Math.Max(ambientPa, 1e-3);
            double lnr = Math.Log(Math.Max(ratio, 1e-6));

            // Boundary turning: Prandtl-Meyer from M_exit to the Mach that matches ambient pressure.
            double dnu = ambientPa < 1e-2
                ? PlumePhysics.NuMax(g) - nuExit
                : PlumePhysics.Nu(PlumePhysics.MachFromPressureRatio(pc / ambientPa, g), g) - nuExit;

            double free = PlumePhysics.SmoothStep(Math.Log(2.5d), Math.Log(1500d), lnr);
            double thFree = Math.Min(Math.Max(thetaExit + dnu, thetaExit), 125d * Math.PI / 180d);
            double thConf = style.ConfinedAngleDeg * Math.PI / 180d;
            double thb = thConf + ((thFree - thConf) * free);
            double n = 2d + (((2d / (g - 1d)) - 2d) * free);
            double ds = 1d / Math.Tan(Math.Min(Math.Max(thb, 2d * Math.PI / 180d), 55d * Math.PI / 180d));

            // Same mass spread over a wider cone -> lower density; style.VacuumCompensation brings it back (artistic).
            double flux = PlumePhysics.ConeFlux(thb, n);
            double dilution = Math.Min(1d, fluxRef / Math.Max(flux, 1e-9));
            double intensity = level * Math.Pow(dilution, -style.VacuumCompensation);

            double lEnv = (style.AtmosphereLengthRe * (1d - free)) + (4000d * free);

            double mism = Math.Abs(lnr);
            double diamondShape = PlumePhysics.SmoothStep(0.05d, 0.6d, mism) * (1d - PlumePhysics.SmoothStep(2.5d, 6d, lnr));
            double barrel = 0.22d * PlumePhysics.SmoothStep(0.05d, 0.6d, mism) * (1d - PlumePhysics.SmoothStep(3d, 6.5d, lnr));
            double cell = style.CellSpacingDiameters * 2d * (1d + (0.25d * Math.Max(0d, lnr)));
            double air = Math.Pow(Math.Min(1d, ambientPa / 101325d), 0.6d);

            // Visible length: where the on-axis emission falls below 2 % of the exit value.
            double length = 3d;
            for (; length < 150d; length += 1d)
            {
                double d = ds / (length + ds);
                if ((d * d * Math.Exp(-length / lEnv)) < 0.02d) break;
            }

            length *= 1.15d;
            double th01 = thb * (2d / Math.PI) * Math.Acos(Math.Pow(0.02d, 1d / n));
            double half = Math.Min((length + ds) * Math.Tan(Math.Min(th01, 1.35d)), 1.5d * length) * (1d + barrel) + 1.2d;

            return new PlumeParams
            {
                ThetaB = (float)thb,
                N = (float)n,
                SourceDist = (float)ds,
                Intensity = (float)intensity,
                EnvLength = (float)lEnv,
                DiamondGain = (float)(style.DiamondGain * diamondShape),
                CellLength = (float)cell,
                BarrelAmp = (float)barrel,
                AfterburnK = (float)(style.AfterburnGain * air * (1d - free)),
                Air = (float)air,
                Free = (float)free,
                Turbulence = (float)(style.Turbulence * air),
                BoxLength = (float)length,
                BoxHalfWidth = (float)Math.Max(1.5d, half),
                Ratio = (float)ratio,
            };
        }
    }
}
