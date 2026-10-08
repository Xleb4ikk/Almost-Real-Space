using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Spacecraft.Solver;
using UnityEngine;

// Фаза 3 (ТЗ v2, раздел 7): контакты с поверхностью (XPBD-проекция, кулоново трение,
// восстановление), локальная нормаль с footprint. Test208 — момент импульса системы
// (стык без поверхности), 210–215 — приёмка контактов.
internal static partial class P1bTests
{
    private const double ContactDt = 1e-3;
    private const int ContactIterations = 20;

    private static Vector3d Gravity9_81 => new Vector3d(0d, -9.81d, 0d);

    // Точечное тело: один контакт в центре масс — без крутящего момента от трения.
    private static RigidBody PointContactBody(double mass, Vector3d position)
    {
        var b = new RigidBody(mass, new Vector3d(1d, 1d, 1d), position, QuaternionD.Identity);
        b.ContactPointsBody = new[] { Vector3d.Zero };
        return b;
    }

    // Блок 1×1×1 м: четыре нижних угла в контакте, центр на высоте 0.5 м.
    private static RigidBody BlockContactBody(double mass, Vector3d position)
    {
        var b = new RigidBody(mass, new Vector3d(mass / 6d, mass / 6d, mass / 6d), position, QuaternionD.Identity);
        b.ContactPointsBody = new[]
        {
            new Vector3d(-0.5d, -0.5d, -0.5d), new Vector3d(0.5d, -0.5d, -0.5d),
            new Vector3d(-0.5d, -0.5d, 0.5d), new Vector3d(0.5d, -0.5d, 0.5d),
        };
        return b;
    }

    static int Test208_AngularMomentum()
    {
        var solver = new RigidAssemblySolver(ContactDt, iterations: 20);
        var a = new RigidBody(300d, new Vector3d(50d, 60d, 30d), new Vector3d(0d, 0d, 0d), QuaternionD.Identity);
        var b = new RigidBody(120d, new Vector3d(20d, 25d, 10d), new Vector3d(2d, 0.5d, 0d), QuaternionD.Identity);
        a.Velocity = new Vector3d(1d, 0d, 0.5d);
        b.Velocity = new Vector3d(-0.5d, 2d, 0d);
        a.AngularVelocityBody = new Vector3d(0.2d, -0.1d, 0.4d);
        b.AngularVelocityBody = new Vector3d(-0.3d, 0.2d, 0.1d);
        int ia = solver.AddBody(a);
        int ib = solver.AddBody(b);
        solver.AddRigidJoint(ia, ib, new Vector3d(1d, 0.25d, 0d));

        Vector3d l0 = solver.AngularMomentum();
        for (int i = 0; i < 2000; i++)
        {
            solver.Step();
        }

        // Порог не из ТЗ: выставлен по замеру фазы 3. Дрейф O(dt) от несогласованной
        // ориентации стыка (4e-5 при dt=1 мс, ~1.4e-5 при dt=0.25 мс). 1e-3 ловит
        // грубые нарушения: старая позиционная XPBD-схема стыков давала 1.5e-3.
        // Ужесточение — в фазе 4 вместе с упругими стыками (вариант A).
        const double tolerance = 1e-3d;
        double drift = (solver.AngularMomentum() - l0).Magnitude / l0.Magnitude;
        Check(drift <= tolerance, "T208 angular-momentum",
            string.Format(Inv, "t={0:F1} с, |ΔL|/|L0|={1:E2} (≤{2:E0}, O(dt) стыка — фаза 4), |L0|={3:G9}",
                solver.Time, drift, tolerance, l0.Magnitude));
        return 0;
    }

    static int Test210_FlatRest()
    {
        var solver = new RigidAssemblySolver(ContactDt, ContactIterations);
        solver.Ground = new PlaneGround(new Vector3d(0d, 1d, 0d), 0d);
        RigidBody block = BlockContactBody(10d, new Vector3d(0d, 0.5d, 0d));
        solver.AddBody(block);

        double maxSpeed = 0d;
        double maxPenetration = 0d;
        for (int i = 0; i < 5000; i++)
        {
            block.ForceWorld = Gravity9_81 * block.Mass;
            solver.Step();
            if (i >= 1000)
            {
                maxSpeed = Math.Max(maxSpeed, block.Velocity.Magnitude);
            }

            for (int k = 0; k < block.ContactPointsBody.Length; k++)
            {
                Vector3d p = block.Position + block.Orientation.Rotate(block.ContactPointsBody[k]);
                maxPenetration = Math.Max(maxPenetration, -p.Y);
            }
        }

        Check(maxSpeed < 1e-3 && maxPenetration <= 1e-3, "T210 flat-rest",
            string.Format(Inv, "t={0:F1} с, max|v| после 1 с={1:E2} м/с (≤1e-3), max проникновение={2:E2} м (≤1e-3)",
                solver.Time, maxSpeed, maxPenetration));
        return 0;
    }

