using System;
using System.Collections.Generic;
using Galilego.Building;
using Galilego.Core;
using Galilego.Spacecraft;
using UnityEngine;

namespace Galilego.Flight
{
    /// <summary>
    /// Nozzle marker on a part MODEL (while there are no RCS/LES models — placeholders are made in a
    /// 3D editor or in the prefab). Position = transform.position, exhaust goes along transform.forward,
    /// force on the ship — backwards. The factory takes markers instead of placeholders, so thrust
    /// comes exactly from where the nozzle sits on the model.
    /// </summary>
    public sealed class FlightMountMarker : MonoBehaviour
    {
        public ThrusterKind Kind = ThrusterKind.Rcs;
        public float ThrustN = 450f;
        public float IspSeconds = 290f;
        [Tooltip("Топливо этого сопла, кг (РСУ — монотопливо; САС — твёрдое топливо мотора).")]
        public float PropellantKg = 8f;
    }

    /// <summary>
    /// LES and RCS parameters. ALL numbers are placeholders of Orion/Apollo order (LES: abort 1.78 MN ≈ 4 s,
    /// jettison 178 kN ≈ 1.8 s; RCS: 450 N per nozzle). No model — tune them here.
    /// </summary>
    [Serializable]
    public sealed class FlightSystemsConfig
    {
        public bool EnableLes = true;
        public double LesDryKg = 2400d;
        public double LesHeight = 7d;
        public double LesRadius = 0.55d;
        public int AbortNozzles = 5;               // abort nozzles (ring, canted inward)
        public double AbortThrustN = 1.78e6d;      // total
        public double AbortIspS = 255d;
        public double AbortPropellantKg = 3000d;
        public double AbortCantDeg = 20d;
        public int JettisonNozzles = 4;
        public double JettisonThrustN = 1.78e5d;
        public double JettisonIspS = 240d;
        public double JettisonPropellantKg = 140d;

        public bool EnableRcs = true;
        public int RcsBlocks = 4;                  // blocks around the ring
        public double RcsThrustN = 450d;           // thrust per nozzle
        public double RcsIspS = 290d;
        public double RcsDryKgPerBlock = 15d;
        public double RcsPropellantKgPerNozzle = 12d;
    }

