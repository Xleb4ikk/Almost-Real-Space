using System;
using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Events
{
    /// <summary>
    /// Адаптивное зерно детекта событий. Правило — «только мельче, не крупнее»:
    /// итог всегда max(базовое, адаптивное). Укрупнять зерно для медленных орбит
    /// запрещено: это была бы регрессия уже закрытого риска multi-crossing
    /// (комментарий на DetectionSubdivisions). Дробить — можно и нужно, когда
    /// характерное время системы мало: быстрый гиперболический пролёт,
    /// тесная/тонкая встреча, в будущем — прожиги с мелкими временами.
    ///
    /// Характерное время τ — из кеплеровых элементов относительно доминантного
    /// тела (один FromState + один обход дерева на чанк, не на подчанк и не на
    /// RHS — цена пренебрежима). Эллипс: период 2π√(a³/μ). Гипербола: аналог
    /// периода по модулю большой полуоси 2π√(|a|³/μ) — для глубокого пролёта
    /// |a| мало и зерно честно мельчает. Невалидные элементы — фолбэк на базу.
    /// </summary>
    public static class DetectionGrain
    {
        public const int MaxSubdivisions = 200;

        public const double GrainDivisor = 20d;

        /// <summary>
        /// Характерное время состояния в секундах. PositiveInfinity = неизвестно,
        /// вызывающий обязан откатиться на базовое зерно.
        /// </summary>
        public static double CharacteristicTimeSeconds(StarSystem system, SpacecraftIntegrationState state, double timeSeconds)
        {
            if (system == null)
            {
                return double.PositiveInfinity;
            }

            OrbitingBody dominant = system.FindDominantBody(state.Position, timeSeconds);
            if (dominant == null)
            {
                return double.PositiveInfinity;
            }

            double mu = dominant.ResolveStandardGravitationalParameter();
            if (mu <= 0d)
            {
                return double.PositiveInfinity;
            }

            dominant.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out Vector3d bodyVelocity);
            OrbitalElements elements = OrbitalElements.FromState(state.Position - bodyPosition, state.Velocity - bodyVelocity, mu);
            if (!elements.IsValid)
            {
                return double.PositiveInfinity;
            }

            if (elements.IsBound)
            {
                return elements.OrbitalPeriodSeconds;
            }

            double semiMajor = elements.SemiMajorAxis;
            if (double.IsNaN(semiMajor) || double.IsInfinity(semiMajor) || semiMajor == 0d)
            {
                return double.PositiveInfinity;
            }

            return 2d * Math.PI * Math.Sqrt(Math.Abs(semiMajor * semiMajor * semiMajor) / mu);
        }

        /// <summary>
        /// Число подчанков на MaxChunkSeconds. Никогда не меньше baseDivisions
        /// (не крупнее базы) и не больше cap. NaN/Inf/неположительное τ —
        /// ровно baseDivisions.
        /// </summary>
        public static int Subdivisions(double maxChunkSeconds, int baseDivisions, double tauSeconds, int cap)
        {
            if (double.IsNaN(tauSeconds) || double.IsInfinity(tauSeconds) || tauSeconds <= 0d)
            {
                return baseDivisions;
            }

            double desiredGrain = tauSeconds / GrainDivisor;
            double baseGrain = maxChunkSeconds / baseDivisions;
            if (desiredGrain >= baseGrain)
            {
                return baseDivisions;
            }

            int adaptive = (int)Math.Ceiling(maxChunkSeconds / desiredGrain);
            if (adaptive < baseDivisions)
            {
                adaptive = baseDivisions;
            }

            if (adaptive > cap)
            {
                adaptive = cap;
            }

            return adaptive;
        }

        public static int Subdivisions(double maxChunkSeconds, int baseDivisions, double tauSeconds)
        {
            return Subdivisions(maxChunkSeconds, baseDivisions, tauSeconds, MaxSubdivisions);
        }

        /// <summary>
        /// Консервативный предикат «внутри чанка возможно пересечение поверхности».
        /// Подчанкование (пол DetectionSubdivisions) нужно ТОЛЬКО если корабль
        /// физически может достичь пороговой сферы: высотного детектора
        /// (атмосфера/поверхность) или SOI-границы любого тела. Знак-тест g0/g1
        /// на границах подчанка ловит пересечение, лишь если оно длиннее зерна;
        /// если сфера недостижима за чанк — пересечения нет, и пол 20 — чистая
        /// цена без пользы (A0-зонд T58: межпланетный круиз, τ=4.5e7 с,
        /// subdiv=20 из пола → ×20 к цене дальнего варпа).
        /// Достижимость — с запасом: зазор до сферы ≤ (|v_корабля|+|v_тела|)·chunk·safety.
        /// Остальные события в грубом зерне не теряются: PropellantDepletion —
        /// монотонная масса (двойного пересечения нет), TimedEvent — по часам,
        /// SOI — переход ключа, но его сфера проверена здесь же.
        /// </summary>
        public static bool NeedsFineGrain(
            StarSystem system,
            System.Collections.Generic.IReadOnlyList<ICrossingDetector> crossingDetectors,
            SpacecraftIntegrationState state,
            double timeSeconds,
            double chunkSeconds,
            double safetyFactor = 2d)
        {
            if (system == null || chunkSeconds <= 0d)
            {
                return true; // нет данных для оценки — консервативно, как раньше
            }

            for (int i = 0; i < crossingDetectors.Count; i++)
            {
                if (crossingDetectors[i] is AltitudeCrossingDetector alt)
                {
                    OrbitingBody body = alt.Body;
                    body.EvaluateWorldState(timeSeconds, out Vector3d bp, out Vector3d bv);
                    double gap = (state.Position - bp).Magnitude - (body.Radius + alt.ThresholdAltitude);
                    if (gap <= ReachableDistance(state.Velocity, bv, chunkSeconds, safetyFactor))
                    {
                        return true;
                    }
                }
            }

            System.Collections.Generic.IReadOnlyList<OrbitingBody> bodies = system.AllBodies;
            for (int i = 0; i < bodies.Count; i++)
            {
                OrbitingBody body = bodies[i];
                if (body.Parent == null)
                {
                    continue; // у корня нет SOI
                }

                body.EvaluateWorldState(timeSeconds, out Vector3d bp, out Vector3d bv);
                double gap = (state.Position - bp).Magnitude - body.SphereOfInfluenceRadius;
                if (gap <= ReachableDistance(state.Velocity, bv, chunkSeconds, safetyFactor))
                {
                    return true;
                }
            }

            return false;
        }

        private static double ReachableDistance(Vector3d shipVelocity, Vector3d bodyVelocity, double chunkSeconds, double safetyFactor)
        {
            return (shipVelocity.Magnitude + bodyVelocity.Magnitude) * chunkSeconds * safetyFactor;
        }
    }
}
