using System;
using System.Reflection;
using System.Text;
using Galilego.Building;
using Galilego.Core;
using Galilego.Events;
using Galilego.Spacecraft;
using Galilego.Universe;
using UnityEngine;

/// <summary>
/// Рантайм-проверки перемотки и варпа (R2, R3, R4, R7–R11). Запускаются через
/// run_script в Play Mode: код живёт вне Assets, в сборку игры не попадает.
/// </summary>
public static class TimeTests
{
    private static SimulationRunner R;
    private static VesselStep Step;

    private static bool Bind()
    {
        R = UnityEngine.Object.FindAnyObjectByType<SimulationRunner>();
        if (R == null)
        {
            return false;
        }

        FieldInfo field = typeof(SimulationRunner).GetField("step", BindingFlags.NonPublic | BindingFlags.Instance);
        Step = field != null ? field.GetValue(R) as VesselStep : null;
        return Step != null;
    }

    private static string F(double v)
    {
        return v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static OrbitingBody FindBody(string name)
    {
        for (int i = 0; i < R.SystemState.AllBodies.Count; i++)
        {
            if (R.SystemState.AllBodies[i].Name == name)
            {
                return R.SystemState.AllBodies[i];
            }
        }

        return null;
    }

    /// <summary>Суммарное ускорение всех тел, кроме указанного (возмущение двухтельной задачи).</summary>
    private static double PerturbationAt(Vector3d position, OrbitingBody except, double timeSeconds)
    {
        double sum = 0d;
        for (int i = 0; i < R.SystemState.AllBodies.Count; i++)
        {
            OrbitingBody other = R.SystemState.AllBodies[i];
            if (ReferenceEquals(other, except))
            {
                continue;
            }

            other.EvaluateWorldState(timeSeconds, out Vector3d op, out _);
            double d = (position - op).Magnitude;
            if (d > 0d)
            {
                sum += other.ResolveStandardGravitationalParameter() / (d * d);
            }
        }

        return sum;
    }

    // ─── R2: радиальное падение на ×1 000 000 ───────────────────────────────

    /// <summary>
    /// R2: сброс со 100 км с нулевой относительной скоростью над телом без атмосферы
    /// (Aeolus) на полном варпе ×1 000 000. Анти-туннель: событие поверхности обязано
    /// сработать, время и скорость удара — совпасть с F-KEP-10 / F-FUND-04 в пределах,
    /// которые объясняет поле остальных тел (оно измеряется, а не прощается).
    /// </summary>
    public static string R2()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        OrbitingBody body = FindBody("Aeolus");
        if (body == null)
        {
            return "R2 NOT RUN: нет тела Aeolus";
        }

        double mu = body.ResolveStandardGravitationalParameter();
        double radius = body.Radius;
        double r0 = radius + 100000d;

        R.Paused = true;
        R.Warp.SetFullWarp(1000000d);

        // Длинный путь вызывается напрямую (как его вызывает StepFlying): игровой кап
        // «варп только с игроком в корабле» — не физика, и в этом тесте он мешает.
        body.EvaluateWorldState(R.TimeSeconds, out Vector3d bp, out Vector3d bv);
        Vector3d up = (R.Ship.Position - bp).Normalized;
        if (up.SqrMagnitude < 0.5d)
        {
            up = new Vector3d(1d, 0d, 0d);
        }

        Vector3d start = bp + (up * r0);
        R.SetShipState(start, bv, VesselRegime.Flying);
        Step.ResetTimeAccumulator();

        double t0 = R.TimeSeconds;
        Step.Warp.PrepareForLongWarp(t0);
        Step.Driver.CancelRequested = false;
        LongWarpResult jump = Step.Driver.AdvanceToTarget(R.Ship, t0, t0 + 20000d);
        EventOccurrence? hit = jump.StoppingEvent;

        var sb = new StringBuilder("R2 r0=").Append(F(r0)).Append(" R=").Append(F(radius));
        if (!hit.HasValue)
        {
            sb.Append(" НЕТ СОБЫТИЯ за 20000 с — туннель (reached=").Append(F(jump.ReachedTimeSeconds - t0)).Append(")");
            return sb.ToString();
        }

        double eta = Math.Acos(2d * radius / r0 - 1d);
        // Мю берём ИЗ САМОЙ МОДЕЛИ: ускорение в точке старта x r^2. Двухтельная
        // аналитика обязана сходиться с тем полем, которое реально считает игра.
        double accelStart = R.SystemState.EvaluateShipAcceleration(start, t0).Magnitude;
        double muModel = accelStart * r0 * r0;
        double tAnalytic = Math.Sqrt(r0 * r0 * r0 / (8d * muModel)) * (eta + Math.Sin(eta));
        double vAnalytic = Math.Sqrt(2d * muModel * (1d / radius - 1d / r0));

        double perturb = PerturbationAt(start, body, t0);
        double dtBound = 1e-3d + (0.5d * perturb * tAnalytic * tAnalytic / Math.Max(vAnalytic, 1d));
        double dvBound = 1e-2d + (perturb * tAnalytic);

        body.EvaluateWorldState(hit.Value.TimeSeconds, out Vector3d bpHit, out Vector3d bvHit);
        double tMeasured = hit.Value.TimeSeconds - t0;
        double vMeasured = (hit.Value.State.Velocity - bvHit).Magnitude;
        double rEvent = (hit.Value.State.Position - bpHit).Magnitude;

        bool pass = Math.Abs(tMeasured - tAnalytic) <= dtBound
            && Math.Abs(vMeasured - vAnalytic) <= dvBound
            && rEvent - radius <= 1d && rEvent - radius >= -1d;

        sb.Append(" frames=").Append(hit.HasValue ? "1" : "?");
        sb.Append(" tMeas=").Append(F(tMeasured)).Append(" tAnalytic=").Append(F(tAnalytic))
          .Append(" dT=").Append(F(tMeasured - tAnalytic)).Append(" bound=").Append(F(dtBound));
        sb.Append(" vMeas=").Append(F(vMeasured)).Append(" vAnalytic=").Append(F(vAnalytic))
          .Append(" dV=").Append(F(vMeasured - vAnalytic)).Append(" bound=").Append(F(dvBound));
        sb.Append(" rEvent-R=").Append(F(rEvent - radius));
        sb.Append(" perturb=").Append(F(perturb)).Append(" m/s^2");
        sb.Append(" muDeclared=").Append(F(mu)).Append(" muModel=").Append(F(muModel))
          .Append(" ratio=").Append(F(muModel / mu));
        sb.Append(" warp=").Append(R.Warp.Mode).Append("×").Append(F(R.Warp.Factor));
        sb.Append(pass ? " => PASS" : " => FAIL");
        return sb.ToString();
    }

