using System;
using System.Collections.Generic;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Доступ к собранной системе ТЕЛЯ ВНЕ Play-режима: фрейм авторинга,
    /// превью рельефа и SurfacePlaced должны знать те же тела, тот же рельеф и
    /// ту же зернистость, что и рантайм, иначе превью врёт.
    ///
    /// Путь ровно тот же, что у SimulationRunner.Awake: BuildBlueprint() (чистые
    /// данные из иерархии сцены) → Build() (HeightfieldTerrain из профиля+сида).
    /// Отдельной «редакторной» модели нет намеренно: одна правда, иначе между
    /// тем, что видно в сцене, и тем, во что игрок падает, будет расхождение.
    ///
    /// Кэш по подписи сцены. Подпись — хеш скалярных полей блюпринта плюс
    /// ключевые параметры рельефа: пересборка на каждый Update редактора ушла бы
    /// в горячий цикл, а без кэша правка сида не подхватывалась бы.
    /// </summary>
    public static class SurfaceSceneSystem
    {
        private static StarSystem cached;
        private static long cachedSignature;
        private static bool cachedValid;
        private static string lastError;
        private static double lastCheckTime = -1d;

        /// <summary>Как часто проверять, не протух ли кэш, секунды.</summary>
        private const double RevalidateIntervalSeconds = 0.5d;

        /// <summary>Система, собранная из текущей сцены; null при ошибке (см. LastError).</summary>
        public static StarSystem System
        {
            get
            {
                if (cached == null)
                {
                    Rebuild();
                    return cached;
                }

                // Подпись проверяется ЗДЕСЬ, а не только внутри Rebuild: иначе
                // протухший кэш вечен. Наблюдалось: первая сборка после входа в
                // Play успела застать сцену, где под StarSystemAuthoring ещё не
                // подъехал Terra, подпись совпала — и кэш остался с одним Sol
                // навсегда, а TryResolve("Terra") молча возвращал false.
                double now = Time.realtimeSinceStartup;
                if (now - lastCheckTime >= RevalidateIntervalSeconds)
                {
                    lastCheckTime = now;
                    if (Stale())
                    {
                        Rebuild();
                    }
                }

                return cached;
            }
        }

        /// <summary>Кэш не соответствует текущей сцене (по числу тел и по подписи).</summary>
        private static bool Stale()
        {
            BodyAuthoring[] found = UnityEngine.Object.FindObjectsByType<BodyAuthoring>(
                FindObjectsInactive.Include);
            if (found.Length != cached.AllBodies.Count)
            {
                return true;
            }

            StarSystemAuthoring[] roots = UnityEngine.Object.FindObjectsByType<StarSystemAuthoring>(
                FindObjectsInactive.Include);
            if (roots == null || roots.Length == 0)
            {
                return false;
            }

            try
            {
                return Signature(roots[0].BuildBlueprint()) != cachedSignature;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string LastError => lastError;

        /// <summary>Сбросить кэш (смена сцены, перезагрузка домена).</summary>
        public static void Invalidate()
        {
            cachedValid = false;
            cached = null;
            cachedSignature = 0L;
        }

        private static void Rebuild()
        {
            cachedValid = true;
            cached = null;
            lastError = null;

            StarSystemAuthoring[] roots = UnityEngine.Object.FindObjectsByType<StarSystemAuthoring>(FindObjectsInactive.Include);
            if (roots == null || roots.Length == 0)
            {
                lastError = "В сцене нет StarSystemAuthoring — нечего собирать.";
                return;
            }

            try
            {
                SystemBlueprint blueprint = roots[0].BuildBlueprint();
                long signature = Signature(blueprint);
                if (cached != null && signature == cachedSignature)
                {
                    return;
                }

                // Таблицы площадок у старой системы Persistent — без освобождения
                // каждая пересборка в редакторе (а она происходит на каждую
                // правку рельефа) текла бы на несколько килобайт. Пересборка
                // редкая, так что ждать конца кадра безопаснее, чем держать
                // второй си��тем: он всё равно не используется.
                DisposeTerrainModifiers(cached);
                cached = blueprint.Build();
                cachedSignature = signature;
            }
            catch (Exception e)
            {
                cached = null;
                lastError = e.Message;
            }
        }

        /// <summary>Освободить таблицы площадок всем телам системы (Persistent).</summary>
        private static void DisposeTerrainModifiers(StarSystem system)
        {
            if (system == null)
            {
                return;
            }

            for (int i = 0; i < system.AllBodies.Count; i++)
            {
                (system.AllBodies[i].Terrain as HeightfieldTerrain)?.DisposeModifiers();
            }
        }

        /// <summary>
        /// Хеш всего, что влияет на тела и форму рельефа. Подробные поля
        /// TerrainProfile сюда не входят: правка TerrainProfileAsset меняет
        /// инстанс ассета, а не этот блюпринт, и пересборку для такого случая
        /// дёргает явно (кнопка в инспекторе фрейма / перестроить превью).
        /// </summary>
        private static long Signature(SystemBlueprint blueprint)
        {
            unchecked
            {
                long hash = 17L;
                if (blueprint.Bodies == null)
                {
                    return hash;
                }

                hash = (hash * 31L) + blueprint.Bodies.Count;
                foreach (BodyBlueprint b in blueprint.Bodies)
                {
                    hash = Mix(hash, b.Name);
                    hash = Mix(hash, b.Radius);
                    hash = Mix(hash, b.StandardGravitationalParameter);
                    hash = Mix(hash, b.SemiMajorAxis);
                    hash = Mix(hash, b.Eccentricity);
                    hash = Mix(hash, b.InclinationDegrees);
                    hash = Mix(hash, b.LongitudeOfAscendingNodeDegrees);
                    hash = Mix(hash, b.ArgumentOfPeriapsisDegrees);
                    hash = Mix(hash, b.MeanAnomalyAtEpochDegrees);
                    hash = Mix(hash, b.EpochTimeSeconds);
                    hash = Mix(hash, b.RotationPeriodSeconds);
                    hash = Mix(hash, b.PrimeMeridianOffsetDegrees);
                    hash = Mix(hash, b.NorthPoleX);
                    hash = Mix(hash, b.NorthPoleY);
                    hash = Mix(hash, b.NorthPoleZ);
                    hash = Mix(hash, b.ParentIndex);
                    hash = Mix(hash, b.TidallyLocked);
                    hash = Mix(hash, b.TerrainSeed);
                    hash = Mix(hash, b.Terrain == null ? 0d : b.Terrain.AmplitudeMeters);
                    hash = Mix(hash, b.Terrain == null ? 0d : b.Terrain.BaseFrequency);
                    hash = Mix(hash, b.Terrain == null ? 0d : b.Terrain.Octaves);
                    hash = Mix(hash, b.Terrain == null ? 0d : b.Terrain.SeaLevelMeters);
                    hash = Mix(hash, b.Terrain == null ? 0d : b.Terrain.ContinentFrequency);
                    hash = Mix(hash, b.Terrain == null ? 0d : b.Terrain.WarpStrength);

                    // Площадки в подписи обязательны: без них правка радиуса или
                    // высоты площадки не пересобрала бы систему, и превью
                    // продолжало бы показывать старую землю, тогда как Play уже
                    // считал бы новую. Молчаливое расхождение визуал/физика.
                    if (b.TerrainModifiers != null)
                    {
                        hash = Mix(hash, b.TerrainModifiers.Length);
                        for (int i = 0; i < b.TerrainModifiers.Length; i++)
                        {
                            TerrainModifier pad = b.TerrainModifiers[i];
                            hash = Mix(hash, pad.LatitudeDegrees);
                            hash = Mix(hash, pad.LongitudeDegrees);
                            hash = Mix(hash, pad.InnerRadiusMeters);
                            hash = Mix(hash, pad.OuterRadiusMeters);
                            hash = Mix(hash, pad.TargetHeightMeters);
                            hash = Mix(hash, pad.OverridesSeaLevel);
                        }
                    }
                }

                return hash;
            }
        }

        private static long Mix(long hash, string value)
        {
            if (value == null)
            {
                return (hash * 31L) + 1L;
            }

            foreach (char c in value)
            {
                hash = (hash * 31L) + c;
            }

            return (hash * 31L) + 2L;
        }

        private static long Mix(long hash, double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            if (bits == long.MinValue)
            {
                bits = 0L;
            }

            return (hash * 31L) + bits;
        }

        private static long Mix(long hash, bool value)
        {
            return (hash * 31L) + (value ? 3L : 4L);
        }

        /// <summary>
        /// Тело по GameObject'у ассета авторинга. Имя GO — ключ связи во всём
        /// проекте (BodyView/PlanetSurfaceRenderer/PlanetCloudsView ищут тело по
        /// нему), поэтому и здесь только имя, без ручных ссылок.
        /// </summary>
        public static bool TryResolve(BodyAuthoring authoring, out OrbitingBody body, out HeightfieldTerrain terrain)
        {
            body = null;
            terrain = null;
            return authoring != null && TryResolve(authoring.gameObject.name, out body, out terrain);
        }

        /// <summary>Тело по имени GameObject'а (меню и превью работают без ссылки на ассет).</summary>
        public static bool TryResolve(string bodyName, out OrbitingBody body, out HeightfieldTerrain terrain)
        {
            body = null;
            terrain = null;
            if (string.IsNullOrEmpty(bodyName))
            {
                return false;
            }

            StarSystem system = System;
            if (system == null)
            {
                return false;
            }

            foreach (OrbitingBody candidate in system.AllBodies)
            {
                if (candidate.Name == bodyName)
                {
                    body = candidate;
                    terrain = candidate.Terrain as HeightfieldTerrain;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Высота рельефа в точке, метры над радиусом, с клампом уровня моря —
        /// так же, как SimulationRunner.GroundHeight, чтобы превью и посадка
        /// игрока считали одну и ту же землю. null Terrain = гладкая сфера.
        /// </summary>
        public static double GroundHeight(OrbitingBody body, double latitudeDegrees, double longitudeDegrees)
        {
            HeightfieldTerrain terrain = body?.Terrain as HeightfieldTerrain;
            if (terrain == null)
            {
                return 0d;
            }

            return terrain.GetHeightMeters(body, latitudeDegrees * (Math.PI / 180d), longitudeDegrees * (Math.PI / 180d));
        }

        /// <summary>Все BodyAuthoring сцены — для меню и списков выбора тела.</summary>
        public static List<BodyAuthoring> AllAuthorings()
        {
            var result = new List<BodyAuthoring>();
            result.AddRange(UnityEngine.Object.FindObjectsByType<BodyAuthoring>(FindObjectsInactive.Include));
            return result;
        }
    }
}
