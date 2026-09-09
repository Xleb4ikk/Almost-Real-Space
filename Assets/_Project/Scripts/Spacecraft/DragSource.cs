using System;
using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Физический контракт сопротивления (B1), зафиксирован до реализации:
    ///   ρ(h)   = ρ0·exp(−h/H) из AtmosphereProfile тела; h&lt;0 → ρ0 (тотальная
    ///            функция: пробные RK-подстадии и середины бисекции бывают где
    ///            угодно, исключений нет); h ≥ Top → 0 ЖЁСТКИМ срезом — та же
    ///            граница, что у Entry/Exit-детекторов (режим и сила согласованы;
    ///            разрыв плотности на Top осознанный, KSP-style, не баг).
    ///   v_atm  = bodyV + ω×r — жёсткое со-вращение атмосферы с телом (ветра нет,
    ///            нагрева нет: B1 — только сила).
    ///   v_rel  = v_ship − v_atm. Ключевая дисциплина (аналогия A4 со скоростью
    ///            относительно поверхности): мировая скорость корабля в формулу
    ///            НЕ входит. Со-вращающийся с атмосферой корабль (v_rel=0) не
    ///            чувствует drag при любой орбитальной скорости.
    ///   F      = −½·ρ·Cd·A·|v_rel|·v_rel — возвращается как Force (от массы не
    ///            зависит; деление — один раз в SpacecraftPhysics). MassFlow = 0.
    ///   Cd, A  — конфигурация аппарата (поля источника), не тела.
    /// События Entry/Exit не трогаем: они — флаги режима, drag — непрерывная
    /// сила. Исключение дальнего варпа внутри атмосферы — автоматически через
    /// проверку вкладов CanEnterLongWarp (ненулевой Force запрещает вход).
    /// Pure, как остальные источники.
    /// </summary>
    public sealed class DragSource : IDynamicsSource
    {
        private readonly OrbitingBody body;

        /// <summary>Коэффициент сопротивления (безразмерный).</summary>
        public double DragCoefficient = 1d;

        /// <summary>Характерная площадь (м²).</summary>
        public double ReferenceAreaM2 = 10d;

        public DragSource(OrbitingBody body)
        {
            this.body = body ?? throw new ArgumentNullException(nameof(body));
        }

        public DynamicsContribution Evaluate(Vector3d position, Vector3d velocity, double mass, double timeSeconds)
        {
            AtmosphereProfile atmosphere = body.Atmosphere;
            if (atmosphere == null || DragCoefficient <= 0d || ReferenceAreaM2 <= 0d
                || atmosphere.ScaleHeightMeters <= 0d || atmosphere.SeaLevelDensityKgPerCubicMeter <= 0d)
            {
                return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 0d, Vector3d.Zero);
            }

            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out Vector3d bodyVelocity);
            Vector3d offset = position - bodyPosition;
            double altitude = offset.Magnitude - body.Radius;
            if (altitude >= atmosphere.TopAltitudeMeters)
            {
                return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 0d, Vector3d.Zero);
            }

            if (altitude < 0d)
            {
                altitude = 0d;
            }

            double density = atmosphere.SeaLevelDensityKgPerCubicMeter * Math.Exp(-altitude / atmosphere.ScaleHeightMeters);
            Vector3d spinAxis = body.SpinAxis;
            double spinRate = body.SpinAngularSpeed;
            Vector3d atmosphereVelocity = bodyVelocity + Vector3d.Cross(spinAxis * spinRate, offset);
            Vector3d relativeVelocity = velocity - atmosphereVelocity;
            double relativeSpeed = relativeVelocity.Magnitude;
            if (relativeSpeed <= 0d || density <= 0d)
            {
                return new DynamicsContribution(Vector3d.Zero, Vector3d.Zero, 0d, Vector3d.Zero);
            }

            double dynamicPressure = 0.5d * density * relativeSpeed * relativeSpeed;
            Vector3d force = relativeVelocity * (-dynamicPressure * DragCoefficient * ReferenceAreaM2 / relativeSpeed);
            return new DynamicsContribution(Vector3d.Zero, force, 0d, Vector3d.Zero);
        }
    }
}
