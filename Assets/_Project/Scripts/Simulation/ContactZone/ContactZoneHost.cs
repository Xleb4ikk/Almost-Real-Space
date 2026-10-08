// Зона контакта (вертикальный срез). Проверено в редакторе Unity 6000.6.0f1:
// компилируется, сценарий плоской площадки пройден (сцена ContactZoneSlice, проба
// ContactZoneSliceProbe: стоянка 10 с, |v| = 0, дрейф от осадки 0).
// Роль: владеет зоной контакта. Вне зоны ничего не делает и не трогает состояние корабля.
// Вход/выход — только через ContactZoneGate (чистая логика, тесты Test300–305).
// Игровой код правится единственной строкой: SimulationRunner.Update пропускает шаг,
// когда ContactZoneHost.Active == true (см. SLICE_PLAN.md, раздел «Точка подключения»).
// Внутри зоны гейт и double-мир работают в МИРОВЫХ координатах: локальный COM прокси
// переводится через текущий кадр места (frame.At(t)).
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Events;
using Galilego.Universe;
using UnityEngine;
using Ship = Galilego.Spacecraft.Spacecraft;

namespace Galilego.Simulation.ContactZone
{
    [DefaultExecutionOrder(-50)]
    public sealed class ContactZoneHost : MonoBehaviour
    {
        public SimulationRunner Runner;
        public ContactZoneProxy Proxy;
        public TerrainPatch Patch;

        [Tooltip("Отладка: логировать входы и выходы.")]
        public bool Log = true;

        public static bool Active { get; private set; }

        private readonly ContactZoneGate gate = new ContactZoneGate();
        private SiteFrame frame;

        /// <summary>От центра масс до точки корабля вдоль локального Up (м).
        /// В игре Ship.Position — низ корабля (точка касания), а не ЦМ: без этого
        /// смещения корабль прыгает на высоту ЦМ при входе в зону и выходе.</summary>
        private double anchorUp;

        private void LateUpdate()
        {
            if (Runner == null || Runner.Ship == null || Runner.DominantBody == null)
            {
                return;
            }

            OrbitingBody body = Runner.DominantBody;
            ITerrainModel terrain = body.Terrain;
            if (terrain == null)
            {
                return;
            }

            double t = Runner.TimeSeconds;
            Ship ship = Runner.Ship;

            if (!Active)
            {
                if (gate.Update(body, terrain, ship.Position, ship.Velocity, t))
                {
                    Enter(body, terrain, ship, t);
                }

                return;
            }

            // Внутри зоны гейту и double-миру нужны МИРОВЫЕ координаты ТОЧКИ КОРАБЛЯ:
            // от ЦМ отнимаем anchorUp вдоль локального Up, затем переводим через
            // текущий кадр места (frame.At(t)), а не через якорь входа.
            Proxy.GetCenterOfMassState(frame, t, out Vector3d com, out Vector3d comVelocity);
            Vector3d anchor = new Vector3d(com.X, com.Y, com.Z - anchorUp);
            SiteFrame current = frame.At(t);
            current.ToWorld(anchor, comVelocity, out Vector3d worldPosition, out Vector3d worldVelocity);
            if (!gate.Update(body, terrain, worldPosition, worldVelocity, t))
            {
                Exit(ship, t);
                return;
            }

            // Эффективная гравитация кадра места (в его локальных осях) — см. SLICE_PLAN.md.
            Proxy.SetFrameGravity(EffectiveLocalGravity(body, current, t));

            // Состояние корабля ведёт зона: пишем обратно в double-мир каждый кадр.
            ship.Position = worldPosition;
            ship.Velocity = worldVelocity;
        }

        private void Enter(OrbitingBody body, ITerrainModel terrain, Ship ship, double t)
        {
            frame = SiteFrame.Anchor(body, terrain, ship.Position, t);
            Patch.Build(body, terrain, frame, t);
            frame.ToLocal(ship.Position, ship.Velocity, out Vector3d lp, out Vector3d lv);
            Proxy.Build(Runner.Parts, frame, lp, lv);

            // Смещение от ЦМ до точки корабля: фиксируется при входе и используется
            // весь сеанс зоны (и при записи состояния, и при выходе).
            Proxy.GetCenterOfMassState(frame, t, out Vector3d com, out _);
            anchorUp = com.Z - lp.Z;

            // В зоне варп запрещён: время идёт по реальному dt (AdvanceTime), а не по
            // варп-фактору; поднятый варп на выходе увёл бы корабль рывком.
            Runner.Warp.SetWarpFactor(WarpController.MinWarpFactor);

            Active = true;
            if (Log) Debug.Log("[ContactZone] вход: " + frame.LatitudeDegrees + ", " + frame.LongitudeDegrees);
        }

        private void Exit(Ship ship, double t)
        {
            Proxy.GetCenterOfMassState(frame, t, out Vector3d com, out Vector3d comVelocity);
            Vector3d anchor = new Vector3d(com.X, com.Y, com.Z - anchorUp);
            frame.At(t).ToWorld(anchor, comVelocity, out Vector3d wp, out Vector3d wv);
            ship.Position = wp;
            ship.Velocity = wv;
            Proxy.Clear();
            Patch.Clear();
            Active = false;
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
            if (Log) Debug.Log("[ContactZone] выход");
        }

        // Эффективное ускорение кадра места: гравитация в точке минус ускорение начала кадра
        // (движение планеты по орбите) минус центробежное от вращения планеты.
        // Кориолис в срезе не учитывается (порядок ω·v ≈ 1e-3 м/с² при посадке, см. план).
        private static Vector3d EffectiveLocalGravity(OrbitingBody body, SiteFrame f, double t)
        {
            Vector3d origin = f.Origin;
            body.EvaluateWorldState(t, out Vector3d bodyPos, out Vector3d bodyVel);
            body.EvaluateWorldState(t + 1d, out _, out Vector3d bodyVelNext);

            Vector3d toCenter = bodyPos - origin;
            double r = toCenter.Magnitude;
            double mu = body.StandardGravitationalParameter;
            Vector3d gravity = toCenter.Normalized * (mu / (r * r));
            Vector3d frameAccel = bodyVelNext - bodyVel;

            Vector3d w = f.AngularVelocity;
            Vector3d centrifugal = Vector3d.Cross(w, Vector3d.Cross(w, origin - bodyPos));

            // Возвращаем в осях места (восток, север, вверх); перевод в Unity — в ContactZoneProxy.
            Vector3d eff = gravity - frameAccel - centrifugal;
            return new Vector3d(Vector3d.Dot(eff, f.East), Vector3d.Dot(eff, f.North), Vector3d.Dot(eff, f.Up));
        }
    }
}
