using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Камера от первого лица: позиция — точка корабля (GO с ShipView),
    /// взгляд — мышь (yaw/pitch, курсор захвачен, Esc — отпустить).
    /// near/far — динамические, от высоты над рельефом доминантного тела:
    /// на масштабах 1e6+ фиксированный far даёт z-войну, а статичный near
    /// на земле «ест» глубину. Горизонт: far = 1.5·√(2R·alt + alt²) + маржа.
    /// Вызов — LateUpdate ПОСЛЕ ShipView (иначе кадр отстаёт на лаг).
    /// </summary>
    public sealed class FirstPersonCamera : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("Трансформ корабля (GO с ShipView).")]
        public Transform Target;

        [Tooltip("Чувствительность мыши.")]
        public float MouseSensitivity = 2f;

        [Tooltip("Кламп наклона взгляда (градусы).")]
        public float PitchClamp = 85f;

        [Tooltip("Захват курсора (Esc — отпустить, ЛКМ — вернуть).")]
        public bool LockCursor = true;

        [Tooltip("Высота глаз над точкой корабля (м; вдоль нормали поверхности).")]
        public double EyeHeightMeters = 2d;

        private float yaw;
        private float pitch;
        private Vector3 eyeOffset;

        private void Start()
        {
            if (LockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
            }
        }

        private void Update()
        {
            if (!LockCursor)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = CursorLockMode.None;
            }

            if (Input.GetMouseButtonDown(0) && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
            }
        }

        private void LateUpdate()
        {
            if (Target == null)
            {
                return;
            }

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                yaw += Input.GetAxis("Mouse X") * MouseSensitivity;
                pitch = Mathf.Clamp(pitch - (Input.GetAxis("Mouse Y") * MouseSensitivity), -PitchClamp, PitchClamp);
            }

            ComputeEyeOffset();
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = Target.position + eyeOffset;
            UpdateClipPlanes();
        }

        /// <summary>
        /// Оффсет глаз: вдоль нормали поверхности доминантного тела (на земле
        /// корабль лежит на рельефе — без оффсета камера «в земле»; в полёте
        /// оффсет вдоль радиуса визуально нейтрален).
        /// </summary>
        private void ComputeEyeOffset()
        {
            eyeOffset = Vector3.zero;
            if (Runner == null || Runner.Ship == null || Runner.DominantBody == null)
            {
                return;
            }

            OrbitingBody body = Runner.DominantBody;
            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPos, out _);
            Vector3d outward = Runner.Ship.Position - bodyPos;
            double magnitude = outward.Magnitude;
            if (magnitude <= 0d)
            {
                return;
            }

            eyeOffset = AstroFrame.ToSimulation((outward / magnitude) * EyeHeightMeters);
        }

        private void UpdateClipPlanes()
        {
            Camera camera = GetComponent<Camera>();
            if (camera == null || Runner == null || Runner.Ship == null || Runner.DominantBody == null)
            {
                return;
            }

            OrbitingBody body = Runner.DominantBody;
            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPos, out _);
            Vector3d relative = Runner.Ship.Position - bodyPos;
            double radius = body.Radius;
            if (body.Terrain != null)
            {
                body.SurfaceLatLonAt(Runner.Ship.Position, Runner.TimeSeconds, out double latDeg, out double lonDeg);
                radius += body.Terrain.GetHeightMeters(body, latDeg * (System.Math.PI / 180d), lonDeg * (System.Math.PI / 180d));
            }

            double altitude = System.Math.Max(0.5d, relative.Magnitude - radius);
            double near = System.Math.Max(0.02d, altitude * 0.001d);
            double horizon = System.Math.Sqrt((2d * radius * altitude) + (altitude * altitude));
            double far = (horizon * 1.5d) + 1e3d;
            camera.nearClipPlane = (float)near;
            camera.farClipPlane = (float)far;
        }
    }
}
