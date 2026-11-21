using System.Collections.Generic;
using Galilego.Building;
using Galilego.Flight;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Effects
{
    /// <summary>
    /// Attaches plumes to every main engine and RCS nozzle of the flight model and drives them.
    /// SpacecraftBuilder calls Attach() right after FlightRuntime.Begin() and Detach() in ReturnToBuild().
    /// </summary>
    public sealed class PlumeRig : MonoBehaviour
    {
        private static PlumeRig instance;

        private readonly List<PlumeEmitter> emitters = new List<PlumeEmitter>();
        private SimulationRunner runner;

        public static void Attach(List<PlacedPart> parts, VesselFlightModel flight, SimulationRunner runner)
        {
            Detach();
            if (parts == null || flight == null || runner == null) return;

            var go = new GameObject("PlumeRig");
            instance = go.AddComponent<PlumeRig>();
            instance.runner = runner;

            // 1) Main engines: nozzle exit plane = engine visual root (pivot at the bottom of the model).
            foreach (Thruster t in flight.Thrusters)
            {
                if (t.Kind != ThrusterKind.Main) continue;
                PlacedPart part = parts[t.PartIndex];
                if (part.Visual == null || part.Visual.Root == null) continue;
                EngineSpec spec = part.EngineSpec;

                var nozzle = new PlumeNozzle
                {
                    ExpansionRatio = spec != null ? spec.ExpansionRatio : 40d,
                    Gamma = spec != null ? spec.ExhaustGamma : 1.2d,
                    ChamberPressurePa = spec != null ? spec.ChamberPressurePa : 5.0e6d,
                    ExitHalfAngleDeg = spec != null ? spec.ExitHalfAngleDeg : 9d,
                    ExitRadiusM = Mathf.Max(0.05f, part.Metrics.Radius),
                };

                PlumeEmitter e = PlumeEmitter.Create(
                    part.Visual.Root.transform, t, flight, nozzle, PlumeStyles.For(spec), part.Rotation, true);
                if (e != null) instance.emitters.Add(e);
            }

            // 2) RCS: the factory appended one Thruster per FlightMountMarker, in part order — they are the LAST M thrusters.
            var markers = new List<(FlightMountMarker m, int part)>();
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Visual == null || parts[i].Visual.Root == null) continue;
                foreach (FlightMountMarker mk in parts[i].Visual.Root.GetComponentsInChildren<FlightMountMarker>())
                    markers.Add((mk, i));
            }

            int first = flight.Thrusters.Count - markers.Count;
            for (int k = 0; k < markers.Count && first >= 0; k++)
            {
                Thruster t = flight.Thrusters[first + k];
                if (t.Kind != ThrusterKind.Rcs) continue;
                var nozzle = new PlumeNozzle
                {
                    ExpansionRatio = 40d, Gamma = 1.3d, ChamberPressurePa = 0.8e6d, ExitHalfAngleDeg = 12d, ExitRadiusM = 0.05d,
                };
                // Marker transform: +Z (forward) is the exhaust direction -> the emitter inherits it.
                PlumeEmitter e = PlumeEmitter.Create(
                    markers[k].m.transform, t, flight, nozzle, PlumeStyles.Monoprop, Quaternion.identity, false);
                if (e != null) instance.emitters.Add(e);
            }

            Debug.Log("[Plume] факелов: " + instance.emitters.Count);
        }

        public static void Detach()
        {
            // Anything left over from a previous launch must go too: this project enters
            // play mode without reloading the scene or the domain, so a plume created by
            // an earlier session can outlive the vessel it belonged to and keep drawing
            // flames on a craft that is back in the builder.
            PlumeEmitter[] stray = FindObjectsByType<PlumeEmitter>(FindObjectsInactive.Include);
            for (int i = 0; i < stray.Length; i++)
                if (stray[i] != null) Destroy(stray[i].gameObject);

            if (instance != null) Destroy(instance.gameObject);
            instance = null;
        }

        // Default order 0: after ShipView (-49) and VesselMirror (-48), so the nozzle poses are final.
        private void LateUpdate()
        {
            if (runner == null || runner.Ship == null) return;
            double pa = runner.AmbientPressurePa(runner.Ship.Position, runner.TimeSeconds);
            float dt = Time.deltaTime;
            for (int i = 0; i < emitters.Count; i++)
                if (emitters[i] != null) emitters[i].Tick(pa, dt);
        }
    }
}