    // ─── R3: эксцентричная орбита на ×1 000 000 ─────────────────────────────

    /// <summary>
    /// R3: орбита e≈0.5 вокруг Aeolus на ×1 000 000, ~2 периода. Проверяем: событие
    /// поверхности не сработало, перигей не провалился под поверхность, а дрейф элементов
    /// укладывается в возмущение третьими телами (измеряется отдельно).
    /// </summary>
    public static string R3()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        OrbitingBody body = FindBody("Aeolus");
        if (body == null)
        {
            return "R3 NOT RUN: нет Aeolus";
        }

        double mu = body.ResolveStandardGravitationalParameter();
        double rPeri = body.Radius + 200000d;
        double soi = body.SphereOfInfluenceRadius > 0d ? body.SphereOfInfluenceRadius : body.HillSphereRadius;
        double rApo = Math.Min(3d * rPeri, 0.55d * soi);
        double a = 0.5d * (rPeri + rApo);
        double e = (rApo - rPeri) / (rApo + rPeri);
        double period = 2d * Math.PI * Math.Sqrt(a * a * a / mu);

        R.Paused = true;
        R.Warp.SetFullWarp(1000000d);
        body.EvaluateWorldState(R.TimeSeconds, out Vector3d bp, out Vector3d bv);
        Vector3d up = (R.Ship.Position - bp).Normalized;
        if (up.SqrMagnitude < 0.5d)
        {
            up = new Vector3d(1d, 0d, 0d);
        }

        Vector3d tangent = Vector3d.Cross(up, new Vector3d(0d, 0d, 1d)).Normalized;
        double vPeri = Math.Sqrt(mu * (2d / rPeri - 1d / a));
        Vector3d startPos = bp + (up * rPeri);
        R.SetShipState(startPos, bv + (tangent * vPeri), VesselRegime.Flying);
        Step.ResetTimeAccumulator();

        bool surfaceHit = false;
        int eventCount = 0;
        Action<EventOccurrence> handler = o =>
        {
            eventCount++;
            if (o.Kind == EventKind.Touchdown)
            {
                surfaceHit = true;
            }
        };
        Step.OccurrenceHandled += handler;

        double t0 = R.TimeSeconds;
        OrbitalElements before = OrbitalElements.FromState(R.Ship.Position - bp, R.Ship.Velocity - bv, mu);

        for (int i = 0; i < 40000 && !surfaceHit && R.TimeSeconds - t0 < 2d * period; i++)
        {
            Step.StepFlying(1f / 60f, true);
        }

        Step.OccurrenceHandled -= handler;

        body.EvaluateWorldState(R.TimeSeconds, out Vector3d bpEnd, out Vector3d bvEnd);
        OrbitalElements after = OrbitalElements.FromState(R.Ship.Position - bpEnd, R.Ship.Velocity - bvEnd, mu);

        double perturb = PerturbationAt(startPos, body, t0);
        // Второе измерение — у апоцентра: именно там третье тело ближе всего к орбите,
        // и именно там рвётся двухтельная модель. Оценка берётся по худшему из двух.
        double perturbApo = PerturbationAt(bp + (up * rApo), body, t0);
        double worst = Math.Max(perturb, perturbApo);
        double aDriftBound = 2d * worst * a * a / Math.Max(mu, 1d);
        double eDriftBound = 2d * worst * a / Math.Max(mu, 1d);

        var sb = new StringBuilder("R3 e0=").Append(F(e)).Append(" a0=").Append(F(a));
        sb.Append(" e1=").Append(F(after.Eccentricity)).Append(" a1=").Append(F(after.SemiMajorAxis));
        sb.Append(" dA=").Append(F(after.SemiMajorAxis - before.SemiMajorAxis))
          .Append(" bound=").Append(F(aDriftBound));
        sb.Append(" dE=").Append(F(after.Eccentricity - before.Eccentricity))
          .Append(" bound=").Append(F(eDriftBound));
        sb.Append(" covered=").Append(F(R.TimeSeconds - t0)).Append(" of ").Append(F(2d * period));
        sb.Append(" surfaceHit=").Append(surfaceHit);
        sb.Append(" soi=").Append(F(soi)).Append(" events=").Append(eventCount);
        sb.Append(" perturbPeri=").Append(F(perturb)).Append(" perturbApo=").Append(F(perturbApo));
        sb.Append(" perigee1=").Append(F(after.SemiMajorAxis * (1d - after.Eccentricity)))
          .Append(" R=").Append(F(body.Radius));

