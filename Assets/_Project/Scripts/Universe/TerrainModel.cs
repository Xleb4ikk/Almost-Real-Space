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
    /// Сейчас единственная реализация — сферическая (h≡0, n≡радиаль);
    /// heightfield рельефа позже = только новая реализация, потребители
    /// (SurfaceMotion, touchdown) не меняются.
    /// </summary>
    public interface ITerrainModel
    {
        double GetHeightMeters(OrbitingBody body, double latitudeRadians, double longitudeRadians);

        Vector3d GetOutwardNormal(OrbitingBody body, Vector3d relativePosition);
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

        public Vector3d GetOutwardNormal(OrbitingBody body, Vector3d relativePosition)
        {
            return relativePosition.Normalized;
        }
    }
}
