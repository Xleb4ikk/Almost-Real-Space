using System;
using Galilego.Core;
using Galilego.Spacecraft.Solver;
using UnityEngine;

// Фаза 2 (ТЗ v2, раздел 7): решатель твёрдых тел и жёсткие стыки, без контактов.
// Test204 — свободное падение (ускорение = g); Test205 — энергия свободного вращения;
// Test206 — ползучесть жёстких стыков под постоянной нагрузкой; Test207 — сохранение
// линейного импульса при внутренних силах стыка.
internal static partial class P1bTests
{
    private static readonly Vector3d GravityWorld = new Vector3d(0d, -9.81d, 0d);

    static int Test204_FreeFall()
    {
        const double dt = 1e-3;
        const int steps = 1000;
        var solver = new RigidAssemblySolver(dt, iterations: 1);
        var body = new RigidBody(10d, new Vector3d(1d, 2d, 3d), Vector3d.Zero, QuaternionD.Identity);
        solver.AddBody(body);

        for (int i = 0; i < steps; i++)
        {
            body.ForceWorld = GravityWorld * body.Mass;
            solver.Step();
        }

        double t = solver.Time;
        Vector3d accel = body.Velocity / t;
        double relErr = (accel - GravityWorld).Magnitude / GravityWorld.Magnitude;
        Check(relErr <= 1e-9, "T204 free-fall",
            string.Format(Inv, "t={0:F3} с, a={1:G12}, |a−g|/|g|={2:E2} (≤1e-9)", t, accel.Y, relErr));
        return 0;
    }

    static int Test205_TorqueFreeEnergy()
    {
        const double dt = 1e-3;
        const int steps = 60000;
        var solver = new RigidAssemblySolver(dt, iterations: 1);
        var inertia = new Vector3d(1d, 2d, 3d);
        var body = new RigidBody(5d, inertia, Vector3d.Zero, QuaternionD.Identity);
        body.AngularVelocityBody = new Vector3d(0.1d, 1d, 0.2d);
        solver.AddBody(body);

        double e0 = solver.KineticEnergy();
        double maxEnergyDrift = 0d;
        double maxNormDrift = 0d;
        for (int i = 0; i < steps; i++)
        {
            solver.Step();
            double e = solver.KineticEnergy();
            maxEnergyDrift = Math.Max(maxEnergyDrift, Math.Abs(e - e0) / e0);
            maxNormDrift = Math.Max(maxNormDrift, Math.Abs(body.Orientation.NormSquared - 1d));
        }

        Check(maxEnergyDrift <= 1e-6 && maxNormDrift <= 1e-12, "T205 torque-free-energy",
            string.Format(Inv, "t={0:F1} с, max|ΔT/T|={1:E2} (≤1e-6), max|1−|q|²|={2:E2} (≤1e-12)",
                solver.Time, maxEnergyDrift, maxNormDrift));
        return 0;
    }

    static int Test206_RigidJointCreep()
    {
        const double dt = 1e-3;
        const int steps = 10000;
        var solver = new RigidAssemblySolver(dt, iterations: 20);

        var a = new RigidBody(1000d, new Vector3d(1000d, 1000d, 500d), new Vector3d(0d, 0d, 0d), QuaternionD.Identity);
        var b = new RigidBody(200d, new Vector3d(40d, 40d, 20d), new Vector3d(1.5d, 0d, 0d), QuaternionD.Identity);
        var c = new RigidBody(100d, new Vector3d(10d, 10d, 5d), new Vector3d(1.5d, 1.5d, 0d), QuaternionD.Identity);
        int ia = solver.AddBody(a);
        int ib = solver.AddBody(b);
        int ic = solver.AddBody(c);
        int jab = solver.AddRigidJoint(ia, ib, new Vector3d(0.75d, 0d, 0d));
        int jbc = solver.AddRigidJoint(ib, ic, new Vector3d(1.5d, 0.75d, 0d));

        // Постоянная нагрузка на первое тело: тяга вверх и момент вокруг оси Z.
        var thrust = new Vector3d(0d, 2000d, 0d);
        var torque = new Vector3d(0d, 0d, 50d);

        double maxSeparation = 0d;
        for (int i = 0; i < steps; i++)
        {
            a.ForceWorld = thrust;
            a.TorqueBody = torque;
            b.ForceWorld = GravityWorld * b.Mass;
            c.ForceWorld = GravityWorld * c.Mass;
            solver.Step();
            maxSeparation = Math.Max(maxSeparation,
                Math.Max(solver.JointSeparation(jab), solver.JointSeparation(jbc)));
        }

        Check(maxSeparation <= 1e-3, "T206 rigid-joint-creep",
            string.Format(Inv, "t={0:F1} с, max расхождение якорей стыков={1:E2} м (≤1e-3)",
                solver.Time, maxSeparation));
        return 0;
    }

    static int Test207_ImpulseConservation()
    {
        const double dt = 1e-3;
        const int steps = 2000;
        var solver = new RigidAssemblySolver(dt, iterations: 20);

        var a = new RigidBody(300d, new Vector3d(50d, 60d, 30d), new Vector3d(0d, 0d, 0d), QuaternionD.Identity);
        var b = new RigidBody(120d, new Vector3d(20d, 25d, 10d), new Vector3d(2d, 0.5d, 0d), QuaternionD.Identity);
        a.Velocity = new Vector3d(1d, 0d, 0.5d);
        b.Velocity = new Vector3d(-0.5d, 2d, 0d);
        a.AngularVelocityBody = new Vector3d(0.2d, -0.1d, 0.4d);
        b.AngularVelocityBody = new Vector3d(-0.3d, 0.2d, 0.1d);
        int ia = solver.AddBody(a);
        int ib = solver.AddBody(b);
        solver.AddRigidJoint(ia, ib, new Vector3d(1d, 0.25d, 0d));

        Vector3d p0 = solver.LinearMomentum();
        double scale = (a.Mass * a.Velocity.Magnitude) + (b.Mass * b.Velocity.Magnitude);
        for (int i = 0; i < steps; i++)
        {
            solver.Step();
        }

        double drift = (solver.LinearMomentum() - p0).Magnitude / scale;
        Check(drift <= 1e-9, "T207 impulse-conservation",
            string.Format(Inv, "t={0:F1} с, |ΔP|/Σm|v|={1:E2} (≤1e-9), P0={2:G9}",
                solver.Time, drift, p0.Magnitude));
        return 0;
    }
}
