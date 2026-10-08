using System;
using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Simulation.ContactZone
{
    /// <summary>
    /// Локальная касательная система места зоны контакта: восток, север, вверх в точке
    /// рельефа под кораблём. Физика зоны работает в этих координатах (метры, около якоря),
    /// орбитальное состояние корабля в double не меняется, пока зона не активна.
    /// Координаты вращаются вместе с планетой (тело-фиксированы): ToLocal/ToWorld на одном
    /// и том же моменте обратимы точно; на разных моментах — ровно соответствуют
    /// совместному вращению места.
    /// </summary>
    public sealed class SiteFrame
    {
        public readonly double LatitudeDegrees;
        public readonly double LongitudeDegrees;
        public readonly double TerrainHeightMeters;

        public readonly Vector3d Origin;
        public readonly Vector3d OriginVelocity;
        public readonly Vector3d East;
        public readonly Vector3d North;
        public readonly Vector3d Up;

        /// <summary>Угловая скорость вращения планеты (рад/с), ось — NorthPoleDirection.</summary>
        public readonly Vector3d AngularVelocity;

        private readonly OrbitingBody body;

        private SiteFrame(OrbitingBody body, double latDeg, double lonDeg, double terrainHeight,
            Vector3d origin, Vector3d originVelocity, Vector3d east, Vector3d north, Vector3d up, Vector3d angularVelocity)
        {
            this.body = body;
            AngularVelocity = angularVelocity;
            LatitudeDegrees = latDeg;
            LongitudeDegrees = lonDeg;
            TerrainHeightMeters = terrainHeight;
            Origin = origin;
            OriginVelocity = originVelocity;
            East = east;
            North = north;
            Up = up;
        }

        /// <summary>Якорь: точка рельефа под заданной мировой позицией в момент t.</summary>
        public static SiteFrame Anchor(OrbitingBody body, ITerrainModel terrain, Vector3d worldPosition, double timeSeconds)
        {
            if (body == null || terrain == null)
            {
                throw new ArgumentNullException(body == null ? nameof(body) : nameof(terrain));
            }

            body.SurfaceLatLonAt(worldPosition, timeSeconds, out double latDeg, out double lonDeg);
            double height = terrain.GetHeightMeters(body, KeplerMath.DegreesToRadians(latDeg),
                KeplerMath.DegreesToRadians(lonDeg));
            return At(body, latDeg, lonDeg, height, timeSeconds);
        }

        /// <summary>Тот же участок поверхности (широта, долгота, высота рельефа) в момент t.</summary>
        public SiteFrame At(double timeSeconds)
        {
            return At(body, LatitudeDegrees, LongitudeDegrees, TerrainHeightMeters, timeSeconds);
        }

        private static SiteFrame At(OrbitingBody body, double latDeg, double lonDeg, double terrainHeight, double t)
        {
            body.GetSurfaceState(latDeg, lonDeg, terrainHeight, t, out Vector3d origin, out Vector3d originVel);
            body.EvaluateWorldState(t, out Vector3d bodyPosition, out _);

            Vector3d up = (origin - bodyPosition).Normalized;
            Vector3d spin = body.NorthPoleDirection.Normalized;
            Vector3d east = Vector3d.Cross(spin, up);
            if (east.Magnitude < 1e-9d)
            {
                east = Vector3d.Cross(new Vector3d(1d, 0d, 0d), up);
            }

            east = east.Normalized;
            Vector3d north = Vector3d.Cross(up, east);
            double omega = body.RotationPeriodSeconds > 0d ? (2d * Math.PI) / body.RotationPeriodSeconds : 0d;
            return new SiteFrame(body, latDeg, lonDeg, terrainHeight, origin, originVel, east, north, up, spin * omega);
        }

        /// <summary>
        /// Мировое состояние → локальное (x=восток, y=север, z=вверх). Скорость — относительно
        /// совместного вращения места: покоящееся на поверхности тело имеет нулевую локальную скорость.
        /// </summary>
        public void ToLocal(Vector3d worldPosition, Vector3d worldVelocity, out Vector3d localPosition, out Vector3d localVelocity)
        {
            Vector3d d = worldPosition - Origin;
            Vector3d dv = worldVelocity - (OriginVelocity + Vector3d.Cross(AngularVelocity, d));
            localPosition = new Vector3d(Vector3d.Dot(d, East), Vector3d.Dot(d, North), Vector3d.Dot(d, Up));
            localVelocity = new Vector3d(Vector3d.Dot(dv, East), Vector3d.Dot(dv, North), Vector3d.Dot(dv, Up));
        }

        /// <summary>Локальное состояние → мировое в этом же кадре места (момент, для которого построен кадр).</summary>
        public void ToWorld(Vector3d localPosition, Vector3d localVelocity, out Vector3d worldPosition, out Vector3d worldVelocity)
        {
            Vector3d d = (East * localPosition.X) + (North * localPosition.Y) + (Up * localPosition.Z);
            worldPosition = Origin + d;
            worldVelocity = OriginVelocity + Vector3d.Cross(AngularVelocity, d)
                + (East * localVelocity.X) + (North * localVelocity.Y) + (Up * localVelocity.Z);
        }
    }
}
