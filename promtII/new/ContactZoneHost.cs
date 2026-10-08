// ЧЕРНОВИК (вертикальный срез). НЕ ПОДКЛЮЧЁН К ПРОЕКТУ И НЕ КОМПИЛИРОВАЛСЯ.
// Проверить в редакторе Unity 6000.6.0f1: компиляция, затем сценарий из SLICE_PLAN.md.
//
// Роль: владеет зоной контакта. Вне зоны ничего не делает и не трогает состояние корабля.
// Вход/выход — только через ContactZoneGate (чистая логика, тесты Test300–305).
// Единственная правка игрового кода: SimulationRunner.Update пропускает шаг, когда
// ContactZoneHost.Active == true (см. SLICE_PLAN.md, раздел «Точка подключения»).
using System.Collections.Generic;
using Galilego.Core;
using Galilego.Spacecraft;
using Galilego.Universe;
using UnityEngine;

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
            Spacecraft ship = Runner.Ship;

            if (!Active)
            {
                if (gate.Update(body, terrain, ship.Position, ship.Velocity, t))
                {
                    Enter(body, terrain, ship, t);
                }

                return;
            }

            // Внутри зоны: решение гейта по состоянию COM, которое ведёт физика.
            Vector3d comPosition;
            Vector3d comVelocity;
            Proxy.GetCenterOfMassState(frame, t, out comPosition, out comVelocity);
            if (!gate.Update(body, terrain, comPosition, comVelocity, t))
            {
                Exit(ship, t);
                return;
            }

            // Эффективная гравитация кадра места (в его локальных осях) — см. SLICE_PLAN.md.
            Proxy.SetFrameGravity(EffectiveLocalGravity(body, frame, t));

            // Состояние корабля ведёт зона: пишем обратно в double-мир каждый кадр.
            frame.At(t).ToWorld(Proxy.CenterOfMassLocal, Proxy.CenterOfMassVelocityLocal, out Vector3d wp, out Vector3d wv);
            ship.Position = wp;
            ship.Velocity = wv;
        }

        private void Enter(OrbitingBody body, ITerrainModel terrain, Spacecraft ship, double t)
        {
            frame = SiteFrame.Anchor(body, terrain, ship.Position, t);
            Patch.Build(body, terrain, frame, t);
            frame.ToLocal(ship.Position, ship.Velocity, out Vector3d lp, out Vector3d lv);
            Proxy.Build(Runner.Parts, frame, lp, lv);
            Active = true;
            if (Log) Debug.Log("[ContactZone] вход: " + frame.LatitudeDegrees + ", " + frame.LongitudeDegrees);
        }

        private void Exit(Spacecraft ship, double t)
        {
            Proxy.GetCenterOfMassState(frame, t, out Vector3d cp, out Vector3d cv);
            ship.Position = cp;
            ship.Velocity = cv;
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
