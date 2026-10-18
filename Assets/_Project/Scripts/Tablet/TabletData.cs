using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>Reference for velocity (and the navball markers).</summary>
    public enum SpeedFrame { Surface, Orbit }

    /// <summary>Which altitude is shown large (toggled by clicking it).</summary>
    public enum AltitudeMode { SeaLevel, Surface }

    /// <summary>Attitude hold mode (SAS). Stability holds the current attitude.</summary>
    public enum SasMode { Stability, Prograde, Retrograde, Normal, Antinormal, RadialIn, RadialOut }

    /// <summary>Six navball markers (order matches the render arrays).</summary>
    public enum NavMarker { Prograde = 0, Retrograde = 1, Normal = 2, Antinormal = 3, RadialIn = 4, RadialOut = 5 }

    /// <summary>
    /// One stage for the ΔV block. Fields map 1:1 to DvStage from DeltaVModel.cs —
    /// converted in DvTabletAdapter.
    /// </summary>
    public sealed class TabletStage
    {
        public int Index;
        public string EngineLabel = string.Empty;
        public string PropellantLabel = string.Empty;
        public int EngineCount;
        public double DryMassKg;
        public double PropellantMassKg;
        public double PropellantRemainingKg;
        public double StartMassKg;
        public double EndMassKg;
        public double ThrustN;
        public double IspS;
        public double BurnTimeS;
        public double DeltaVMs;
        public double PlanDeltaVMs;
        public double PlanBurnTimeS;
        public double TwrStart;
        public double TwrEnd;
        public bool IsSpent;
        public bool IsJettisoned;
        public bool IsActive;
        public string[] Warnings = new string[0];

        public bool HasEngine { get { return EngineCount > 0; } }
    }

    /// <summary>Orbit parameters. No apoapsis (hyperbola) — HasApoapsis = false.</summary>
    public struct TabletOrbit
    {
        public bool Valid;
        public bool HasApoapsis;
        public double ApAltitudeM;
        public double ApTimeS;
        public double ApSpeedMs;
        public double PeAltitudeM;
        public double PeTimeS;
        public double PeSpeedMs;
        public double PeriodS;
        public double Eccentricity;
        public double InclinationDeg;
    }

    /// <summary>
    /// EVERYTHING the tablet displays. The game fills this object (Fill); the tablet
    /// knows nothing else about the simulation. Units: SI (m, m/s, s, kg, N, Pa),
    /// angles in degrees, g-loads in g (g0 = 9.80665).
    /// </summary>
    public sealed class TabletTelemetry
    {
        // ── Reference body and frame ─────────────────────────────────────
        public string[] ReferenceBodies = { "ЗЕМЛЯ" };
        public int ReferenceBodyIndex;
        public SpeedFrame Frame = SpeedFrame.Surface;

        // ── Speed / acceleration / gravity ──────────────────────────────
        public double SpeedMs;           // speed magnitude in the selected frame
        public double VerticalSpeedMs;   // + upward (along the radius vector from the body)
        public double AccelerationMs2;   // acceleration (however the game decides: |d|v|/dt| or proper)
        public double GForce;            // g-load = proper acceleration / g0
        public double GravityG;          // gravity at the craft position, in g

        // ── Altitude and environment ─────────────────────────────────────
        public double AltitudeSeaLevelM;
        public double AltitudeSurfaceM;
        public double PressurePa;

        // ── Orbit ────────────────────────────────────────────────────────
        public TabletOrbit Orbit;

        // ── Attitude (navball) ───────────────────────────────────────────
        /// <summary>Ship rotation relative to the local horizon: ship → (X=east, Y=up, Z=north).</summary>
        public Quaternion Attitude = Quaternion.identity;
        public bool MarkersValid;
        /// <summary>Unit vectors in the horizon frame (east, up, north).</summary>
        public Vector3 ProgradeDir = Vector3.forward;
        public Vector3 NormalDir = Vector3.up;
        public Vector3 RadialOutDir = Vector3.up;

        // ── Control systems ──────────────────────────────────────────────
        public bool SasEnabled;
        public SasMode SasMode;
        public bool RcsEnabled;

        // ── Ship control (the "CONTROL" panel) ──────────────────────────
        /// <summary>Throttle commanded by the player, 0..1.</summary>
        public double Throttle01;

        /// <summary>Main engine master switch: at least one is on.</summary>
        public bool EngineOn;

        /// <summary>There is something to control (a flight model exists) — the engine button is active.</summary>
        public bool EngineAvailable;

        /// <summary>"Stage" will do something now: there is something to ignite or jettison.</summary>
        public bool CanStage;

        /// <summary>Current stage is already ignited (button says "jettison", otherwise "ignite").</summary>
        public bool StageIgnited;

        // ── Mass / thrust / ΔV ───────────────────────────────────────────
        public double CurrentMassKg;
        public double ThrustN;
        public double Twr;
        public bool InFlight;
        public double TotalDeltaVMs;
        public double PlanDeltaVMs;
        public double TotalBurnTimeS;
        public string DvEnvLabel = "ВАКУУМ";
        public TabletStage[] Stages = new TabletStage[0];
        public string[] Notes = new string[0];

        /// <summary>Direction of a navball marker in the horizon frame.</summary>
        public Vector3 MarkerDir(NavMarker m)
        {
            switch (m)
            {
                case NavMarker.Prograde: return ProgradeDir;
                case NavMarker.Retrograde: return -ProgradeDir;
                case NavMarker.Normal: return NormalDir;
                case NavMarker.Antinormal: return -NormalDir;
                case NavMarker.RadialOut: return RadialOutDir;
                default: return -RadialOutDir;
            }
        }
    }

    /// <summary>Commands the tablet sends to the game. All optional — empty stubs by default.</summary>
    public interface ITabletCommands
    {
        void SetSasEnabled(bool on);
        void SetSasMode(SasMode mode);
        void SetRcsEnabled(bool on);
        void SetSpeedFrame(SpeedFrame frame);
        void SetReferenceBody(int index);

        /// <summary>Throttle 0..1 (like Z/X on the keyboard).</summary>
        void SetThrottle(double throttle01);

        /// <summary>Main engine master switch (like F9/F10).</summary>
        void SetEnginesEnabled(bool on);

        /// <summary>Stage: ignite/jettison (like Space).</summary>
        void RequestStage();
    }

    /// <summary>
    /// The "game ↔ tablet" bridge. The only thing to write to hook it up: a subclass
    /// that fills in simulation data in Fill and executes the commands.
    /// </summary>
    public abstract class TabletLink : MonoBehaviour, ITabletCommands
    {
        /// <summary>Fill the telemetry with the current state (called by the tablet up to 20 times/s).</summary>
        public abstract void Fill(TabletTelemetry t);

        public virtual void SetSasEnabled(bool on) { }
        public virtual void SetSasMode(SasMode mode) { }
        public virtual void SetRcsEnabled(bool on) { }
        public virtual void SetSpeedFrame(SpeedFrame frame) { }
        public virtual void SetReferenceBody(int index) { }
        public virtual void SetThrottle(double throttle01) { }
        public virtual void SetEnginesEnabled(bool on) { }
        public virtual void RequestStage() { }
    }
}
