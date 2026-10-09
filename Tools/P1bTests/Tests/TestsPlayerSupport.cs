using System;
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Simulation.Player;

// Ходьба игрока по опорам (вертикальная опора, шаг, наклон, потолок, углы,
// падение, движущаяся платформа). Все тесты — на чистом PlayerSurfaceController
// с синтетическими IPlayerSupport, без Unity.
internal static partial class P1bTests
{
    private static readonly Vector3d LocalUp = new Vector3d(0d, 0d, 1d);

    // ─── синтетические опоры ────────────────────────────────────────────────

    private sealed class Box
    {
        public Vector3d Min;
        public Vector3d Max;
        public Vector3d Normal = new Vector3d(0d, 0d, 1d);
        public bool IsCeiling;
        public bool IsFloor = true;

        public static Box Floor(double minX, double maxX, double minY, double maxY, double topZ)
        {
            return new Box
            {
                Min = new Vector3d(minX, minY, -100d),
                Max = new Vector3d(maxX, maxY, topZ)
            };
        }

        public static Box BoxWithTop(double minX, double maxX, double minY, double maxY, double minZ, double topZ)
        {
            return new Box
            {
                Min = new Vector3d(minX, minY, minZ),
                Max = new Vector3d(maxX, maxY, topZ)
            };
        }

        public static Box Wall(double minX, double maxX, double minY, double maxY, double minZ, double maxZ)
        {
            return new Box
            {
                Min = new Vector3d(minX, minY, minZ),
                Max = new Vector3d(maxX, maxY, maxZ),
                IsFloor = false
            };
        }

        public static Box Ceiling(double minX, double maxX, double minY, double maxY, double minZ, double maxZ)
        {
            return new Box
            {
                Min = new Vector3d(minX, minY, minZ),
                Max = new Vector3d(maxX, maxY, maxZ),
                IsFloor = false,
                IsCeiling = true
            };
        }
    }

    private sealed class BoxSupport : IPlayerSupport, IMovingSupport
    {
        public readonly List<Box> Floors = new List<Box>();
        public readonly List<Box> Blockers = new List<Box>();
        public Func<int, Vector3d> VelocityProvider;

        public bool Floor(Vector3d from, double maxDrop, out double height, out Vector3d normal, out int sourceId)
        {
            height = 0d;
            normal = LocalUp;
            sourceId = -1;
            bool found = false;
            double best = double.NegativeInfinity;
            for (int i = 0; i < Floors.Count; i++)
            {
                Box b = Floors[i];
                if (!b.IsFloor)
                {
                    continue;
                }

                if (from.X < b.Min.X - 1e-9d || from.X > b.Max.X + 1e-9d
                    || from.Y < b.Min.Y - 1e-9d || from.Y > b.Max.Y + 1e-9d)
                {
                    continue;
                }

                double top = b.Max.Z;
                if (top > from.Z + 1e-9d || top < from.Z - maxDrop)
                {
                    continue;
                }

                if (top > best)
                {
                    best = top;
                    normal = b.Normal;
                    sourceId = i;
                    found = true;
                }
            }

            if (found)
            {
                height = best;
            }

            return found;
        }

        public bool Ceiling(Vector3d head, double maxRise, out double height)
        {
            height = 0d;
            bool found = false;
            double best = double.PositiveInfinity;
            for (int i = 0; i < Blockers.Count; i++)
            {
                Box b = Blockers[i];
                if (!b.IsCeiling)
                {
                    continue;
                }

                if (head.X < b.Min.X || head.X > b.Max.X || head.Y < b.Min.Y || head.Y > b.Max.Y)
                {
                    continue;
                }

                if (b.Min.Z < head.Z - 1e-9d || b.Min.Z > head.Z + maxRise)
                {
                    continue;
                }

                if (b.Min.Z < best)
                {
                    best = b.Min.Z;
                    found = true;
                }
            }

            if (found)
            {
                height = best;
            }

            return found;
        }