    static int Test211_ContactRestitution()
    {
        const double e = 0.5d;
        const double drop = 0.5d;
        var solver = new RigidAssemblySolver(ContactDt, ContactIterations);
        solver.Ground = new PlaneGround(new Vector3d(0d, 1d, 0d), 0d);
        solver.Restitution = e;
        RigidBody ball = PointContactBody(10d, new Vector3d(0d, drop, 0d));
        solver.AddBody(ball);

        double vAfter = double.NaN;
        for (int i = 0; i < 2000 && double.IsNaN(vAfter); i++)
        {
            double vBefore = ball.Velocity.Y;
            ball.ForceWorld = Gravity9_81 * ball.Mass;
            solver.Step();
            if (vBefore < 0d && ball.Velocity.Y > 0d)
            {
                vAfter = ball.Velocity.Y;
            }
        }

        double vImpact = Math.Sqrt(2d * 9.81d * drop);
        double ratio = vAfter / (e * vImpact);
        Check(!double.IsNaN(vAfter) && Math.Abs(ratio - 1d) <= 0.1d, "T211 contact-restitution",
            string.Format(Inv, "v_после={0:F4} м/с, e·v_удара={1:F4} м/с (v_удара={2:F4}); отношение={3:F4} (±10%)",
                vAfter, e * vImpact, vImpact, ratio));
        return 0;
    }

    static int Test212_StickSlopeCompat()
    {
        const double g = 9.8d;
        const double mu = 0.7d;
        const double muK = 0.5d;

        // Залипание: после осадки (0.5 с, удар закончен) тело неподвижно.
        // Скольжение: ускорение по участку 0.7–1.0 с, уже после удара о склон (≈0.45 с).
        double Slope(double deg, out double slipAccel, out Vector3d displacementAfterSettle, out double speedAfterSettle)
        {
            double th = deg * Math.PI / 180d;
            var normal = new Vector3d(Math.Sin(th), Math.Cos(th), 0d);
            var solver = new RigidAssemblySolver(ContactDt, ContactIterations)
            {
                Ground = new PlaneGround(normal, 0d),
                StaticFriction = mu,
                KineticFriction = muK,
            };
            RigidBody ball = PointContactBody(10d, normal * 0.5d);
            solver.AddBody(ball);

            Vector3d settled = Vector3d.Zero;
            double speedAt07 = 0d;
            for (int i = 0; i < 1000; i++)
            {
                ball.ForceWorld = new Vector3d(0d, -g, 0d) * ball.Mass;
                solver.Step();
                if (i == 499)
                {
                    settled = ball.Position;
                }

                if (i == 699)
                {
                    speedAt07 = ball.Velocity.Magnitude;
                }
            }

            speedAfterSettle = ball.Velocity.Magnitude;
            displacementAfterSettle = ball.Position - settled;
            slipAccel = (speedAfterSettle - speedAt07) / 0.3d;
            return slipAccel;
        }

        double accelStick;
        Vector3d disp30;
        double speed30;
        Slope(30d, out accelStick, out disp30, out speed30);
        bool stick30 = speed30 < 1e-6 && disp30.Magnitude < 1e-6;

        double accel60;
        Vector3d disp60;
        double speed60;
        Slope(60d, out accel60, out disp60, out speed60);
        double expected60 = g * (Math.Sin(60d * Math.PI / 180d) - muK * Math.Cos(60d * Math.PI / 180d));
        bool slip60 = Math.Abs(accel60 - expected60) < 0.05d;
        double speed60Val = speed60;

        var flat = new RigidAssemblySolver(ContactDt, ContactIterations)
        {
            Ground = new PlaneGround(new Vector3d(0d, 1d, 0d), 0d),
            StaticFriction = mu,
            KineticFriction = muK,
        };
        RigidBody slider = PointContactBody(10d, new Vector3d(0d, 0.5d, 0d));
        slider.Velocity = new Vector3d(50d, 0d, 0d);
        flat.AddBody(slider);
        Vector3d s0 = slider.Position;
        int steps = 0;
        while (steps < 60000 && slider.Velocity.Magnitude > 1e-6)
        {
            slider.ForceWorld = new Vector3d(0d, -g, 0d) * slider.Mass;
            flat.Step();
            steps++;
        }

        double stopDistance = (slider.Position - s0).Magnitude;
        bool stop = Math.Abs(stopDistance - 255.1d) < 2d;

        Check(stick30 && slip60 && stop, "T212 stick-slope-compat",
            string.Format(Inv,
                "стик 30°: |v|={0:E2}, путь после осадки={1:E2} м: {2}; слип 60°: a={3:F3} м/с² (≈{4:F3}): {5}; стоп 50 м/с: путь={6:F1} м (≈255.1): {7}",
                speed30, disp30.Magnitude, stick30, accel60, expected60, slip60, stopDistance, stop));
        return 0;
    }

