using System;
using Galilego.Core;

namespace Galilego.Flight
{
    public enum ThrusterKind
    {
        Main = 0,          // main engine (propellant from stage pools)
        Rcs = 1,           // RCS thruster (own propellant)
        AbortMotor = 2,    // launch abort escape motor (solid propellant)
        JettisonMotor = 3  // launch abort system jettison motor
    }

    /// <summary>
    /// ONE nozzle/engine with a real application point. Position and Direction are in
    /// ship space (Y axis along the rocket, origin at the bottom of the stack, like
    /// PartDefinition.Offset). Direction is the direction of FORCE on the ship (opposite
    /// the exhaust). Torque is computed as (Position − CoM) × F, so an offset engine,
    /// CoM drift from fuel burn and nozzle gimbal produce real torque, not "thrust from the CoM".
    /// </summary>
    public sealed class Thruster
    {
        public const double G0 = 9.80665d;
        public const double SeaLevelPa = 101325d;

        public string Name;
        public ThrusterKind Kind;
        public int PartIndex;                       // owning part (PhysX body)
        public Vector3d Position;                   // application point (gimbal pivot for Main)
        public Vector3d Direction = new Vector3d(0d, 1d, 0d);
        public double ThrustVacN;
        public double IspVacS = 300d;
        public double IspSlS;                       // <=0 — same as vacuum
        public double MinThrottle;                  // minimum throttle 0..1
        public double ThrottleRatePerS = 2d;        // throttle ramp rate
        public double MaxOperatingPressurePa = double.PositiveInfinity; // above this — ignition impossible
        public double GimbalMaxRad;
        public double GimbalRateRadS = 0.3d;
        public object EngineKey;                    // EngineSpec — link to the StageCore group
        public int Stage = -1;
        public bool Enabled = true;
        public double OwnPropKg;                    // RCS / solid motor propellant

        // ─ state ─
        public double Level;                        // actual thrust fraction 0..1
        public double GimbalX, GimbalZ;             // gimbal angles (rad) around the ship's X and Z axes
        public bool Ignited;                        // solid motors: ignited

        public double IspAt(double pressurePa)
        {
            if (!(IspSlS > 0d))
            {
                return IspVacS;
            }

            double k = Math.Min(1d, Math.Max(0d, pressurePa / SeaLevelPa));
            return IspVacS - ((IspVacS - IspSlS) * k);
        }

        /// <summary>Thrust at the current level and pressure: F = ṁ·g0·Isp(p).</summary>
        public double ThrustAt(double pressurePa)
        {
            return Level * ThrustVacN * IspAt(pressurePa) / IspVacS;
        }

        /// <summary>Mass flow at the current level, kg/s (independent of pressure).</summary>
        public double MassFlow
        {
            get { return Level * ThrustVacN / (G0 * IspVacS); }
        }

        /// <summary>Force direction including gimbal.</summary>
        public Vector3d CurrentDirection()
        {
            Vector3d d = Direction.Normalized;
            if (GimbalX == 0d && GimbalZ == 0d)
            {
                return d;
            }

            QuaternionD rx = QuaternionD.FromAxisAngle(new Vector3d(1d, 0d, 0d), GimbalX);
            QuaternionD rz = QuaternionD.FromAxisAngle(new Vector3d(0d, 0d, 1d), GimbalZ);
            return rx.Rotate(rz.Rotate(d)).Normalized;
        }
    }

    /// <summary>Player/autopilot commands for one frame.</summary>
    public sealed class FlightControls
    {
        public double Pitch;     // -1..1, rotation around the ship's X axis
        public double Yaw;       // -1..1, around Z
        public double Roll;      // -1..1, around the rocket's Y axis
        public Vector3d Translate; // RCS translation -1..1 along ship axes
        public bool SasOn = true;  // attitude hold (autopilot)
        public bool RcsOn = true;
    }
}
