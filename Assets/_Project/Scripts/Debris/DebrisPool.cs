using System;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;

namespace Galilego.Debris
{
    /// <summary>
    /// Один слот мусора. Тело предсоздано в конструкторе пула и никогда не
    /// пересоздаётся через new в цикле обновления — Spawn только перезаписывает
    /// поля Position/Velocity/Mass. Слияние обломков здесь — НЕ физика: если
    /// понадобится визуальное укрупнение, это TODO слоя рендера, не этого файла.
    /// </summary>
    public sealed class DebrisSlot
    {
        public readonly Spacecraft.Spacecraft Body;
        public bool Active;
        public double DeathTimeSeconds;
        public double NextCheckTimeSeconds;
        public long Sequence;

        public DebrisSlot()
        {
            Body = new Spacecraft.Spacecraft(Vector3d.Zero, Vector3d.Zero, 1d);
            Active = false;
            DeathTimeSeconds = double.PositiveInfinity;
            NextCheckTimeSeconds = 0d;
            Sequence = 0;
        }
    }

    /// <summary>
    /// Фиксированный пул на N=256 без аллокаций в горячем пути. Новый обломок
    /// занимает свободный слот или вытесняет самый старый при заполнении.
    /// </summary>
    public sealed class DebrisPool
    {
        public const int Capacity = 256;

        private readonly DebrisSlot[] slots = new DebrisSlot[Capacity];
        private long nextSequence = 1;

        public DebrisPool()
        {
            for (int i = 0; i < Capacity; i++)
            {
                slots[i] = new DebrisSlot();
            }
        }

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < Capacity; i++)
                {
                    if (slots[i].Active)
                    {
                        n++;
                    }
                }

