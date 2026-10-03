using System;
using Galilego.Core;

namespace Galilego.Universe
{
    /// <summary>
    /// Запросы воды планеты — ЕДИНСТВЕННЫЙ источник правды о море.
    /// Разделяет два понятия, которые раньше сливались в клампе:
    ///   - поверхность воды (плоская, Radius + SeaLevel) — для плавания,
    ///     приводнения, подводных эффектов;
    ///   - дно (сырая высота БЕЗ клампа) — для глубины, геометрии океана,
    ///     посадки на дно при нырянии.
    /// GetHeightMeters (клампнутая) остаётся для кораблей/детекторов: посадка
    /// на воду = посадка на плоскую твердь уровня моря (мягкое приводнение —
    /// будущий этап, см. GetSplashdownInfo).
    /// Чистые функции без UnityEngine — покрываются стендом P1bTests.
    /// </summary>
    public static class WaterQuery
    {
        /// <summary>
        /// Порог «моря нет»: уровень моря ≤ этого — океан отсутствует.
        /// Профиль использует −1e30, рантайм — NegativeInfinity.
        /// </summary>
        public const double NoOceanSentinel = -1e29d;

        /// <summary>Есть ли на теле океан (уровень моря задан).</summary>
        public static bool HasOcean(OrbitingBody body)
        {
            return body != null && body.Terrain != null
                && body.Terrain.GetSeaLevelMeters() > NoOceanSentinel;
        }

        /// <summary>Уровень моря над Radius (м). false — моря нет.</summary>
        public static bool TryGetSeaLevel(OrbitingBody body, out double seaLevelMeters)
        {
            if (HasOcean(body))
            {
                seaLevelMeters = body.Terrain.GetSeaLevelMeters();
                return true;
            }

            seaLevelMeters = double.NaN;
            return false;
        }

        /// <summary>
        /// Сырая высота дна/суши над Radius (м) БЕЗ клампа морем.
        /// NaN — нет террейна.
        /// </summary>
        public static double RawSeabedHeightAt(OrbitingBody body, double latitudeDegrees, double longitudeDegrees)
        {
            if (body == null || body.Terrain == null)
            {
                return double.NaN;
            }

            return body.Terrain.GetRawHeightMeters(
                body,
                latitudeDegrees * (Math.PI / 180d),
                longitudeDegrees * (Math.PI / 180d));
        }

        /// <summary>
        /// Глубина воды в точке (м, ≥ 0): уровень моря минус сырое дно.
        /// 0 — суша, мелководье у берега или моря нет.
        /// </summary>
        public static double WaterDepthAt(OrbitingBody body, double latitudeDegrees, double longitudeDegrees)
        {
            if (!TryGetSeaLevel(body, out double sea))
            {
                return 0d;
            }

            double raw = RawSeabedHeightAt(body, latitudeDegrees, longitudeDegrees);
            if (double.IsNaN(raw))
            {
                return 0d;
            }

            return Math.Max(0d, sea - raw);
        }

        /// <summary>
        /// Вода ли в точке (сырое дно ниже уровня моря). Строгое сравнение:
        /// рендер добавляет визуальный эпсилон (amp·0.001) для сглаживания
        /// кромки, физика — нет.
        /// </summary>
        public static bool IsWaterAt(OrbitingBody body, double latitudeDegrees, double longitudeDegrees)
        {
            return WaterDepthAt(body, latitudeDegrees, longitudeDegrees) > 0d;
        }

        /// <summary>
        /// Радиус водной поверхности от центра тела (м). NaN — моря нет.
        /// </summary>
        public static double SeaSurfaceRadius(OrbitingBody body)
        {
            if (!TryGetSeaLevel(body, out double sea))
            {
                return double.NaN;
            }

            return body.Radius + sea;
        }

        /// <summary>
        /// Погружение точки мира под воду (м): радиус поверхности минус
        /// дистанция до центра тела. &gt; 0 — под водой, ≤ 0 — над водой.
        /// NaN — моря нет.
        /// </summary>
        public static double SubmersionDepthAt(OrbitingBody body, Vector3d worldPosition, double timeSeconds)
        {
            double surface = SeaSurfaceRadius(body);
            if (double.IsNaN(surface))
            {
                return double.NaN;
            }

            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPos, out _);
            return surface - (worldPosition - bodyPos).Magnitude;
        }

        public static bool IsSubmergedAt(
            OrbitingBody body,
            Vector3d worldPosition,
            double timeSeconds,
            out double depthMeters)
        {
            depthMeters = SubmersionDepthAt(body, worldPosition, timeSeconds);
            if (double.IsNaN(depthMeters) || depthMeters <= 0d)
            {
                return false;
            }

            body.SurfaceLatLonAt(
                worldPosition, timeSeconds, out double latitudeDegrees, out double longitudeDegrees);
            return IsWaterAt(body, latitudeDegrees, longitudeDegrees);
        }

        /// <summary>
        /// Высота точки мира над СЫРЫМ дном (м): дистанция до центра минус
        /// радиус дна в этой lat/lon. Для упора при нырянии (в отличие от
        /// клампнутой высоты, которая в океане всегда «уровень моря»).
        /// NaN — нет террейна.
        /// </summary>
        public static double ClearanceAboveSeabedAt(OrbitingBody body, Vector3d worldPosition, double timeSeconds)
        {
            if (body == null || body.Terrain == null)
            {
                return double.NaN;
            }

            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPos, out _);
            body.SurfaceLatLonAt(worldPosition, timeSeconds, out double latDeg, out double lonDeg);
            double raw = RawSeabedHeightAt(body, latDeg, lonDeg);
            if (double.IsNaN(raw))
            {
                return double.NaN;
            }

            return (worldPosition - bodyPos).Magnitude - (body.Radius + raw);
        }

        /// <summary>
        /// Информация для будущего мягкого приводнения (пока только данные,
        /// поведение не меняет: посадка идёт по клампнутой высоте).
        /// </summary>
        public struct SplashdownInfo
        {
            /// <summary>Точка над водой (сырое дно ниже моря).</summary>
            public bool IsWater;

            /// <summary>Глубина воды (м, ≥ 0).</summary>
            public double WaterDepthMeters;

            /// <summary>Уровень моря над Radius (м, NaN — моря нет).</summary>
            public double SeaLevelMeters;

            /// <summary>Сырое дно над Radius (м, NaN — нет террейна).</summary>
            public double SeabedHeightMeters;
        }

        /// <summary>Сводка воды в точке для логики приводнения.</summary>
        public static SplashdownInfo GetSplashdownInfo(OrbitingBody body, double latitudeDegrees, double longitudeDegrees)
        {
            var info = new SplashdownInfo
            {
                IsWater = false,
                WaterDepthMeters = 0d,
                SeaLevelMeters = double.NaN,
                SeabedHeightMeters = RawSeabedHeightAt(body, latitudeDegrees, longitudeDegrees)
            };
            if (TryGetSeaLevel(body, out double sea) && !double.IsNaN(info.SeabedHeightMeters))
            {
                info.SeaLevelMeters = sea;
                info.WaterDepthMeters = Math.Max(0d, sea - info.SeabedHeightMeters);
                info.IsWater = info.WaterDepthMeters > 0d;
            }

            return info;
        }
    }
}
