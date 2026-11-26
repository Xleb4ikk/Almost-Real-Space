using System;
using Galilego.Core;

namespace Galilego.Planning
{
    /// <summary>Мир для планировщика (адаптер над StarSystem игры; в тестах — синтетическая система).</summary>
    public interface IPlannerWorld
    {
        double UniverseNow { get; }
        int BodyCount { get; }
        string BodyName(int i);
        /// <summary>Индекс родителя или −1 у корня.</summary>
        int ParentOf(int i);
        double Mu(int i);
        double Radius(int i);
        /// <summary>Радиус сферы действия; для корня +∞.</summary>
        double SoiRadius(int i);
        /// <summary>Минимальная высота над поверхностью/атмосферой, ниже которой периапсис недопустим, м.</summary>
        double SafeAltitude(int i);
        /// <summary>Состояние тела в мировой (инерциальной) СК.</summary>
        bool TryBodyState(int i, double universeTime, out Vector3d position, out Vector3d velocity);
        bool TryShipState(out Vector3d position, out Vector3d velocity);
        /// <summary>Тело, в сфере действия которого корабль сейчас.</summary>
        int ShipBody();
    }

    /// <summary>n-body распространение корабля с физикой игры (адаптер над SpacecraftPhysics) — тот же прогноз, что и на карте.</summary>
    public interface IPlannerPropagator
    {
        bool TryPropagate(double t0, Vector3d worldPos, Vector3d worldVel, double t1, out Vector3d pos, out Vector3d vel);
    }

    /// <summary>
    /// Необязательный контекст аппарата: false — корабль на поверхности/разрушен или под тягой. Планировщик
    /// строит отлёт из свободного баллистического состояния; под тягой мгновенное состояние — не орбита.
    /// </summary>
    public interface IPlannerShipState
    {
        bool ShipIsFreeFlying { get; }
    }

    /// <summary>Трек тела относительно родителя в инерциальных осях (то, что нужно решателю Ламберта).</summary>
    public sealed class BodyTrack : IOrbitTrack
    {
        private readonly IPlannerWorld world;
        private readonly int body, parent;
        public BodyTrack(IPlannerWorld world, int body, int parent) { this.world = world; this.body = body; this.parent = parent; }

        public bool TryEvaluate(double t, out Vector3d position, out Vector3d velocity)
        {
            position = velocity = Vector3d.Zero;
            if (!world.TryBodyState(body, t, out Vector3d p, out Vector3d v)) { return false; }
            if (parent >= 0)
            {
                if (!world.TryBodyState(parent, t, out Vector3d pp, out Vector3d pv)) { return false; }
                p -= pp; v -= pv;
            }

            position = p; velocity = v;
            return true;
        }
    }
}