        public bool Sweep(Vector3d from, Vector3d to, double radius, out Vector3d normal, out double fraction)
        {
            normal = LocalUp;
            fraction = 1d;
            double bestT = double.PositiveInfinity;
            Vector3d bestN = LocalUp;
            bool hit = false;

            for (int i = 0; i < Blockers.Count; i++)
            {
                Box b = Blockers[i];
                if (!(b.Max.Z > from.Z + 1e-9d)
                    || !(b.Min.Z < from.Z + PlayerSurfaceController.CapsuleHeight - 1e-9d))
                {
                    continue;
                }

                double minX = b.Min.X - radius;
                double maxX = b.Max.X + radius;
                double minY = b.Min.Y - radius;
                double maxY = b.Max.Y + radius;
                double dx = to.X - from.X;
                double dy = to.Y - from.Y;
                double tEnter = double.NegativeInfinity;
                double tExit = double.PositiveInfinity;
                Vector3d entryNormal = LocalUp;
                bool ok = true;

                if (Math.Abs(dx) < 1e-12d)
                {
                    if (from.X < minX || from.X > maxX)
                    {
                        ok = false;
                    }
                }
                else
                {
                    double t1 = (minX - from.X) / dx;
                    double t2 = (maxX - from.X) / dx;
                    if (t1 > t2)
                    {
                        double tmp = t1;
                        t1 = t2;
                        t2 = tmp;
                    }

                    if (t1 > tEnter)
                    {
                        tEnter = t1;
                        entryNormal = new Vector3d(dx > 0d ? -1d : 1d, 0d, 0d);
                    }

                    if (t2 < tExit)
                    {
                        tExit = t2;
                    }
                }

                if (ok && Math.Abs(dy) < 1e-12d)
                {
                    if (from.Y < minY || from.Y > maxY)
                    {
                        ok = false;
                    }
                }
                else if (ok)
                {
                    double t1 = (minY - from.Y) / dy;
                    double t2 = (maxY - from.Y) / dy;
                    if (t1 > t2)
                    {
                        double tmp = t1;
                        t1 = t2;
                        t2 = tmp;
                    }

                    if (t1 > tEnter)
                    {
                        tEnter = t1;
                        entryNormal = new Vector3d(0d, dy > 0d ? -1d : 1d, 0d);
                    }

                    if (t2 < tExit)
                    {
                        tExit = t2;
                    }
                }

                if (!ok)
                {
                    continue;
                }

                double t = Math.Max(tEnter, 0d);
                if (t > tExit || t > 1d)
                {
                    continue;
                }

                if (t < bestT)
                {
                    bestT = t;
                    bestN = entryNormal;
                    hit = true;
                }
            }

            if (hit)
            {
                fraction = bestT;
                normal = bestN;
            }

            return hit;
        }

        public bool TryGetSupportVelocity(int sourceId, out Vector3d velocity)
        {
            velocity = Vector3d.Zero;
            if (VelocityProvider == null)
            {
                return false;
            }

            velocity = VelocityProvider(sourceId);
            return true;
        }
    }

    private sealed class SlopeSupport : IPlayerSupport
    {
        private readonly double tan;
        private readonly Vector3d normal;

        public SlopeSupport(double angleDegrees)
        {
            double radians = angleDegrees * (Math.PI / 180d);
            tan = Math.Tan(radians);
            normal = new Vector3d(-Math.Sin(radians), 0d, Math.Cos(radians));
        }

        public bool Floor(Vector3d from, double maxDrop, out double height, out Vector3d outNormal, out int sourceId)
        {
            height = tan * from.X;
            outNormal = normal;
            sourceId = 0;
            return height <= from.Z + 1e-9d && height >= from.Z - maxDrop;
        }

        public bool Ceiling(Vector3d head, double maxRise, out double height)
        {
            height = 0d;
            return false;
        }

        public bool Sweep(Vector3d from, Vector3d to, double radius, out Vector3d outNormal, out double fraction)
        {
            outNormal = LocalUp;
            fraction = 1d;
            return false;
        }
    }

    // Заглушка детали корабля: площадка 2×2 м, горизонталь 2 м/с, вертикаль
    // 0.5·sin(2π·t/4). Поза — на КОНЕЦ подшага (тест выставляет Time = t + dt).
    private sealed class StubPlatformSupport : IPlayerSupport, IMovingSupport
    {
        public double Time;
        public double CenterX => 2d * Time;
        public double CenterZ => 0.5d * Math.Sin((2d * Math.PI / 4d) * Time);
        public double VelocityZ => 0.5d * (2d * Math.PI / 4d) * Math.Cos((2d * Math.PI / 4d) * Time);

        public bool Floor(Vector3d from, double maxDrop, out double height, out Vector3d normal, out int sourceId)
        {
            height = CenterZ;
            normal = LocalUp;
            sourceId = 0;
            bool inside = Math.Abs(from.X - CenterX) <= 1d && Math.Abs(from.Y) <= 1d;
            return inside && height <= from.Z + 1e-9d && height >= from.Z - maxDrop;
        }

