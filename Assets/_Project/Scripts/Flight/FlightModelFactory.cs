using System;
using System.Collections.Generic;
using Galilego.Building;
using Galilego.Core;
using Galilego.Spacecraft;
using UnityEngine;

namespace Galilego.Flight
{
    /// <summary>
    /// Nozzle marker on a part MODEL (while there are no RCS models — placeholders are made in a
    /// 3D editor or in the prefab). Position = transform.position, exhaust goes along transform.forward,
    /// force on the ship — backwards. The factory takes markers instead of placeholders, so thrust
    /// comes exactly from where the nozzle sits on the model.
    /// </summary>
    public sealed class FlightMountMarker : MonoBehaviour
    {
        public ThrusterKind Kind = ThrusterKind.Rcs;
        public float ThrustN = 450f;
        public float IspSeconds = 290f;
        [Tooltip("Топливо этого сопла, кг (РСУ — монотопливо).")]
        public float PropellantKg = 8f;
    }

    /// <summary>
    /// RCS parameters. ALL numbers are placeholders of Orion/Apollo order (RCS: 450 N per nozzle).
    /// No model — tune them here.
    /// </summary>
    [Serializable]
    public sealed class FlightSystemsConfig
    {
        public bool EnableRcs = true;
        public int RcsBlocks = 4;                  // blocks around the ring
        public double RcsThrustN = 450d;           // thrust per nozzle
        public double RcsIspS = 290d;
        public double RcsDryKgPerBlock = 15d;
        public double RcsPropellantKgPerNozzle = 12d;

        /// <summary>
        /// Fallback RCS for a ship with no RCS BLOCK PARTS: a hidden ring on the host
        /// part (the pre-model behaviour). Off by default — the player mounts
        /// RCS blocks from the inventory, and their nozzles drive the control system
        /// (part nozzles use RcsBlockPlacement nozzle constants: 450 N, 290 s, 12 kg).
        /// </summary>
        public bool VirtualRcsWithoutBlocks;
    }