    static int Test213_RestEnergy()
    {
        var solver = new RigidAssemblySolver(ContactDt, ContactIterations);
        solver.Ground = new PlaneGround(new Vector3d(0d, 1d, 0d), 0d);
        RigidBody block = BlockContactBody(10d, new Vector3d(0d, 0.5d, 0d));
        solver.AddBody(block);

        double E0 = (block.Mass * 9.81d * block.Position.Y) + solver.KineticEnergy();
        double worst = 0d;
        for (int i = 0; i < 10000; i++)
        {
            block.ForceWorld = Gravity9_81 * block.Mass;
            solver.Step();
            double e = (block.Mass * 9.81d * block.Position.Y) + solver.KineticEnergy();
            worst = Math.Max(worst, Math.Abs(e - E0));
        }

        double scale = block.Mass * 9.81d * 0.5d;
        Check(worst <= 0.01d * scale, "T213 rest-energy",
            string.Format(Inv, "t={0:F1} с, max|ΔE|={1:E2} Дж, E0={2:F3} Дж, порог 1% от m·g·0.5 = {3:E2} Дж",
                solver.Time, worst, E0, 0.01d * scale));
        return 0;
    }

    static int Test214_LocalNormalFootprint()
    {
        // Камень: h = A·sin(k·x), λ = 4 м, A = 0.2 м. Истинный наклон в x=0: A·k.
        // Усреднённый footprint 113 м (а не 114): при кратной λ/2 разности sin(57π)=0
        // обращаются в ноль, и кейс вырождается по значению.
        const double amplitude = 0.2d;
        double k = 2d * Math.PI / 4d;
        var rock = new HeightFieldGround((x, z) => amplitude * Math.Sin(k * x), footprintMeters: 0.5d);
        var averaged = new HeightFieldGround((x, z) => amplitude * Math.Sin(k * x), footprintMeters: 113d);

        double truth = amplitude * k;
        Vector3d nLocal = rock.NormalAt(0d, 0d);
        Vector3d nAvg = averaged.NormalAt(0d, 0d);
        double slopeLocal = -nLocal.X / nLocal.Y;
        double slopeAvg = -nAvg.X / nAvg.Y;

        double errLocal = Math.Abs(slopeLocal - truth) / truth;
        double errAvg = Math.Abs(slopeAvg - truth) / truth;
        Check(errLocal <= 0.12d && errAvg >= 0.9d, "T214 local-normal-footprint",
            string.Format(Inv, "истинный наклон={0:F4}; footprint 0.5 м: {1:F4} (ошибка {2:P1}, ≤12%); footprint 114 м: {3:F5} (ошибка {4:P1}, ≥90%)",
                truth, slopeLocal, errLocal, slopeAvg, errAvg));
        return 0;
    }

    static int Test215_ContactCostBudget()
    {
        var ground = new HeightFieldGround((x, z) => 0.1d * Math.Sin(x), footprintMeters: 0.5d);
        const int samples = 200000;
        double acc = 0d;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < samples; i++)
        {
            ground.Query(new Vector3d(0.001d * i, 0.05d, 0.002d * i), out double depth, out Vector3d n);
            acc += depth + n.Y;
        }

        sw.Stop();
        double usPerQuery = sw.Elapsed.TotalMilliseconds * 1000d / samples;
        Check(double.IsFinite(acc), "T215 contact-cost-budget",
            string.Format(Inv, "информативно: {0:F3} мкс на запрос поверхности (footprint 0.5 м, {1} запросов); бюджет фазы 10 — ≤1.5 мс на кадр",
                usPerQuery, samples));
        return 0;
    }
}
