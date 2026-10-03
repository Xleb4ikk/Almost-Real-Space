// ============================================================================
// ORBITAL ELEMENTS
// ============================================================================
// Структура для представления орбитальных элементов Кеплера
// Перемещено из Vector3d.cs для архитектурной чистоты (Фаза 2.2)

using System;

namespace Galilego.Core
{
    /// <summary>
    /// Орбитальные элементы Кеплера, описывающие орбиту небесного тела.
    /// </summary>
    public readonly struct OrbitalElements
    {
        public readonly bool IsValid;
        public readonly bool IsBound;
        public readonly double SemiMajorAxis;
        public readonly double Eccentricity;
        public readonly Vector3d EccentricityVector;
        public readonly double InclinationDegrees;
        public readonly double LongitudeOfAscendingNodeDegrees;
        public readonly double ArgumentOfPeriapsisDegrees;
        public readonly double TrueAnomalyDegrees;
        public readonly double MeanAnomalyDegrees;
        public readonly double PeriapsisDistance;
        public readonly double ApoapsisDistance;
        public readonly double OrbitalPeriodSeconds;
        public readonly double SpecificOrbitalEnergy;
        public readonly double SpecificAngularMomentum;

        private OrbitalElements(
            bool isValid,
            bool isBound,
            double semiMajorAxis,
            double eccentricity,
            Vector3d eccentricityVector,
            double inclinationDegrees,
            double longitudeOfAscendingNodeDegrees,
            double argumentOfPeriapsisDegrees,
            double trueAnomalyDegrees,
            double meanAnomalyDegrees,
            double periapsisDistance,
            double apoapsisDistance,
            double orbitalPeriodSeconds,
            double specificOrbitalEnergy,
            double specificAngularMomentum)
        {
            IsValid = isValid;
            IsBound = isBound;
            SemiMajorAxis = semiMajorAxis;
            Eccentricity = eccentricity;
            EccentricityVector = eccentricityVector;
            InclinationDegrees = inclinationDegrees;
            LongitudeOfAscendingNodeDegrees = longitudeOfAscendingNodeDegrees;
            ArgumentOfPeriapsisDegrees = argumentOfPeriapsisDegrees;
            TrueAnomalyDegrees = trueAnomalyDegrees;
            MeanAnomalyDegrees = meanAnomalyDegrees;
            PeriapsisDistance = periapsisDistance;
            ApoapsisDistance = apoapsisDistance;
            OrbitalPeriodSeconds = orbitalPeriodSeconds;
            SpecificOrbitalEnergy = specificOrbitalEnergy;
            SpecificAngularMomentum = specificAngularMomentum;
        }

        public static OrbitalElements Invalid => new OrbitalElements(
            false,
            false,
            double.NaN,
            double.NaN,
            Vector3d.Zero,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN);

        /// <summary>
        /// Вычисляет орбитальные элементы из векторов состояния.
        /// </summary>
        /// <param name="relativePosition">Вектор положения относительно центрального тела в АСТРОДИНАМИЧЕСКОЙ системе (Z-up)</param>
        /// <param name="relativeVelocity">Вектор скорости относительно центрального тела в АСТРОДИНАМИЧЕСКОЙ системе (Z-up)</param>
        /// <param name="standardGravitationalParameter">Стандартный гравитационный параметр μ = G·M центрального тела (м³/с²)</param>
        /// <returns>Орбитальные элементы или Invalid, если расчёт невозможен</returns>
        /// <remarks>
        /// ⚠️ ВАЖНО: КОНТРАКТ АСТРОДИНАМИЧЕСКОЙ СИСТЕМЫ КООРДИНАТ
        /// 
        /// Этот метод ожидает, что оба вектора переданы в АСТРОДИНАМИЧЕСКОЙ системе координат,
        /// где ось +Z ВСЕГДА указывает на север опорной плоскости (эклиптика/экватор).
        /// 
        /// Если ваши векторы находятся в симуляционном кадре Unity (Y-up),
        /// вы ДОЛЖНЫ преобразовать их мостом AstroFrame перед вызовом:
        ///
        /// <code>
        /// // ✅ ПРАВИЛЬНО:
        /// var astroPos = AstroFrame.ToAstro(simPos);
        /// var astroVel = AstroFrame.ToAstro(simVel);
        /// var elements = OrbitalElements.FromState(astroPos, astroVel, mu);
        /// 
        /// // ❌ НЕПРАВИЛЬНО (приведёт к неверным наклонению, LAN, аргументу перицентра):
        /// var elements = OrbitalElements.FromState(simPos, simVel, mu);
        /// </code>
        /// 
        /// Формулы орбитальной механики:
        /// - Угловой момент: h = r × v
        /// - Вектор эксцентриситета: e = (v × h)/μ - r/|r|
        /// - Узел: n = k × h, где k = (0, 0, 1) в астродинамической системе
        /// - Наклонение: i = arccos(h_z / |h|)
        /// - Долгота восходящего узла: Ω = arctan2(n_y, n_x)
        /// - Аргумент перицентра: ω = arccos(n · e / (|n| |e|))
        /// </remarks>
        public static OrbitalElements FromState(Vector3d relativePosition, Vector3d relativeVelocity, double standardGravitationalParameter)
        {
            const double epsilon = 1e-10d;

            double radius = relativePosition.Magnitude;
            double speedSquared = relativeVelocity.SqrMagnitude;
            if (radius <= epsilon || standardGravitationalParameter <= 0d)
            {
                return Invalid;
            }

            Vector3d angularMomentum = Vector3d.Cross(relativePosition, relativeVelocity);
            double angularMomentumMagnitude = angularMomentum.Magnitude;
            if (angularMomentumMagnitude <= epsilon)
            {
                return Invalid;
            }

#if UNITY_EDITOR
            // Guard удалён в A2: проблема непреобразованных Y-up векторов решена
            // конструктивно мостом AstroFrame (ToAstro/ToSimulation), эвристика по
            // угловому моменту давала бы ложные срабатывания на векторах, честно
            // прошедших мост. См. Universe/AstroFrame.cs.
#endif

            Vector3d node = Vector3d.Cross(new Vector3d(0d, 0d, 1d), angularMomentum);
            double nodeMagnitude = node.Magnitude;
            // Узел — ОТНОСИТЕЛЬНЫЙ тест: абсолютный epsilon=1e-10 при |h|~1e10
            // принимает fp-мусор направления (sin π ≠ 0 в поворотах!) за настоящий
            // узел. Поймано T26: e=0, i=180° давал Ω=40° из шума и зеркало 2R.
            // e безразмерен изначально — ему абсолютный порог корректен.
            bool hasAscendingNode = nodeMagnitude > 1e-9d * angularMomentumMagnitude;
            Vector3d eccentricityVector =
                (Vector3d.Cross(relativeVelocity, angularMomentum) / standardGravitationalParameter) -
                (relativePosition / radius);

            double eccentricity = eccentricityVector.Magnitude;
            double energy = (0.5d * speedSquared) - (standardGravitationalParameter / radius);
            bool isParabolic = Math.Abs(energy) <= epsilon;
            double semiMajorAxis = isParabolic
                ? double.PositiveInfinity
                : -standardGravitationalParameter / (2d * energy);

            bool isBound = !isParabolic && semiMajorAxis > 0d && eccentricity < 1d;
            double periapsisDistance = (angularMomentumMagnitude * angularMomentumMagnitude) /
                (standardGravitationalParameter * (1d + eccentricity));
            double apoapsisDistance = isBound
                ? semiMajorAxis * (1d + eccentricity)
                : double.PositiveInfinity;
            double orbitalPeriodSeconds = isBound
                ? 2d * Math.PI * Math.Sqrt((semiMajorAxis * semiMajorAxis * semiMajorAxis) / standardGravitationalParameter)
                : double.PositiveInfinity;

            double inclination = SafeAcos(angularMomentum.Z / angularMomentumMagnitude);
            double longitudeOfAscendingNode = hasAscendingNode
                ? NormalizeAngle(Math.Atan2(node.Y, node.X))
                : 0d;

            // При node≈0 (околоэкваториальная орбита) узел не определён, и ω=0
            // верно лишь если перицентр случайно в нуле долготы. Правило общее:
            // ω обязано отображать перифокальный угол ν (меряется от ИСТИННОГО
            // перицентра e-вектором ниже) в истинную долготу. Прямое: ω=Λ_p,
            // ретроградное (поворот R1(π) зеркалит угол): ω=−Λ_p.
            // Без этого KeplerPredictor зеркалит такие орбиты (поймано T25:
            // хорда давала ν=120.9° при ω=0 — ошибка в масштабе орбиты).
            double argumentOfPeriapsis = 0d;
            if (hasAscendingNode && eccentricity > epsilon)
            {
                argumentOfPeriapsis = SafeAcos(Vector3d.Dot(node, eccentricityVector) / (nodeMagnitude * eccentricity));
                if (eccentricityVector.Z < 0d)
                {
                    argumentOfPeriapsis = (2d * Math.PI) - argumentOfPeriapsis;
                }
            }
            else if (eccentricity > epsilon)
            {
                double periapsisLongitude = Math.Atan2(eccentricityVector.Y, eccentricityVector.X);
                argumentOfPeriapsis = angularMomentum.Z >= 0d
                    ? NormalizeAngle(periapsisLongitude)
                    : NormalizeAngle(-periapsisLongitude);
            }
            else if (angularMomentum.Z < 0d)
            {
                argumentOfPeriapsis = Math.PI;
            }

            double trueAnomaly = 0d;
            if (eccentricity > epsilon)
            {
                trueAnomaly = SafeAcos(Vector3d.Dot(eccentricityVector, relativePosition) / (eccentricity * radius));
                if (Vector3d.Dot(relativePosition, relativeVelocity) < 0d)
                {
                    trueAnomaly = (2d * Math.PI) - trueAnomaly;
                }
            }
            else if (hasAscendingNode)
            {
                trueAnomaly = SafeAcos(Vector3d.Dot(node, relativePosition) / (nodeMagnitude * radius));
                if (relativePosition.Z < 0d)
                {
                    trueAnomaly = (2d * Math.PI) - trueAnomaly;
                }
            }
            else if (angularMomentum.Z >= 0d)
            {
                trueAnomaly = NormalizeAngle(Math.Atan2(relativePosition.Y, relativePosition.X));
            }
            else
            {
                // Круговая ретроградная экваториальная: с ω=π долгота L требует
                // ν=π−L (поворот R1(π) зеркалит угол). Без этого — зеркало 2R.
                trueAnomaly = NormalizeAngle(Math.PI - Math.Atan2(relativePosition.Y, relativePosition.X));
            }

            double meanAnomaly = double.NaN;
            if (isBound)
            {
                double eccentricAnomaly = 2d * Math.Atan2(
                    Math.Sqrt(1d - eccentricity) * Math.Sin(trueAnomaly * 0.5d),
                    Math.Sqrt(1d + eccentricity) * Math.Cos(trueAnomaly * 0.5d));
                meanAnomaly = NormalizeAngle(eccentricAnomaly - (eccentricity * Math.Sin(eccentricAnomaly)));
            }

            return new OrbitalElements(
                true,
                isBound,
                semiMajorAxis,
                eccentricity,
                eccentricityVector,
                RadiansToDegrees(inclination),
                RadiansToDegrees(longitudeOfAscendingNode),
                RadiansToDegrees(argumentOfPeriapsis),
                RadiansToDegrees(NormalizeAngle(trueAnomaly)),
                double.IsNaN(meanAnomaly) ? double.NaN : RadiansToDegrees(meanAnomaly),
                periapsisDistance,
                apoapsisDistance,
                orbitalPeriodSeconds,
                energy,
                angularMomentumMagnitude);
        }

        /// <summary>
        /// Вычисляет радиус сферы влияния (Sphere of Influence) по формуле Лапласа.
        /// </summary>
        /// <param name="semiMajorAxis">Большая полуось орбиты вокруг родительского тела (м)</param>
        /// <param name="orbitingMass">Масса орбитирующего тела (кг)</param>
        /// <param name="parentMass">Масса родительского тела (кг)</param>
        /// <returns>Радиус сферы влияния (м)</returns>
        public static double CalculateSphereOfInfluenceRadius(double semiMajorAxis, double orbitingMass, double parentMass)
        {
            if (semiMajorAxis <= 0d || orbitingMass <= 0d || parentMass <= 0d)
            {
                return 0d;
            }

            return semiMajorAxis * Math.Pow(orbitingMass / parentMass, 0.4d);
        }

        /// <summary>
        /// Вычисляет радиус сферы Хилла (Hill sphere).
        /// </summary>
        /// <param name="semiMajorAxis">Большая полуось орбиты вокруг родительского тела (м)</param>
        /// <param name="eccentricity">Эксцентриситет орбиты</param>
        /// <param name="orbitingMass">Масса орбитирующего тела (кг)</param>
        /// <param name="parentMass">Масса родительского тела (кг)</param>
        /// <returns>Радиус сферы Хилла (м)</returns>
        public static double CalculateHillRadius(double semiMajorAxis, double eccentricity, double orbitingMass, double parentMass)
        {
            if (semiMajorAxis <= 0d || orbitingMass <= 0d || parentMass <= 0d)
            {
                return 0d;
            }

            return semiMajorAxis * (1d - Math.Max(0d, eccentricity)) * Math.Pow(orbitingMass / (3d * parentMass), 1d / 3d);
        }

        private static double AtanhBounded(double x)
        {
            if (x >= 1d)
            {
                return 20d;
            }

            if (x <= -1d)
            {
                return -20d;
            }

            return 0.5d * Math.Log((1d + x) / (1d - x));
        }

        private static double SafeAcos(double value)
        {
            return Math.Acos(Math.Max(-1d, Math.Min(1d, value)));
        }

        private static double NormalizeAngle(double angle)
        {
            double twoPi = Math.PI * 2d;
            angle %= twoPi;
            if (angle < 0d)
            {
                angle += twoPi;
            }

            return angle;
        }

        private static double RadiansToDegrees(double radians)
        {
            return radians * (180d / Math.PI);
        }

#if !UNITY_5_3_OR_NEWER
        /// <summary>
        /// Batch calculation of orbital elements using Burst-compiled job.
        /// Significantly faster for multiple state vectors (3-8x speedup).
        /// Recommended for 10+ calculations.
        /// </summary>
        /// <param name="positions">Array of position vectors in astrodynamic frame (Z-up)</param>
        /// <param name="velocities">Array of velocity vectors in astrodynamic frame (Z-up)</param>
        /// <param name="mus">Array of standard gravitational parameters μ = G·M (m³/s²)</param>
        /// <param name="results">Output array for orbital elements data</param>
        /// <param name="dependency">Optional job dependency</param>
        /// <returns>JobHandle for the scheduled job</returns>
        public static Unity.Jobs.JobHandle CalculateBatch(
            Unity.Collections.NativeArray<Unity.Mathematics.double3> positions,
            Unity.Collections.NativeArray<Unity.Mathematics.double3> velocities,
            Unity.Collections.NativeArray<double> mus,
            Unity.Collections.NativeArray<OrbitalElementsData> results,
            Unity.Jobs.JobHandle dependency = default)
        {
            if (positions.Length != velocities.Length || positions.Length != mus.Length || positions.Length != results.Length)
            {
                throw new System.ArgumentException("All arrays must have the same length");
            }

            var job = new Simulation.OrbitalElementsJob
            {
                Positions = positions,
                Velocities = velocities,
                Mus = mus,
                Results = results
            };

            // Optimal batch size for 16-32 threads: divide work evenly
            int batchSize = Unity.Mathematics.math.max(1, positions.Length / (UnityEngine.SystemInfo.processorCount - 2));
            return Unity.Jobs.IJobParallelForExtensions.Schedule(job, positions.Length, batchSize, dependency);
        }
#endif

        /// <summary>
        /// Convert OrbitalElementsData back to OrbitalElements.
        /// Used after batch calculation to get managed representation.
        /// </summary>
        public static OrbitalElements FromData(OrbitalElementsData data)
        {
            return new OrbitalElements(
                data.IsValid != 0,
                data.IsBound != 0,
                data.SemiMajorAxis,
                data.Eccentricity,
                new Vector3d(data.EccentricityVector.X, data.EccentricityVector.Y, data.EccentricityVector.Z),
                data.InclinationDegrees,
                data.LongitudeOfAscendingNodeDegrees,
                data.ArgumentOfPeriapsisDegrees,
                data.TrueAnomalyDegrees,
                data.MeanAnomalyDegrees,
                data.PeriapsisDistance,
                data.ApoapsisDistance,
                data.OrbitalPeriodSeconds,
                data.SpecificOrbitalEnergy,
                data.SpecificAngularMomentum);
        }

        /// <summary>
        /// Вычисляет позиции периапсиса и апоапсиса в астродинамической системе координат (Z-up).
        /// </summary>
        /// <param name="periapsisPosition">Выходной параметр: позиция периапсиса в астродинамической системе (м)</param>
        /// <param name="apoapsisPosition">Выходной параметр: позиция апоапсиса в астродинамической системе (м), или Vector3d.Zero для гиперболических орбит</param>
        /// <returns>true, если расчёт успешен; false, если орбитальные элементы невалидны</returns>
        /// <remarks>
        /// Этот метод вычисляет мировые позиции апсид, используя:
        /// - Вектор эксцентриситета для определения направления на периапсис
        /// - Формулы r_pe = a(1-e) и r_ap = a(1+e) для расчёта расстояний
        /// - Матрицу поворота R(Ω, i, ω) для преобразования из орбитальной плоскости в инерциальную систему
        /// 
        /// Для гиперболических орбит (e >= 1.0) возвращается только периапсис,
        /// а apoapsisPosition устанавливается в Vector3d.Zero.
        /// 
        /// Возвращаемые позиции находятся в АСТРОДИНАМИЧЕСКОЙ системе координат (Z-up),
        /// центрированной на центральном теле. Для преобразования в симуляционный кадр
        /// используйте AstroFrame.ToSimulation().
        /// </remarks>
        public bool TryGetApsisPositions(out Vector3d periapsisPosition, out Vector3d apoapsisPosition)
        {
            periapsisPosition = Vector3d.Zero;
            apoapsisPosition = Vector3d.Zero;

            // Проверка валидности орбитальных элементов
            if (!IsValid)
            {
                return false;
            }

            const double epsilon = 1e-10d;

            // Проверка, что вектор эксцентриситета валиден
            if (EccentricityVector.SqrMagnitude < epsilon)
            {
                // Круговая орбита - нет определённых апсид
                return false;
            }

            // Расчёт расстояния до периапсиса: r_pe = a(1-e)
            // Для гиперболических орбит a < 0, но формула всё равно работает
            double periapsisDistance = SemiMajorAxis * (1.0 - Eccentricity);

            // Направление на периапсис - нормализованный вектор эксцентриситета
            Vector3d periapsisDirection = EccentricityVector.Normalized;

            // Позиция периапсиса в астродинамической системе
            periapsisPosition = periapsisDirection * periapsisDistance;

            // Для эллиптических орбит вычисляем апоапсис
            if (Eccentricity < 1.0)
            {
                // Расчёт расстояния до апоапсиса: r_ap = a(1+e)
                double apoapsisDistance = SemiMajorAxis * (1.0 + Eccentricity);

                // Направление на апоапсис - противоположно периапсису
                Vector3d apoapsisDirection = -periapsisDirection;

                // Позиция апоапсиса в астродинамической системе
                apoapsisPosition = apoapsisDirection * apoapsisDistance;
            }
            else
            {
                // Гиперболическая или параболическая орбита - апоапсиса нет
                apoapsisPosition = Vector3d.Zero;
            }

            return true;
        }

        /// <summary>
        /// Вычисляет время до достижения периапсиса и апоапсиса из текущей позиции на орбите.
        /// </summary>
        /// <param name="mu">Гравитационный параметр μ = GM центрального тела (м³/с²)</param>
        /// <param name="timeToPeriapsis">Выходной параметр: время до периапсиса (секунды)</param>
        /// <param name="timeToApoapsis">Выходной параметр: время до апоапсиса (секунды), или NaN для гиперболических орбит</param>
        /// <returns>true, если расчёт успешен; false, если орбитальные элементы невалидны</returns>
        /// <remarks>
        /// Этот метод вычисляет время до достижения апсид, используя:
        /// - Среднее движение: n = sqrt(μ / a³)
        /// - Текущую среднюю аномалию M (уже вычислена в FromState)
        /// - Время до периапсиса: Δt_pe = (2π - M) / n
        /// - Время до апоапсиса: Δt_ap = (π - M) / n (для эллиптических орбит)
        /// 
        /// Метод корректно обрабатывает wraparound, когда M > π:
        /// - Если M < π (между периапсисом и апоапсисом), время до апоапсиса = (π - M) / n
        /// - Если M > π (между апоапсисом и периапсисом), время до апоапсиса = (3π - M) / n
        /// 
        /// Для гиперболических орбит (e >= 1.0) timeToApoapsis устанавливается в NaN.
        /// 
        /// Возвращаемое время является относительным (от текущего момента).
        /// Для получения абсолютного времени добавьте текущее время симуляции.
        /// </remarks>
        public bool TryGetTimeToApsides(double mu, out double timeToPeriapsis, out double timeToApoapsis)
        {
            timeToPeriapsis = double.NaN;
            timeToApoapsis = double.NaN;

            // Проверка валидности орбитальных элементов
            if (!IsValid)
            {
                return false;
            }

            // Гиперболическая ветка: перицентр один, проходится один раз.
            // H0 из истинной аномалии (без blowup у асимптоты: |ν| < ν∞ строго
            // для конечных r, AtanhBounded режет только буквально бесконечность).
            // Mh0 < 0 — летим к перицентру: Δt = −Mh0/n. Mh0 ≥ 0 — перицентр
            // позади (в точности 0 — сейчас в нём): будущего перицентра нет.
            // Апоапсиса у гиперболы нет никогда. Околопараболы (|e−1|≈0, n→0)
            // не поддерживаем, как и предиктор (там NotSupportedException).
            if (!IsBound)
            {
                timeToApoapsis = double.NaN;
                if (Eccentricity <= 1d + 1e-9d)
                {
                    return false;
                }

                if (mu <= 1e-10d)
                {
                    return false;
                }

                double alpha = -SemiMajorAxis;
                if (!(alpha > 1e-10d))
                {
                    return false;
                }

                double nuRad = TrueAnomalyDegrees * (Math.PI / 180.0);
                if (nuRad > Math.PI)
                {
                    nuRad -= 2d * Math.PI;
                }

                double halfFactor = Math.Sqrt((Eccentricity - 1d) / (Eccentricity + 1d)) * Math.Tan(0.5d * nuRad);
                double h0 = 2d * AtanhBounded(halfFactor);
                double meanHyperbolic = (Eccentricity * Math.Sinh(h0)) - h0;
                if (meanHyperbolic > 0d)
                {
                    timeToPeriapsis = double.NaN;
                    return true;
                }

                if (meanHyperbolic == 0d)
                {
                    timeToPeriapsis = 0d;
                    return true;
                }

                double meanMotionHyp = Math.Sqrt(mu / (alpha * alpha * alpha));
                timeToPeriapsis = -meanHyperbolic / meanMotionHyp;
                return true;
            }

            const double epsilon = 1e-10d;

            // Проверка валидности параметров
            if (mu <= epsilon || SemiMajorAxis <= epsilon)
            {
                return false;
            }

            // Вычисляем среднее движение: n = sqrt(μ / a³)
            double n = Math.Sqrt(mu / (SemiMajorAxis * SemiMajorAxis * SemiMajorAxis));

            // Получаем текущую среднюю аномалию M (уже вычислена в FromState, в градусах)
            if (double.IsNaN(MeanAnomalyDegrees))
            {
                return false;
            }

            // Конвертируем среднюю аномалию из градусов в радианы
            double M = MeanAnomalyDegrees * (Math.PI / 180.0);

            // Нормализуем M в диапазон [0, 2π)
            M = NormalizeAngle(M);

            // Вычисляем время до периапсиса: Δt_pe = (2π - M) / n
            // Когда M = 0, мы в периапсисе, время = 0
            // Когда M = 2π, мы снова в периапсисе, время = 0
            // Ровно в периапсисе (M≈0) ответ 0, а не полный период: раньше
            // (2π − 0)/n возвращал период, противореча комментарию (поймано T50).
            timeToPeriapsis = M <= 1e-12d ? 0d : (2.0 * Math.PI - M) / n;

            // Если время больше орбитального периода, вычитаем период
            if (timeToPeriapsis > OrbitalPeriodSeconds)
            {
                timeToPeriapsis -= OrbitalPeriodSeconds;
            }

            // Вычисляем время до апоапсиса (только для эллиптических орбит)
            if (Eccentricity < 1.0)
            {
                // Апоапсис находится при M = π
                // Если M < π (между периапсисом и апоапсисом), время = (π - M) / n
                // Если M > π (между апоапсисом и периапсисом), время = (3π - M) / n = (2π + π - M) / n
                // Граница включена: ровно в апоапсисе (M = π) время = 0, а не полный
                // период (та же ошибка, что была с периапсисом при M = 0, поймано T56).
                if (M <= Math.PI)
                {
                    timeToApoapsis = (Math.PI - M) / n;
                }
                else
                {
                    timeToApoapsis = (3.0 * Math.PI - M) / n;
                }
            }
            else
            {
                // Гиперболическая или параболическая орбита - апоапсиса нет
                timeToApoapsis = double.NaN;
            }

            return true;
        }
    }
}
