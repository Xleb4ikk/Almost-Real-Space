using Galilego.Core;
using Galilego.Universe;
using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>
    /// Крепление планшета к кораблю. Пока у корабля нет интерьера и видимой
    /// модели, планшет висит у точки корабля: поднят вдоль местной вертикали,
    /// смещён в сторону (на восток) и развёрнут экраном к точке корабля. Так он
    /// доступен и из заглушки интерьера (ходьба вокруг точки корабля ≤ 3 м),
    /// и снаружи, а подъём не даёт ему оказаться в рельефе. Когда появится
    /// интерьер (или рука-планшет Orion), крепление выключается и планшет
    /// становится ребёнком кабины.
    ///
    /// LateUpdate после ShipView (-90) и до камеры (-40): позиция берётся из
    /// того же FloatingOrigin, что и корабль, кадром позже не отстаёт.
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
