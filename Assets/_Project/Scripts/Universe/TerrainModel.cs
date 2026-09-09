using System;
using Galilego.Core;

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
        /// </summary>
        double GetHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians);

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

        public Vector3d GetOutwardNormal(OrbitingBody body, Vector3d relativePosition, double timeSeconds)
        {
            return relativePosition.Normalized;
        }
    }

    /// <summary>
    /// Процедурный рельеф: fBm value-noise на ТЕЛЕ-FIXED единичном направлении
    /// (не lat/lon-текстура — без швов и вырождений на полюсах). Детерминирован
    /// seed'ом: физика и рендер читают одну и ту же функцию.
    ///   H(lat,lon) = fBm(dir(lat,lon))·Amplitude, кламп снизу уровнем моря.
    /// Море — жёсткий кламп: море физически плоское, посадка на воду = посадка.
    /// Нормаль — из той же функции H через соседние точки, но мировые позиции
    /// соседей берутся body.GetSurfaceState (прямой маппинг lat/lon→world:
    /// tilt+spin учтены ЯДРОМ, не дубликатом математики): паритет по построению.
    /// </summary>
    public sealed class HeightfieldTerrain : ITerrainModel
    {
        /// <summary>Зерно шума: одинаковый seed = одинаковый рельеф.</summary>
        public int Seed;

        /// <summary>Амплитуда рельефа над/под Radius (м).</summary>
        public double AmplitudeMeters = 1000d;

        /// <summary>Базовая частота (циклов на единичный вектор направления).</summary>
        public double BaseFrequency = 3d;

        /// <summary>Число октав (каждая ×2 частота, ×0.5 амплитуда).</summary>
        public int Octaves = 5;

        /// <summary>Уровень моря над Radius (м). −∞ = моря нет.</summary>
        public double SeaLevelMeters = double.NegativeInfinity;

        /// <summary>Угловой шаг соседей для нормали (рад): разрешает 5 октав с запасом.</summary>
        private const double NormalEpsilonRadians = 1e-4d;

        public double GetHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians)
        {
            Vector3d direction = LatLonToDirection(latitudeRadians, longitudeRadians);
            double height = SampleFbm(direction) * AmplitudeMeters;
            if (height < SeaLevelMeters)
            {
                height = SeaLevelMeters;
            }

            return height;
        }

        public Vector3d GetOutwardNormal(OrbitingBody body, Vector3d relativePosition, double timeSeconds)
        {
            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
            body.SurfaceLatLonAt(bodyPosition + relativePosition, timeSeconds, out double latDeg, out double lonDeg);
            double lat = latDeg * (Math.PI / 180d);
            double lon = lonDeg * (Math.PI / 180d);

            double latUp = Math.Min(lat + NormalEpsilonRadians, 1.5707963267948966d - 1e-9d);
            double h0 = GetHeightMeters(body, lat, lon);
            double hUp = GetHeightMeters(body, latUp, lon);
            double hEast = GetHeightMeters(body, lat, lon + NormalEpsilonRadians);

            body.GetSurfaceState(lat * (180d / Math.PI), lon * (180d / Math.PI), h0, timeSeconds, out Vector3d p0, out _);
            body.GetSurfaceState(latUp * (180d / Math.PI), lon * (180d / Math.PI), hUp, timeSeconds, out Vector3d p1, out _);
            body.GetSurfaceState(lat * (180d / Math.PI), (lon + NormalEpsilonRadians) * (180d / Math.PI), hEast, timeSeconds, out Vector3d p2, out _);

            Vector3d normal = Vector3d.Cross(p1 - p0, p2 - p0).Normalized;
            if (Vector3d.Dot(normal, relativePosition) < 0d)
            {
                normal = -normal;
            }

            return normal;
        }

        /// <summary>Тел-fixed направление из lat/lon (радианы) — тот же базис, что в GetSurfaceState.</summary>
        private static Vector3d LatLonToDirection(double lat, double lon)
        {
            double cosLat = Math.Cos(lat);
            return new Vector3d(cosLat * Math.Cos(lon), cosLat * Math.Sin(lon), Math.Sin(lat));
        }

        /// <summary>
        /// fBm value-noise на единичном направлении, нормирован в ~[−1, 1].
        /// Октавный сдвиг решётки декоррелирует уровни без аллокаций.
        /// </summary>
        private double SampleFbm(Vector3d direction)
        {
            int octaves = Math.Max(1, Octaves);
            double amplitude = 1d;
            double frequency = Math.Max(1e-6d, BaseFrequency);
            double sum = 0d;
            double norm = 0d;
            Vector3d offset = new Vector3d(Seed * 17.31d, Seed * 7.77d, Seed * 29.13d);
            for (int o = 0; o < octaves; o++)
            {
                sum += amplitude * ValueNoise(direction * frequency + offset);
                norm += amplitude;
                amplitude *= 0.5d;
                frequency *= 2d;
                offset = new Vector3d(offset.Y + 19.19d, offset.Z + 7.47d, offset.X + 3.13d);
            }

            return norm > 0d ? sum / norm : 0d;
        }

        /// <summary>Трилинейный value-noise с quintic-сглаживанием, диапазон ~[−1, 1].</summary>
        private static double ValueNoise(Vector3d p)
        {
            int ix = (int)Math.Floor(p.X);
            int iy = (int)Math.Floor(p.Y);
            int iz = (int)Math.Floor(p.Z);
            double fx = p.X - ix;
            double fy = p.Y - iy;
            double fz = p.Z - iz;
            double ux = Quintic(fx);
            double uy = Quintic(fy);
            double uz = Quintic(fz);

            double c000 = LatticeValue(ix, iy, iz);
            double c100 = LatticeValue(ix + 1, iy, iz);
            double c010 = LatticeValue(ix, iy + 1, iz);
            double c110 = LatticeValue(ix + 1, iy + 1, iz);
            double c001 = LatticeValue(ix, iy, iz + 1);
            double c101 = LatticeValue(ix + 1, iy, iz + 1);
            double c011 = LatticeValue(ix, iy + 1, iz + 1);
            double c111 = LatticeValue(ix + 1, iy + 1, iz + 1);

            double x00 = c000 + (ux * (c100 - c000));
            double x10 = c010 + (ux * (c110 - c010));
            double x01 = c001 + (ux * (c101 - c001));
            double x11 = c011 + (ux * (c111 - c011));
            double y0 = x00 + (uy * (x10 - x00));
            double y1 = x01 + (uy * (x11 - x01));
            return y0 + (uz * (y1 - y0));
        }

        private static double Quintic(double t)
        {
            return t * t * t * (t * ((t * 6d) - 15d) + 10d);
        }

        /// <summary>Целочисленный хеш решётки → значение в [−1, 1]. Детерминирован бит-в-бит.</summary>
        private static double LatticeValue(int x, int y, int z)
        {
            unchecked
            {
                int h = (x * 374761393) + (y * 668265263) + (z * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return ((h & 0xFFFF) / 32767.5d) - 1d;
            }
        }
    }
}