    /// <summary>Builds a VesselFlightModel from a blueprint: real engines, RCS.</summary>
    public static class FlightModelFactory
    {
        /// <summary>
        /// defs — PartDefinition[] of the blueprint (BuildMath.ToPartDefinitions); a NEW array is
        /// returned (model Defs). defs masses are full (with propellant and hardware) — as
        /// Proxy.Build must receive them at start.
        /// </summary>
        public static VesselFlightModel Build(
            List<PlacedPart> parts, PartDefinition[] defs, List<StageCore> cores, FlightSystemsConfig cfg)
        {
            cfg = cfg ?? new FlightSystemsConfig();
            int n = defs.Length;
            int capsule = -1;
            for (int i = 0; i < n; i++) if (parts[i].Kind == PartKind.Capsule) { capsule = i; break; }
            int host = capsule >= 0 ? capsule : TopMost(parts);

            var all = new PartDefinition[n];
            Array.Copy(defs, all, n);
            var dry = new double[n]; var prop = new double[n]; var hw = new double[n]; var stage = new int[n];
            for (int i = 0; i < n; i++) stage[i] = -1;

            for (int c = 0; c < cores.Count; c++)
                for (int k = 0; k < cores[c].PartIndices.Count; k++) stage[cores[c].PartIndices[k]] = c;

            for (int i = 0; i < n; i++)
            {
                PartMetrics m = parts[i].Metrics;
                prop[i] = m.PropellantMassKg;
                dry[i] = Math.Max(0d, m.MassKg - m.PropellantMassKg);
            }

            var model = new VesselFlightModel(all, dry, prop, stage, hw);

            // ─ main engines: application point — gimbal pivot (top of the engine), axis +Y ─
            for (int i = 0; i < n; i++)
            {
                if (parts[i].Kind != PartKind.Engine || parts[i].EngineSpec == null) continue;
                EngineSpec s = parts[i].EngineSpec;
                PartDefinition d = all[i];
                model.Thrusters.Add(new Thruster
                {
                    Name = s.DisplayName, Kind = ThrusterKind.Main, PartIndex = i, EngineKey = s, Stage = stage[i],
                    Position = new Vector3d(d.Offset.x, d.Offset.y + (0.5d * d.Dimensions.y), d.Offset.z),
                    Direction = new Vector3d(0d, 1d, 0d),
                    ThrustVacN = s.ThrustVacuumNewtons, IspVacS = s.IspVacuumSeconds, IspSlS = s.IspSeaLevelSeconds,
                    MinThrottle = s.MinThrottle, ThrottleRatePerS = s.ThrottleRatePerS,
                    MaxOperatingPressurePa = s.MaxOperatingPressurePa,
                    GimbalMaxRad = s.GimbalDegrees * Math.PI / 180d, GimbalRateRadS = s.GimbalRateDegPerS * Math.PI / 180d,
                });
            }

            if (host < 0) return model;

            // ─ markers on part models (if any) ─
            var markers = new List<(FlightMountMarker m, int part)>();
            for (int i = 0; i < n; i++)
            {
                if (parts[i].Visual == null || parts[i].Visual.Root == null) continue;
                foreach (FlightMountMarker mk in parts[i].Visual.Root.GetComponentsInChildren<FlightMountMarker>())
                    markers.Add((mk, i));
            }

            PartDefinition hd = all[host];
            double ringR = Math.Max(0.5d, hd.Dimensions.x * 0.95d);

            // ─ RCS: the player's blocks (markers on the parts) drive the control system;
            // the hidden ring on the host is only an explicit fallback for ships without blocks ─
            double hwRcs = 0d;
            bool rcsMarked = markers.Exists(x => x.m.Kind == ThrusterKind.Rcs);
            if (cfg.EnableRcs && !rcsMarked && !cfg.VirtualRcsWithoutBlocks)
            {
                Debug.LogWarning("[Flight] РСУ: на корабле нет блоков РСУ — каналы РСУ/SAS останутся без тяги. "
                    + "Поставь блоки РСУ на бак в ангаре.");
            }

            if (cfg.EnableRcs && !rcsMarked && cfg.VirtualRcsWithoutBlocks)
            {
                double y = hd.Offset.y + (0.2d * hd.Dimensions.y);
                for (int b = 0; b < cfg.RcsBlocks; b++)
                {
                    double a = b * 2d * Math.PI / cfg.RcsBlocks;
                    var rad = new Vector3d(Math.Cos(a), 0d, Math.Sin(a));
                    var tan = new Vector3d(-Math.Sin(a), 0d, Math.Cos(a));
                    var pos = new Vector3d(hd.Offset.x + (ringR * rad.X), y, hd.Offset.z + (ringR * rad.Z));
                    // 5 nozzles per block: tangential ±, radially inward (exhaust outward), along the axis ±.
                    foreach (Vector3d f in new[] { tan, -tan, -rad, new Vector3d(0d, 1d, 0d), new Vector3d(0d, -1d, 0d) })
                        model.Thrusters.Add(new Thruster
                        {
                            Name = "РСУ " + b, Kind = ThrusterKind.Rcs, PartIndex = host, Position = pos, Direction = f,
                            ThrustVacN = cfg.RcsThrustN, IspVacS = cfg.RcsIspS, OwnPropKg = cfg.RcsPropellantKgPerNozzle,
                        });
                    hwRcs += cfg.RcsDryKgPerBlock;
                }
            }

            // Markers from models: position/direction come from transforms (real nozzle locations).
            foreach (var (mk, part) in markers)
            {
                PlacedPart placed = parts[part];
                Transform root = placed.Visual.Root.transform;
                Vector3 local = root.InverseTransformPoint(mk.transform.position);
                Vector3 dirLocal = root.InverseTransformDirection(-mk.transform.forward).normalized;

                // The visual root can be rotated (radial RCS blocks): lift the nozzle
                // offset and direction into the vessel frame before adding the centre.
                Vector3 centerLocal = new Vector3(0f, placed.Metrics.Center, 0f) + placed.PhysicsCenterLocal;
                Vector3 centerRel = placed.Rotation * (local - centerLocal);
                Vector3 dirVessel = placed.Rotation * dirLocal;
                PartDefinition d = all[part];
                model.Thrusters.Add(new Thruster
                {
                    Name = mk.name, Kind = mk.Kind, PartIndex = part,
                    Position = new Vector3d(d.Offset.x + centerRel.x, d.Offset.y + centerRel.y, d.Offset.z + centerRel.z),
                    Direction = new Vector3d(dirVessel.x, dirVessel.y, dirVessel.z),
                    ThrustVacN = mk.ThrustN, IspVacS = mk.IspSeconds, OwnPropKg = mk.PropellantKg,
                });
            }

            hw[host] += hwRcs;

            // Initial Proxy masses: part = dry + propellant + hardware + RCS propellant on it.
            for (int i = 0; i < n; i++) all[i].MassKg = model.PartMassKg(i);
            return model;
        }

        private static int TopMost(List<PlacedPart> parts)
        {
            int best = -1;
            for (int i = 0; i < parts.Count; i++)
                if (best < 0 || parts[i].Pivot.y > parts[best].Pivot.y) best = i;
            return best;
        }
    }
}
