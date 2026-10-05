using System;
using Galilego.Core;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Геометрия поверхности тела. Жёсткий контракт (п.5/12 v3): высота h и
    /// нормаль n относятся К ОДНОЙ И ТОЙ ЖЕ геометрии — реализация обязана
    /// выводить n из той же функции формы, что и h. Рассинхрон «поверхность
    /// из одного места, нормаль из другого» запрещён: контактная точка
    /// (проекция) и разложение сил обязаны согласовываться, иначе трение
    /// держит там, где проекция уже отпустила, или наоборот.
    /// Реализации: SphericalTerrain (гладкая сфера) и HeightfieldTerrain
    /// (процедурный fBm-рельеф + уровень моря).
    /// </summary>
    public interface ITerrainModel
    {
        /// <summary>
        /// Высота поверхности над Radius (м) в тел-fixed координатах
        /// (lat/lon в радианах — те же, что выдаёт OrbitingBody.SurfaceLatLonAt).
        /// Вращение тела учтено самим фактом body-fixed координат.
        /// Включает кламп морем: в океане возвращает уровень моря (плоская
        /// вода для посадки кораблей и детекторов касания).
        /// </summary>
        double GetHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians);

        /// <summary>
        /// СЫРАЯ высота рельефа над Radius (м) БЕЗ клампа морем: в океане —
        /// глубина дна (отрицательная относительно уровня моря). Единственный
        /// путь узнать настоящее дно (для плавания, водной геометрии, глубины).
        /// </summary>
        double GetRawHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians);

        /// <summary>
        /// Уровень моря над Radius (м). NegativeInfinity (и любое ≤ −1e29) =
        /// моря нет. Совпадает с порогом, по которому рендер красит воду.
        /// </summary>
        double GetSeaLevelMeters();

        /// <summary>
        /// Внешняя нормаль поверхности в точке relPos (относительно центра тела,
        /// мировые координаты) в момент timeSeconds. Время обязательно: рельеф
        /// вращается вместе с телом, нормаль в мировом базисе зависит от спина.
        /// </summary>
        Vector3d GetOutwardNormal(OrbitingBody body, Vector3d relativePosition, double timeSeconds);
    }

    /// <summary>
    /// Гладкая сфера: h≡0, нормаль — радиальная от центра тела. Точна по
    /// построению (не приближение): относительная позиция уже лежит на сфере.
    /// Без полюсов и тригонометрии — только нормализация.
    /// </summary>
    public sealed class SphericalTerrain : ITerrainModel
    {
        public double GetHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians)
        {
            return 0d;
        }

        public double GetRawHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians)
        {
            return 0d;
        }

        public double GetSeaLevelMeters()
        {
            return double.NegativeInfinity;
        }

        public Vector3d GetOutwardNormal(OrbitingBody body, Vector3d relativePosition, double timeSeconds)
        {
            return relativePosition.Normalized;
        }
    }
}
