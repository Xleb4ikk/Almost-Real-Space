using System;
using System.IO;
using Galilego.Core;
using Galilego.Events;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Рантайм-загрузчик испечённых эфемерид. Файлы лежат в
    /// StreamingAssets/&lt;папка&gt; (кладутся инспекторной генерацией, уезжают в
    /// билд автоматически). Файлов нет — тела остаются на кеплеровых рельсах:
    /// это штатная ситуация, не ошибка.
    /// Вызов — только с главного потока (UnityEngine API внутри).
    /// </summary>
    public static class EphemerisRuntime
    {
        /// <summary>Имя подпапки в StreamingAssets по умолчанию (согласовано с инспекторной генерацией).</summary>
        public const string DefaultFolder = "Ephemerides";

        public static bool AttachFromStreamingAssets(StarSystem sys, string folder = DefaultFolder)
        {
            if (sys == null)
            {
                throw new ArgumentNullException(nameof(sys));
            }

            string dir = Path.Combine(Application.streamingAssetsPath, folder);
            if (!Directory.Exists(dir) || !File.Exists(Path.Combine(dir, "manifest.txt")))
            {
                Debug.LogWarning("EphemerisRuntime: испечённые эфемериды не найдены в " + dir +
                    " — тела остаются на кеплеровых рельсах. Сгенерируйте их в инспекторе StarSystemAuthoring.");
                return false;
            }

            EphemerisBaker.AttachFiles(sys, dir);
            return true;
        }

        /// <summary>
        /// Решение кадра ускоренного времени: атмосферный кап ×3, эффективный
        /// фактор, слой исполнения (физика vs дальний варп). Горизонт — конец
        /// испечённого мира; без эфемерид — без ограничений. После решения
        /// caller обязан: при useLongWarp — warp.PrepareForLongWarp(t) и
        /// LongWarpDriver.AdvanceToTarget(t + simSeconds); иначе — физическая
        /// цепочка с EffectiveThrottle. Вызов — только с главного потока.
        /// </summary>
        public static WarpFrameDecision ComputeFrame(
            StarSystem sys, WarpController warp, Vector3d shipPosition, double timeSeconds, double realDtSeconds)
        {
            if (sys == null)
            {
                throw new ArgumentNullException(nameof(sys));
            }

            bool inAtmosphere = sys.IsInsideAtmosphere(shipPosition, timeSeconds);
            double horizon = double.PositiveInfinity;
            double bakedEnd = sys.BakedEndSeconds();
            if (double.IsFinite(bakedEnd))
            {
                horizon = bakedEnd - timeSeconds;
            }

            return WarpPlanner.ComputeFrame(warp, inAtmosphere, realDtSeconds, horizon);
        }
    }
}