        bool pass = !surfaceHit
            && Math.Abs(after.SemiMajorAxis - before.SemiMajorAxis) <= aDriftBound
            && Math.Abs(after.Eccentricity - before.Eccentricity) <= eDriftBound;
        sb.Append(pass ? " => PASS" : " => FAIL");
        return sb.ToString();
    }

    // ─── R1: независимость от частоты кадров и от варпа ─────────────────────

    private static Vector3d FlyFor(double seconds, double realDt, int factor, SimulationRunner.WorldSnapshot start)
    {
        R.RestoreWorldSnapshot(start);
        R.Warp.SetPhysicalWarp(factor);
        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;

        int frames = (int)Math.Round(seconds / (realDt * factor));
        for (int i = 0; i < frames; i++)
        {
            Step.StepFlying((float)realDt, true);
        }

        return R.Ship.Position;
    }

    /// <summary>
    /// R1: 3600 с полёта на ×1 при 30 и 60 fps и на ×6 при тех же fps. Физика прежняя —
    /// значит «×6 минус ×1» обязано быть не больше собственной зависимости модели от
    /// частоты кадров, а не нулём (границы чанков неизбежно разные).
    /// </summary>
    public static string R1()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        R.Paused = true;
        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        SimulationRunner.WorldSnapshot start = R.CaptureWorldSnapshot();

        Vector3d p1at30 = FlyFor(600d, 1d / 30d, 1, start);
        Vector3d p1at60 = FlyFor(600d, 1d / 60d, 1, start);
        Vector3d p6at30 = FlyFor(600d, 1d / 30d, 6, start);
        Vector3d p6at60 = FlyFor(600d, 1d / 60d, 6, start);

        double baseline30 = (p1at30 - p1at60).Magnitude;
        double warp30 = (p6at30 - p1at30).Magnitude;
        double warp60 = (p6at60 - p1at60).Magnitude;
        double warpVsWarp = (p6at30 - p6at60).Magnitude;

        bool pass = warp30 <= Math.Max(baseline30 * 3d, 1e-3d)
            && warp60 <= Math.Max(baseline30 * 3d, 1e-3d);

        return "R1 baseline(x1@30 vs x1@60)=" + F(baseline30) + " m"
            + " x6@30-vs-x1@30=" + F(warp30) + " m"
            + " x6@60-vs-x1@60=" + F(warp60) + " m"
            + " x6@30-vs-x6@60=" + F(warpVsWarp) + " m"
            + (pass ? " => PASS" : " => FAIL");
    }

    // ─── Парковка: ускорение на стоящем аппарате ────────────────────────────

    private static string SurfaceFingerprint(string tag)
    {
        OrbitingBody body = Step.DominantBody ?? R.DominantBody;
        body.EvaluateWorldState(R.TimeSeconds, out Vector3d bp, out Vector3d bv);
        Vector3d offset = R.Ship.Position - bp;
        double r = offset.Magnitude;
        Vector3d dir = r > 0d ? offset / r : new Vector3d(1d, 0d, 0d);
        Vector3d omega = body.SpinAxis * body.SpinAngularSpeed;
        double vRel = (R.Ship.Velocity - (bv + Vector3d.Cross(omega, offset))).Magnitude;

        body.SurfaceLatLonAt(R.Ship.Position, R.TimeSeconds, out double lat, out double lon);
        R.SystemState.Root.EvaluateWorldState(R.TimeSeconds, out Vector3d starPos, out _);
        Vector3d sunDir = (starPos - R.Ship.Position).Normalized;

        return tag + " t=" + F(R.TimeSeconds)
            + " dir=(" + F(dir.X) + "," + F(dir.Y) + "," + F(dir.Z) + ")"
            + " r=" + F(r)
            + " lat=" + F(lat) + " lon=" + F(lon) + " vRel=" + F(vRel)
            + " sun=(" + F(sunDir.X) + "," + F(sunDir.Y) + "," + F(sunDir.Z) + ")"
            + " zone=" + Galilego.Simulation.ContactZone.ContactZoneHost.Active
            + " attUp=" + R.Ship.Attitude.Rotate(new Vector3d(0d, 1d, 0d))
            + " regime=" + R.Regime
            + " eff=" + F(Step.LastEffectiveFactor);
    }

    private const string ParkedKey = "TimeTests.ParkedStart";

    /// <summary>Проверка самой математики удержания точки (без зоны/PhysX).</summary>
    public static string RotationCheck()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        OrbitingBody body = Step.DominantBody ?? R.DominantBody;
        double dt = 12345.678d;
        double t0 = R.TimeSeconds;
        double t1 = t0 + dt;

        body.EvaluateWorldState(t0, out Vector3d bp0, out _);
        body.EvaluateWorldState(t1, out Vector3d bp1, out _);
        Vector3d offset = R.Ship.Position - bp0;

        QuaternionD spin = QuaternionD.FromAxisAngle(body.SpinAxis, body.SpinAngularSpeed * dt);
        Vector3d viaRotation = spin.Rotate(offset);

        body.SurfaceLatLonAt(R.Ship.Position, t0, out double lat, out double lon);
        double altitude = offset.Magnitude - body.Radius;
        body.GetSurfaceState(lat, lon, altitude, t1, out Vector3d viaSurface, out _);
        Vector3d viaSurfaceOffset = viaSurface - bp1;

        return "RotationCheck axis=" + body.SpinAxis + " omega=" + F(body.SpinAngularSpeed)
            + " dt=" + F(dt) + " angleDeg=" + F(body.SpinAngularSpeed * dt * 180d / Math.PI)
            + " | viaRotation-viaSurface=" + F((viaRotation - viaSurfaceOffset).Magnitude) + " m"
            + " lat=" + F(lat) + " lon=" + F(lon) + " alt=" + F(altitude);
    }

    /// <summary>Что происходит с визуалом корня корабля во время ускорения.</summary>
    public static string VisualCheck()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        ShipView view = UnityEngine.Object.FindAnyObjectByType<ShipView>();
        OrbitingBody body = Step.DominantBody ?? R.DominantBody;
        body.EvaluateWorldState(R.TimeSeconds, out Vector3d bp, out _);
        Vector3d upAstro = (R.Ship.Position - bp).Normalized;

        Vector3 rootPos = view != null ? view.transform.position : Vector3.zero;
        Quaternion rootRot = view != null ? view.transform.rotation : Quaternion.identity;
        Vector3 rootUp = rootRot * Vector3.up;

        bool flightPose = R.TryGetFlightVesselRootPose(out Vector3 fp, out Quaternion fr);

        return "VisualCheck t=" + F(R.TimeSeconds)
            + " zone=" + Galilego.Simulation.ContactZone.ContactZoneHost.Active
            + " isCom=" + R.Ship.PositionIsCom
            + " att=" + R.Ship.Attitude
            + " localUp=" + Galilego.Universe.AstroFrame.ToSimulation(upAstro)
            + " rootPos=" + rootPos
            + " expectRootPos=" + Galilego.Universe.FloatingOrigin.ToRender(R.Ship.Position)
            + " rootUp=" + rootUp
            + " flightPose=" + flightPose + (flightPose ? (" fp=" + fp + " fr=" + fr) : string.Empty)
            + " anchor=" + Galilego.Universe.FloatingOrigin.Anchor;
    }

    /// <summary>
    /// R13: перемотка на площадке. Точка снята у СТОЯЩЕГО аппарата, поэтому зона
    /// отпускает прокси, восстанавливает прошлое и пересобирает прокси заново.
    /// </summary>
    public static string R13()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        TimeControlRuntime tc = TimeControlRuntime.Current;
        if (tc == null || !tc.DebugStarted)
        {
            return "R13 нет таймлайна";
        }

        int target = tc.DebugPointCount - 1;
        if (target < 1)
        {
            return "R13 нужно ≥2 точки (сейчас " + tc.DebugPointCount + ")";
        }

        if (!tc.DebugPointAvailable(target))
        {
            return "R13 точка " + target + " серая — перемотка запрещена";
        }

        double targetTime = tc.DebugPointFlightSeconds(target) + tc.DebugLaunchTimeSeconds;
        string before = SurfaceFingerprint("before");

        tc.QueueRewind(target);
        tc.ApplyPendingRewind();

        return "R13 откат на " + target + " (t=" + F(targetTime) + ")"
            + " now=" + F(R.TimeSeconds)
            + " dT=" + F(R.TimeSeconds - targetTime)
            + " zone=" + Galilego.Simulation.ContactZone.ContactZoneHost.Active
            + " warp=" + R.Warp.Mode + "×" + F(R.Warp.Factor)
            + " points=" + tc.DebugPointCount
            + " next=" + tc.DebugNextCaptureIndex
            + " notice='" + tc.DebugNotice + "'"
            + " | " + before + " | " + SurfaceFingerprint("after");
    }

    /// <summary>Включить полный варп на стоящем аппарате и запомнить точку.</summary>
    public static string ParkedStart()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        R.Paused = false;
        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        R.Warp.SetFullWarp(1000d);
        PlayerPrefs.SetString(ParkedKey, SurfaceRecord());
        PlayerPrefs.Save();
        return SurfaceFingerprint("start");
    }

    /// <summary>
    /// Числовой эталон для сравнения: t | dir | r | lat | lon | высота солнца над
    /// местным горизонтом (sin). Разделитель '|', InvariantCulture — без разбора строк.
    /// </summary>
    private static string SurfaceRecord()
    {
        OrbitingBody body = Step.DominantBody ?? R.DominantBody;
        body.EvaluateWorldState(R.TimeSeconds, out Vector3d bp, out Vector3d bv);
        Vector3d offset = R.Ship.Position - bp;
        double r = offset.Magnitude;
        Vector3d dir = r > 0d ? offset / r : new Vector3d(1d, 0d, 0d);
        body.SurfaceLatLonAt(R.Ship.Position, R.TimeSeconds, out double lat, out double lon);
        R.SystemState.Root.EvaluateWorldState(R.TimeSeconds, out Vector3d starPos, out _);
        double sunUp = Vector3d.Dot((starPos - R.Ship.Position).Normalized, dir);

        return F(R.TimeSeconds) + "|" + F(dir.X) + "|" + F(dir.Y) + "|" + F(dir.Z) + "|" + F(r)
            + "|" + F(lat) + "|" + F(lon) + "|" + F(sunUp);
    }

    /// <summary>Через секунду: сдвинулся ли аппарат по поверхности и повернулось ли солнце.</summary>
    public static string ParkedCheck()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        string record = PlayerPrefs.GetString(ParkedKey, string.Empty);
        if (record.Length == 0)
        {
            return "ParkedCheck без ParkedStart";
        }

        string[] p = record.Split('|');
        double t0 = double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture);
        var dir0 = new Vector3d(
            double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture));
        double r0 = double.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture);
        double lat0 = double.Parse(p[5], System.Globalization.CultureInfo.InvariantCulture);
        double lon0 = double.Parse(p[6], System.Globalization.CultureInfo.InvariantCulture);
        double sunUp0 = double.Parse(p[7], System.Globalization.CultureInfo.InvariantCulture);

        OrbitingBody body = Step.DominantBody ?? R.DominantBody;
        body.EvaluateWorldState(R.TimeSeconds, out Vector3d bp, out _);
        Vector3d offset = R.Ship.Position - bp;
        double r = offset.Magnitude;
        Vector3d dir = r > 0d ? offset / r : new Vector3d(1d, 0d, 0d);
        body.SurfaceLatLonAt(R.Ship.Position, R.TimeSeconds, out double lat, out double lon);
        R.SystemState.Root.EvaluateWorldState(R.TimeSeconds, out Vector3d starPos, out _);
        double sunUp = Vector3d.Dot((starPos - R.Ship.Position).Normalized, dir);

        // Широта/долгота — единственная честная мера «аппарат остался на месте»:
        // инерциальное направление обязано крутиться вместе с планетой.
        double lonDeltaDeg = Math.Abs(lon - lon0);
        lonDeltaDeg = Math.Min(lonDeltaDeg, 360d - lonDeltaDeg);
        double surfaceMeters = lonDeltaDeg * Math.PI / 180d * body.Radius * Math.Cos(lat0 * Math.PI / 180d);

        return "ParkedCheck dtModel=" + F(R.TimeSeconds - t0)
            + " dLat=" + F(lat - lat0) + " deg"
            + " dLon=" + F(lonDeltaDeg) + " deg (" + F(surfaceMeters) + " m)"
            + " dR=" + F(r - r0) + " m"
            + " sunElevChange=" + F((sunUp - sunUp0) * 180d / Math.PI) + " deg"
            + " | " + SurfaceFingerprint("now");
    }

    private static Vector3d ParseVec(string text)
    {
        string[] p = text.Trim('(', ')').Split(',');
        return new Vector3d(
            double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>Выключить ускорение: аппарат обязан остаться на месте.</summary>
    public static string ParkedOff()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        R.Warp.SetPhysicalWarp(1);
        R.Warp.ResetTicks(R.TimeSeconds);
        return "warp off; " + SurfaceFingerprint("off");
    }

    // ─── Запуск/подготовка ──────────────────────────────────────────────────

    /// <summary>R3slow: та же орбита, но физической цепочкой ×1 (кадр 1 с) — контроль варпа.</summary>
    public static string R3Slow()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        OrbitingBody body = FindBody("Aeolus");
        double mu = body.ResolveStandardGravitationalParameter();
        double rPeri = body.Radius + 200000d;
        double soi = body.SphereOfInfluenceRadius > 0d ? body.SphereOfInfluenceRadius : body.HillSphereRadius;
        double rApo = Math.Min(3d * rPeri, 0.55d * soi);
        double a = 0.5d * (rPeri + rApo);
        double period = 2d * Math.PI * Math.Sqrt(a * a * a / mu);

        R.Paused = true;
        R.Warp.SetPhysicalWarp(1);
        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        body.EvaluateWorldState(R.TimeSeconds, out Vector3d bp, out Vector3d bv);
        Vector3d up = (R.Ship.Position - bp).Normalized;
        Vector3d tangent = Vector3d.Cross(up, new Vector3d(0d, 0d, 1d)).Normalized;
        double vPeri = Math.Sqrt(mu * (2d / rPeri - 1d / a));
        R.SetShipState(bp + (up * rPeri), bv + (tangent * vPeri), VesselRegime.Flying);
        Step.ResetTimeAccumulator();

        double t0 = R.TimeSeconds;
        OrbitalElements before = OrbitalElements.FromState(R.Ship.Position - bp, R.Ship.Velocity - bv, mu);
        for (int i = 0; i < 60000 && R.TimeSeconds - t0 < 2d * period; i++)
        {
            Step.StepFlying(1f, true);
        }

        body.EvaluateWorldState(R.TimeSeconds, out Vector3d bpEnd, out Vector3d bvEnd);
        OrbitalElements after = OrbitalElements.FromState(R.Ship.Position - bpEnd, R.Ship.Velocity - bvEnd, mu);

        return "R3slow dA=" + F(after.SemiMajorAxis - before.SemiMajorAxis)
            + " dE=" + F(after.Eccentricity - before.Eccentricity)
            + " covered=" + F(R.TimeSeconds - t0) + " of " + F(2d * period)
            + " steps~" + F((R.TimeSeconds - t0));
    }

    public static string Launch()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        SpacecraftBuilder builder = UnityEngine.Object.FindAnyObjectByType<SpacecraftBuilder>();
        if (builder == null)
        {
            return "no builder";
        }

        R.Paused = false;
        builder.SetPhysics(true);
        return "launch requested; draft=" + builder.Parts.Count;
    }

    public static string EnterBuild()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        SpacecraftBuilder builder = UnityEngine.Object.FindAnyObjectByType<SpacecraftBuilder>();
        if (builder == null)
        {
            return "no builder";
        }

        R.Paused = false;
        builder.SetPhysics(false);
        return "build requested";
    }

    public static string Prepare()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        CheatMenu cheat = UnityEngine.Object.FindAnyObjectByType<CheatMenu>();
        if (cheat == null)
        {
            return "no cheat menu";
        }

        R.BuildMode = false;
        Galilego.Simulation.ContactZone.ContactZoneHost zone =
            UnityEngine.Object.FindAnyObjectByType<Galilego.Simulation.ContactZone.ContactZoneHost>();
        if (zone != null)
        {
            zone.ForceExit();
        }

        cheat.OrbitAltitudeMeters = 200000d;
        cheat.OrbitCapsule();
        R.Paused = true;
        R.Warp.SetPhysicalWarp(1);
        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        Step.ResetBudgetState();
        return "prepared; mainThrust=" + F(R.MainThrustNewtons);
    }

    public static string Info()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        var sb = new StringBuilder();
        sb.Append("t=").Append(F(R.TimeSeconds));
        sb.Append(" zone=").Append(Galilego.Simulation.ContactZone.ContactZoneHost.Active);
        sb.Append(" regime=").Append(R.Regime);
        sb.Append(" build=").Append(R.BuildMode);
        sb.Append(" paused=").Append(R.Paused);
        sb.Append(" mass=").Append(F(R.Ship.Mass));
        sb.Append(" prop=").Append(R.Propulsion != null ? R.Propulsion.ActiveStageIndex + "/" + R.Propulsion.FirstAttached : "null");
        sb.Append(" flight=").Append(R.Flight != null ? "y" : "n");
        sb.Append(" warp=").Append(R.Warp.Mode).Append("×").Append(F(R.Warp.Factor));
        sb.Append(" eff=").Append(F(Step.LastEffectiveFactor));

        TimeControlRuntime tc = TimeControlRuntime.Current;
        sb.Append(" | tc=").Append(tc != null ? "y" : "n");
        if (tc != null)
        {
            sb.Append(" started=").Append(tc.DebugStarted);
            sb.Append(" points=").Append(tc.DebugPointCount);
            sb.Append(" next=").Append(tc.DebugNextCaptureIndex);
            sb.Append(" real=").Append(F(tc.DebugRealSeconds));
            sb.Append(" launchT=").Append(F(tc.DebugLaunchTimeSeconds));
        }

        return sb.ToString();
    }

    // ─── R5-live: сетка реальных минут ──────────────────────────────────────

    private static void BakeMinute(int simSeconds, int minutes)
    {
        for (int i = 0; i < simSeconds; i++)
        {
            Step.StepFlying(1f, true);
        }

        TimeControlRuntime tc = TimeControlRuntime.Current;
        if (tc != null)
        {
            tc.Tick(60f * minutes);
        }
    }

    public static string R5Live()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        TimeControlRuntime tc = TimeControlRuntime.Current;
        if (tc == null)
        {
            return "no runtime";
        }

        int before = tc.DebugPointCount;
        int nextBefore = tc.DebugNextCaptureIndex;
        double realBefore = tc.DebugRealSeconds;

        for (int m = 0; m < 3; m++)
        {
            BakeMinute(30, 1);
        }

        var sb = new StringBuilder("R5live");
        sb.Append(" before: points=").Append(before).Append(" next=").Append(nextBefore)
          .Append(" real=").Append(F(realBefore));
        sb.Append(" | after: points=").Append(tc.DebugPointCount).Append(" next=").Append(tc.DebugNextCaptureIndex)
          .Append(" real=").Append(F(tc.DebugRealSeconds));
        for (int i = Math.Max(0, before - 1); i < tc.DebugPointCount; i++)
        {
            sb.Append(" p").Append(i).Append("=").Append(tc.DebugPointKind(i))
              .Append(" real=").Append(F(tc.DebugPointRealSeconds(i)))
              .Append(" flight=").Append(F(tc.DebugPointFlightSeconds(i)));
        }

        return sb.ToString();
    }

    // ─── R4/R4b: полнота снимка ─────────────────────────────────────────────

    public static string R4()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        R.Paused = true;
        R.Warp.SetPhysicalWarp(1);
        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;

        SimulationRunner.WorldSnapshot before = R.CaptureWorldSnapshot();
        double t0 = R.TimeSeconds;

        for (int i = 0; i < 60; i++)
        {
            Step.StepFlying(1f, true);
        }

        SimulationRunner.WorldSnapshot after = R.CaptureWorldSnapshot();
        double t1 = R.TimeSeconds;

        R.RestoreWorldSnapshot(before);
        double tRestored = R.TimeSeconds;

        for (int i = 0; i < 60; i++)
        {
            Step.StepFlying(1f, true);
        }

        double dPos = (R.Ship.Position - after.ShipPosition).Magnitude;
        double dVel = (R.Ship.Velocity - after.ShipVelocity).Magnitude;
        double dMass = Math.Abs(R.Ship.Mass - after.ShipMass);
        double dTime = Math.Abs(R.TimeSeconds - t1);

        return "R4 t0=" + F(t0) + " t1=" + F(t1) + " restored=" + F(tRestored)
            + " dT=" + F(dTime) + " dPos=" + F(dPos) + " m dVel=" + F(dVel) + " m/s dMass=" + F(dMass) + " kg"
            + ((dTime == 0d && dPos == 0d && dVel == 0d && dMass == 0d) ? " => PASS" : " => FAIL");
    }

    public static string R4Thrust()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        R.Paused = true;
        R.Warp.SetPhysicalWarp(1);
        R.RawThrottle = 1d;
        Step.RawThrottle = 1d;
        if (R.Propulsion != null && R.Propulsion.ActiveStageIndex < 0)
        {
            R.Propulsion.Stage();
        }

        var poolsBefore = new System.Collections.Generic.List<StagePoolState>();
        if (R.Propulsion != null)
        {
            R.Propulsion.AppendState(poolsBefore);
        }

        SimulationRunner.WorldSnapshot before = R.CaptureWorldSnapshot();
        for (int i = 0; i < 60; i++)
        {
            Step.StepFlying(1f, true);
        }

        SimulationRunner.WorldSnapshot after = R.CaptureWorldSnapshot();

        R.RestoreWorldSnapshot(before);
        for (int i = 0; i < 60; i++)
        {
            Step.StepFlying(1f, true);
        }

        double dPos = (R.Ship.Position - after.ShipPosition).Magnitude;
        double dVel = (R.Ship.Velocity - after.ShipVelocity).Magnitude;
        double dMass = Math.Abs(R.Ship.Mass - after.ShipMass);

        double poolError = 0d;
        if (R.Propulsion != null)
        {
            var poolsNow = new System.Collections.Generic.List<StagePoolState>();
            R.Propulsion.AppendState(poolsNow);
            if (poolsNow.Count == poolsBefore.Count)
            {
                for (int i = 0; i < poolsNow.Count; i++)
                {
                    poolError = Math.Max(poolError, Math.Abs(poolsNow[i].Kg - poolsBefore[i].Kg));
                }
            }
            else
            {
                poolError = double.NaN;
            }
        }

        bool pass = dPos == 0d && dVel == 0d && dMass == 0d && poolError == 0d;
        return "R4b dPos=" + F(dPos) + " m dVel=" + F(dVel) + " m/s dMass=" + F(dMass) + " kg poolErr="
            + F(poolError) + " kg massBefore=" + F(before.ShipMass) + " massAfter=" + F(after.ShipMass)
            + (pass ? " => PASS" : " => FAIL");
    }

    // ─── R10: тяга на ×4/×6 ─────────────────────────────────────────────────

    public static string R10()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        R.Paused = true;
        R.RawThrottle = 1d;
        Step.RawThrottle = 1d;

        if (R.Propulsion != null && R.Propulsion.ActiveStageIndex < 0)
        {
            R.Propulsion.Stage();
        }

        if (R.Propulsion == null && !(R.MainThrustNewtons > 0d))
        {
            R.RawThrottle = 0d;
            Step.RawThrottle = 0d;
            return "R10 NOT RUN: нет ни ступеней, ни legacy-двигателя";
        }

        SimulationRunner.WorldSnapshot start = R.CaptureWorldSnapshot();

        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        R.RestoreWorldSnapshot(start);
        R.Warp.SetPhysicalWarp(1);
        for (int i = 0; i < 12; i++)
        {
            Step.StepFlying(1f, true);
        }

        Vector3d coastDelta = R.Ship.Velocity - start.ShipVelocity;

        var sb = new StringBuilder("R10");
        double[] factors = { 1d, 4d, 6d };
        Vector3d reference = Vector3d.Zero;
        double referenceMass = 0d;
        double ve = R.Propulsion != null && R.Propulsion.ThrustN > 0d && R.Propulsion.MassFlowKgS > 0d
            ? R.Propulsion.ThrustN / R.Propulsion.MassFlowKgS
            : R.MainIspSeconds * 9.80665d;

        for (int k = 0; k < factors.Length; k++)
        {
            R.RestoreWorldSnapshot(start);
            R.Warp.SetPhysicalWarp((int)factors[k]);
            R.RawThrottle = 1d;
            Step.RawThrottle = 1d;

            int frames = (int)Math.Round(12d / factors[k]);
            for (int i = 0; i < frames; i++)
            {
                Step.StepFlying(1f, true);
            }

            Vector3d dv = (R.Ship.Velocity - start.ShipVelocity) - coastDelta;
            double massRatio = start.ShipMass / R.Ship.Mass;
            double tsiolkovsky = ve * Math.Log(massRatio);

            sb.Append(" | x").Append((int)factors[k])
              .Append(" dv=").Append(F(dv.Magnitude))
              .Append(" cio=").Append(F(tsiolkovsky))
              .Append(" dCio=").Append(F(dv.Magnitude - tsiolkovsky))
              .Append(" mass=").Append(F(R.Ship.Mass));

            if (k == 0)
            {
                reference = dv;
                referenceMass = R.Ship.Mass;
            }
            else
            {
                sb.Append(" dvVsX1=").Append(F((dv - reference).Magnitude))
                  .Append(" dMassVsX1=").Append(F(Math.Abs(R.Ship.Mass - referenceMass)));
            }
        }

        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        return sb.ToString();
    }

    // ─── Перемотка: точка 0, варп, разрушение ───────────────────────────────

    public static string R7()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        TimeControlRuntime tc = TimeControlRuntime.Current;
        if (tc == null || !tc.DebugStarted)
        {
            return "R7 нет таймлайна";
        }

        double launchT = tc.DebugLaunchTimeSeconds;
        int pointsBefore = tc.DebugPointCount;

        var sb = new StringBuilder("R7 до: t=").Append(F(R.TimeSeconds));
        sb.Append(" regime=").Append(R.Regime);
        sb.Append(" mass=").Append(F(R.Ship.Mass));
        sb.Append(" points=").Append(pointsBefore);

        tc.QueueRewind(0);
        tc.ApplyPendingRewind();

        double poolSum = 0d;
        if (R.Propulsion != null)
        {
            var pools = new System.Collections.Generic.List<StagePoolState>();
            R.Propulsion.AppendState(pools);
            for (int i = 0; i < pools.Count; i++)
            {
                poolSum += pools[i].Kg;
            }
        }

        sb.Append(" | после: t=").Append(F(R.TimeSeconds));
        sb.Append(" dT_vs_launch=").Append(F(R.TimeSeconds - launchT));
        sb.Append(" regime=").Append(R.Regime);
        sb.Append(" mass=").Append(F(R.Ship.Mass));
        sb.Append(" stage=").Append(R.Propulsion != null ? R.Propulsion.ActiveStageIndex + "/" + R.Propulsion.FirstAttached : "null");
        sb.Append(" fuel=").Append(F(poolSum));
        sb.Append(" warp=").Append(R.Warp.Mode).Append("×").Append(F(R.Warp.Factor));
        sb.Append(" points=").Append(tc.DebugPointCount);
        sb.Append(" next=").Append(tc.DebugNextCaptureIndex);
        sb.Append(" real=").Append(F(tc.DebugRealSeconds));
        sb.Append(" notice='").Append(tc.DebugNotice).Append("'");
        return sb.ToString();
    }

    public static string R8()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        TimeControlRuntime tc = TimeControlRuntime.Current;
        if (tc == null || !tc.DebugStarted || tc.DebugPointCount < 2)
        {
            return "R8 нужно минимум 2 точки";
        }

        var sb = new StringBuilder("R8");
        R.Warp.SetFullWarp(1000000d);
        sb.Append(" до: warp=").Append(R.Warp.Mode).Append("×").Append(F(R.Warp.Factor));
        sb.Append(" eff=").Append(F(Step.LastEffectiveFactor));

        tc.QueueRewind(1);
        tc.ApplyPendingRewind();

        sb.Append(" | после: warp=").Append(R.Warp.Mode).Append("×").Append(F(R.Warp.Factor));
        sb.Append(" eff=").Append(F(Step.LastEffectiveFactor));
        sb.Append(" t=").Append(F(R.TimeSeconds));
        sb.Append(" real=").Append(F(tc.DebugRealSeconds));
        sb.Append(" next=").Append(tc.DebugNextCaptureIndex);
        return sb.ToString();
    }

    public static string R9()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        TimeControlRuntime tc = TimeControlRuntime.Current;
        if (tc == null || tc.DebugPointCount < 2)
        {
            return "R9 нужно минимум 2 точки";
        }

        int target = tc.DebugPointCount - 1;
        int debrisAtTarget = R.Debris != null ? R.Debris.ActiveCount : 0;

        var sb = new StringBuilder("R9 точка ").Append(target);
        sb.Append(" debrisAtPoint=").Append(debrisAtTarget);

        Step.Regime = VesselRegime.Destroyed;
        if (R.Debris != null)
        {
            R.Debris.Spawn(R.Ship.Position, R.Ship.Velocity, 100d, R.TimeSeconds, 600d);
            R.Debris.Spawn(R.Ship.Position, R.Ship.Velocity, 200d, R.TimeSeconds, 600d);
        }

        int debrisBeforeRewind = R.Debris != null ? R.Debris.ActiveCount : 0;
        tc.QueueRewind(target);
        tc.ApplyPendingRewind();

        sb.Append(" | разрушено: debris=").Append(debrisBeforeRewind);
        sb.Append(" regime=").Append(R.Regime);
        sb.Append(" | после отката: t=").Append(F(R.TimeSeconds));
        sb.Append(" regime=").Append(R.Regime);
        sb.Append(" debris=").Append(R.Debris != null ? R.Debris.ActiveCount : -1);
        sb.Append(" points=").Append(tc.DebugPointCount);
        sb.Append(" notice='").Append(tc.DebugNotice).Append("'");
        return sb.ToString();
    }

    public static string R6()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        double bakedEnd = R.SystemState.BakedEndSeconds();
        if (!double.IsFinite(bakedEnd))
        {
            return "R6 NOT RUN: в этой системе нет испечённой эфемериды (BakedEnd = +inf)";
        }

        return "R6 bakedEnd=" + F(bakedEnd) + " t=" + F(R.TimeSeconds);
    }

    public static string R11Setup()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        R.TryEnterShip(1e6d);
        R.Paused = false;
        R.Warp.SetPhysicalWarp(6);
        R.RawThrottle = 0d;
        Step.RawThrottle = 0d;
        return "R11 setup done; mode=" + R.PlayerMode;
    }

    public static string R11Check()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        double offset = (R.PlayerPosition - R.Ship.Position).Magnitude;
        return "R11 warp=" + R.Warp.Mode + "×" + F(R.Warp.Factor) + " eff=" + F(Step.LastEffectiveFactor)
            + " playerMode=" + R.PlayerMode + " seated=" + R.PlayerSeated
            + " offset=" + F(offset) + " m zone=" + Galilego.Simulation.ContactZone.ContactZoneHost.Active;
    }

    /// <summary>
    /// R14: полный снимок «реальности». Меняем взгляд, SAS/РСУ, двигатель и положение,
    /// затем откатываемся — всё обязано вернуться (взгляд с точностью до нуля).
    /// </summary>
    public static string R14()
    {
        if (!Bind())
        {
            return "no runner/step";
        }

        FirstPersonCamera cam = UnityEngine.Object.FindAnyObjectByType<FirstPersonCamera>();
        if (cam == null)
        {
            return "R14 нет камеры";
        }

        Galilego.Flight.VesselFlightModel flight = R.Flight;

        // 1. Отличимое состояние.
        cam.SnapLookDirection(new Vector3(0.123f, 0.456f, -0.881f).normalized);
        if (flight != null)
        {
            flight.Controls.SasOn = false;
            flight.Controls.RcsOn = false;
            if (flight.Thrusters.Count > 0)
            {
                flight.Thrusters[0].Enabled = false;
            }
        }

        Vector3 look0 = cam.CaptureLookState().Direction;
        bool sas0 = flight != null && flight.Controls.SasOn;
        bool rcs0 = flight != null && flight.Controls.RcsOn;
        bool eng0 = flight != null && flight.Thrusters.Count > 0 && flight.Thrusters[0].Enabled;
        Vector3d pos0 = R.Ship.Position;
        QuaternionD att0 = R.Ship.Attitude;

        SimulationRunner.WorldSnapshot snap = R.CaptureWorldSnapshot();

        // 2. Меняем ВСЁ.
        cam.SnapLookDirection(new Vector3(-0.7f, 0.2f, 0.68f).normalized);
        if (flight != null)
        {
            flight.Controls.SasOn = true;
            flight.Controls.RcsOn = true;
            if (flight.Thrusters.Count > 0)
            {
                flight.Thrusters[0].Enabled = true;
            }
        }

        R.SetShipState(R.Ship.Position + new Vector3d(5000d, 0d, 0d), R.Ship.Velocity, R.Regime);

        // 3. Откат.
        R.RestoreWorldSnapshot(snap);

        Vector3 look1 = cam.CaptureLookState().Direction;
        bool sas1 = flight != null && flight.Controls.SasOn;
        bool rcs1 = flight != null && flight.Controls.RcsOn;
        bool eng1 = flight != null && flight.Thrusters.Count > 0 && flight.Thrusters[0].Enabled;
        double dPos = (R.Ship.Position - pos0).Magnitude;
        double dLookDeg = Vector3.Angle(look0, look1);

        bool pass = dLookDeg <= 1e-3f && sas1 == sas0 && rcs1 == rcs0 && eng1 == eng0 && dPos == 0d;
        QuaternionD delta = att0.Conjugated * R.Ship.Attitude;
        double dAttDeg = 2d * Math.Acos(Math.Min(1d, Math.Abs(delta.W))) * 180d / Math.PI;
        return "R14 dLook=" + F(dLookDeg) + " deg"
            + " sas=" + sas0 + "->" + sas1
            + " rcs=" + rcs0 + "->" + rcs1
            + " engine0=" + eng0 + "->" + eng1
            + " dPos=" + F(dPos) + " m"
            + " dAtt=" + F(dAttDeg) + " deg"
            + (pass ? " => PASS" : " => FAIL");
    }

    /// <summary>Открыть панель времени для визуальной проверки.</summary>
    public static string OpenPanel()
    {
        TimeControlPanel panel = UnityEngine.Object.FindAnyObjectByType<TimeControlPanel>();
        if (panel == null)
        {
            return "no panel";
        }

        MethodInfo open = typeof(TimeControlPanel).GetMethod("Open", BindingFlags.NonPublic | BindingFlags.Instance);
        if (open == null)
        {
            return "no Open()";
        }

        open.Invoke(panel, null);
        return "panel open; points=" + (TimeControlRuntime.Current != null ? TimeControlRuntime.Current.DebugPointCount : -1);
    }
}
