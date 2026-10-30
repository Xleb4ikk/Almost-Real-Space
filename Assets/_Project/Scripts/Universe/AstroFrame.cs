using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// The boundary between physics and the engine. Physics lives in the inertial
    /// astrodynamics frame (Z-up, SI, double, see KeplerMath): gravity, integrators
    /// and events are computed there. The engine needs a Y-up float frame.
    /// The boundary is TYPED: a sim vector is a distinct type (SimVector3, and in
    /// Unity UnityEngine.Vector3), so it cannot be fed by accident to a function
    /// expecting Vector3d without an explicit pass through this bridge. There is
    /// exactly one mapping, fixed here:
    /// sim = (x, z, −y) — a −90° rotation about X (det=+1, handedness preserved,
    /// cross product commutes with the bridge); north +Z becomes up +Y. The inverse
    /// is the transposed matrix: astro = (sim.X, −sim.Z, sim.Y).
    /// </summary>
    public static class AstroFrame
    {
#if UNITY_5_3_OR_NEWER
        /// <summary>Astrodynamics double vector into the simulation float frame.</summary>
        public static UnityEngine.Vector3 ToSimulation(Vector3d astro)
        {
            return new UnityEngine.Vector3((float)astro.X, (float)astro.Z, (float)-astro.Y);
        }

        /// <summary>Simulation frame back into astrodynamics.</summary>
        public static Vector3d ToAstro(UnityEngine.Vector3 sim)
        {
            return new Vector3d(sim.x, -sim.z, sim.y);
        }

        /// <summary>Core orientation (QuaternionD, body→world) into a Unity quaternion:
        /// the same axis permutation by conjugation — the angle is preserved and the axis
        /// goes through the position bridge. Cheap in double, converted to float on output.</summary>
        public static UnityEngine.Quaternion ToSimulation(QuaternionD astro)
        {
            QuaternionD sim = ToSimulationFrame(astro);
            return new UnityEngine.Quaternion((float)sim.X, (float)sim.Y, (float)sim.Z, (float)sim.W);
        }

        /// <summary>
        /// A rotation ALREADY assembled in sim axes: only a float cast, no permutation
        /// and no conjugation.
        ///
        /// Needed where the basis is built explicitly in sim coordinates (SurfaceFrameMath
        /// .SimulationRotation: the permutation is applied to EACH image of the local axis
        /// before the matrix is assembled). Calling ToSimulation(QuaternionD) again on such
        /// a result would permute the axes a second time and tilt the frame.
        /// </summary>
        public static UnityEngine.Quaternion SimulationQuaternion(QuaternionD sim)
        {
            return new UnityEngine.Quaternion((float)sim.X, (float)sim.Y, (float)sim.Z, (float)sim.W);
        }
#else
        /// <summary>
        /// Outside Unity (test harness): the same mapping on a double triple, so the
        /// roundtrip tests exercise the real axis permutation rather than a stub.
        /// </summary>
        public static SimVector3 ToSimulation(Vector3d astro)
        {
            return new SimVector3(astro.X, astro.Z, -astro.Y);
        }

        public static Vector3d ToAstro(SimVector3 sim)
        {
            return new Vector3d(sim.X, -sim.Z, sim.Y);
        }
#endif

        /// <summary>
        /// ORIENTATION bridge: q_sim = R·q_astro·R⁻¹, where R is the same (x, z, −y)
        /// rotation as the position bridge (R_x(−90°), det=+1). A pure double function,
        /// unit-tested; the Unity branch above merely packs the result into
        /// UnityEngine.Quaternion. Conjugation preserves the angle and maps the axis via
        /// the position bridge: a rotation about north +Z becomes a rotation about up +Y.
        /// </summary>
        public static QuaternionD ToSimulationFrame(QuaternionD astro)
        {
            QuaternionD frame = QuaternionD.FromAxisAngle(new Vector3d(1d, 0d, 0d), -System.Math.PI / 2d);
            return (frame * astro * frame.Conjugated).Normalized;
        }

        /// <summary>
        /// Render rotation of the VESSEL from Ship.Attitude (body→astro). The ship's body axes are the
        /// vessel's own build axes (Y along the rocket, the same numbers as the visual's local axes and
        /// PartDefinition.Offset), NOT astro-style axes — so only the WORLD side goes through the bridge:
        /// q_sim = R·q. The generic ToSimulationFrame (R·q·R⁻¹) also bridges the body side and turns
        /// the rocket axis (local Y) onto body Z: the craft was drawn 90° off its thrust direction.
        /// </summary>
        public static QuaternionD BodyToSimulationFrame(QuaternionD attitude)
        {
            QuaternionD frame = QuaternionD.FromAxisAngle(new Vector3d(1d, 0d, 0d), -System.Math.PI / 2d);
            return (frame * attitude).Normalized;
        }
    }

    /// <summary>
    /// The canonical sim-frame (Y-up) type outside Unity — the typed astro/sim
    /// boundary: a sim vector is not type-compatible with Vector3d, so the frame
    /// cannot be mixed up away from AstroFrame without an explicit field cast. In
    /// Unity, UnityEngine.Vector3 itself plays this role (float, same x/y/z field
    /// order). IMPORTANT: the struct always compiles — roundtrip tests reference the
    /// type by name, and the ToSimulation/ToAstro branches above are mutually
    /// exclusive, so there is no signature conflict.
    /// </summary>
    public struct SimVector3
    {
        public double X;
        public double Y;
        public double Z;

        public SimVector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }
}
