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
    [UnityEngine.DefaultExecutionOrder(-40)]
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

        [Tooltip("TAA на камере. Выключено: TAA покадрово дробит проекцию и «плывёт» яркой звездой/деталями.")]
        public bool TemporalAA = false;

        [Tooltip("SMAA — пространственное сглаживание (без TAA-гостинга). Включается, когда TAA выключен: убирает рваный силуэт рельефа на фоне неба.")]
        public bool SubpixelAA = true;

        [Tooltip("Диагностика: раз в секунду писать позицию камеры и цели.")]
        public bool LogCameraDiagnostics = true;

        private float diagnosticTimer;

        [Tooltip("Высота глаз над точкой корабля (м; вдоль нормали поверхности).")]
        public double EyeHeightMeters = 2d;

        private Vector3 lookDirection;
        private Vector3 lastUp;
        private bool hasLook;
        private Vector3 eyeOffset;

        private void Start()
        {
            var hdCamera = GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
            if (hdCamera != null)
            {
                hdCamera.antialiasing = TemporalAA
                    ? UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing
                    : SubpixelAA
                        ? UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing
                        : UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.None;
            }

            if (LockCursor)
            {
                SetCursorLocked(true);
            }
        }

        /// <summary>Захват/освобождение курсора вместе с видимостью (меньше
        /// editor-warning «Screen position out of view frustum»).</summary>
        private static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void Update()
        {
            if (!LockCursor)
            {
                return;
            }

            if (PlayerInput.Down(GameKey.Escape))
            {
                SetCursorLocked(false);
            }

            if (PlayerInput.MouseLeftDown() && Cursor.lockState != CursorLockMode.Locked)
            {
                SetCursorLocked(true);
            }

#if UNITY_EDITOR
            // В редакторе при потере фокуса окна отпускаем курсор — иначе
            // legacy mouse-модуль сыпет «out of view frustum».
            if (!UnityEngine.Application.isFocused && Cursor.lockState == CursorLockMode.Locked)
            {
                SetCursorLocked(false);
            }
#endif
        }

        private void LateUpdate()
        {
            // Временная диагностика — ДО early-return, чтобы логировать всегда.
            diagnosticTimer += Time.deltaTime;
            if (diagnosticTimer >= 1f)
            {
                diagnosticTimer = 0f;
                Vector3 tp = Target != null ? Target.position : Vector3.zero;
                Vector2 md = PlayerInput.MouseDelta();
                Debug.Log(string.Format(
                    "[Camera] pos=({0:F3},{1:F3},{2:F3}) target=({3:F3},{4:F3},{5:F3}) look=({6:F4},{7:F4},{8:F4}) mouse=({9:F5},{10:F5}) simT={11:F3}",
                    transform.position.x, transform.position.y, transform.position.z,
                    tp.x, tp.y, tp.z,
                    lookDirection.x, lookDirection.y, lookDirection.z,
                    md.x, md.y,
                    Runner != null ? (float)Runner.TimeSeconds : 0f));
            }

            if (Target == null)
            {
                return;
            }

            ComputeEyeOffset();
            Vector3 up = ComputeLocalUp();
            TransportLook(up);
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                ApplyMouse(up);
            }

            // Опорная вертикаль для крена — локальная вертикаль. Когда взгляд
            // ей параллелен (солнце ровно в зените при спавне «в полдень»),
            // LookRotation вырождается (произвольный крен + рывок при выходе
            // из зенита): тогда берём крен с ПРЕДЫДУЩЕГО кадра камеры — он
            // непрерывен. Самый первый кадр — любая непараллельная ось.
            Vector3 rotationUp = up;
            if (Mathf.Abs(Vector3.Dot(lookDirection, up)) > 0.999f)
            {
                Vector3 previous = transform.up - (lookDirection * Vector3.Dot(transform.up, lookDirection));
                rotationUp = previous.sqrMagnitude > 1e-6f
                    ? previous.normalized
                    : (Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right);
            }

            transform.rotation = Quaternion.LookRotation(lookDirection, rotationUp);
            transform.position = Target.position + eyeOffset;
            UpdateClipPlanes();
        }

        /// <summary>
        /// Взгляд хранится ВЕКТОРОМ (не yaw-числом): когда рамка локальной
        /// вертикали поворачивается (орбита, вращение планеты), вектор тянется
        /// вместе с ней — картинка крутится физично, а не «уплывает» мимо мыши.
        /// </summary>
        private void TransportLook(Vector3 up)
        {
            if (!hasLook)
            {
                // Начальный взгляд — на солнце (по данным симуляции): игрок
                // сразу видит звезду при спавне. Если звезды нет — fallback.
                Vector3 initial = Vector3.zero;
                bool aimed = false;
                if (Runner != null && Runner.Ship != null
                    && Runner.SystemState != null && Runner.SystemState.Root != null)
                {
                    Runner.SystemState.Root.EvaluateWorldState(Runner.TimeSeconds, out Vector3d starPos, out _);
                    Vector3d toStar = starPos - Runner.Ship.Position;
                    double toStarMagnitude = toStar.Magnitude;
                    if (toStarMagnitude > 0d)
                    {
                        initial = AstroFrame.ToSimulation(toStar / toStarMagnitude);
                        aimed = true;
                    }
                }

                if (!aimed)
                {
                    Vector3 fallbackUp = ComputeLocalUp();
                    initial = Vector3.ProjectOnPlane(Vector3.forward, fallbackUp);
                    if (initial.sqrMagnitude < 1e-4f)
                    {
                        initial = Vector3.ProjectOnPlane(Vector3.right, fallbackUp);
                    }
                }

                lookDirection = initial.normalized;
                hasLook = true;
            }
            else if (lastUp.sqrMagnitude > 1e-20f)
            {
                Quaternion frameRotation = Quaternion.FromToRotation(lastUp, up);
                if (frameRotation != Quaternion.identity)
                {
                    lookDirection = frameRotation * lookDirection;
                }
            }

            lastUp = up;
        }

        /// <summary>Мышь: вправо/влево — вокруг локальной вертикали, вверх/вниз —
        /// вокруг локальной горизонтали (как в обычных FPS, но на «низ» планеты).</summary>
        private void ApplyMouse(Vector3 up)
        {
            Vector2 md = PlayerInput.MouseDelta();
            float mouseX = md.x * MouseSensitivity;
            float mouseY = md.y * MouseSensitivity;

            // Мёртвая зона: при захваченном курсоре в редакторе новый Input System
            // может отдавать микро-шум дельты каждый кадр → камера непрерывно
            // чуть поворачивается («тряска» взгляда). Гасим мелкие значения.
            const float deadzone = 0.02f;
            if (Mathf.Abs(mouseX) < deadzone)
            {
                mouseX = 0f;
            }

            if (Mathf.Abs(mouseY) < deadzone)
            {
                mouseY = 0f;
            }

            if (mouseX != 0f)
            {
                lookDirection = Quaternion.AngleAxis(mouseX, up) * lookDirection;
            }

            if (mouseY != 0f)
            {
                Vector3 right = Vector3.Cross(up, lookDirection);
                if (right.sqrMagnitude > 1e-6f)
                {
                    right.Normalize();
                    lookDirection = Quaternion.AngleAxis(-mouseY, right) * lookDirection;
                }
            }

            ClampPitch(up);
        }

        /// <summary>Кламп наклона относительно локального горизонта.</summary>
        private void ClampPitch(Vector3 up)
        {
            float sinPitch = Mathf.Clamp(Vector3.Dot(lookDirection, up), -1f, 1f);
            float pitchDeg = Mathf.Asin(sinPitch) * Mathf.Rad2Deg;
            if (pitchDeg <= PitchClamp && pitchDeg >= -PitchClamp)
            {
                return;
            }

            Vector3 flat = Vector3.ProjectOnPlane(lookDirection, up);
            if (flat.sqrMagnitude < 1e-6f)
            {
                return; // смотрим почти ровно по вертикали — не дёргаем
            }

            flat.Normalize();
            float clamped = Mathf.Clamp(pitchDeg, -PitchClamp, PitchClamp) * Mathf.Deg2Rad;
            lookDirection = (flat * Mathf.Cos(clamped)) + (up * Mathf.Sin(clamped));
        }

        /// <summary>Локальная вертикаль (из double-мира, через мост).</summary>
        private Vector3 ComputeLocalUp()
        {
            if (Runner == null || Runner.Ship == null || Runner.DominantBody == null)
            {
                return Vector3.up;
            }

            OrbitingBody body = Runner.DominantBody;
            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPos, out _);
            Vector3d outward = Runner.PlayerPosition - bodyPos;
            double magnitude = outward.Magnitude;
            if (magnitude <= 0d)
            {
                return Vector3.up;
            }

            return AstroFrame.ToSimulation(outward / magnitude);
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

            eyeOffset = ComputeLocalUp() * (float)EyeHeightMeters;
        }

        private void UpdateClipPlanes()
        {
            Camera camera = GetComponent<Camera>();
            if (camera == null || Runner == null || Runner.DominantBody == null)
            {
                return;
            }

            OrbitingBody body = Runner.DominantBody;
            body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPos, out _);
            Vector3d relative = Runner.PlayerPosition - bodyPos;
            double radius = body.Radius;
            if (body.Terrain != null)
            {
                body.SurfaceLatLonAt(Runner.PlayerPosition, Runner.TimeSeconds, out double latDeg, out double lonDeg);
                radius += body.Terrain.GetHeightMeters(body, latDeg * (System.Math.PI / 180d), lonDeg * (System.Math.PI / 180d));
            }

            double altitude = System.Math.Max(0.5d, relative.Magnitude - radius);
            double near = System.Math.Max(0.1d, altitude * 0.001d);
            double horizon = System.Math.Sqrt((2d * radius * altitude) + (altitude * altitude));
            // far — не меньше запаса 20 км: высокие дальние горы выше геометрического
            // горизонта тоже должны попадать в кадр (иначе их режет по far).
            double far = System.Math.Max((horizon * 1.5d) + 1e3d, 20000d);

            // far НЕ растягиваем под атмосферу: она рисуется камера-центрированным
            // куполом и всегда влезает во фрустум. Иначе near/far ~1e8 → z-fighting
            // (мерцание рельефа/диска даже без движения).
            camera.nearClipPlane = (float)near;
            camera.farClipPlane = (float)far;
        }
    }
}
