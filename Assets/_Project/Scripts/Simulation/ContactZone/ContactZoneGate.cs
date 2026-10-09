using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Simulation.ContactZone
{
    /// <summary>
    /// Решение о входе корабля в зону контакта. Зона — только посадка: вход возможен лишь при
    /// малой высоте над рельефом и малой скорости относительно поверхности. Орбитальный и
    /// входной режимы (высокая скорость) зону не включают по построению. Гистерезис:
    /// вход (h &lt; Enter и v &lt; EnterSpeed), выход (h &gt; Exit или v &gt; ExitSpeed).
    /// Класс не меняет состояние корабля: Update получает позицию и скорость по значению.
    /// </summary>
    public sealed class ContactZoneGate
    {
        public double EnterAltitudeMeters { get; set; } = 300d;
        public double ExitAltitudeMeters { get; set; } = 400d;
        public double EnterSpeedMps { get; set; } = 60d;
        public double ExitSpeedMps { get; set; } = 120d;

        public bool Inside { get; private set; }

        /// <summary>Высота над рельефом и скорость относительно поверхности (co-rotating).</summary>
        public static void Measure(OrbitingBody body, ITerrainModel terrain, Vector3d position, Vector3d velocity,
            double timeSeconds, out double altitudeAboveTerrain, out double relativeSpeed)
        {
            body.SurfaceLatLonAt(position, timeSeconds, out double latDeg, out double lonDeg);
            double terrainHeight = terrain.GetHeightMeters(body, KeplerMath.DegreesToRadians(latDeg),
                KeplerMath.DegreesToRadians(lonDeg));
            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
            double radial = (position - bodyPosition).Magnitude - body.Radius;
            altitudeAboveTerrain = radial - terrainHeight;

            body.GetSurfaceState(latDeg, lonDeg, radial, timeSeconds, out _, out Vector3d surfaceVelocity);
            relativeSpeed = (velocity - surfaceVelocity).Magnitude;
        }

        /// <summary>Обновить решение на текущем кадре. Возвращает true, если зона активна.</summary>
        public bool Update(OrbitingBody body, ITerrainModel terrain, Vector3d position, Vector3d velocity, double timeSeconds)
        {
            Measure(body, terrain, position, velocity, timeSeconds, out double altitude, out double speed);
            if (!Inside)
            {
                if (altitude < EnterAltitudeMeters && speed < EnterSpeedMps)
                {
                    Inside = true;
                }
            }
            else if (altitude > ExitAltitudeMeters || speed > ExitSpeedMps)
            {
                Inside = false;
            }

            return Inside;
        }

        public void Reset()
        {
            Inside = false;
        }
    }
}
