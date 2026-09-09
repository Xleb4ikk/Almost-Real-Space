using System;
using System.Collections.Generic;
using UnityEngine;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Небесное тело на кеплеровой "рельсе" вокруг родителя.
    /// Прямое обобщение старого MoonRail: раньше рельса всегда была "вокруг Юпитера",
    /// теперь родитель (Parent) — произвольный. Одна и та же структура описывает
    /// и планету вокруг звезды, и спутник вокруг планеты — на любую глубину вложенности.
    /// Дерево строится связями Parent/Children; корень дерева (звезда) — тело без Parent.
    /// </summary>
    [Serializable]
    public sealed class OrbitingBody
    {
        public string Name = "Body";

        [Header("Физика")]
        public double Mass = 1d;
        public double StandardGravitationalParameter = 1d;
        public double Radius = 1d;

        /// <summary>
        /// Допустимая скорость удара о поверхность (м/с) для реакции Touchdown:
        /// ниже — посадка, выше — разрушение с разлётом (A4). Задел под
        /// пер-стыковые лимиты прочности: сейчас один порог на всё тело.
        /// </summary>
        public double CrashToleranceMps = 5d;

        /// <summary>
        /// Трение поверхности для SurfaceMotion (Кулон, плейсхолдеры до
        /// материалов/рельефа): стик пока |a_t| ≤ μ_s·|a_n|, скольжение
        /// тормозит μ_k·|a_n| против движения.
        /// </summary>
        public double SurfaceStaticFrictionMu = 0.7d;

        public double SurfaceKineticFrictionMu = 0.5d;

        /// <summary>Ось вращения (та же нормализация+фолбэк, что в GetSurfaceState).</summary>
        public Vector3d SpinAxis
        {
            get
            {
                Vector3d axis = NorthPoleDirection.Normalized;
                if (axis.SqrMagnitude < 0.5d)
                {
                    axis = new Vector3d(0d, 0d, 1d);
                }

                return axis;
            }
        }

        /// <summary>Угловая скорость вращения (рад/с), 0 если не вращается.</summary>
        public double SpinAngularSpeed
        {
            get { return RotationPeriodSeconds > 0d ? (2d * Math.PI) / RotationPeriodSeconds : 0d; }
        }

        [Header("Кеплеровы элементы (относительно Parent; для корня не используются)")]
        public double SemiMajorAxis = 1d;
        public double Eccentricity;
        public double InclinationDegrees;
        public double LongitudeOfAscendingNodeDegrees;
        public double ArgumentOfPeriapsisDegrees;
        public double MeanAnomalyAtEpochDegrees;
        public double EpochTimeSeconds;

        /// <summary>
        /// Родитель, вокруг которого вращается тело по кеплеровым элементам выше.
        /// Null означает корень дерева (звезда) — её положение задаётся напрямую
        /// через RootPosition/RootVelocity, а не элементами.
        /// </summary>
        public OrbitingBody Parent;
        public readonly List<OrbitingBody> Children = new List<OrbitingBody>();

        [Header("Только для корня дерева (звезды)")]
        // TODO: пока корень (звезда) неподвижен, а корабль интегрируется в едином мировом
        // каркасе, indirect terms не нужны. Если RootPosition/RootVelocity когда-нибудь перестанут
        // быть константами (переход на систему координат относительно текущего доминирующего тела),
        // в формулу гравитации придётся добавить indirect terms, иначе будет тихая систематическая ошибка.
        public Vector3d RootPosition = Vector3d.Zero;
        public Vector3d RootVelocity = Vector3d.Zero;

        [Header("Вращение вокруг своей оси (нужно для старта с поверхности)")]
        public double RotationPeriodSeconds; // 0 = вращение не учитывается
        public double PrimeMeridianOffsetDegrees;
        public Vector3d NorthPoleDirection = new Vector3d(0d, 0d, 1d);

        [Header("Атмосфера (null = нет атмосферы; см. AtmosphereProfile)")]
        public AtmosphereProfile Atmosphere;

        /// <summary>
        /// Модель поверхности (SphericalTerrain по умолчанию). null — потребители
        /// (детектор касания, SurfaceMotion) обязаны трактовать как сферу.
        /// </summary>
        public ITerrainModel Terrain;

        public double SphereOfInfluenceRadius;
        public double HillSphereRadius;

        public BakedEphemeris Baked;

        public void SyncMassFromGravitationalParameter()
        {
            if (StandardGravitationalParameter > 0d)
            {
                Mass = PhysicsSolver.StandardGravitationalParameterToMass(StandardGravitationalParameter);
            }
        }

        public double ResolveStandardGravitationalParameter()
        {
            return StandardGravitationalParameter > 0d
                ? StandardGravitationalParameter
                : PhysicsSolver.MassToStandardGravitationalParameter(Mass);
        }

        public double ResolveSubtreeStandardGravitationalParameter()
        {
            double total = ResolveStandardGravitationalParameter();
            for (int i = 0; i < Children.Count; i++)
            {
                total += Children[i].ResolveSubtreeStandardGravitationalParameter();
            }

            return total;
        }

        /// <summary>Пересчитывает SOI/Hill радиусы относительно Parent. Для корня — нет смысла, пропускается.</summary>
        public void UpdateInfluenceRadii()
        {
            if (Parent == null)
            {
                return;
            }

            double subMu = ResolveSubtreeStandardGravitationalParameter();
            double subMass = PhysicsSolver.StandardGravitationalParameterToMass(subMu);
            double parentOwnMass = PhysicsSolver.StandardGravitationalParameterToMass(
                Parent.ResolveStandardGravitationalParameter());

            SphereOfInfluenceRadius = OrbitalElements.CalculateSphereOfInfluenceRadius(
                SemiMajorAxis, subMass, parentOwnMass);

            HillSphereRadius = OrbitalElements.CalculateHillRadius(
                SemiMajorAxis, Eccentricity, subMass, parentOwnMass);
        }

        /// <summary>
        /// Мировое положение/скорость тела в момент времени t (астродинамическая система,
        /// т.е. без Unity-специфичного маппинга осей — это забота слоя рендера).
        /// Для корня — фиксировано. Для остальных — рекурсивно: состояние родителя
        /// плюс кеплеров оффсет относительно него. Прямое обобщение
        /// UniverseManager.EvaluateMoonState на произвольную глубину дерева.
        /// </summary>
        /// <summary>
        /// Счётчики для теста кэша (раздел 6 P1b): сколько раз реально
        /// вычислялись мировые состояния и локальные оффсеты. Не влияют на
        /// физику, только инструментация.
        /// </summary>
        public static long WorldStateEvaluationCount;
        public static long LocalOffsetEvaluationCount;

        public static void ResetEvaluationCounters()
        {
            WorldStateEvaluationCount = 0;
            LocalOffsetEvaluationCount = 0;
        }

        public void EvaluateWorldState(double timeSeconds, out Vector3d position, out Vector3d velocity)
        {
            WorldStateEvaluationCount++;
            if (Parent == null)
            {
                position = RootPosition;
                velocity = RootVelocity;
                return;
            }

            Parent.EvaluateWorldState(timeSeconds, out Vector3d parentPosition, out Vector3d parentVelocity);
            EvaluateLocalOffset(timeSeconds, out Vector3d localPosition, out Vector3d localVelocity);

            position = parentPosition + localPosition;
            velocity = parentVelocity + localVelocity;
        }

        /// <summary>
        /// Локальный кеплеров оффсет относительно родителя без рекурсии наверх.
        /// Публичная обёртка над приватным ядром — нужна кэшу StarSystem, чтобы
        /// собрать мировые позиции за один проход родители-вперёд вместо
        /// N рекурсивных EvaluateWorldState. Математика бит-в-бит та же.
        /// </summary>
        public void ComputeLocalOffset(double timeSeconds, out Vector3d position, out Vector3d velocity)
        {
            EvaluateLocalOffset(timeSeconds, out position, out velocity);
        }

        /// <summary>
        /// Положение/скорость ОТНОСИТЕЛЬНО родителя (кеплеров оффсет), без рекурсии наверх.
        /// Якоби-приближение: внешняя орбита — это движение барицентра подсистемы,
        /// μ = μ_parent_own + μ_this_subtree (собственная + все потомки рекурсивно).
        /// Для луны вокруг планеты это μ_планеты + μ_луны как раньше; для планеты
        /// с луной вокруг звезды это μ_звезды + μ_планеты + μ_луны — иначе период
        /// внешней орбиты занижен на долю массы луны (~1.2% → ~0.6% к n, десятки
        /// градусов фазы за 80 витков, поймано Step 0/T39).
        /// </summary>
        private void EvaluateLocalOffset(double timeSeconds, out Vector3d position, out Vector3d velocity)
        {
            LocalOffsetEvaluationCount++;
            if (Baked != null)
            {
                double muLocal = Parent.ResolveStandardGravitationalParameter() + ResolveSubtreeStandardGravitationalParameter();
                // Вердикт о диапазоне — только TryEvaluate (у неё fp-допуск на
                // границах: EndSeconds пересчитывается с округлением и может
                // расходиться со спаном на микросекунды). Сегмент не читается —
                // по-прежнему громко.
                if (Baked.TryEvaluate(timeSeconds, out Vector3d bakedPosition, out Vector3d bakedVelocity))
                {
                    position = bakedPosition;
                    velocity = bakedVelocity;
                    return;
                }

                // Фолбэк за концом рельсы: кеплерово продолжение, снятое с
                // интегрированного состояния в EndSeconds — стык бесшовный.
                // Игрок, дошедший до конца испечённого мира, остаётся в физике:
                // орбиты продолжаются кеплерово, а не бросают исключение.
                if (Baked.TryEvaluateContinuation(timeSeconds, muLocal, out Vector3d contPosition, out Vector3d contVelocity))
                {
                    position = contPosition;
                    velocity = contVelocity;
                    return;
                }

                // Причина отказа — из GetFailReason, а не общий крик «вне
                // интервала»: битые данные ВНУТРИ спана вели бы чинящего
                // программиста не туда (аудит S2/4.3).
                BakedEphemeris.FailReason reason = Baked.GetFailReason(timeSeconds);
                string reasonText = reason switch
                {
                    BakedEphemeris.FailReason.OutOfRange => "время вне испечённого интервала",
                    BakedEphemeris.FailReason.NoData => "данные эфемериды отсутствуют (Coeffs пусты)",
                    BakedEphemeris.FailReason.Corrupt => "структура эфемериды повреждена (Degree/сегменты вне диапазонов)",
                    _ => "нет продолжения за концом рельсы (невалидный/отсутствующий фиттинг) или данные внутри спана нечитаемы"
                };
                throw new EphemerisRangeException(
                    "Тело " + Name + ": " + reasonText + " (время " + timeSeconds +
                    ", интервал [" + Baked.T0Seconds + ", " + Baked.EndSeconds + "]).",
                    timeSeconds, Name);
            }
            // Рельса — только замкнутая кеплерова орбита. Молчаливые клампы
            // (a≥1м, e≤0.999) удалены: гиперболическая орбита тела, тихо
            // превращённая в эллипс, маскирует баг в данных/генерации пресета.
            // Все существующие пресеты имеют e∈{0, 0.02, 0.09} — проверено.
            if (!(SemiMajorAxis > 0d) || !(Eccentricity >= 0d) || !(Eccentricity < 1d))
            {
                throw new ArgumentOutOfRangeException(nameof(Eccentricity),
                    "Тело " + Name + ": рельса обязана быть эллипсом (SemiMajorAxis=" +
                    SemiMajorAxis + ", Eccentricity=" + Eccentricity + "). " +
                    "Гиперболические/вырожденные элементы для кеплеровой рельсы недопустимы.");
            }

            double semiMajorAxis = SemiMajorAxis;
            double eccentricity = Eccentricity;
            double inclination = KeplerMath.DegreesToRadians(InclinationDegrees);
            double ascendingNode = KeplerMath.DegreesToRadians(LongitudeOfAscendingNodeDegrees);
            double periapsis = KeplerMath.DegreesToRadians(ArgumentOfPeriapsisDegrees);
            double meanAnomalyAtEpoch = KeplerMath.DegreesToRadians(MeanAnomalyAtEpochDegrees);

            double mu = Parent.ResolveStandardGravitationalParameter() + ResolveSubtreeStandardGravitationalParameter();

            double meanMotion = Math.Sqrt(mu / (semiMajorAxis * semiMajorAxis * semiMajorAxis));
            double meanAnomaly = KeplerMath.NormalizeAngle(
                meanAnomalyAtEpoch + (meanMotion * (timeSeconds - EpochTimeSeconds)));
            double eccentricAnomaly = KeplerMath.SolveEccentricAnomaly(meanAnomaly, eccentricity);

            double cosE = Math.Cos(eccentricAnomaly);
            double sinE = Math.Sin(eccentricAnomaly);
            double radius = semiMajorAxis * (1d - (eccentricity * cosE));
            double orbitalYScale = Math.Sqrt(1d - (eccentricity * eccentricity));

            Vector3d orbitalPosition = new Vector3d(
                semiMajorAxis * (cosE - eccentricity),
                semiMajorAxis * orbitalYScale * sinE,
                0d);

            double velocityFactor = Math.Sqrt(mu * semiMajorAxis) / radius;
            Vector3d orbitalVelocity = new Vector3d(
                -velocityFactor * sinE,
                velocityFactor * orbitalYScale * cosE,
                0d);

            position = KeplerMath.RotateOrbitalToWorld(orbitalPosition, ascendingNode, inclination, periapsis);
            velocity = KeplerMath.RotateOrbitalToWorld(orbitalVelocity, ascendingNode, inclination, periapsis);
        }

        public void ApplyPeriapsisAndApoapsis(double periapsisDistance, double apoapsisDistance)
        {
            // Мутация элементов при живой эфемериде молча игнорируется (рельса
            // приоритетнее в EvaluateLocalOffset) — громко (аудит S2/1.3).
            if (Baked != null)
            {
                throw new InvalidOperationException(
                    "Тело " + Name + " использует испечённую эфемериду — правка кеплеровых элементов молча не подействует. Переиспеките систему или уберите Baked.");
            }

            // apoapsis ≤ periapsis даёт e<0 / a≤0 — рельса отвергнет громко на
            // первой оценке состояния; здесь тоже громко, при сборке конфига.
            if (!(apoapsisDistance > periapsisDistance) || !(periapsisDistance > 0d))
            {
                throw new ArgumentOutOfRangeException(nameof(periapsisDistance),
                    "Апоапсис (" + apoapsisDistance + ") обязан быть больше периапсиса (" + periapsisDistance + "), оба положительны.");
            }

            SemiMajorAxis = 0.5d * (periapsisDistance + apoapsisDistance);
            Eccentricity = (apoapsisDistance - periapsisDistance) / (apoapsisDistance + periapsisDistance);
        }

        /// <summary>Угол положения нулевого меридиана в момент t (радианы) — собственное вращение тела.</summary>
        public double RotationAngleAtTime(double timeSeconds)
        {
            double offset = KeplerMath.DegreesToRadians(PrimeMeridianOffsetDegrees);
            if (RotationPeriodSeconds <= 0d)
            {
                return offset;
            }

            double angularSpeed = (2d * Math.PI) / RotationPeriodSeconds;
            return KeplerMath.NormalizeAngle(offset + (angularSpeed * (timeSeconds - EpochTimeSeconds)));
        }

        /// <summary>
        /// Ориентация тела в момент t: кватернион body→world (конвенция
        /// QuaternionD — v_world = q·v_body·q⁻¹). Тот же путь, что GetSurfaceState:
        /// сначала собственное вращение вокруг ẑ на угол нулевого меридиана,
        /// затем наклон оси тем же поворотом Родригеса. При оси (0,0,1) и нуле
        /// меридиана — тождество бит-в-бит (T17c-ветка ApplyAxisTilt).
        /// Body-frame зафиксирован: +Z — северный полюс (ось вращения),
        /// +X — нулевой меридиан на экваторе, +Y — 90°E.
        /// Рендер-слой берёт отсюда готовую ориентацию, не собирая её из
        /// SpinAxis/углов вручную (мёртвое Unity-поле VisualTransform удалено).
        /// </summary>
        public QuaternionD GetVisualOrientation(double timeSeconds)
        {
            GetTiltParams(out Vector3d axis, out Vector3d k, out double cosAngle);
            QuaternionD tilt;
            if (cosAngle < -1d + 1e-12d)
            {
                // Тот же антиподальный случай, что в GetSurfaceState: (x,−y,−z) —
                // поворот на π вокруг x̂, а не переворот вокруг k≈0.
                tilt = QuaternionD.FromAxisAngle(new Vector3d(1d, 0d, 0d), Math.PI);
            }
            else if (k.SqrMagnitude < 1e-300d)
            {
                tilt = QuaternionD.Identity;
            }
            else
            {
                tilt = QuaternionD.FromAxisAngle(k, Math.Acos(cosAngle));
            }

            QuaternionD spin = QuaternionD.FromAxisAngle(new Vector3d(0d, 0d, 1d), RotationAngleAtTime(timeSeconds));
            return (tilt * spin).Normalized;
        }

        /// <summary>
        /// Мировое положение/скорость точки на поверхности тела (широта/долгота в градусах,
        /// высота над Radius в метрах) в момент t. Нужно, чтобы игра могла корректно
        /// начинаться с конкретной точки на поверхности планеты: учитывает и текущее
        /// орбитальное положение тела, и его собственное вращение (иначе стартовая
        /// площадка "плыла" бы вместе с вращением планеты, а не оставалась на месте).
        ///
        /// Ось вращения учитывается честно: экваториальный оффсет поворачивается
        /// к NorthPoleDirection формулой Родригеса (при оси (0,0,1) — тождество
        /// бит-в-бит, доказано T17c). Раньше оффсет строился в сырых осях без
        /// поворота, а скорость уже считалась через ω×r от настоящей оси, —
        /// позиция и скорость были несогласованы при наклонённой оси.
        /// </summary>
        public void GetSurfaceState(
            double latitudeDegrees, double longitudeDegrees, double altitudeMeters, double timeSeconds,
            out Vector3d worldPosition, out Vector3d worldVelocity)
        {
            EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out Vector3d bodyVelocity);

            double lat = KeplerMath.DegreesToRadians(latitudeDegrees);
            double lon = KeplerMath.DegreesToRadians(longitudeDegrees) + RotationAngleAtTime(timeSeconds);
            double r = Radius + altitudeMeters;

            Vector3d equatorial = new Vector3d(
                r * Math.Cos(lat) * Math.Cos(lon),
                r * Math.Cos(lat) * Math.Sin(lon),
                r * Math.Sin(lat));

            GetTiltParams(out Vector3d axis, out Vector3d k, out double cosAngle);

            Vector3d localOffset;
            if (cosAngle < -1d + 1e-12d)
            {
                localOffset = new Vector3d(equatorial.X, -equatorial.Y, -equatorial.Z);
            }
            else
            {
                localOffset = ApplyAxisTilt(equatorial, k, cosAngle);
            }

            worldPosition = bodyPosition + localOffset;

            double angularSpeed = RotationPeriodSeconds > 0d ? (2d * Math.PI) / RotationPeriodSeconds : 0d;
            Vector3d angularVelocity = axis * angularSpeed;
            Vector3d rotationVelocity = Vector3d.Cross(angularVelocity, localOffset);

            worldVelocity = bodyVelocity + rotationVelocity;
        }

        /// <summary>
        /// Обратная проекция к GetSurfaceState: мировая позиция → lat/lon.
        /// Базис — то же отображение Родригеса (u=R(x̂), v=R(ŷ) теми же операциями,
        /// что форвард), а не вторая слегка другая система координат: иначе
        /// прямое и обратное преобразования разъехались бы на ulp-шум спора.
        /// Границы: полюса (|экваториальной проекции|≈0) → lon=0 детерминированно;
        /// долгота нормализуется в [0,2π) — шов ±π существует только в
        /// представлении, физическое состояние непрерывно (см. тест).
        /// </summary>
        public void SurfaceLatLonAt(Vector3d worldPosition, double timeSeconds, out double latitudeDegrees, out double longitudeDegrees)
        {
            EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
            Vector3d offset = worldPosition - bodyPosition;

            GetTiltParams(out Vector3d axis, out Vector3d k, out double cosAngle);

            Vector3d u;
            Vector3d v;
            if (cosAngle < -1d + 1e-12d)
            {
                u = new Vector3d(1d, 0d, 0d);
                v = new Vector3d(0d, -1d, 0d);
            }
            else
            {
                u = ApplyAxisTilt(new Vector3d(1d, 0d, 0d), k, cosAngle);
                v = ApplyAxisTilt(new Vector3d(0d, 1d, 0d), k, cosAngle);
            }

            double axial = Vector3d.Dot(offset, axis);
            Vector3d equatorialProjection = offset - (axis * axial);
            double projectionLength = equatorialProjection.Magnitude;

            double latitude = Math.Atan2(axial, projectionLength);
            double longitude;
            if (projectionLength <= 1e-9d * Math.Max(Radius, 1d))
            {
                longitude = 0d;
            }
            else
            {
                double rotated = Math.Atan2(Vector3d.Dot(equatorialProjection, v), Vector3d.Dot(equatorialProjection, u));
                longitude = KeplerMath.NormalizeAngle(rotated - RotationAngleAtTime(timeSeconds));
            }

            latitudeDegrees = latitude * (180d / Math.PI);
            longitudeDegrees = longitude * (180d / Math.PI);
        }

        /// <summary>
        /// Параметры наклона оси: нормализованная ось (дефолт при вырождении),
        /// вектор k = ẑ×axis и его косинус. Один источник для форварда и инверса.
        /// </summary>
        private void GetTiltParams(out Vector3d axis, out Vector3d k, out double cosAngle)
        {
            axis = NorthPoleDirection.Normalized;
            if (axis.SqrMagnitude < 0.5d)
            {
                axis = new Vector3d(0d, 0d, 1d);
            }

            cosAngle = Vector3d.Dot(new Vector3d(0d, 0d, 1d), axis);
            k = Vector3d.Cross(new Vector3d(0d, 0d, 1d), axis);
        }

        /// <summary>
        /// Поворот Родригеса экваториального вектора к оси: v + k×v + k×(k×v)/(1+c).
        /// При оси (0,0,1): k — точный ноль, поправка — точные нули, результат
        /// бит-в-бит равен входу (доказано T17c).
        /// </summary>
        private static Vector3d ApplyAxisTilt(Vector3d equatorial, Vector3d k, double cosAngle)
        {
            Vector3d kCross = Vector3d.Cross(k, equatorial);
            return equatorial + kCross + (Vector3d.Cross(k, kCross) / (1d + cosAngle));
        }

        /// <summary>
        /// Приливный захват 1:1. Период = орбитальный период тела по ПРИВЕДЁННОЙ
        /// массе μ_local = μ_родителя(собств.) + μ_своего поддерева — ровно той же,
        /// что использует EvaluateLocalOffset (Step 0: орбита планеты с луной летит
        /// по барицентру подсистемы). «Похожая» μ_родителя(собств.) дала бы период,
        /// чуть меньший орбитального, и сторона уползала бы виток за витком —
        /// ловится T79 на десятках витков, не на одном снимке.
        ///
        /// Автодоворот: в эпоху к родителю смотрит долгота 0 (замок без «нужной
        /// стороны» бессмысленен): θ(Epoch) = atan2-угол направления на родителя
        /// в тел-экваториальном базисе (тот же базис, что у SurfaceLatLonAt).
        ///
        /// Лиbrация: при e>0 направление на родителя гуляет вокруг среднего с
        /// амплитудой ±2e (уравнение центра ν−M ≈ 2e·sin M; Луна: 2e≈6.3° —
        /// близко к наблюдаемой оптической либрации). Фаза привязана к СРЕДНЕЙ
        /// аномалии — осознанная модель, не баг. Только 1:1: на высоком e реальные
        /// тела иногда предпочитают резонанс (Меркурий 3:2 при e≈0.206) —
        /// оговорка без громкой валидации: физически корректное поведение, а не ошибка.
        ///
        /// Идемпотентно: читает только элементы + состояния на эпоху → пересборка
        /// без правки данных даёт бит-в-бит тот же период/оффсет (T79).
        /// Ретроградный захват (h·axis ≤ 0) громко отвергается: ω вращения ядра
        /// в RotationAngleAtTime всегда положительна.
        /// </summary>
        public void ApplyTidalLock()
        {
            if (Parent == null)
            {
                throw new InvalidOperationException("Приливный захват требует родителя: у корня дерева (звезды) его нет.");
            }

            if (!(SemiMajorAxis > 0d))
            {
                throw new InvalidOperationException("Приливный захват \"" + Name + "\": SemiMajorAxis обязан быть положительным.");
            }

            double muLocal = Parent.ResolveStandardGravitationalParameter() + ResolveSubtreeStandardGravitationalParameter();
            if (!(muLocal > 0d))
            {
                throw new InvalidOperationException("Приливный захват \"" + Name + "\": μ_local (родитель + поддерево) обязана быть положительной.");
            }

            EvaluateWorldState(EpochTimeSeconds, out Vector3d bodyP0, out Vector3d bodyV0);
            Parent.EvaluateWorldState(EpochTimeSeconds, out Vector3d parentP0, out Vector3d parentV0);
            Vector3d relative = bodyP0 - parentP0;
            Vector3d relativeVelocity = bodyV0 - parentV0;
            GetTiltParams(out Vector3d axis, out Vector3d k, out double cosAngle);

            Vector3d orbitAngularMomentum = Vector3d.Cross(relative, relativeVelocity);
            if (Vector3d.Dot(orbitAngularMomentum, axis) <= 0d)
            {
                throw new InvalidOperationException(
                    "Приливный захват \"" + Name + "\": поддержан только для проградных орбит (h·axis > 0).");
            }

            RotationPeriodSeconds = 2d * Math.PI * Math.Sqrt(
                (SemiMajorAxis * SemiMajorAxis * SemiMajorAxis) / muLocal);
            double omega = 2d * Math.PI / RotationPeriodSeconds;

            // θ(Epoch) = lonFacing ⇒ SurfaceLatLonAt(подродительская точка, Epoch) = 0.
            Vector3d u = ApplyAxisTilt(new Vector3d(1d, 0d, 0d), k, cosAngle);
            Vector3d v = ApplyAxisTilt(new Vector3d(0d, 1d, 0d), k, cosAngle);
            double axial = Vector3d.Dot(relative, axis);
            Vector3d equatorialProjection = relative - (axis * axial);
            double lonFacing = Math.Atan2(Vector3d.Dot(equatorialProjection, v), Vector3d.Dot(equatorialProjection, u));
            PrimeMeridianOffsetDegrees = KeplerMath.NormalizeAngle(lonFacing) * (180d / Math.PI);
        }
    }
}
