using System;
using System.Collections.Generic;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Физическое ядро звёздной системы: дерево тел (звезда → планеты → спутники)
    /// + интегрирование корабля. Это НЕ замена всего старого UniverseManager —
    /// сознательно без камеры, IMGUI-телеметрии и рендера орбит, это отдельные
    /// ответственности, которые стоит развести по своим классам.
    ///
    /// Модель движения — гибрид:
    /// - Все небесные тела двигаются по кеплеровым рельсам (OrbitingBody), не
    ///   возмущая друг друга — как раньше EvaluateMoonState, но на любую глубину.
    /// - Корабль летает по полному n-body: на каждом шаге интегратора получает
    ///   гравитацию ОТ ВСЕХ тел системы одновременно (раньше — только от Юпитера
    ///   и его лун, теперь — от звезды, всех планет и всех их спутников разом).
    /// </summary>
    public sealed class StarSystem
    {
        private readonly OrbitingBody root;
        private readonly List<OrbitingBody> allBodies = new List<OrbitingBody>();

        public OrbitingBody Root => root;
        public IReadOnlyList<OrbitingBody> AllBodies => allBodies;

        private readonly Dictionary<OrbitingBody, int> bodyIndex = new Dictionary<OrbitingBody, int>();

        private Vector3d[] cachedPositions;
        private double cachedTime = double.NaN;
        private bool cacheValid;
        private long cachedStamp;

        /// <summary>Инструментация кэша для теста 6: попадания/промахи по времени подстадии.</summary>
        public long CacheHits;
        public long CacheMisses;

        public StarSystem(OrbitingBody rootBody)
        {
            root = rootBody ?? throw new ArgumentNullException(nameof(rootBody));
            FlattenTree(root);

            for (int i = 0; i < allBodies.Count; i++)
            {
                bodyIndex[allBodies[i]] = i;
            }

            foreach (OrbitingBody body in allBodies)
            {
                body.SyncMassFromGravitationalParameter();
            }

            ValidateNamesUnique();
            ValidateHillStability();
            ValidateRadii();
            ValidateScale();
            minimumBodyRadius = ComputeMinimumBodyRadius();

            foreach (OrbitingBody body in allBodies)
            {
                body.UpdateInfluenceRadii();
            }
        }

        /// <summary>
        /// Дубликаты имён молча ломают attach по манифесту: обоим телам
        /// подставляется эфемерида первого (аудит S2/2.1). Громко при загрузке.
        /// </summary>
        private void ValidateNamesUnique()
        {
            var names = new HashSet<string>(allBodies.Count, StringComparer.Ordinal);
            foreach (OrbitingBody body in allBodies)
            {
                if (!names.Add(body.Name ?? string.Empty))
                {
                    throw new InvalidOperationException(
                        "Дубликат имени тела: \"" + body.Name + "\" — attach по манифесту и диагностика становятся неоднозначными.");
                }
            }
        }

        /// <summary>
        /// Неположительный или не-конечный радиус — контентный баг, который
        /// молча даёт абсурдную физику (деление на ноль в детекторах высоты,
        /// мгновенный «удар» при первом же IsInsideAnyBody). Громко при загрузке.
        /// Нижний пол радиуса НЕ валидируется сознательно: малые тела
        /// (астероиды) — легитимный контент, а гарантии детекта столкновений
        /// мусора против суб-километровых тел —Capability Tier1 (DebrisUpdater,
        /// MinHopSeconds), не инвариант системы.
        /// </summary>
        private void ValidateRadii()
        {
            foreach (OrbitingBody body in allBodies)
            {
                if (!(body.Radius > 0d) || !double.IsFinite(body.Radius))
                {
                    throw new InvalidOperationException(
                        "Тело \"" + body.Name + "\": радиус обязан быть конечным и положительным, получено " + body.Radius + ".");
                }
            }
        }

        /// <summary>
        /// Верхняя граница масштаба — fp-бюджет double. Позиции — абсолютные
        /// векторы от корня; локальные дельты (высота над поверхностью,
        /// посадочные контакты) вычисляются вычитанием абсолютов, и их точность
        /// = ulp масштаба. Таблица: ulp(4.5e11)=6.1e-5 м, ulp(1e13)≈2e-3 м —
        /// сантиметровый рельеф ещё различим; выше рельеф/высоты «плывут» и
        ///SurfaceMotion-пороги перестают быть осмысленными. Орбита дальше
        /// MaxOrbitRadiusMeters громко отвергается при загрузке, а не даёт
        /// тихую деградацию точности. Граница действует на КАЖДУЮ рельсу
        /// (пер-тельную полуось); суммарный вынос глубоких цепочек не
        /// проверяется — дизайн-запас в 20× от дальнего тела пресета (4.5e11).
        /// </summary>
        public const double MaxOrbitRadiusMeters = 1e13d;

        private void ValidateScale()
        {
            if (root.RootPosition.Magnitude > MaxOrbitRadiusMeters)
            {
                throw new InvalidOperationException(
                    "Корень системы в " + root.RootPosition.Magnitude.ToString("E2") + " м от начала координат — выше fp-бюджета " +
                    MaxOrbitRadiusMeters.ToString("E1") + " м (ulp > 2e-3 м: рельеф и посадочные допуски теряют смысл).");
            }

            foreach (OrbitingBody body in allBodies)
            {
                if (body.Parent != null && body.SemiMajorAxis > MaxOrbitRadiusMeters)
                {
                    throw new InvalidOperationException(
                        "Тело \"" + body.Name + "\": полуось " + body.SemiMajorAxis.ToString("E2") + " м выше fp-бюджета " +
                        MaxOrbitRadiusMeters.ToString("E1") + " м (ulp > 2e-3 м: рельеф и посадочные допуски теряют смысл).");
                }
            }
        }

        private double minimumBodyRadius;

        /// <summary>
        /// Минимальный радиус по всем телам — консервативная оценка для LOD-слоёв
        /// (например, кап прыжков debris от туннелирования: hop ≤ k·R/v). Снимок
        /// на момент конструктора/Revalidate: Radius тел публично мутируем, после
        /// правки радиусов зовите Revalidate (там же громкая валидация).
        /// </summary>
        public double MinimumBodyRadiusMeters => minimumBodyRadius;

        private double ComputeMinimumBodyRadius()
        {
            double min = double.PositiveInfinity;
            for (int i = 0; i < allBodies.Count; i++)
            {
                if (allBodies[i].Radius < min)
                {
                    min = allBodies[i].Radius;
                }
            }

            return min;
        }

        /// <summary>
        /// Громкая валидация долгосрочной стабильности спутников (правило дизайна):
        /// a ≤ 0.3 радиуса Хилла планеты и разнос соседних лун ≥ 3 взаимных R_H.
        /// Предел проградной устойчивости ~0.4-0.5 R_H (Hamilton & Burns и далее);
        /// 0.3 — дизайн-запас. Луна за пределом НЕ взрывается мгновенно — она
        /// хаотична с ляпуновским временем ДО ГОДА (измерено T60/probechaos:
        /// I-2 на 0.54 R_H разбегался на 6.7e10 м за 8 лет), т.е. любая
        /// долговременная эфемерида физически несверяема, а игровой мир
        /// недетерминирован между прогонами. Ловим при загрузке системы, а не
        /// при выпечке: контентный баг — контент и чинит, до пекаря дело
        /// не доходит. Неконические орбиты (a≤0, e≥1) пропускаются — их громко
        /// отвергает кеплерова рельса на оценке состояния.
        /// </summary>
        private void ValidateHillStability()
        {
            var violations = new List<string>();
            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody body = allBodies[i];
                OrbitingBody parent = body.Parent;
                if (parent == null || parent.Parent == null)
                {
                    continue; // корень и планеты (прямые дети звезды) не проверяются
                }

                if (!(body.SemiMajorAxis > 0d) || !(body.Eccentricity >= 0d) || !(body.Eccentricity < 1d))
                {
                    continue;
                }

                double muGrandparent = parent.Parent.ResolveStandardGravitationalParameter();
                double muSubtree = parent.ResolveSubtreeStandardGravitationalParameter();
                if (!(muGrandparent > 0d) || !(muSubtree > 0d) || !(parent.SemiMajorAxis > 0d))
                {
                    continue;
                }

                // R_H = a_P · (μ_subtree(P) / (3·μ_own(G)))^{1/3}
                double hill = parent.SemiMajorAxis * Math.Pow(muSubtree / (3d * muGrandparent), 1d / 3d);
                if (!(hill > 0d))
                {
                    continue;
                }

                if (body.SemiMajorAxis > HillStabilityFraction * hill)
                {
                    violations.Add(string.Format(
                        "{0}: a={1:E2}м > {2:F2}·R_H({3})={4:E2}м",
                        body.Name, body.SemiMajorAxis, HillStabilityFraction, parent.Name, HillStabilityFraction * hill));
                }
            }

            // Разнос сиблингов-лун: Δa ≥ 3 взаимных R_H.
            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody parent = allBodies[i];
                if (parent.Parent == null || parent.Children.Count < 2)
                {
                    continue;
                }

                double muParent = parent.ResolveStandardGravitationalParameter();
                if (!(muParent > 0d))
                {
                    continue;
                }

                for (int a = 0; a < parent.Children.Count; a++)
                {
                    OrbitingBody c1 = parent.Children[a];
                    if (!(c1.SemiMajorAxis > 0d) || !(c1.Eccentricity < 1d))
                    {
                        continue;
                    }

                    for (int b = a + 1; b < parent.Children.Count; b++)
                    {
                        OrbitingBody c2 = parent.Children[b];
                        if (!(c2.SemiMajorAxis > 0d) || !(c2.Eccentricity < 1d))
                        {
                            continue;
                        }

                        double mu1 = c1.ResolveSubtreeStandardGravitationalParameter();
                        double mu2 = c2.ResolveSubtreeStandardGravitationalParameter();
                        double aMean = 0.5d * (c1.SemiMajorAxis + c2.SemiMajorAxis);
                        double mutual = aMean * Math.Pow((mu1 + mu2) / (3d * muParent), 1d / 3d);
                        double gap = Math.Abs(c1.SemiMajorAxis - c2.SemiMajorAxis);
                        if (!(mutual > 0d))
                        {
                            continue;
                        }

                        if (gap < MutualSeparationFactor * mutual)
                        {
                            violations.Add(string.Format(
                                "{0} и {1}: разнос a={2:E2}м < {3:F1}·взаимного R_H={4:E2}м",
                                c1.Name, c2.Name, gap, MutualSeparationFactor, MutualSeparationFactor * mutual));
                        }
                    }
                }
            }

            if (violations.Count > 0)
            {
                throw new InvalidOperationException(
                    "Система нестабильна по правилу Хилла (a ≤ " + HillStabilityFraction.ToString("F2") + " R_H, разнос лун ≥ " +
                    MutualSeparationFactor.ToString("F1") + " взаимных R_H): " + string.Join("; ", violations) +
                    ". Такая конфигурация хаотична с ляпуновским временем до года — раздвиньте/уменьшите орбиты спутников.");
            }
        }

        /// <summary>Дизайн-порог проградной устойчивости спутников (доля R_H).</summary>
        private const double HillStabilityFraction = 0.3d;

        /// <summary>Минимальный разнос соседних лун во взаимных радиусах Хилла.</summary>
        private const double MutualSeparationFactor = 3d;

        public void InvalidatePositionCache()
        {
            cacheValid = false;
            cachedTime = double.NaN;
        }

        /// <summary>
        /// Единственная рекомендуемая точка attach эфемериды: подмена Baked и
        /// инъвалидация кэша атомарно. Прямая запись body.Baked = ... валидна
        /// только ДО первого запроса состояний; после — кэш отдавал бы
        /// закэшированные кеплеровы позиции молча (аудит S3/1.2).
        /// Заменённая файловая эфемерида получает Dispose: держатель FileStream
        /// иначе жил до GC-финализатора и блокировал перезапись файла.
        /// </summary>
        public void AttachEphemeris(OrbitingBody body, BakedEphemeris ephemeris)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (!bodyIndex.ContainsKey(body))
            {
                throw new InvalidOperationException(
                    "Тело " + body.Name + " не принадлежит этой системе — attach чужого тела запрещён.");
            }

            BakedEphemeris previous = body.Baked;
            body.Baked = ephemeris;
            if (previous != null && !ReferenceEquals(previous, ephemeris) && previous.FilePath != null)
            {
                previous.Dispose();
            }

            InvalidatePositionCache();
        }

        /// <summary>
        /// Повтор полного цикла валидации после мутаций конфигурации (μ,
        /// элементы): SyncMass → Hill → Scale → Radii → InfluenceRadii → кэш.
        /// Вне конструктора этот порядок существовал только в чьей-то голове —
        /// правка μ после создания системы оставляла SOI и Hill-вердикты
        /// устаревшими молча (аудит S2/2.3).
        /// СОСТАВ ТЕЛ НЕ ОБНОВЛЯЕТСЯ: FlattenTree/allBodies/bodyIndex строятся
        /// только в конструкторе. Тело, добавленное в дерево после ctor,
        /// не попадёт в гравитацию/детекты/debris молча — новые тела требуют
        /// пересоздания системы.
        /// </summary>
        public void Revalidate()
        {
            foreach (OrbitingBody body in allBodies)
            {
                body.SyncMassFromGravitationalParameter();
            }

            ValidateNamesUnique();
            ValidateHillStability();
            ValidateRadii();
            ValidateScale();
            minimumBodyRadius = ComputeMinimumBodyRadius();

            foreach (OrbitingBody body in allBodies)
            {
                body.UpdateInfluenceRadii();
            }

            InvalidatePositionCache();
        }

        private void FlattenTree(OrbitingBody body)
        {
            if (!visitSet.Add(body))
            {
                // Цикл в иерархии (A.Parent=B, B.Parent=A) — FlattenTree без
                // защиты падает StackOverflow всего процесса (аудит S2/1.1).
                throw new InvalidOperationException(
                    "Цикл в иерархии тел: " + body.Name + " посещён второй раз.");
            }

            allBodies.Add(body);
            foreach (OrbitingBody child in body.Children)
            {
                // Односторонняя починка «задали только Children» оставляем, но
                // КОНФЛИКТ (child.Parent указывает на чужое тело) — громко, а не
                // молчаливая перезапись иерархии.
                if (child.Parent != null && !ReferenceEquals(child.Parent, body))
                {
                    throw new InvalidOperationException(
                        "Тело " + child.Name + " объявлено ребёнком " + body.Name +
                        ", но Parent указывает на " + child.Parent.Name + ".");
                }

                child.Parent = body;
                FlattenTree(child);
            }
        }

        private readonly HashSet<OrbitingBody> visitSet = new HashSet<OrbitingBody>();

        /// <summary>
        /// Гравитационное ускорение в точке shipPosition в момент времени t от ВСЕХ тел
        /// системы сразу. Это ровно та функция, которую передают в OrbitIntegrator.StepForward
        /// как accelerationProvider — один в один как раньше EvaluateShipAcceleration,
        /// только суммирование идёт по всему дереву, а не по "Юпитер + 4 захардкоженные луны".
        /// </summary>
        public Vector3d EvaluateShipAcceleration(Vector3d shipPosition, double timeSeconds)
        {
            Vector3d totalAcceleration = Vector3d.Zero;

            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody body = allBodies[i];
                body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);

                totalAcceleration += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                    shipPosition,
                    bodyPosition,
                    body.ResolveStandardGravitationalParameter());
            }

            return totalAcceleration;
        }

        /// <summary>
        /// Мировые позиции всех тел на момент t за один проход родители-вперёд.
        /// Гранулярность — конкретное время подстадии DOPRI (t, t+c₂h, …), НЕ весь
        /// шаг: стадии видят позиции в своём собственном времени, иначе RK5 тихо
        /// теряет порядок. Экономия — внутри одного RHS-вызова позиция планеты
        /// считается один раз, а не по разу на каждого её спутника (наивный путь
        /// пересчитывает родителя рекурсией для каждого потомка). Результат
        /// бит-в-бит совпадает с наивным: те же сложения в том же порядке.
        /// Плюс мемоизация последнего t: повторный RHS в то же время (FSAL)
        /// переиспользует массив без пересчёта.
        /// </summary>
        public void EnsureCachedPositions(double timeSeconds)
        {
            // Штамп набора эфемерид: прямая запись body.Baked = ... после
            // запросов состояний не зовёт InvalidatePositionCache — без штампа
            // кэш отдавал бы закэшированные кеплеровы позиции молча
            // (аудит S3/1.2). O(n) по телам — на 12 телах это дешевле кэш-промаха.
            long stamp = 0L;
            for (int i = 0; i < allBodies.Count; i++)
            {
                stamp = (stamp * 31) + (allBodies[i].Baked != null ? 1L : 0L) + i;
            }

            if (cacheValid && timeSeconds == cachedTime && cachedPositions != null && cachedPositions.Length == allBodies.Count && stamp == cachedStamp)
            {
                CacheHits++;
                return;
            }

            cachedStamp = stamp;

            CacheMisses++;
            if (cachedPositions == null || cachedPositions.Length != allBodies.Count)
            {
                cachedPositions = new Vector3d[allBodies.Count];
            }

            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody body = allBodies[i];
                if (body.Parent == null)
                {
                    cachedPositions[i] = body.RootPosition;
                    continue;
                }

                body.ComputeLocalOffset(timeSeconds, out Vector3d local, out _);
                int parentIdx = bodyIndex[body.Parent];
                cachedPositions[i] = cachedPositions[parentIdx] + local;
            }

            cachedTime = timeSeconds;
            cacheValid = true;
        }

        /// <summary>
        /// Та же сумма μ/r³, что и EvaluateShipAcceleration, но позиции тел берутся
        /// из кэша подстадии. Используется внутри горячего RHS-цикла.
        /// </summary>
        public Vector3d EvaluateShipAccelerationCached(Vector3d shipPosition, double timeSeconds)
        {
            EnsureCachedPositions(timeSeconds);

            Vector3d total = Vector3d.Zero;
            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody body = allBodies[i];
                total += PhysicsSolver.CalculateAccelerationFromStandardGravitationalParameter(
                    shipPosition,
                    cachedPositions[i],
                    body.ResolveStandardGravitationalParameter());
            }

            return total;
        }

        /// <summary>
        /// Проверка для планировщика: false = точка внутри любого тела,
        /// кандидат трассы invalid. Корабля нет, событию сработать неоткуда.
        /// </summary>
        public bool TryEvaluateAcceleration(Vector3d shipPosition, double timeSeconds, out Vector3d acceleration)
        {
            EnsureCachedPositions(timeSeconds);

            acceleration = Vector3d.Zero;
            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody body = allBodies[i];
                Vector3d bodyPos = cachedPositions[i];
                double sqr = (bodyPos - shipPosition).SqrMagnitude;
                if (sqr < PhysicsSolver.NumericalFloorSqrDistance
                    || PhysicsSolver.IsInsideBodySqr(sqr, body.Radius))
                {
                    acceleration = Vector3d.Zero;
                    return false;
                }

                if (!PhysicsSolver.TryCalculateAccelerationFromStandardGravitationalParameter(
                    shipPosition, bodyPos, body.ResolveStandardGravitationalParameter(), body.Radius, out Vector3d term))
                {
                    acceleration = Vector3d.Zero;
                    return false;
                }

                acceleration += term;
            }

            return acceleration.IsFinite;
        }

        /// <summary>Чистый предикат «внутри тела» — переиспользуется Tier1 без полного драйвера.</summary>
        public bool IsInsideBody(Vector3d shipPosition, OrbitingBody body, double timeSeconds)
        {
            if (body == null)
            {
                return false;
            }

            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
            double sqr = (bodyPosition - shipPosition).SqrMagnitude;
            return PhysicsSolver.IsInsideBodySqr(sqr, body.Radius);
        }

        /// <summary>Внутри любого тела системы (для периодической проверки мусора).</summary>
        public bool IsInsideAnyBody(Vector3d shipPosition, double timeSeconds, out OrbitingBody hitBody)
        {
            EnsureCachedPositions(timeSeconds);
            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody body = allBodies[i];
                double sqr = (cachedPositions[i] - shipPosition).SqrMagnitude;
                if (PhysicsSolver.IsInsideBodySqr(sqr, body.Radius))
                {
                    hitBody = body;
                    return true;
                }
            }

            hitBody = null;
            return false;
        }

        /// <summary>
        /// Корабль внутри атмосферы любого тела: порог — TopAltitudeMeters, тот
        /// же, что у среза DragSource и AltitudeCrossingDetector (один источник
        /// истины). Граница — срезом (alt &lt; top), как везде. Источник правила
        /// «в атмосфере warp не выше ×3» для кадра игры.
        /// </summary>
        public bool IsInsideAtmosphere(Vector3d shipPosition, double timeSeconds)
        {
            EnsureCachedPositions(timeSeconds);
            for (int i = 0; i < allBodies.Count; i++)
            {
                OrbitingBody body = allBodies[i];
                if (body.Atmosphere == null)
                {
                    continue;
                }

                double altitude = (shipPosition - cachedPositions[i]).Magnitude - body.Radius;
                if (altitude < body.Atmosphere.TopAltitudeMeters)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Один шаг интегрирования корабля через DOPRI5 — как в старом StepSimulation,
        /// только accelerationProvider теперь EvaluateShipAcceleration этого класса.
        /// Небесные тела отдельно "продвигать" не нужно: OrbitingBody.EvaluateWorldState
        /// сам вычисляет их состояние по времени на лету, хранимого state нет.
        /// Legacy B9 cluster: живые вызовы идут через SpacecraftPhysics.
        /// </summary>
        [Obsolete("Legacy B9 cluster: use SpacecraftPhysics (SpacecraftOrbitIntegrator). Chain: StepShip -> OrbitIntegrator -> RK4/CelestialBody.")]
#pragma warning disable 0618 // Legacy B9 cluster: internal delegation within obsolete surface.
        public IntegrationResult StepShip(CelestialBody ship, double currentTimeSeconds, double dt)
        {
            return OrbitIntegrator.StepForward(
                ship.Position,
                ship.Velocity,
                currentTimeSeconds,
                dt,
                EvaluateShipAcceleration,
                absoluteTolerance: OrbitIntegrator.DefaultAbsoluteTolerance,
                relativeTolerance: OrbitIntegrator.DefaultRelativeTolerance);
        }
#pragma warning restore 0618

        public double BakedEndSeconds()
        {
            double end = double.PositiveInfinity;
            for (int i = 0; i < allBodies.Count; i++)
            {
                BakedEphemeris b = allBodies[i].Baked;
                if (b != null && b.EndSeconds < end)
                {
                    end = b.EndSeconds;
                }
            }

            return end;
        }

        /// <summary>Положение/скорость произвольного тела в момент t — для рендера, камеры, UI.</summary>
        public void EvaluateBodyState(OrbitingBody body, double timeSeconds, out Vector3d position, out Vector3d velocity)
        {
            body.EvaluateWorldState(timeSeconds, out position, out velocity);
        }

        /// <summary>
        /// Готовый визуальный кадр тела на момент t: позиция (EvaluateWorldState —
        /// рельс/эфемерида), скорость и ориентация body→world (GetVisualOrientation:
        /// собственное вращение + наклон оси одним кватернионом). Рендер-слой
        /// конвертирует в Unity-трансформ через AstroFrame и НЕ лезет во
        /// внутренности представления вращения (SpinAxis/углы/tilt). Конвенция
        /// body-frame зафиксирована в GetVisualOrientation.
        /// </summary>
        public void GetVisualFrame(OrbitingBody body, double timeSeconds, out Vector3d position, out Vector3d velocity, out QuaternionD orientation)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            EvaluateBodyState(body, timeSeconds, out position, out velocity);
            orientation = body.GetVisualOrientation(timeSeconds);
        }

        /// <summary>
        /// Тело, в чьей сфере влияния сейчас находится корабль — самое глубокое (внутреннее)
        /// совпадение: если корабль внутри SOI планеты И внутри SOI её спутника одновременно,
        /// вернётся спутник. Обобщение auto-SOI логики UniverseManager на дерево любой глубины.
        /// </summary>
        public OrbitingBody FindDominantBody(Vector3d shipPosition, double timeSeconds)
        {
            OrbitingBody best = root;
            int bestDepth = -1;
            FindDominantRecursive(root, shipPosition, timeSeconds, 0, ref best, ref bestDepth);
            return best;
        }

        private void FindDominantRecursive(
            OrbitingBody body, Vector3d shipPosition, double timeSeconds,
            int depth, ref OrbitingBody best, ref int bestDepth)
        {
            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
            double distance = (shipPosition - bodyPosition).Magnitude;

            bool insideSoi = body.Parent == null || distance <= body.SphereOfInfluenceRadius;
            if (insideSoi && depth >= bestDepth)
            {
                bestDepth = depth;
                best = body;
            }

            foreach (OrbitingBody child in body.Children)
            {
                FindDominantRecursive(child, shipPosition, timeSeconds, depth + 1, ref best, ref bestDepth);
            }
        }
    }
}