                return n;
            }
        }

        public DebrisSlot SlotAt(int index)
        {
            return slots[index];
        }

        /// <summary>
        /// Занять слот без единого new: переиспользует предсозданное Body.
        /// При заполнении вытесняет самый старый (минимальный Sequence).
        /// Возвращает индекс занятого слота.
        /// </summary>
        public int Spawn(Vector3d position, Vector3d velocity, double mass, double nowSeconds, double lifetimeSeconds)
        {
            int free = -1;
            for (int i = 0; i < Capacity; i++)
            {
                if (!slots[i].Active)
                {
                    free = i;
                    break;
                }
            }

            if (free < 0)
            {
                long oldest = long.MaxValue;
                int oldestIdx = 0;
                for (int i = 0; i < Capacity; i++)
                {
                    if (slots[i].Sequence < oldest)
                    {
                        oldest = slots[i].Sequence;
                        oldestIdx = i;
                    }
                }

                free = oldestIdx;
            }

            DebrisSlot slot = slots[free];
            slot.Body.Position = position;
            slot.Body.Velocity = velocity;
            slot.Body.Mass = mass;
            slot.Active = true;
            slot.DeathTimeSeconds = nowSeconds + lifetimeSeconds;
            slot.NextCheckTimeSeconds = nowSeconds;
            slot.Sequence = nextSequence++;
            return free;
        }

        public void Release(int index)
        {
            slots[index].Active = false;
        }

        public void Clear()
        {
            for (int i = 0; i < Capacity; i++)
            {
                slots[i].Active = false;
            }
        }
    }

    /// <summary>
    /// Tier1-обновление мусора: только баллистика (единственный GravitySource,
    /// масса константна), грубые допуски ×50 от активного корабля, без полной
    /// событийной машины с бисекцией на каждый кадр. Вместо неё — периодическая
    /// проверка IsInsideAnyBody как чистого предиката. Сон по дистанции: далеко
    /// от фокуса — редкие крупные прыжки 10–60с, рядом — мелкие. Точность
    /// сознательно ниже Tier0: у мусора нет геймплейных последствий.
    ///
    /// Антитуннельный кап: проверка IsInsideAnyBody стоит только на КОНЦЕ прыжка,
    /// поэтому за один hop обломок обязан не успевать пересечь тело целиком.
    /// Гарантия: если обломок проходит через хорду длины L со скоростью v, то
    /// интервал времени L/v содержит конец hop'а ⟺ L/v ≥ hop. Кап
    /// hop ≤ k·R_min/v (k=0.25) покрывает все хорды с L ≥ 0.25·R_min, т.е. все
    /// входы с прицельным параметром ≤ 0.99·R — не детектится только
    /// касательный ободок глубиной ~0.8% радиуса (задокументированный остаток).
    /// Для крупных тел (R/v > FarHop/k) кап не работает — LOD-прыжки экономят
    /// как раньше. Пол R_min/v·k ≥ MinHopSeconds иначе: тела мельче
    /// MinHopSeconds·v/k детектятся не гарантированно — граница возможностей
    /// Tier1, валидация масштаба системы отвергает такие тела громко (S2).
    /// </summary>
    public sealed class DebrisUpdater
    {
        public const double NearDistanceMeters = 100000d;
        public const double NearHopSeconds = 1d;
        public const double FarHopSeconds = 30d;
        public const double CoarseFactor = 50d;

        /// <summary>Доля радиуса в антитуннельном капе: hop ≤ 0.25·R/v.</summary>
        public const double TunnelSafetyFactor = 0.25d;

        /// <summary>
        /// Зона досягаемости тела: кап применяется только к телам ближе
        /// ReachabilityRadii·R (иначе далёкие быстрые тела давили бы hop'ы
        /// по всей системе). Из 100·R обломок, летящий с любой разумной
        /// скоростью, добирается за часы — кап успевает включиться заранее.
        /// </summary>
        public const double ReachabilityRadii = 100d;

        /// <summary>Пол прыжка: кап для тел мельче ~MinHop·v/k перестаёт работать.</summary>
        public const double MinHopSeconds = 0.25d;

        /// <summary>
        /// Коэффициент отскока от поверхности (0 = старое поведение: обломок
        /// исчезает при входе в тело). > 0 — упрощённый игровой отскок: позиция
        /// проецируется на радиус тела, нормальная скорость отражается с этим
        /// коэффициентом, тангенциальная гасится TangentialDampFactor раз.
        /// </summary>
        public double SurfaceRestitution;

        /// <summary>Во сколько раз гасится тангенциальная скорость при отскоке.</summary>
        public double TangentialDampFactor = 0.5d;

        private readonly StarSystem starSystem;
        private readonly SpacecraftPhysics physics;

        public DebrisUpdater(StarSystem starSystem)
        {
            this.starSystem = starSystem ?? throw new ArgumentNullException(nameof(starSystem));
            physics = new SpacecraftPhysics
            {
                PositionAbsoluteTolerance = 1e-2d * CoarseFactor,
                VelocityAbsoluteTolerance = 1e-5d * CoarseFactor,
                MassAbsoluteTolerance = 1e-4d * CoarseFactor,
                RelativeTolerance = 1e-10d,
            };
            physics.Sources.Add(new CachedGravitySource(starSystem));
        }

        /// <summary>Шаг прыжка по LOD-дистанции до фокуса (активный корабль/камера).</summary>
        public double HopForDistance(double distanceMeters)
        {
            return distanceMeters < NearDistanceMeters ? NearHopSeconds : FarHopSeconds;
        }

        /// <summary>
        /// Полный hop слота: LOD-прыжок по дистанции до фокуса, сжатый
        /// антитуннельным капом 0.25·R/v по телам в зоне досягаемости
        /// (дистанция &lt; 100·R) с ОТНОСИТЕЛЬНОЙ скоростью к телу (не
        /// инерциальной — обломок рядом с планетой имеет относительные 5 км/с
        /// при инерциальных 30), с полом MinHopSeconds. Знаменатель клампится
        /// снизу параболической скоростью у тела: v_rel → 0 (со-движущийся
        /// обломок) иначе дал бы cap → ∞ и ТИХО отключал кап — а свободное
        /// падение за hop способно разогнать такой обломок как раз до
        /// параболической скорости.
        /// Состояния тел берутся из локального кэша по времени (одно
        /// заполнение на уникальный t вместо per-slot×body вызовов —
        /// AdvanceAll по 256 слотам стоит 12 решений, а не 3072).
        /// </summary>
        public double HopForSlot(DebrisSlot slot, Vector3d focusPosition, double nowSeconds)
        {
            double hop = HopForDistance((slot.Body.Position - focusPosition).Magnitude);
            RefreshBodyStates(nowSeconds);
            for (int i = 0; i < starSystem.AllBodies.Count; i++)
            {
                OrbitingBody body = starSystem.AllBodies[i];
                Vector3d relativePosition = slot.Body.Position - bodyPositionsCache[i];
                if (relativePosition.Magnitude >= ReachabilityRadii * body.Radius)
                {
                    continue;
                }

                double vRel = (slot.Body.Velocity - bodyVelocitiesCache[i]).Magnitude;
                double mu = body.ResolveStandardGravitationalParameter();
                double floorSpeed = mu > 0d
                    ? Math.Sqrt(2d * mu / Math.Max(relativePosition.Magnitude, body.Radius))
                    : 0d;
                if (vRel < floorSpeed)
                {
                    vRel = floorSpeed;
                }

                double cap = TunnelSafetyFactor * body.Radius / vRel;
                if (cap < hop)
                {
                    hop = Math.Max(cap, MinHopSeconds);
                }
            }

            return hop;
        }

        private Vector3d[] bodyPositionsCache;
        private Vector3d[] bodyVelocitiesCache;
        private double bodyStateTime = double.NaN;

        /// <summary>
        /// Локальный кэш состояний тел на момент nowSeconds. Только главный
        /// поток (владелец пула): mutable-состояние без синхронизации.
        /// </summary>
        private void RefreshBodyStates(double nowSeconds)
        {
            int count = starSystem.AllBodies.Count;
            if (bodyPositionsCache == null || bodyPositionsCache.Length != count)
            {
                bodyPositionsCache = new Vector3d[count];
                bodyVelocitiesCache = new Vector3d[count];
                bodyStateTime = double.NaN;
            }

            if (bodyStateTime == nowSeconds)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                starSystem.EvaluateBodyState(starSystem.AllBodies[i], nowSeconds, out bodyPositionsCache[i], out bodyVelocitiesCache[i]);
            }

            bodyStateTime = nowSeconds;
        }

        /// <summary>
        /// Продвинуть один слот к targetTime одним прыжком LOD-размера.
        /// Без аллокаций: только Step + предикат. Возвращает false если слот
        /// освобождён (вошёл в тело или истёк lifetime).
        /// </summary>
        public bool AdvanceSlot(DebrisPool pool, int index, double nowSeconds, Vector3d focusPosition)
        {
            DebrisSlot slot = pool.SlotAt(index);
            if (!slot.Active)
            {
                return false;
            }

            if (nowSeconds >= slot.DeathTimeSeconds)
            {
                pool.Release(index);
                return false;
            }

            double hop = HopForSlot(slot, focusPosition, nowSeconds);

            physics.Step(slot.Body, nowSeconds, hop);

            double checkTime = nowSeconds + hop;
            if (starSystem.IsInsideAnyBody(slot.Body.Position, checkTime, out OrbitingBody hitBody))
            {
                if (!BounceOffSurface(slot, hitBody, checkTime))
                {
                    pool.Release(index);
                    return false;
                }
            }

            if (checkTime >= slot.DeathTimeSeconds)
            {
                pool.Release(index);
                return false;
            }

            slot.NextCheckTimeSeconds = checkTime;
            return true;
        }

        /// <summary>
        /// Упрощённый отскок от поверхности: проекция на радиус тела (сферическая
        /// модель, ITerrainModel рельеф добавит высоту позже), отражение нормали
        /// с restitution, гашение тангенциальной. false — отскок невозможен
        /// (реституция выключена или вырожденная геометрия) → слот освобождается.
        /// </summary>
        private bool BounceOffSurface(DebrisSlot slot, OrbitingBody body, double timeSeconds)
        {
            if (SurfaceRestitution <= 0d || body == null)
            {
                return false;
            }

            if (timeSeconds >= slot.DeathTimeSeconds)
            {
                return false;
            }

            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out Vector3d bodyVelocity);
            Vector3d relative = slot.Body.Position - bodyPosition;
            double distance = relative.Magnitude;
            if (distance <= 0d)
            {
                return false;
            }

            Vector3d normal = relative / distance;
            Vector3d surfaceVelocity = bodyVelocity
                + Vector3d.Cross(body.SpinAxis * body.SpinAngularSpeed, normal * body.Radius);
            Vector3d relativeVelocity = slot.Body.Velocity - surfaceVelocity;
            double normalSpeed = Vector3d.Dot(relativeVelocity, normal);
            Vector3d tangential = relativeVelocity - (normal * normalSpeed);

            slot.Body.Position = bodyPosition + (normal * (body.Radius * 1.001d));
            slot.Body.Velocity = surfaceVelocity
                + (tangential * TangentialDampFactor)
                + (normal * (Math.Abs(normalSpeed) * SurfaceRestitution));
            return true;
        }

        /// <summary>
        /// Продвинуть все активные слоты (вызывать по частям через планировщик
        /// при большом N — этот метод для тестов/малых N).
        /// </summary>
        public void AdvanceAll(DebrisPool pool, double nowSeconds, Vector3d focusPosition)
        {
            for (int i = 0; i < DebrisPool.Capacity; i++)
            {
                if (pool.SlotAt(i).Active)
                {
                    AdvanceSlot(pool, i, nowSeconds, focusPosition);
                }
            }
        }
    }
}