        public bool Ceiling(Vector3d head, double maxRise, out double height)
        {
            height = 0d;
            return false;
        }

        public bool Sweep(Vector3d from, Vector3d to, double radius, out Vector3d normal, out double fraction)
        {
            normal = LocalUp;
            fraction = 1d;
            return false;
        }

        public bool TryGetSupportVelocity(int sourceId, out Vector3d velocity)
        {
            velocity = new Vector3d(2d, 0d, VelocityZ);
            return true;
        }
    }

    // ─── тесты ──────────────────────────────────────────────────────────────

    static int Test310_StepUp()
    {
        var low = new BoxSupport();
        low.Floors.Add(Box.Floor(-10d, 10d, -10d, 10d, 0d));
        Box lowStep = Box.BoxWithTop(1d, 2d, -2d, 2d, -100d, 0.4d);
        low.Floors.Add(lowStep);
        low.Blockers.Add(lowStep);
        var cLow = new PlayerSurfaceController { Position = new Vector3d(0d, 0d, 0d) };
        double onStep = double.NaN;
        for (int i = 0; i < 120; i++)
        {
            cLow.Step(low, 0.02d, new Vector3d(1d, 0d, 0d));
            if (cLow.Position.X >= 1.5d && double.IsNaN(onStep))
            {
                onStep = cLow.Position.Z;
            }
        }

        var high = new BoxSupport();
        high.Floors.Add(Box.Floor(-10d, 10d, -10d, 10d, 0d));
        Box highStep = Box.BoxWithTop(1d, 2d, -2d, 2d, -100d, 0.6d);
        high.Floors.Add(highStep);
        high.Blockers.Add(highStep);
        var cHigh = new PlayerSurfaceController { Position = new Vector3d(0d, 0d, 0d) };
        for (int i = 0; i < 50; i++)
        {
            cHigh.Step(high, 0.02d, new Vector3d(1d, 0d, 0d));
        }

        bool stepOk = Math.Abs(onStep - 0.4d) <= 1e-3d;
        // 0.6 — блокировка: высота не меняется (X упирается в стену уступа на радиусе капсулы).
        bool blockOk = Math.Abs(cHigh.Position.Z) <= 1e-3d && cHigh.Blocked
            && cHigh.Position.X <= 1d - PlayerSurfaceController.CapsuleRadius + 1e-3d;
        Check(stepOk && blockOk, "T310 step-up",
            string.Format(Inv, "ступень 0.4: высота на вершине={0:F5} м (±1 мм): {1}; 0.6: x={2:F4} (упор у {3:F2}), z={4:E2}, Blocked={5}: {6}",
                onStep, stepOk, cHigh.Position.X, PlayerSurfaceController.CapsuleRadius, cHigh.Position.Z, cHigh.Blocked, blockOk));
        return 0;
    }

    static int Test311_SlopeLimit()
    {
        var gentle = new SlopeSupport(25d);
        var cGentle = new PlayerSurfaceController { Position = new Vector3d(0d, 0d, 0d) };
        Vector3d start = cGentle.Position;
        int steps = 100;
        for (int i = 0; i < steps; i++)
        {
            cGentle.Step(gentle, 0.02d, new Vector3d(1d, 0d, 0d));
        }

        double speedAlong = (cGentle.Position - start).Magnitude / (steps * 0.02d);
        bool gentleOk = speedAlong >= 0.9d;

        var steep = new SlopeSupport(35d);
        var cSteep = new PlayerSurfaceController { Position = new Vector3d(0d, 0d, 0d) };
        for (int i = 0; i < 50; i++)
        {
            cSteep.Step(steep, 0.02d, new Vector3d(1d, 0d, 0d));
        }

        bool steepOk = cSteep.Velocity.Z < 0d && cSteep.Position.Z <= 1e-9d && double.IsFinite(cSteep.Position.Z);

        Check(gentleOk && steepOk, "T311 slope-limit",
            string.Format(Inv, "25°: скорость вдоль склона={0:F3} м/с (≥0.9 от 1): {1}; 35°: v_z={2:F3} (вниз), z={3:F4} (без подъёма): {4}",
                speedAlong, gentleOk, cSteep.Velocity.Z, cSteep.Position.Z, steepOk));
        return 0;
    }

