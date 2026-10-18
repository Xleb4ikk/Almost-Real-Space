using Galilego.Core;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>
    /// Attaches the tablet to the ship. The tablet hangs at the ship point: raised
    /// along the local vertical, offset sideways (east) and turned to face the ship,
    /// so it is reachable both inside and outside and the raise keeps it out of the
    /// terrain. LateUpdate runs after ShipView (-90) and before the camera (-40):
    /// the position comes from the same FloatingOrigin as the ship, so it never lags a frame.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public sealed class TabletMount : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены. Пусто — найдётся при старте.")]
        public SimulationRunner Runner;

        [Tooltip("Подъём над точкой корабля вдоль местной вертикали (м).")]
        public float UpOffset = 1.1f;

        [Tooltip("Снос в сторону (на восток) от точки корабля (м).")]
        public float SideOffset = 1.2f;

        [Tooltip("Разворачивать экран к точке корабля.")]
        public bool FaceShip = true;

        private void Start()
        {
            if (Runner == null)
            {
                Runner = FindAnyObjectByType<SimulationRunner>();
            }
        }

        private void LateUpdate()
        {
            if (Runner == null || Runner.Ship == null || Runner.DominantBody == null)
            {
                return;
            }

            OrbitingBody body = Runner.DominantBody;
            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPos, out _);
            Vector3d radial = Runner.Ship.Position - bodyPos;
            Vector3d upAstro = radial.SqrMagnitude > 1e-12d ? radial.Normalized : new Vector3d(0d, 0d, 1d);

            Vector3d axis = body.SpinAxis.Normalized;
            Vector3d northAstro = axis - (upAstro * Vector3d.Dot(axis, upAstro));
            if (northAstro.SqrMagnitude < 1e-12d)
            {
                northAstro = Vector3d.Cross(upAstro, new Vector3d(1d, 0d, 0d));
                if (northAstro.SqrMagnitude < 1e-12d)
                {
                    northAstro = Vector3d.Cross(upAstro, new Vector3d(0d, 1d, 0d));
                }
            }

            northAstro = northAstro.Normalized;
            Vector3d eastAstro = Vector3d.Cross(upAstro, northAstro).Normalized;

            Vector3 up = AstroFrame.ToSimulation(upAstro);
            Vector3 east = AstroFrame.ToSimulation(eastAstro);
            Vector3 shipRender = FloatingOrigin.ToRender(Runner.Ship.Position);

            transform.position = shipRender + (up * UpOffset) + (east * SideOffset);

            if (FaceShip)
            {
                Vector3 look = shipRender - transform.position;
                Vector3 flat = Vector3.ProjectOnPlane(look, up);
                if (flat.sqrMagnitude < 1e-6f)
                {
                    flat = -east;
                }

                transform.rotation = Quaternion.LookRotation(flat.normalized, up);
            }
        }
    }
}
