using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Galilego.Core;
using Galilego.Spacecraft;
using UnityEngine;

// Фаза 1 (ТЗ v2, разделы 3.1 и 7): геометрия и масса деталей.
// Test200 — аналитика тензора инерции примитивов с поворотами;
// Test201 — явная масса и линейная связь инерции с массой;
// Test202 — точечная конфигурация через PartDefinition даёт прежний результат Test30;
// Test203 — независимая дискретизация тестового аппарата T1 против аналитики.
internal static partial class P1bTests
{
    private static bool NearRel(double actual, double expected, double relTol)
    {
        double scale = Math.Max(Math.Abs(expected), 1e-300);
        return Math.Abs(actual - expected) <= relTol * scale;
    }

    private static string DiagText(Matrix3x3 m)
    {
        return string.Format(Inv, "Ixx={0:G9} Iyy={1:G9} Izz={2:G9} Ixy={3:G9} Ixz={4:G9} Iyz={5:G9}",
            m.M11, m.M22, m.M33, m.M12, m.M13, m.M23);
    }

    private static Vector3 Vec3Of(JsonElement e)
    {
        return new Vector3(
            (float)e.GetProperty("x").GetDouble(),
            (float)e.GetProperty("y").GetDouble(),
            (float)e.GetProperty("z").GetDouble());
    }

    private static Vector4 Vec4Of(JsonElement e)
    {
        return new Vector4(
            (float)e.GetProperty("x").GetDouble(),
            (float)e.GetProperty("y").GetDouble(),
            (float)e.GetProperty("z").GetDouble(),
            (float)e.GetProperty("w").GetDouble());
    }

