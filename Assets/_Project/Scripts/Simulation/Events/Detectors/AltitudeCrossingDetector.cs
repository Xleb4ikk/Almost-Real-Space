using System;
using Galilego.Core;
using Galilego.Universe;

namespace Galilego.Events
{
    /// <summary>
    /// Пересечение высотной оболочки вокруг тела:
    /// g = |r_корабль − r_тело(t)| − (Radius + ThresholdAltitude).
    /// Один класс на два порога: вход/выход из атмосферы (порог =
    /// TopAltitudeMeters) и касание поверхности (порог = 0). Позиция тела
    /// вычисляется на лету через EvaluateWorldState — это чтение, не мутация,
    /// поэтому pure-контракт детектора не нарушен. Фабрики ниже задают
    /// осмысленные связки «порог + направление + приоритет», чтобы вызывающему
    /// коду не приходилось собирать их вручную и ошибаться в знаках.
    /// </summary>
    public sealed class AltitudeCrossingDetector : ICrossingDetector
    {
        private readonly OrbitingBody body;
        private readonly double thresholdAltitude;
        private readonly ITerrainModel terrain;

        public EventDirection Direction { get; }

        public int Priority { get; }

        public string Name { get; }

        public EventKind Kind { get; }

        public OrbitingBody Body => body;

        /// <summary>Порог высоты над Radius (м): сфера, чьё пересечение детектится.</summary>
        public double ThresholdAltitude => thresholdAltitude;

        public AltitudeCrossingDetector(
            OrbitingBody body, double thresholdAltitude, EventDirection direction, int priority, string name, EventKind kind)
            : this(body, thresholdAltitude, direction, priority, name, kind, null)
        {
        }

        /// <summary>
        /// Вариант с рельефом: порог отсчитывается от локальной высоты поверхности
        /// (используется для касания: g = |rel| − (R + H(lat,lon))). null — сферический путь.
        /// </summary>
        public AltitudeCrossingDetector(
            OrbitingBody body, double thresholdAltitude, EventDirection direction, int priority, string name, EventKind kind, ITerrainModel terrainModel)
        {
            this.body = body ?? throw new ArgumentNullException(nameof(body));
            this.thresholdAltitude = thresholdAltitude;
            terrain = terrainModel;
            Direction = direction;
            Priority = priority;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Kind = kind;
        }

        /// <summary>Вход в атмосферу сверху вниз (g: + → −).</summary>
        public static AltitudeCrossingDetector ForAtmosphereEntry(OrbitingBody body)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (body.Atmosphere == null)
                throw new ArgumentException("У тела нет атмосферы — вход детектировать не на чем.", nameof(body));
            return new AltitudeCrossingDetector(
                body, body.Atmosphere.TopAltitudeMeters, EventDirection.Falling,
                EventPriorities.Atmosphere, "AtmosphereEntry:" + body.Name, EventKind.AtmosphereEntry);
        }

        /// <summary>Выход из атмосферы снизу вверх (g: − → +).</summary>
        public static AltitudeCrossingDetector ForAtmosphereExit(OrbitingBody body)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (body.Atmosphere == null)
                throw new ArgumentException("У тела нет атмосферы — выход детектировать не на чем.", nameof(body));
            return new AltitudeCrossingDetector(
                body, body.Atmosphere.TopAltitudeMeters, EventDirection.Rising,
                EventPriorities.Atmosphere, "AtmosphereExit:" + body.Name, EventKind.AtmosphereExit);
        }

        /// <summary>Касание поверхности (g: + → − через Radius); рельеф тела учитывается автоматически.</summary>
        public static AltitudeCrossingDetector ForTouchdown(OrbitingBody body)
        {
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            return new AltitudeCrossingDetector(
                body, 0d, EventDirection.Falling,
                EventPriorities.Touchdown, "Touchdown:" + body.Name, EventKind.Touchdown, body.Terrain);
        }

        public double Evaluate(SpacecraftIntegrationState state, double timeSeconds)
        {
            body.EvaluateWorldState(timeSeconds, out Vector3d bodyPosition, out _);
            Vector3d relative = state.Position - bodyPosition;
            if (terrain == null)
            {
                return relative.Magnitude - (body.Radius + thresholdAltitude);
            }

            body.SurfaceLatLonAt(state.Position, timeSeconds, out double latDeg, out double lonDeg);
            double surfaceRadius = body.Radius + thresholdAltitude
                + terrain.GetHeightMeters(body, latDeg * (Math.PI / 180d), lonDeg * (Math.PI / 180d));
            return relative.Magnitude - surfaceRadius;
        }
    }
}
