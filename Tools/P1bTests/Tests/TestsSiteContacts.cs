using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Galilego.Core;
using Galilego.Spacecraft.Solver;
using UnityEngine;

// Фаза 6 (ТЗ §7): постройки. Test240 — посадка на верхнюю грань большой постройки
// из запечённого префаба места; Test241 — удар о стену: скольжение вдоль, без
// прохода сквозь; Test242 — 100 случайных посадок без проваливания сквозь
// коллайдер. Данные — Tools/P1bTests/Data/site_1_colliders.json (экспорт
// SiteColliderExporter: выпуклые хуллы + воксельные боксы в кадре места).
internal static partial class P1bTests
{
    private const double SiteDt = 1e-3;
    private const int SiteIterations = 12;

    private static SiteColliderSet LoadSiteColliders()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "site_1_colliders.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "site_1_colliders.json");
        }

        using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
        {
            var set = new SiteColliderSet();
            foreach (JsonElement hull in doc.RootElement.GetProperty("hulls").EnumerateArray())
            {
                var points = new List<Vector3d>();
                foreach (JsonElement p in hull.GetProperty("points").EnumerateArray())
                {
                    points.Add(new Vector3d(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble()));
                }

                set.Add(new SiteHullCollider(points));
            }

            foreach (JsonElement box in doc.RootElement.GetProperty("boxes").EnumerateArray())
            {
                JsonElement c = box.GetProperty("center");
                JsonElement h = box.GetProperty("half");
                JsonElement q = box.GetProperty("rotation");
                set.Add(new SiteBoxCollider(
                    new Vector3d(c[0].GetDouble(), c[1].GetDouble(), c[2].GetDouble()),
                    new Vector3d(h[0].GetDouble(), h[1].GetDouble(), h[2].GetDouble()),
                    new QuaternionD(q[0].GetDouble(), q[1].GetDouble(), q[2].GetDouble(), q[3].GetDouble())));
            }

            set.BuildGrid(8d);
            return set;
        }
    }

    /// <summary>Верхняя поверхность постройки в столбце (x, z): скан сверху вниз.</summary>
    private static double SiteSurfaceTop(SiteColliderSet set, double x, double z)
    {
        for (double y = 300d; y > -20d; y -= 0.05d)
        {
            if (set.QueryPoint(new Vector3d(x, y, z), out double depth, out _) && depth > 0.005d)
            {
                return y + 0.05d;
            }
        }

        return double.NaN;
    }

    /// <summary>Танк + 4 ноги фазы 4 (слидеры, контакт — пятка).</summary>
    private static RigidAssemblySolver BuildSiteLander(Vector3d tankPosition, out int tank, out int[] legs)
    {
        const double tankMass = 4800d;
        const double k = 49000d;
        var solver = new RigidAssemblySolver(SiteDt, SiteIterations);
        var tankBody = new RigidBody(tankMass, new Vector3d(5e4d, 5e4d, 5e4d), tankPosition, QuaternionD.Identity);
        tank = solver.AddBody(tankBody);
        legs = new int[4];
        double[] xs = { 2.2d, -2.2d, 0d, 0d };
        double[] zs = { 0d, 0d, 2.2d, -2.2d };
        for (int i = 0; i < 4; i++)
        {
            var top = tankPosition + new Vector3d(xs[i], 0d, zs[i]);
            legs[i] = solver.AddBody(LegBody(top));
            solver.AddSliderJoint(tank, legs[i], top, new Vector3d(0d, -1d, 0d), -0.3d, 0.3d, k, 0.4d,
                damperReferenceMass: tankMass / 4d);
        }

        return solver;
    }

    private static void ApplySiteGravity(RigidAssemblySolver solver, int tank, int[] legs, double tankMass)
    {
        solver.Bodies[tank].ForceWorld = new Vector3d(0d, -9.81d * tankMass, 0d);
        for (int i = 0; i < legs.Length; i++)
        {
            solver.Bodies[legs[i]].ForceWorld = new Vector3d(0d, -9.81d * LegMass, 0d);
        }
    }

    private static Vector3d LegTip(RigidAssemblySolver solver, int leg)
    {
        RigidBody body = solver.Bodies[leg];
        return body.Position + body.Orientation.Rotate(new Vector3d(0d, -0.5d * LegLength, 0d));
    }

    // Test240: посадка на верхнюю грань большой постройки (горизонтальная плита крыши).
    static int Test240_SiteBoxRoof()
    {
        SiteColliderSet site = LoadSiteColliders();
        double roof = double.NaN;
        double roofX = 0d;
        double roofZ = 0d;
        double bestFootprint = -1d;

        // «Большая постройка» — самая большая горизонтальная плита-бокс (мировой AABB:
        // тонкая по Y, большая по XZ) высоко над землёй.
        for (int i = 0; i < site.Count; i++)
        {
            if (!(site.GetCollider(i) is SiteBoxCollider))
            {
                continue;
            }

            site.GetColliderAabb(i, out Vector3d min, out Vector3d max);
            double thickness = max.Y - min.Y;
            double area = (max.X - min.X) * (max.Z - min.Z);
            if (thickness > 3d || min.Y < 100d)
            {
                continue;
            }

            if (area > bestFootprint)
            {
                bestFootprint = area;
                roofX = 0.5d * (min.X + max.X);
                roofZ = 0.5d * (min.Z + max.Z);
                roof = max.Y;
            }
        }

        // Танк ставим так, чтобы ПЯТКИ (низ ног) были на 1.5 м выше поверхности:
        // нога свисает на LegLength ниже танка.
        var solver = BuildSiteLander(new Vector3d(roofX, roof + LegLength + 1.5d, roofZ), out int tank, out int[] legs);
        solver.Ground = new CompositeGround(
            new PlaneGround(new Vector3d(0d, 1d, 0d), 0d),
            new SiteContactGround(site));

        int steps = 4000;
        for (int step = 0; step < steps; step++)
        {
            ApplySiteGravity(solver, tank, legs, 4800d);
            solver.Step();
        }

        double worstSink = 0d;
        double worstAbove = 0d;
        bool finite = true;
        for (int i = 0; i < legs.Length; i++)
        {
            Vector3d tip = LegTip(solver, legs[i]);
            double localRoof = SiteSurfaceTop(site, tip.X, tip.Z);
            double reference = double.IsNaN(localRoof) ? roof : localRoof;
            worstSink = Math.Max(worstSink, reference - tip.Y);
            worstAbove = Math.Max(worstAbove, tip.Y - reference);
            finite &= tip.IsFinite;
        }

        double speed = solver.Bodies[tank].Velocity.Magnitude;
        bool standOk = worstSink <= 0.15d && worstAbove <= 0.6d && speed <= 0.05d && finite;
        bool contactOk = solver.LastContactCount > 0;
        Check(standOk && contactOk, "T240 site-roof",
            string.Format(Inv,
                "крыша {0:F1} м (XZ {1:F0},{2:F0}): пятки ниже поверхности ≤{3:F3} м (≤0.15), выше ≤{4:F3} (≤0.6), |v|={5:F4} м/с (≤0.05), контактов {6}: {7}",
                roof, roofX, roofZ, worstSink, worstAbove, speed, solver.LastContactCount, standOk && contactOk));
        return 0;
    }

    // Test241: удар о стену 3 м/с: скольжение вдоль, без прохода сквозь.
    static int Test241_WallSlide()
    {
        SiteColliderSet site = LoadSiteColliders();

        // Стена — самая большая вертикальная плита-бокс (мировой AABB: тонкая по Z,
        // высокая по Y).
        double wallX = 0d;
        double wallY = 0d;
        double faceZ = 0d;
        double bestFace = -1d;
        for (int i = 0; i < site.Count; i++)
        {
            if (!(site.GetCollider(i) is SiteBoxCollider))
            {
                continue;
            }

            site.GetColliderAabb(i, out Vector3d min, out Vector3d max);
            double thickness = max.Z - min.Z;
            double height = max.Y - min.Y;
            if (thickness > 3d || height < 40d)
            {
                continue;
            }

            double face = (max.X - min.X) * height;
            if (face > bestFace)
            {
                bestFace = face;
                wallX = 0.5d * (min.X + max.X);
                wallY = 0.5d * (min.Y + max.Y);
                faceZ = min.Z;
            }
        }
        var solver = BuildSiteLander(new Vector3d(wallX, wallY, faceZ - 3d), out int tank, out int[] legs);
        solver.Ground = new SiteContactGround(site);
        solver.Bodies[tank].Velocity = new Vector3d(0.5d, 0d, 3d);

        int steps = 1000;
        for (int step = 0; step < steps; step++)
        {
            solver.Step();
        }

        double worstPenetration = double.NegativeInfinity;
        bool finite = true;
        for (int i = 0; i < legs.Length; i++)
        {
            Vector3d tip = LegTip(solver, legs[i]);
            worstPenetration = Math.Max(worstPenetration, tip.Z - faceZ);
            finite &= tip.IsFinite;
        }

        double slide = solver.Bodies[tank].Position.X - wallX;
        bool noPassOk = worstPenetration <= 0.03d && finite;
        bool slideOk = slide >= 0.2d;
        Check(noPassOk && slideOk, "T241 wall-slide",
            string.Format(Inv,
                "стена Z={0:F1} ({1:F0}×{2:F0} м): проникновение пяток ≤{3:F3} м (≤0.03); сдвиг вдоль стены={4:F2} м (≥0.2): {5}",
                faceZ, Math.Sqrt(bestFace), Math.Sqrt(bestFace), worstPenetration, slide, noPassOk && slideOk));
        return 0;
    }

    // Test242: 100 случайных посадок: ни одна пятка не провалилась сквозь коллайдер.
    static int Test242_LandingSweep100()
    {
        SiteColliderSet site = LoadSiteColliders();

        // Плита крыши — та же, что в Test240 (мировой AABB): разброс посадок вокруг неё.
        double roofX = 0d;
        double roofZ = 0d;
        double roofTop = 0d;
        double roofHalfX = 0d;
        double roofHalfZ = 0d;
        double bestFootprint = -1d;
        for (int i = 0; i < site.Count; i++)
        {
            if (!(site.GetCollider(i) is SiteBoxCollider))
            {
                continue;
            }

            site.GetColliderAabb(i, out Vector3d min, out Vector3d max);
            double thickness = max.Y - min.Y;
            double area = (max.X - min.X) * (max.Z - min.Z);
            if (thickness > 3d || min.Y < 100d)
            {
                continue;
            }

            if (area > bestFootprint)
            {
                bestFootprint = area;
                roofX = 0.5d * (min.X + max.X);
                roofZ = 0.5d * (min.Z + max.Z);
                roofTop = max.Y;
                roofHalfX = 0.5d * (max.X - min.X);
                roofHalfZ = 0.5d * (max.Z - min.Z);
            }
        }

        var random = new Random(20261009);
        int tunnels = 0;
        int stoodOnRoof = 0;
        double worstPenetration = 0d;
        double worstInterior = 0d;
        bool finite = true;

        for (int drop = 0; drop < 100; drop++)
        {
            double x = roofX + ((random.NextDouble() * 2d) - 1d) * (roofHalfX - 4d);
            double z = roofZ + ((random.NextDouble() * 2d) - 1d) * (roofHalfZ - 4d);
            double localTop = SiteSurfaceTop(site, x, z);
            if (double.IsNaN(localTop))
            {
                localTop = roofTop;
            }

            double height = localTop + LegLength + 1.5d + (random.NextDouble() * 4.5d);
            double tiltX = ((random.NextDouble() * 2d) - 1d) * 0.1d;
            double tiltZ = ((random.NextDouble() * 2d) - 1d) * 0.1d;
            var orientation = QuaternionD.FromAxisAngle(new Vector3d(1d, 0d, 0d), tiltX)
                * QuaternionD.FromAxisAngle(new Vector3d(0d, 0d, 1d), tiltZ);

            var solver = BuildSiteLander(new Vector3d(x, height, z), out int tank, out int[] legs);
            solver.Bodies[tank].Orientation = orientation;
            solver.Ground = new CompositeGround(
                new PlaneGround(new Vector3d(0d, 1d, 0d), 0d),
                new SiteContactGround(site));
            solver.Bodies[tank].Velocity = new Vector3d(0d, -random.NextDouble() * 3d, 0d);

            for (int step = 0; step < 1200; step++)
            {
                ApplySiteGravity(solver, tank, legs, 4800d);
                solver.Step();
            }

            bool onRoof = solver.Bodies[tank].Position.Y > localTop + 0.5d;
            if (onRoof)
            {
                stoodOnRoof++;
            }

            for (int i = 0; i < legs.Length; i++)
            {
                Vector3d tip = LegTip(solver, legs[i]);
                finite &= tip.IsFinite;
                if (site.QueryPoint(tip, out double depth, out _) && depth > worstPenetration)
                {
                    worstPenetration = depth;
                }

                double tipTop = SiteSurfaceTop(site, tip.X, tip.Z);
                if (!double.IsNaN(tipTop) && tip.Y < tipTop - 1.5d && tip.Y > 2d)
                {
                    tunnels++;
                    worstInterior = Math.Max(worstInterior, tipTop - tip.Y);
                }
            }
        }

        bool ok = tunnels == 0 && worstPenetration <= 0.05d && finite;
        Check(ok, "T242 landing-sweep-100",
            string.Format(Inv,
                "100 посадок: провалившихся пяток={0} (0), макс. проникновение={1:F4} м (≤0.05), на крыше остались={2}, finite={3}: {4}",
                tunnels, worstPenetration, stoodOnRoof, finite, ok));
        return 0;
    }
}