    private static List<PartDefinition> LoadVesselT1()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "test_vessel_t1.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "test_vessel_t1.json");
        }

        using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
        {
            var list = new List<PartDefinition>();
            foreach (JsonElement e in doc.RootElement.GetProperty("parts").EnumerateArray())
            {
                var def = new PartDefinition
                {
                    Name = e.GetProperty("name").GetString(),
                    MassKg = e.GetProperty("massKg").GetDouble(),
                    JointStrengthNewtons = e.GetProperty("jointStrengthNewtons").GetDouble(),
                    BreakTorqueNm = e.GetProperty("breakTorqueNm").GetDouble(),
                    Offset = Vec3Of(e.GetProperty("offset")),
                    Shape = (PartShape)Enum.Parse(typeof(PartShape), e.GetProperty("shape").GetString()),
                    Dimensions = Vec3Of(e.GetProperty("dimensions")),
                    Orientation = Vec4Of(e.GetProperty("orientation")),
                    ParentIndex = e.GetProperty("parentIndex").GetInt32(),
                    JointAnchor = Vec3Of(e.GetProperty("jointAnchor")),
                    EngineOffset = Vec3Of(e.GetProperty("engineOffset")),
                };
                list.Add(def);
            }

            return list;
        }
    }

    // Test200: аналитика главных моментов примитивов и их поворот в систему корабля.
    static int Test200_CylinderInertia()
    {
        // Цилиндр r=1, h=2, m=12: Ixx=Izz=m(3r²+h²)/12=7, Iyy=m r²/2=6.
        // Поворот на +90° вокруг Z переводит ось Y детали в −X: Ixx=6, Iyy=7, Izz=7.
        // Поворот на +90° вокруг X переводит ось Y в Z: Ixx=7, Iyy=7, Izz=6.
        // Сфера r=0.5, m=10: I=0.4·m·r²=1 во всех осях.
        // Коробка 1×2×3, m=6: Ixx=m(b²+c²)/12=6.5, Iyy=m(a²+c²)/12=5, Izz=m(a²+b²)/12=2.5.
        var cylinder = new Part(12d, Vector3d.Zero, 1e9d, PartShape.Cylinder,
            new Vector3d(1d, 2d, 0d), QuaternionD.Identity);
        var cylZ = new Part(12d, Vector3d.Zero, 1e9d, PartShape.Cylinder,
            new Vector3d(1d, 2d, 0d), QuaternionD.FromAxisAngle(new Vector3d(0d, 0d, 1d), Math.PI / 2d));
        var cylX = new Part(12d, Vector3d.Zero, 1e9d, PartShape.Cylinder,
            new Vector3d(1d, 2d, 0d), QuaternionD.FromAxisAngle(new Vector3d(1d, 0d, 0d), Math.PI / 2d));
        var sphere = new Part(10d, Vector3d.Zero, 1e9d, PartShape.Sphere,
            new Vector3d(0.5d, 0d, 0d), QuaternionD.Identity);
        var box = new Part(6d, Vector3d.Zero, 1e9d, PartShape.Box,
            new Vector3d(1d, 2d, 3d), QuaternionD.Identity);

        Matrix3x3 i0 = AssemblyStats.Compute(new List<Part> { cylinder }).Inertia;
        Matrix3x3 iz = AssemblyStats.Compute(new List<Part> { cylZ }).Inertia;
        Matrix3x3 ix = AssemblyStats.Compute(new List<Part> { cylX }).Inertia;
        Matrix3x3 isph = AssemblyStats.Compute(new List<Part> { sphere }).Inertia;
        Matrix3x3 ibox = AssemblyStats.Compute(new List<Part> { box }).Inertia;

        bool cylOk = NearRel(i0.M11, 7d, 1e-12) && NearRel(i0.M22, 6d, 1e-12) && NearRel(i0.M33, 7d, 1e-12)
            && i0.M12 == 0d && i0.M13 == 0d && i0.M23 == 0d;
        bool cylZOk = NearRel(iz.M11, 6d, 1e-12) && NearRel(iz.M22, 7d, 1e-12) && NearRel(iz.M33, 7d, 1e-12)
            && Math.Abs(iz.M12) < 1e-12 && Math.Abs(iz.M13) < 1e-12 && Math.Abs(iz.M23) < 1e-12;
        bool cylXOk = NearRel(ix.M11, 7d, 1e-12) && NearRel(ix.M22, 7d, 1e-12) && NearRel(ix.M33, 6d, 1e-12)
            && Math.Abs(ix.M12) < 1e-12 && Math.Abs(ix.M13) < 1e-12 && Math.Abs(ix.M23) < 1e-12;
        bool sphOk = NearRel(isph.M11, 1d, 1e-12) && NearRel(isph.M22, 1d, 1e-12) && NearRel(isph.M33, 1d, 1e-12);
        bool boxOk = NearRel(ibox.M11, 6.5d, 1e-12) && NearRel(ibox.M22, 5d, 1e-12) && NearRel(ibox.M33, 2.5d, 1e-12)
            && Math.Abs(ibox.M12) < 1e-12;

        Check(cylOk && cylZOk && cylXOk && sphOk && boxOk, "T200 cylinder-inertia",
            string.Format(Inv,
                "цилиндр(0°): {0} [{1}]; Z+90°: {2}; X+90°: {3}; сфера: {4}; коробка: {5}",
                cylOk, DiagText(i0), cylZOk, cylXOk, sphOk, boxOk));
        return 0;
    }

    // Test201: масса явная (не из формы), инерция линейна по массе, валидация размеров.
    static int Test201_ExplicitMassAndScaling()
    {
        List<PartDefinition> defs = LoadVesselT1();
        double dryMass = 0d;
        foreach (PartDefinition d in defs)
        {
            if (d.Name != "Топливо")
            {
                dryMass += d.MassKg;
            }
        }

        AssemblyStats full = AssemblyStats.Compute(PartAssembly.FromDefinitions(defs, 5000d));
        AssemblyStats half = AssemblyStats.Compute(PartAssembly.FromDefinitions(defs, 2500d));

        bool massOk = NearRel(full.TotalMassKg, 5000d, 1e-12) && NearRel(dryMass, 3000d, 1e-12);
        bool halfMassOk = NearRel(half.TotalMassKg, 2500d, 1e-12);
        bool comOk = Math.Abs(full.CenterOfMass.X - half.CenterOfMass.X) < 1e-12
            && Math.Abs(full.CenterOfMass.Y - half.CenterOfMass.Y) < 1e-12
            && Math.Abs(full.CenterOfMass.Z - half.CenterOfMass.Z) < 1e-12;

        // Половина массы при тех же геометриях → ровно половина каждого элемента тензора.
        bool linearOk = NearRel(half.Inertia.M11, 0.5d * full.Inertia.M11, 1e-12)
            && NearRel(half.Inertia.M22, 0.5d * full.Inertia.M22, 1e-12)
            && NearRel(half.Inertia.M33, 0.5d * full.Inertia.M33, 1e-12)
            && NearRel(half.Inertia.M12, 0.5d * full.Inertia.M12, 1e-12)
            && NearRel(half.Inertia.M23, 0.5d * full.Inertia.M23, 1e-12);

        bool rejectsBadSize = false;
        try
        {
            AssemblyStats.Compute(new List<Part>
            {
                new Part(10d, Vector3d.Zero, 1e9d, PartShape.Box, new Vector3d(1d, 0d, 1d), QuaternionD.Identity)
            });
        }
        catch (ArgumentOutOfRangeException)
        {
            rejectsBadSize = true;
        }

        Check(massOk && halfMassOk && comOk && linearOk && rejectsBadSize, "T201 explicit-mass-scaling",
            string.Format(Inv,
                "масса=5000: {0} (сухая=3000: {1}); масса=2500: {2}; COM не зависит от масштаба: {3}; I∝m: {4}; нулевой размер отвергнут: {5}",
                NearRel(full.TotalMassKg, 5000d, 1e-12), NearRel(dryMass, 3000d, 1e-12),
                halfMassOk, comOk, linearOk, rejectsBadSize));
        return 0;
    }

    // Test202: точечные детали через PartDefinition дают прежний результат Test30.
    static int Test202_PointCompat()
    {
        var defs = new List<PartDefinition>
        {
            new PartDefinition { Name = "A", MassKg = 700d, Offset = new Vector3(0f, 0f, 0f) },
            new PartDefinition { Name = "B", MassKg = 200d, Offset = new Vector3(3f, 0f, 0f) },
            new PartDefinition { Name = "C", MassKg = 100d, Offset = new Vector3(0f, 1f, 0f) },
        };
        AssemblyStats viaDefs = AssemblyStats.Compute(PartAssembly.FromDefinitions(defs, 1000d));

        var direct = new List<Part>
        {
            new Part(700d, new Vector3d(0d, 0d, 0d), 1e9d),
            new Part(200d, new Vector3d(3d, 0d, 0d), 1e9d),
            new Part(100d, new Vector3d(0d, 1d, 0d), 1e9d),
        };
        AssemblyStats viaPart = AssemblyStats.Compute(direct);

        bool expectedOk = NearRel(viaDefs.TotalMassKg, 1000d, 1e-9)
            && NearRel(viaDefs.CenterOfMass.X, 0.6d, 1e-9)
            && NearRel(viaDefs.CenterOfMass.Y, 0.1d, 1e-9)
            && NearRel(viaDefs.Inertia.M11, 90d, 1e-9)
            && NearRel(viaDefs.Inertia.M22, 1440d, 1e-9)
            && NearRel(viaDefs.Inertia.M33, 1530d, 1e-9)
            && NearRel(viaDefs.Inertia.M12, 60d, 1e-9);
        bool sameOk = Math.Abs(viaDefs.Inertia.M11 - viaPart.Inertia.M11) < 1e-12
            && Math.Abs(viaDefs.Inertia.M22 - viaPart.Inertia.M22) < 1e-12
            && Math.Abs(viaDefs.Inertia.M33 - viaPart.Inertia.M33) < 1e-12
            && Math.Abs(viaDefs.Inertia.M12 - viaPart.Inertia.M12) < 1e-12
            && Math.Abs(viaDefs.CenterOfMass.X - viaPart.CenterOfMass.X) < 1e-12;

        Check(expectedOk && sameOk, "T202 point-compat",
            string.Format(Inv,
                "через PartDefinition = эталон Test30: {0}; совпадает с прямым Part: {1}; I11={2:G12} I12={3:G12}",
                expectedOk, sameOk, viaDefs.Inertia.M11, viaDefs.Inertia.M12));
        return 0;
    }

    // Test203: независимая дискретизация аппарата T1 против аналитики.
    // Точки берутся по объёму каждой детали (полярная сетка для цилиндров, декартова для коробки),
    // тензор собирается отдельным проходом по точкам, без PerKgShipFrame.
    static int Test203_DiscretizedReference()
    {
        List<Part> parts = PartAssembly.FromDefinitions(LoadVesselT1(), 5000d);
        AssemblyStats analytic = AssemblyStats.Compute(parts);

        // Проход 1: масса и центр масс по точкам.
        double mass = 0d;
        Vector3d weighted = Vector3d.Zero;
        foreach (Part p in parts)
        {
            EnumerateVolumeSamples(p, (pos, w) =>
            {
                mass += w;
                weighted += pos * w;
            });
        }

        Vector3d center = weighted / mass;

        // Проход 2: тензор относительно центра масс.
        double ixx = 0d;
        double iyy = 0d;
        double izz = 0d;
        double ixy = 0d;
        double ixz = 0d;
        double iyz = 0d;
        foreach (Part p in parts)
        {
            EnumerateVolumeSamples(p, (pos, w) =>
            {
                Vector3d d = pos - center;
                ixx += w * ((d.Y * d.Y) + (d.Z * d.Z));
                iyy += w * ((d.X * d.X) + (d.Z * d.Z));
                izz += w * ((d.X * d.X) + (d.Y * d.Y));
                ixy -= w * d.X * d.Y;
                ixz -= w * d.X * d.Z;
                iyz -= w * d.Y * d.Z;
            });
        }

        double scale = Math.Max(Math.Max(Math.Abs(analytic.Inertia.M11), Math.Abs(analytic.Inertia.M22)),
            Math.Abs(analytic.Inertia.M33));
        double errDiag = Math.Max(Math.Max(
            Math.Abs(ixx - analytic.Inertia.M11), Math.Abs(iyy - analytic.Inertia.M22)),
            Math.Abs(izz - analytic.Inertia.M33));
        double errOff = Math.Max(Math.Max(
            Math.Abs(ixy - analytic.Inertia.M12), Math.Abs(ixz - analytic.Inertia.M13)),
            Math.Abs(iyz - analytic.Inertia.M23));
        double errCom = (center - analytic.CenterOfMass).Magnitude;

        bool massOk = NearRel(mass, 5000d, 1e-9);
        bool comOk = errCom < 1e-6;
        bool diagOk = errDiag <= 1e-3 * scale;
        bool offOk = errOff <= 1e-3 * scale;

        Check(massOk && comOk && diagOk && offOk, "T203 discretized-reference",
            string.Format(Inv,
                "масса={0:F6} ({1}); |ΔCOM|={2:E2} м ({3}); max|ΔI_диаг|/I_max={4:E2} ({5}); max|ΔI_вне|/I_max={6:E2} ({7})",
                mass, massOk, errCom, comOk, errDiag / scale, diagOk, errOff / scale, offOk));
        return 0;
    }

    // Точки объёма детали в системе корабля с весами, сумма весов = массе детали.
    private static void EnumerateVolumeSamples(Part p, Action<Vector3d, double> sink)
    {
        const int NRadial = 100;
        const int NAngular = 64;
        const int NAxial = 64;
        const int NBox = 64;

        switch (p.Shape)
        {
            case PartShape.Cylinder:
            {
                double radius = p.Dimensions.X;
                double height = p.Dimensions.Y;
                double dr = radius / NRadial;
                double dTheta = 2d * Math.PI / NAngular;
                double dy = height / NAxial;
                double total = 0d;
                for (int i = 0; i < NRadial; i++)
                {
                    double r = (i + 0.5d) * dr;
                    total += r * dr * dTheta * dy * NAngular * NAxial;
                }

                for (int i = 0; i < NRadial; i++)
                {
                    double r = (i + 0.5d) * dr;
                    for (int j = 0; j < NAngular; j++)
                    {
                        double theta = (j + 0.5d) * dTheta;
                        double x = r * Math.Cos(theta);
                        double z = r * Math.Sin(theta);
                        for (int k = 0; k < NAxial; k++)
                        {
                            double y = -0.5d * height + ((k + 0.5d) * dy);
                            double w = p.MassKg * (r * dr * dTheta * dy) / total;
                            Vector3d local = new Vector3d(x, y, z);
                            sink(p.LocalPosition + p.Orientation.Rotate(local), w);
                        }
                    }
                }

                break;
            }

            case PartShape.Box:
            {
                double a = p.Dimensions.X;
                double b = p.Dimensions.Y;
                double c = p.Dimensions.Z;
                double w = p.MassKg / (NBox * NBox * NBox);
                for (int i = 0; i < NBox; i++)
                {
                    double x = -0.5d * a + ((i + 0.5d) * a / NBox);
                    for (int j = 0; j < NBox; j++)
                    {
                        double y = -0.5d * b + ((j + 0.5d) * b / NBox);
                        for (int k = 0; k < NBox; k++)
                        {
                            double z = -0.5d * c + ((k + 0.5d) * c / NBox);
                            sink(p.LocalPosition + p.Orientation.Rotate(new Vector3d(x, y, z)), w);
                        }
                    }
                }

                break;
            }

            default:
                throw new ArgumentException("Дискретизация T203 поддерживает только Cylinder и Box.");
        }
    }
}