    static int Test312_CeilingBlocks()
    {
        var lowCeiling = new BoxSupport();
        lowCeiling.Floors.Add(Box.Floor(-10d, 10d, -10d, 10d, 0d));
        lowCeiling.Blockers.Add(Box.Ceiling(1d, 3d, -2d, 2d, 1.5d, 1.7d));
        var cLow = new PlayerSurfaceController { Position = new Vector3d(0d, 0d, 0d) };
        bool blockedSeen = false;
        for (int i = 0; i < 100; i++)
        {
            cLow.Step(lowCeiling, 0.02d, new Vector3d(1d, 0d, 0d));
            blockedSeen |= cLow.Blocked;
        }

        bool lowOk = blockedSeen && Math.Abs(cLow.Position.X) <= 0.56d && Math.Abs(cLow.Position.Z) <= 1e-9d;

        var highCeiling = new BoxSupport();
        highCeiling.Floors.Add(Box.Floor(-10d, 10d, -10d, 10d, 0d));
        highCeiling.Blockers.Add(Box.Ceiling(1d, 3d, -2d, 2d, 2.0d, 2.2d));
        var cHigh = new PlayerSurfaceController { Position = new Vector3d(0d, 0d, 0d) };
        for (int i = 0; i < 100; i++)
        {
            cHigh.Step(highCeiling, 0.02d, new Vector3d(1d, 0d, 0d));
        }

        bool highOk = cHigh.Position.X > 1.5d && !cHigh.Blocked;

        Check(lowOk && highOk, "T312 ceiling-blocks",
            string.Format(Inv, "потолок 1.5: Blocked={0}, x={1:F3} (≤0.56), z={2:E2}: {3}; потолок 2.0: x={4:F3} (>1.5), Blocked={5}: {6}",
                blockedSeen, cLow.Position.X, cLow.Position.Z, lowOk, cHigh.Position.X, cHigh.Blocked, highOk));
        return 0;
    }

    static int Test313_RoofLanding()
    {
        var support = new BoxSupport();
        support.Floors.Add(Box.Floor(-2d, 2d, -2d, 2d, 0d));
        support.Floors.Add(Box.Floor(-100d, 100d, -100d, 100d, -3d));
        var c = new PlayerSurfaceController { Position = new Vector3d(0d, 0d, 3d), Airborne = true };
        int landedAt = -1;
        for (int i = 0; i < 200 && landedAt < 0; i++)
        {
            c.Step(support, 0.02d, Vector3d.Zero);
            if (!c.Airborne)
            {
                landedAt = i;
            }
        }

        bool landOk = landedAt >= 0 && Math.Abs(c.Position.Z) <= 1e-3d && Math.Abs(c.Velocity.Z) <= 1e-3d;
        double zAfter = c.Position.Z;
        for (int i = 0; i < 10; i++)
        {
            c.Step(support, 0.02d, Vector3d.Zero);
        }

        bool noBounce = Math.Abs(c.Position.Z - zAfter) <= 0.01d;
        Check(landOk && noBounce, "T313 roof-landing",
            string.Format(Inv, "падение 3 м: приземление на шаге {0}, z={1:E2} м, v_z={2:E2} м/с: {3}; без отскока (Δz={4:E2}): {5}",
                landedAt, c.Position.Z, c.Velocity.Z, landOk, c.Position.Z - zAfter, noBounce));
        return 0;
    }

    static int Test314_EdgeFall()
    {
        var support = new BoxSupport();
        support.Floors.Add(Box.Floor(-2d, 2d, -2d, 2d, 0d));
        support.Floors.Add(Box.Floor(-100d, 100d, -100d, 100d, -3d));
        var c = new PlayerSurfaceController { Position = new Vector3d(0d, 0d, 0d) };
        int airborneAt = -1;
        for (int i = 0; i < 300 && airborneAt < 0; i++)
        {
            c.Step(support, 0.02d, new Vector3d(1d, 0d, 0d));
            if (c.Airborne)
            {
                airborneAt = i;
            }
        }

        bool fellOk = airborneAt >= 0 && c.Position.X > 2d - 0.05d && c.Position.X < 2d + 0.1d;
        bool landed = false;
        for (int i = 0; i < 300 && !landed; i++)
        {
            c.Step(support, 0.02d, new Vector3d(1d, 0d, 0d));
            landed = !c.Airborne;
        }

        bool groundOk = landed && Math.Abs(c.Position.Z + 3d) <= 1e-3d;
        Check(fellOk && groundOk, "T314 edge-fall",
            string.Format(Inv, "сход с края на шаге {0} (x={1:F3}), падение до z={2:F3} (рельеф −3): {3}",
                airborneAt, c.Position.X, c.Position.Z, fellOk && groundOk));
        return 0;
    }

