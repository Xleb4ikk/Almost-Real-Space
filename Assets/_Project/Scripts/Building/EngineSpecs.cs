using System;
using System.Collections.Generic;
using Galilego.Spacecraft;

namespace Galilego.Building
{
    /// <summary>
    /// Engine spec sheet: model, propellant pair, thrust, impulse, mass —
    /// "for game" numbers from open sources (NASA/ULA/Astronautix).
    /// The FBX model path lives in the spec, so EngineSpecs is the SINGLE place
    /// to add an engine: drop the model into Assets/_Project/Models/Parts/
    /// Engines/&lt;name&gt;/ and add one Register(...) line. ModelKey is a substring
    /// of the model name (RS25_Engine → RS25), used to find the spec in the scene.
    /// The entry order is the order of buttons and cards in the UI; the first
    /// entry is the default engine.
    /// </summary>
    public sealed class EngineSpec
    {
        public readonly string Id;
        public readonly string DisplayName;

        /// <summary>Substring of the model prefab name (RS25_Engine → RS25).</summary>
        public readonly string ModelKey;

        /// <summary>Path to the FBX model from the project root (Assets/...).</summary>
        public readonly string ModelPath;

        /// <summary>Engine dry mass, kg.</summary>
        public readonly double MassKg;

        /// <summary>Nozzle expansion ratio (exit/critical area).</summary>
        public readonly double ExpansionRatio;

        /// <summary>Thrust in vacuum, N.</summary>
        public readonly double ThrustVacuumNewtons;

        /// <summary>Specific impulse in vacuum, s.</summary>
        public readonly double IspVacuumSeconds;

        /// <summary>
        /// The engine works only in vacuum (upper stage): not rated for full
        /// thrust in an atmosphere.
        /// </summary>
        public readonly bool VacuumOnly;

        /// <summary>Engine propellant pair (oxidizer, fuel, O/F, flow).</summary>
        public readonly BipropellantMix Mix;

        // ─── Flight parameters (Galilego.Flight flight model) ───

        /// <summary>Sea-level Isp (101 325 Pa), s; ≤0 — same as in vacuum.</summary>
        public double IspSeaLevelSeconds;

        /// <summary>Minimum throttle, fraction of nominal (RS-25: 67 %/109 % ≈ 0.61; 1.0 — not throttleable).</summary>
        public double MinThrottle = 0.1d;

        /// <summary>Maximum nozzle gimbal (swing) angle, degrees.</summary>
        public double GimbalDegrees;

        /// <summary>Gimbal rate, degrees/s.</summary>
        public double GimbalRateDegPerS = 20d;

        /// <summary>Throttle ramp rate, fraction/s.</summary>
        public double ThrottleRatePerS = 2d;

        /// <summary>Ambient pressure above which ignition/operation is impossible (nozzle flow separation), Pa.</summary>
        public double MaxOperatingPressurePa = double.PositiveInfinity;

        public EngineSpec(
            string id, string displayName, string modelKey, string modelPath, double massKg,
            double expansionRatio,
            double thrustVacuumNewtons, double ispVacuumSeconds, bool vacuumOnly,
            BipropellantMix mix)
        {
            Id = id;
            DisplayName = displayName ?? id;
            ModelKey = modelKey;
            ModelPath = modelPath;
            MassKg = massKg;
            ExpansionRatio = expansionRatio;
            ThrustVacuumNewtons = thrustVacuumNewtons;
            IspVacuumSeconds = ispVacuumSeconds;
            VacuumOnly = vacuumOnly;
            Mix = mix;
        }

        public override string ToString()
        {
            return Id;
        }
    }

    /// <summary>
    /// Engine registry — the SINGLE place to add engines: one Register(...)
    /// line with the model path; scene models are picked up automatically
    /// (EnginePrefabs auto-sync or the Galilego → Building → Setup Scene button):
    ///   public static readonly EngineSpec X = Register(new EngineSpec(...));
    /// </summary>
    public static class EngineSpecs
    {
        // The list is declared FIRST: static fields initialize in order.
        private static readonly List<EngineSpec> all = new List<EngineSpec>();

        // RS-25 (SSME): 109 % RPL — 512 300 lbf (2279 kN) in vacuum, Isp
        // 452 s, expansion ratio 69:1, dry mass 7 774 lb ≈ 3526 kg
        // (the game still uses a 3500 kg placeholder). Data:
        // NASA/L3Harris. Works at sea level too (Isp 366 s) — NOT vacuum-only.
        // Propellant: the PropellantPairs.Rs25 pair (LOX/LH2).
        public static readonly EngineSpec Rs25 = Register(new EngineSpec(
            "RS25", "RS-25", "RS25",
            "Assets/_Project/Models/Parts/Engines/RS-25/RS25_Engine.fbx",
            3500d, 69d, 2279000d, 452d, false,
            PropellantPairs.Rs25.Mix)
        {
            IspSeaLevelSeconds = 366d, MinThrottle = 0.61d, GimbalDegrees = 10.5d,
            GimbalRateDegPerS = 20d, ThrottleRatePerS = 1.5d,
        });

        // AJ10-118K (Delta II second stage): 43.4 kN in vacuum, Isp 320.5 s,
        // expansion ratio 65:1, mass 98 kg. The 65:1 nozzle means a vacuum-only
        // engine for an upper stage. Sources: Astronautix/Purdue/ULA.
        // Propellant: the PropellantPairs.Aj10 pair (N2O4/Aerozine-50).
        public static readonly EngineSpec Aj10 = Register(new EngineSpec(
            "AJ10", "AJ10-118K", "AJ10",
            "Assets/_Project/Models/Parts/Engines/AJ10-118K/AJ10-118K_Engine.fbx",
            98d, 65d, 43400d, 320.5d, true,
            PropellantPairs.Aj10.Mix)
        {
            // Not throttleable (fixed thrust), gimbal ±4°; the 65:1 nozzle doesn't work at sea level (flow separation).
            IspSeaLevelSeconds = 0d, MinThrottle = 1d, GimbalDegrees = 4d,
            GimbalRateDegPerS = 10d, ThrottleRatePerS = 10d, MaxOperatingPressurePa = 5000d,
        });

        public static IReadOnlyList<EngineSpec> All
        {
            get { return all; }
        }

        /// <summary>Default engine (when none is specified).</summary>
        public static EngineSpec Default
        {
            get { return Rs25; }
        }

        /// <summary>Find by Id; an unknown Id yields Default.</summary>
        public static EngineSpec Get(string id)
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
        /// Spec by model prefab name: whichever ModelKey occurs in the name
        /// (RS25_Engine → RS-25). None found — null (a model without a spec).
        /// </summary>
        public static EngineSpec MatchPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                return null;
            }

            for (int i = 0; i < all.Count; i++)
            {
                if (prefabName.IndexOf(all[i].ModelKey, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return all[i];
                }
            }

            return null;
        }

        private static EngineSpec Register(EngineSpec spec)
        {
            all.Add(spec);
            return spec;
        }
    }
}
