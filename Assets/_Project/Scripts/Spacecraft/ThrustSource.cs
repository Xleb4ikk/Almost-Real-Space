using System;
using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Модель удельного импульса. Выделена в интерфейс, потому что constant-Isp
    /// (шаг 1, проверяется против Циолковского) и высотная интерполяция
    /// sea-level/vacuum (шаг 2, нужен для старта с поверхности через атмосферу) —
    /// это два разных допущения, и выбор между ними обязан быть виден в коде,
    /// а не зашит молча внутрь источника тяги.
    /// </summary>
    public interface IIspModel
    {
        double GetSpecificImpulseSeconds(Vector3d position, double timeSeconds);
    }

    /// <summary>
    /// Постоянный Isp. Стартовая модель: ровно то, что проверяется против
    /// аналитики Циолковского. Для первой версии химических двигателей.
    /// </summary>
    public sealed class ConstantIsp : IIspModel
    {
        public double SpecificImpulseSeconds;

        public ConstantIsp(double specificImpulseSeconds)
        {
            SpecificImpulseSeconds = specificImpulseSeconds;
        }

        public double GetSpecificImpulseSeconds(Vector3d position, double timeSeconds)
        {
            return SpecificImpulseSeconds;
        }
    }

    /// <summary>
    /// Интерполяция Isp по давлению: Isp(h) = Ivac − (Ivac − Isl)·(p(h)/p0),
    /// p(h)/p0 = exp(−h/H) из AtmosphereProfile тела. На земле (h≤0) — ровно
    /// sea-level, высоко — асимптотически vacuum, монотонно между. Тело без
    /// атмосферы или с неполными данными — чистый vacuum, без исключений.
    /// </summary>
    public sealed class AltitudeInterpolatedIsp : IIspModel
    {
        private readonly OrbitingBody body;
        private readonly double seaLevelIspSeconds;
        private readonly double vacuumIspSeconds;

        public AltitudeInterpolatedIsp(OrbitingBody body, double seaLevelIspSeconds, double vacuumIspSeconds)
        {
            this.body = body ?? throw new ArgumentNullException(nameof(body));
            this.seaLevelIspSeconds = seaLevelIspSeconds;
            this.vacuumIspSeconds = vacuumIspSeconds;
        }

        public double GetSpecificImpulseSeconds(Vector3d position, double timeSeconds)
        {
            AtmosphereProfile atmosphere = body.Atmosphere;
            if (atmosphere == null || atmosphere.ScaleHeightMeters <= 0d || body.Radius <= 0d)
            {
                return vacuumIspSeconds;
            }

            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
            double altitude = (position - bodyPosition).Magnitude - body.Radius;
            if (altitude <= 0d)
            {
                return seaLevelIspSeconds;
            }

            double pressureRatio = Math.Exp(-altitude / atmosphere.ScaleHeightMeters);
            return vacuumIspSeconds - ((vacuumIspSeconds - seaLevelIspSeconds) * pressureRatio);
        }
    }

    /// <summary>
    /// Химический/электрический двигатель как IDynamicsSource:
    /// Force = throttle·Tmax·dir, MassFlow = throttle·Tmax/(Isp·g0).
    /// Throttle приходит расписанием ThrottleAt(t) 0..1 (клампится) — тем же
    /// способом, что WarpController сэмплирует ввод по control-tick, поэтому
    /// физика при ×3 совпадает с ×1 (см. T1/T14). Направление инерциальное,
    /// нормализуется при использовании. Pure: только читает поля, мутаций нет.
    ///
    /// Сухая масса — жёсткая граница: при mass ≤ DryMassKg вклад нулевой
    /// (двигатель заглох, топливо кончилось). Момент исчерпания ловится НЕ
    /// здесь, а событием по состоянию PropellantDepletionDetector (масса
    /// пересекает порог сверху вниз — структурно то же, что Touchdown по
    /// высоте): внутри RK-подстадий интегратор вправе заглядывать чуть ниже
    /// порога, это пробные состояния, коммит делает драйвер на корне.
    /// </summary>
    public sealed class ThrustSource : IDynamicsSource
    {
        public const double StandardGravity = 9.80665d;

        public double ThrustMaxNewtons;
        public double DryMassKg;
        public Vector3d ThrustDirection = new Vector3d(1d, 0d, 0d);

        /// <summary>
        /// Направление тяги в body-frame («вдоль носа», дефолт +Z). Активно только
        /// когда задан AttitudeSnapshot: мировое направление = snapshot(bodyDir).
        /// Без снимка действует legacy-поле ThrustDirection как мировое (все старые
        /// тесты идут этим путём без изменений); оба сразу не смешиваются —
        /// приоритет явно: снимок есть → body-frame, нет → мировое поле.
        /// Снимок читается как есть (состояние на начало шага, паттерн
        /// ControlInputSnapshot): пересчёт на каждой RK-подстадии не нужен —
        /// поворот за внутренний шаг пренебрежим, доказано R3-отрицательным
        /// контролем (фиксированный мировой вектор даёт другую траекторию).
        /// </summary>
        public Vector3d BodyThrustDirection = new Vector3d(0d, 0d, 1d);

        public QuaternionD? AttitudeSnapshot;

        /// <summary>Расписание газа по времени 0..1. null = заглушено (0).</summary>
        public Func<double, double> ThrottleAt;

        public IIspModel IspModel;

        public ThrustSource(double thrustMaxNewtons, double dryMassKg, IIspModel ispModel)
        {
            ThrustMaxNewtons = thrustMaxNewtons;
            DryMassKg = dryMassKg;
            IspModel = ispModel ?? throw new ArgumentNullException(nameof(ispModel));
        }

        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            double throttle = ThrottleAt != null ? ThrottleAt(timeSeconds) : 0d;
            if (throttle < 0d)
            {
                throttle = 0d;
            }
            else if (throttle > 1d)
            {
                throttle = 1d;
            }

            if (throttle <= 0d || mass <= DryMassKg || ThrustMaxNewtons <= 0d)
            {
                return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 0d, Vector3d.Zero);
            }

            double isp = IspModel.GetSpecificImpulseSeconds(position, timeSeconds);
            if (isp <= 0d)
            {
                return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 0d, Vector3d.Zero);
            }

            double thrust = throttle * ThrustMaxNewtons;
            double massFlow = thrust / (isp * StandardGravity);
            Vector3d worldDirection = AttitudeSnapshot.HasValue
                ? AttitudeSnapshot.Value.Normalized.Rotate(BodyThrustDirection).Normalized
                : ThrustDirection.Normalized;
            return new DynamicsContribution(Vector3d.Zero, worldDirection * thrust, massFlow, Vector3d.Zero);
        }
    }
}