    static int Test315_InnerCorner()
    {
        var support = new BoxSupport();
        support.Floors.Add(Box.Floor(-10d, 10d, -10d, 10d, 0d));
        support.Blockers.Add(Box.Wall(-1d, 0d, -1d, 3d, 0d, 2d));
        support.Blockers.Add(Box.Wall(-1d, 3d, -1d, 0d, 0d, 2d));
        var c = new PlayerSurfaceController { Position = new Vector3d(1d, 1d, 0d) };
        double diag = 1d / Math.Sqrt(2d);
        for (int i = 0; i < 300; i++)
        {
            c.Step(support, 0.02d, new Vector3d(-diag, -diag, 0d));
        }

        double r = PlayerSurfaceController.CapsuleRadius;
        bool cornerOk = Math.Abs(c.Position.X - r) <= 1e-3d && Math.Abs(c.Position.Y - r) <= 1e-3d;
        bool noPenetration = c.Position.X >= r - 1e-3d && c.Position.Y >= r - 1e-3d;
        Check(cornerOk && noPenetration, "T315 inner-corner",
            string.Format(Inv, "позиция=({0:F4}, {1:F4}) (ожидание ({2:F2}, {2:F2}) ±1 мм): {3}; без проникновения: {4}",
                c.Position.X, c.Position.Y, r, cornerOk, noPenetration));
        return 0;
    }

    static int Test316_Stairs()
    {
        var support = new BoxSupport();
        support.Floors.Add(Box.Floor(-10d, 0d, -2d, 2d, 0d));
        const int count = 10;
        const double rise = 0.18d;
        const double run = 0.4d;
        for (int k = 0; k < count; k++)
        {
            Box step = Box.BoxWithTop(k * run, (k + 1) * run, -2d, 2d, -100d, (k + 1) * rise);
            support.Floors.Add(step);
            support.Blockers.Add(step);
        }

        // Верхняя площадка: без неё игрок, пройдя лестницу, сходит с края и падает.
        support.Floors.Add(Box.Floor(count * run, count * run + 6d, -2d, 2d, count * rise));

        var c = new PlayerSurfaceController { Position = new Vector3d(-0.5d, 0d, 0d) };
        double dt = 0.01d;
        double expectedStep = 1d * dt;
        double minStep = double.PositiveInfinity;
        double previousX = c.Position.X;
        bool dbg = Environment.GetEnvironmentVariable("P1B_PLAYER_DEBUG") == "1";
        for (int i = 0; i < 600; i++)
        {
            c.Step(support, dt, new Vector3d(1d, 0d, 0d));
            double dx = c.Position.X - previousX;
            previousX = c.Position.X;
            if (c.Position.X > 0.2d && c.Position.X < count * run - 0.1d)
            {
                minStep = Math.Min(minStep, dx);
            }

            if (dbg && (i % 50 == 0 || c.Blocked))
            {
                Console.WriteLine("  [T316] i=" + i + " x=" + c.Position.X.ToString("F4", Inv)
                    + " z=" + c.Position.Z.ToString("F4", Inv)
                    + " dx=" + dx.ToString("E2", Inv)
                    + " blocked=" + c.Blocked + " airborne=" + c.Airborne);
            }
        }

        double expectedHeight = count * rise;
        bool heightOk = Math.Abs(c.Position.Z - expectedHeight) <= 1e-3d;
        bool noStops = minStep >= 0.95d * expectedStep;
        Check(heightOk && noStops, "T316 stairs",
            string.Format(Inv, "10 ступеней 0.18 м: высота={0:F4} м (ожидание {1:F2}): {2}; мин. шаг вдоль={3:E2} м (≥0.95 от {4:E2}): {5}",
                c.Position.Z, expectedHeight, heightOk, minStep, 0.95d * expectedStep, noStops));
        return 0;
    }