    /// <summary>Builds a VesselFlightModel from a blueprint: real engines, LES, RCS.</summary>
    public static class FlightModelFactory
    {
        /// <summary>
        /// defs — PartDefinition[] of the blueprint (BuildMath.ToPartDefinitions); for LES a tower
        /// part is appended, so a NEW array is returned (model Defs). defs masses are full
        /// (with propellant and hardware) — as Proxy.Build must receive them at start.
        /// </summary>
        public static VesselFlightModel Build(
            List<PlacedPart> parts, PartDefinition[] defs, List<StageCore> cores, FlightSystemsConfig cfg)
        {
            cfg = cfg ?? new FlightSystemsConfig();
            int n = defs.Length;
            int capsule = -1;
            for (int i = 0; i < n; i++) if (parts[i].Kind == PartKind.Capsule) { capsule = i; break; }
            int host = capsule >= 0 ? capsule : TopMost(parts);

            bool les = cfg.EnableLes && host >= 0;
            int total = n + (les ? 1 : 0);
            var all = new PartDefinition[total];
            Array.Copy(defs, all, n);
            var dry = new double[total]; var prop = new double[total]; var hw = new double[total]; var stage = new int[total];
            for (int i = 0; i < total; i++) stage[i] = -1;

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
            double topY = hd.Offset.y + (0.5d * hd.Dimensions.y);
            double ringR = Math.Max(0.5d, hd.Dimensions.x * 0.95d);
            int lesIndex = -1;

            // ─ LES: the tower is a separate part (own Rigidbody in the zone, own joint) ─
            if (les)
            {
                lesIndex = n;
                all[lesIndex] = new PartDefinition
                {
                    Name = "САС", Offset = new Vector3(hd.Offset.x, (float)(topY + (0.5d * cfg.LesHeight)), hd.Offset.z),
                    Shape = PartShape.Cylinder, Dimensions = new Vector3((float)cfg.LesRadius, (float)cfg.LesHeight, 0f),
                    ParentIndex = host, JointStrengthNewtons = 4e6d, BreakTorqueNm = 4e7d,
                    JointAnchor = new Vector3(0f, (float)(-0.5d * cfg.LesHeight), 0f),
                };
                hw[lesIndex] = cfg.LesDryKg;
                double lesY = all[lesIndex].Offset.y;

                bool abortMarked = markers.Exists(x => x.m.Kind == ThrusterKind.AbortMotor);
                bool jetMarked = markers.Exists(x => x.m.Kind == ThrusterKind.JettisonMotor);
                // Abort: nozzles at the bottom of the tower, canted INWARD (net force through the axis and stack COM).
                if (!abortMarked)
                    AddRing(model, ThrusterKind.AbortMotor, lesIndex, cfg.AbortNozzles, new Vector3d(hd.Offset.x, topY + 0.6d, hd.Offset.z),
                        0.45d, cfg.AbortThrustN, cfg.AbortIspS, cfg.AbortPropellantKg, cfg.AbortCantDeg, "Увод");
                if (!jetMarked)
                    AddRing(model, ThrusterKind.JettisonMotor, lesIndex, cfg.JettisonNozzles, new Vector3d(hd.Offset.x, lesY + (0.35d * cfg.LesHeight), hd.Offset.z),
                        0.40d, cfg.JettisonThrustN, cfg.JettisonIspS, cfg.JettisonPropellantKg, 25d, "Отстрел");
                model.ArmLes(lesIndex);

                // Stays with the capsule during an abort: the capsule, everything above it, the tower.
                var keep = new bool[total];
                for (int i = 0; i < n; i++) keep[i] = i == host || (parts[i].Pivot.y >= parts[host].Pivot.y && i != host && parts[i].Kind != PartKind.Engine);
                keep[lesIndex] = true;
                model.AbortKeep = keep;
            }

            // ─ RCS: blocks around the ring on the host; each block has 5 nozzles pointing in different directions ─
            double hwRcs = 0d;
            bool rcsMarked = markers.Exists(x => x.m.Kind == ThrusterKind.Rcs);
            if (cfg.EnableRcs && !rcsMarked)
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
                Transform root = parts[part].Visual.Root.transform;
                Vector3 local = root.InverseTransformPoint(mk.transform.position);
                Vector3 dirLocal = root.InverseTransformDirection(-mk.transform.forward).normalized;
                PartDefinition d = all[part];
                Vector3 centerRel = local - new Vector3(0f, parts[part].Metrics.Center, 0f);
                int target = mk.Kind == ThrusterKind.AbortMotor || mk.Kind == ThrusterKind.JettisonMotor ? (lesIndex >= 0 ? lesIndex : part) : part;
                model.Thrusters.Add(new Thruster
                {
                    Name = mk.name, Kind = mk.Kind, PartIndex = target,
                    Position = new Vector3d(d.Offset.x + centerRel.x, d.Offset.y + centerRel.y, d.Offset.z + centerRel.z),
                    Direction = new Vector3d(dirLocal.x, dirLocal.y, dirLocal.z),
                    ThrustVacN = mk.ThrustN, IspVacS = mk.IspSeconds, OwnPropKg = mk.PropellantKg,
                });
            }

            hw[host] += hwRcs;

            // Initial Proxy masses: part = dry + propellant + hardware + RCS/LES propellant on it.
            for (int i = 0; i < total; i++) all[i].MassKg = model.PartMassKg(i);
            return model;
        }

        private static void AddRing(VesselFlightModel model, ThrusterKind kind, int part, int count, Vector3d center,
            double radius, double totalThrustN, double isp, double propellantKg, double cantDeg, string name)
        {
            double cant = cantDeg * Math.PI / 180d;
            for (int k = 0; k < count; k++)
            {
                double a = k * 2d * Math.PI / count;
                var rad = new Vector3d(Math.Cos(a), 0d, Math.Sin(a));
                // Exhaust down-and-out → force up-and-in: F = cos(c)·ŷ − sin(c)·r̂.
                Vector3d dir = (new Vector3d(0d, Math.Cos(cant), 0d)) - (rad * Math.Sin(cant));
                model.Thrusters.Add(new Thruster
                {
                    Name = name + " " + k, Kind = kind, PartIndex = part,
                    Position = center + (rad * radius), Direction = dir,
                    ThrustVacN = totalThrustN / count, IspVacS = isp, OwnPropKg = propellantKg / count,
                });
            }
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
