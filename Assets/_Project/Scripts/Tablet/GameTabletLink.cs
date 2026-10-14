using System.Collections.Generic;
using Galilego.Building;
using Galilego.Core;
using Galilego.Universe;
using UnityEngine;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Tablet
{
    /// <summary>
    /// Мост «симуляция Almost Real Space ↔ планшет»: наследник TabletLink, который
    /// игра вешает на объект планшета (ShipTablet.Link). В Fill кладётся
    /// телеметрия из SimulationRunner, команды исполняются тем, что уже
    /// существует в игре:
    ///  • система отсчёта скорости (поверхность/орбита) и опорное тело — реально
    ///    меняют показания (тело по умолчанию — доминантное, из SOI);
    ///  • САС/РСУ — принимаются и показываются, но исполнителя у них в игре пока
    ///    нет (у корабля нет модели вращения: AttitudePhysics/RcsBlock не
    ///    подключены к шагу полёта) — при включении на экран добавляется
    ///    примечание, чтобы состояние не выглядело рабочим.
    ///
    /// ΔV не пересчитывается: берётся та же панель, что рисует BuilderUI
    /// (DeltaVPanel.Live ?? Plan) и копируется DvTabletAdapter — как и задумано
    /// в README дизайна.
    ///
    /// Навбол: настоящей ориентации корабля в игре нет, поэтому Attitude —
    /// направление взгляда пилота (камеры) в местном горизонтном кадре
    /// (X=восток, Y=вверх, Z=север), а маркеры — реальные направления скорости
    /// в том же кадре. Появится угловое состояние корабля — меняется только
    /// FillAttitude.
    /// </summary>
    public sealed class GameTabletLink : TabletLink
    {
        [Tooltip("SimulationRunner сцены. Пусто — найдётся автоматически.")]
        public SimulationRunner Runner;

        [Tooltip("BuilderUI (источник панели ΔV). Пусто — найдётся автоматически.")]
        public BuilderUI BuilderUi;

        private static readonly double G0 = DvEnvironment.G0;

        // Состояние команд (показывается на экране).
        private bool sasEnabled;
        private SasMode sasMode = SasMode.Stability;
        private bool rcsEnabled;
        private SpeedFrame frame = SpeedFrame.Surface;
        private int referenceBodyIndex = -1;      // -1 — авто: доминантное тело

        private string[] bodyNames = { "—" };
        private int bodyCountSeen = -1;

        // Измерение собственного ускорения между вызовами Fill.
        private Vector3d lastVelocity;
        private double lastVelocityTime = double.NaN;
        private double properAcceleration;
        private bool hasProperAcceleration;

        public override void SetSasEnabled(bool on)
        {
            sasEnabled = on;
        }

        public override void SetSasMode(SasMode mode)
        {
            sasMode = mode;
        }

        public override void SetRcsEnabled(bool on)
        {
            rcsEnabled = on;
        }

        public override void SetSpeedFrame(SpeedFrame f)
        {
            frame = f;
        }

        public override void SetReferenceBody(int index)
        {
            referenceBodyIndex = index;
        }

        public override void Fill(TabletTelemetry t)
        {
            EnsureRefs();

            Ship ship = Runner != null ? Runner.Ship : null;
            if (ship == null || Runner.SystemState == null || bodyCountSeen != Runner.SystemState.AllBodies.Count)
            {
                EnsureBodies();
            }

            t.ReferenceBodies = bodyNames;
            t.Frame = frame;
            t.SasEnabled = sasEnabled;
            t.SasMode = sasMode;
            t.RcsEnabled = rcsEnabled;

            OrbitingBody body = ResolveBody(t);
            if (ship == null || body == null)
            {
                t.ReferenceBodyIndex = 0;
                return;
            }

            double time = Runner.TimeSeconds;
            body.EvaluateWorldState(time, out Vector3d bodyPos, out Vector3d bodyVel);
            Vector3d r = ship.Position - bodyPos;
            Vector3d vInertial = ship.Velocity - bodyVel;
            double radius = r.Magnitude;
            Vector3d up = radius > 1e-6d ? r / radius : new Vector3d(0d, 0d, 1d);

            // Базис местного горизонта: X=восток, Y=вверх, Z=север.
            HorizonBasis(body, up, out Vector3d east, out Vector3d north);

            // Скорость выбранной системы отсчёта: поверхность — минус вращение
            // тела, орбита — относительно центра тела (инерциальная).
            Vector3d omega = body.SpinAxis.Normalized * body.SpinAngularSpeed;
            Vector3d vSurface = vInertial - Vector3d.Cross(omega, r);
            Vector3d v = frame == SpeedFrame.Surface ? vSurface : vInertial;

            t.SpeedMs = v.Magnitude;
            t.VerticalSpeedMs = Vector3d.Dot(v, up);

            // Гравитация — суммарная от всей системы, как в физике полёта.
            Vector3d gravity = Runner.SystemState.EvaluateShipAcceleration(ship.Position, time);
            t.GravityG = gravity.Magnitude / G0;

            UpdateProperAcceleration(ship, gravity, time);
            t.AccelerationMs2 = hasProperAcceleration ? properAcceleration : 0d;
            t.GForce = hasProperAcceleration ? properAcceleration / G0 : 0d;

            // Высоты.
            double sea = radius - body.Radius;
            double surface = sea;
            if (body.Terrain != null)
            {
                body.SurfaceLatLonAt(ship.Position, time, out double latDeg, out double lonDeg);
                surface -= body.Terrain.GetHeightMeters(
                    body, latDeg * (System.Math.PI / 180d), lonDeg * (System.Math.PI / 180d));
            }

            t.AltitudeSeaLevelM = sea;
            t.AltitudeSurfaceM = surface;
            t.PressurePa = PressureAt(body, sea);

            FillAttitude(t, east, up, north);
            FillMarkers(t, v, up, east, north);
            FillOrbit(t, body, r, vInertial);

            // Масса / тяга / TWR.
            t.CurrentMassKg = ship.Mass;
            double throttle = Runner.Warp != null ? Runner.Warp.CurrentControlSnapshot.Throttle : 0d;
            double thrust = 0d;
            if (Runner.Propulsion != null)
            {
                thrust = Runner.Propulsion.ThrustN * throttle;
            }
            else if (Runner.MainThrustNewtons > 0d)
            {
                thrust = Runner.MainThrustNewtons * throttle;
            }

            t.ThrustN = thrust;
            double gSurface = body.Radius > 0d
                ? body.ResolveStandardGravitationalParameter() / (body.Radius * body.Radius)
                : G0;
            t.Twr = ship.Mass > 0d ? thrust / (ship.Mass * gSurface) : 0d;

            FillStages(t, ship);
            AppendCommandNote(t);
        }

        private void EnsureRefs()
        {
            if (Runner == null)
            {
                Runner = FindAnyObjectByType<SimulationRunner>();
            }

            if (BuilderUi == null)
            {
                BuilderUi = FindAnyObjectByType<BuilderUI>();
            }
        }

        /// <summary>Имена тел системы для кнопки опорного тела (кешируются).</summary>
        private void EnsureBodies()
        {
            var all = Runner != null && Runner.SystemState != null ? Runner.SystemState.AllBodies : null;
            if (all == null || all.Count == 0)
            {
                bodyNames = new[] { "—" };
                bodyCountSeen = -1;
                return;
            }

            var names = new string[all.Count];
            for (int i = 0; i < all.Count; i++)
            {
                names[i] = (all[i].Name ?? "?").ToUpperInvariant();
            }

            bodyNames = names;
            bodyCountSeen = all.Count;
        }

        /// <summary>
        /// Опорное тело: выбор игрока или доминантное (SOI). Возвращает индекс в
        /// ReferenceBodyIndex — именно то, что подсветит кнопку на планшете.
        /// </summary>
        private OrbitingBody ResolveBody(TabletTelemetry t)
        {
            var all = Runner != null && Runner.SystemState != null ? Runner.SystemState.AllBodies : null;
            if (all == null || all.Count == 0)
            {
                t.ReferenceBodies = new[] { "—" };
                return null;
            }

            int index = referenceBodyIndex;
            if (index < 0 || index >= all.Count)
            {
                index = 0;
                OrbitingBody dominant = Runner.DominantBody;
                for (int i = 0; i < all.Count; i++)
                {
                    if (ReferenceEquals(all[i], dominant))
                    {
                        index = i;
                    }
                }
            }

            t.ReferenceBodies = bodyNames;
            t.ReferenceBodyIndex = index;
            return all[index];
        }

        private static void HorizonBasis(OrbitingBody body, Vector3d up, out Vector3d east, out Vector3d north)
        {
            Vector3d axis = body.SpinAxis.Normalized;
            north = axis - (up * Vector3d.Dot(axis, up));
            if (north.SqrMagnitude < 1e-12d)
            {
                north = Vector3d.Cross(up, new Vector3d(1d, 0d, 0d));
                if (north.SqrMagnitude < 1e-12d)
                {
                    north = Vector3d.Cross(up, new Vector3d(0d, 1d, 0d));
                }
            }

            north = north.Normalized;
            east = Vector3d.Cross(up, north).Normalized;
        }

        /// <summary>Вектор astro-кадра в горизонтный кадр планшета (восток, вверх, север).</summary>
        private static Vector3 ToHorizon(Vector3d d, Vector3d east, Vector3d up, Vector3d north)
        {
            return new Vector3(
                (float)Vector3d.Dot(d, east),
                (float)Vector3d.Dot(d, up),
                (float)Vector3d.Dot(d, north));
        }

        /// <summary>
        /// Ориентация для навбола. Пока у корабля нет собственного углового
        /// состояния — это направление взгляда пилота: маркеры скорости и
        /// картушка остаются в одном (горизонтном) кадре и согласованы.
        /// </summary>
        private void FillAttitude(TabletTelemetry t, Vector3d east, Vector3d up, Vector3d north)
        {
            t.MarkersValid = false;
            Camera cam = Camera.main;
            if (cam == null)
            {
                t.Attitude = Quaternion.identity;
                return;
            }

            Vector3d look = AstroFrame.ToAstro(cam.transform.forward);
            Vector3d camUp = AstroFrame.ToAstro(cam.transform.up);
            Vector3 f = ToHorizon(look.Normalized, east, up, north);
            Vector3 u = ToHorizon(camUp.Normalized, east, up, north);
            if (f.sqrMagnitude < 1e-6f)
            {
                t.Attitude = Quaternion.identity;
                return;
            }

            f.Normalize();
            u -= Vector3.Dot(u, f) * f;
            if (u.sqrMagnitude < 1e-6f)
            {
                u = Vector3.Cross(f, Vector3.right);
                if (u.sqrMagnitude < 1e-6f)
                {
                    u = Vector3.Cross(f, Vector3.up);
                }
            }

            t.Attitude = Quaternion.LookRotation(f, u.normalized);
            t.MarkersValid = true;
        }

        /// <summary>
        /// Маркеры навбола: прогрейд/ретрогрейд — по вектору скорости выбранной
        /// системы отсчёта, нормаль — по моменту орбиты, радиаль — радиус
        /// в плоскости орбиты. Вырожденные случаи (скорость у нуля, радиальный
        /// полёт) прячут маркеры, а не показывают шум.
        /// </summary>
        private void FillMarkers(TabletTelemetry t, Vector3d v, Vector3d up, Vector3d east, Vector3d north)
        {
            bool ok = false;
            if (v.Magnitude > 0.5d)
            {
                Vector3d prograde = v.Normalized;
                Vector3d normal = Vector3d.Cross(up, prograde);
                if (normal.SqrMagnitude > 1e-10d)
                {
                    normal = normal.Normalized;
                    Vector3d radialOut = Vector3d.Cross(prograde, normal).Normalized;
                    t.ProgradeDir = ToHorizon(prograde, east, up, north);
                    t.NormalDir = ToHorizon(normal, east, up, north);
                    t.RadialOutDir = ToHorizon(radialOut, east, up, north);
                    ok = true;
                }
            }

            t.MarkersValid = t.MarkersValid && ok;
        }

        private static void FillOrbit(TabletTelemetry t, OrbitingBody body, Vector3d r, Vector3d vRel)
        {
            double mu = body.ResolveStandardGravitationalParameter();
            OrbitalElements el = OrbitalElements.FromState(r, vRel, mu);
            var orbit = new TabletOrbit();
            if (el.IsValid)
            {
                orbit.Valid = true;
                orbit.Eccentricity = el.Eccentricity;
                orbit.InclinationDeg = el.InclinationDegrees;
                bool bound = el.IsBound && el.Eccentricity < 1d;
                orbit.HasApoapsis = bound;
                orbit.PeAltitudeM = el.PeriapsisDistance - body.Radius;
                orbit.PeSpeedMs = VisViva(mu, el.PeriapsisDistance, el.SemiMajorAxis);
                if (bound)
                {
                    orbit.ApAltitudeM = el.ApoapsisDistance - body.Radius;
                    orbit.ApSpeedMs = VisViva(mu, el.ApoapsisDistance, el.SemiMajorAxis);
                    orbit.PeriodS = el.OrbitalPeriodSeconds;
                }
                else
                {
                    orbit.ApAltitudeM = double.NaN;
                    orbit.ApTimeS = double.NaN;
                    orbit.ApSpeedMs = double.NaN;
                }

                if (el.TryGetTimeToApsides(mu, out double toPeriapsis, out double toApoapsis))
                {
                    orbit.PeTimeS = toPeriapsis;
                    if (bound)
                    {
                        orbit.ApTimeS = toApoapsis;
                    }
                }
                else
                {
                    orbit.PeTimeS = double.NaN;
                }
            }

            t.Orbit = orbit;
        }

        /// <summary>Скорость на радиусе по «vis-viva»: v = √(μ·(2/r − 1/a)).</summary>
        private static double VisViva(double mu, double distance, double semiMajorAxis)
        {
            if (!(distance > 0d) || semiMajorAxis == 0d)
            {
                return double.NaN;
            }

            double energyTerm = (2d / distance) - (1d / semiMajorAxis);
            return energyTerm > 0d ? System.Math.Sqrt(mu * energyTerm) : double.NaN;
        }

        /// <summary>
        /// Давление: собственной модели давления у игры нет — считаем из её же
        /// высотной модели плотности атмосферы (p ∝ ρ), масштаб — земной
        /// (101325 Па при 1.225 кг/м³). Для вакуумных тел — 0.
        /// </summary>
        private static double PressureAt(OrbitingBody body, double altitudeMeters)
        {
            AtmosphereProfile atmosphere = body.Atmosphere;
            if (atmosphere == null
                || !(atmosphere.ScaleHeightMeters > 0d)
                || !(atmosphere.SeaLevelDensityKgPerCubicMeter > 0d))
            {
                return 0d;
            }

            double h = System.Math.Max(0d, altitudeMeters);
            if (h >= atmosphere.TopAltitudeMeters)
            {
                return 0d;
            }

            double density = atmosphere.SeaLevelDensityKgPerCubicMeter
                * System.Math.Exp(-h / atmosphere.ScaleHeightMeters);
            return 101325d * (density / 1.225d);
        }

        /// <summary>
        /// Собственное ускорение (перегрузка) — численная производная инерциальной
        /// скорости минус гравитация: в свободном падении ≈ 0, на thrust ≈ F/m,
        /// стоя на поверхности ≈ g. Окно измерения ≤ 1 с сим-времени: длиннее —
        /// это варп, где среднее бессмысленно (держим прежнее значение).
        /// </summary>
        private void UpdateProperAcceleration(Ship ship, Vector3d gravity, double time)
        {
            if (double.IsFinite(lastVelocityTime))
            {
                double dt = time - lastVelocityTime;
                if (dt > 1e-4d && dt <= 1d)
                {
                    Vector3d inertialAcceleration = (ship.Velocity - lastVelocity) / dt;
                    double measured = (inertialAcceleration - gravity).Magnitude;
                    properAcceleration = hasProperAcceleration
                        ? (properAcceleration * 0.6d) + (measured * 0.4d)
                        : measured;
                    hasProperAcceleration = true;
                }
            }

            lastVelocity = ship.Velocity;
            lastVelocityTime = time;
        }

        /// <summary>
        /// ΔV — из панели строителя (panel.Live ?? panel.Plan), поля копируются
        /// как есть. Масса в не-полёте — полная масса сборки (в полёте её
        /// проставит адаптер из состояния модели).
        /// </summary>
        private void FillStages(TabletTelemetry t, Ship ship)
        {
            DvResult result = null;
            if (BuilderUi != null && BuilderUi.DeltaV != null)
            {
                result = BuilderUi.DeltaV.Live ?? BuilderUi.DeltaV.Plan;
            }

            DvTabletAdapter.Apply(result, t);
            if (result != null && !result.InFlight)
            {
                t.CurrentMassKg = result.TotalMassKg;
                t.ThrustN = 0d;
                t.Twr = 0d;
            }
        }

        /// <summary>
        /// Пока САС/РСУ в игре нечему исполнять, включённое состояние обязано
        /// объясняться на экране — иначе это молчаливая ложь интерфейса.
        /// </summary>
        private void AppendCommandNote(TabletTelemetry t)
        {
            if (!sasEnabled && !rcsEnabled)
            {
                return;
            }

            var notes = new List<string>(t.Notes ?? new string[0]);
            notes.Add("САС/РСУ ещё не подключены к кораблю: у него нет модели вращения, переключатели пока только показывают состояние");
            t.Notes = notes.ToArray();
        }
    }
}