    static int Test317_MovingPlatform()
    {
        var platform = new StubPlatformSupport();
        var c = new PlayerSurfaceController { Position = new Vector3d(0d, 0d, 0d) };
        c.SetGroundSource(0); // уже стоим на платформе (sourceId 0)
        double dt = 0.01d;
        double worstX = 0d;
        double worstZ = 0d;
        for (int i = 0; i < 1000; i++)
        {
            platform.Time = (i + 1) * dt;
            c.Step(platform, dt, Vector3d.Zero);
            worstX = Math.Max(worstX, Math.Abs(c.Position.X - platform.CenterX));
            worstZ = Math.Max(worstZ, Math.Abs(c.Position.Z - platform.CenterZ));
        }

        bool xOk = worstX <= 2e-3d;
        bool zOk = worstZ <= 2e-3d;
        Check(xOk && zOk, "T317 moving-platform",
            string.Format(Inv, "10 с: max|Δx|={0:E2} м (≤2 мм), max|Δz|={1:E2} м (≤2 мм), платформа x={2:F2}, z={3:F3}",
                worstX, worstZ, platform.CenterX, platform.CenterZ));
        return 0;
    }

    static int Test318_FallNoTunneling()
    {
        // Тонкая плита на пути падения: подшаг 0.5 с (пролёт метры) не должен
        // её проскочить — вертикаль дробится, посадка проверяется до сдвига.
        var support = new BoxSupport();
        support.Floors.Add(Box.Floor(-5d, 5d, -5d, 5d, 0d));
        support.Floors.Add(Box.Floor(-50d, 50d, -50d, 50d, -50d));
        var c = new PlayerSurfaceController { Position = new Vector3d(0d, 0d, 10d), Airborne = true };
        int landedAt = -1;
        for (int i = 0; i < 20 && landedAt < 0; i++)
        {
            c.Step(support, 0.5d, Vector3d.Zero);
            if (!c.Airborne)
            {
                landedAt = i;
            }
        }

        bool ok = landedAt >= 0 && Math.Abs(c.Position.Z) <= 1e-3d;
        Check(ok, "T318 fall-tunneling",
            string.Format(Inv, "падение 10 м подшагами 0.5 с: приземление на шаге {0}, z={1:F4} м (плита 0, не −50): {2}",
                landedAt, c.Position.Z, ok));
        return 0;
    }

    static int Test319_SteepSlopeWall()
    {
        // Крутой скат у стены: ветка скольжения обязана резать горизонталь
        // свипом о стену, а не проходить сквозь неё.
        var support = new BoxSupport();
        Box steepFloor = Box.Floor(-10d, 10d, -10d, 10d, 0d);
        double radians = 35d * (Math.PI / 180d);
        steepFloor.Normal = new Vector3d(-Math.Sin(radians), 0d, Math.Cos(radians));
        support.Floors.Add(steepFloor);
        support.Blockers.Add(Box.Wall(1d, 2d, -2d, 2d, 0d, 2d));
        var c = new PlayerSurfaceController { Position = new Vector3d(0d, 0d, 0d) };
        for (int i = 0; i < 100; i++)
        {
            c.Step(support, 0.02d, new Vector3d(1d, 0d, 0d));
        }

        double limit = 1d - PlayerSurfaceController.CapsuleRadius;
        bool ok = c.Position.X <= limit + 1e-3d && c.Position.X >= limit - 1e-2d;
        Check(ok, "T319 steep-slope-wall",
            string.Format(Inv, "скат 35° у стены x=1: позиция x={0:F4} (упор у {1:F2}): {2}",
                c.Position.X, limit, ok));
        return 0;
    }

    static int Test320_JumpNotCancelled()
    {
        // Прыжок не должен отменяться на первом же подшаге: посадка в воздухе
        // проверяется только на спуске.
        var support = new BoxSupport();
        support.Floors.Add(Box.Floor(-10d, 10d, -10d, 10d, 0d));
        var c = new PlayerSurfaceController
        {
            Position = new Vector3d(0d, 0d, 0d),
            Airborne = true,
            Velocity = new Vector3d(0d, 0d, 4.5d)
        };
        double maxZ = 0d;
        int landedAt = -1;
        for (int i = 0; i < 500 && landedAt < 0; i++)
        {
            c.Step(support, 0.01d, Vector3d.Zero);
            maxZ = Math.Max(maxZ, c.Position.Z);
            if (!c.Airborne)
            {
                landedAt = i;
            }
        }

        double apex = 4.5d * 4.5d / (2d * 9.81d);
        bool ok = landedAt >= 0 && maxZ >= 0.5d && maxZ <= apex + 1e-2d && Math.Abs(c.Position.Z) <= 1e-3d;
        Check(ok, "T320 jump-not-cancelled",
            string.Format(Inv, "прыжок 4.5 м/с: апогей {0:F3} м (теория {1:F3}), посадка z={2:F3} м (шаг {3}): {4}",
                maxZ, apex, c.Position.Z, landedAt, ok));
        return 0;
    }
}
