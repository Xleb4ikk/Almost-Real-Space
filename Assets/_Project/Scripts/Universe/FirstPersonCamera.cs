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

        [Tooltip("Высота глаз вплавь (м): пловец лежит, голова ~0.5 м над ногами. " +
            "Синхронизировано с SimulationRunner.SwimEyeHeightMeters/UnderwaterEffect: " +
            "иначе на мелководье у берега голова на 2-метровом росте никогда не уходит под воду.")]
        public double SwimEyeHeightMeters = 0.5d;

        private Vector3 lookDirection;
        private Vector3 lastUp;
        private Vector3 lastFlatDirection;
        private bool hasLook;
        private Vector3 eyeOffset;
        private bool cursorReleasedByUser;
        private string mouseIgnoredReason;

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
                cursorReleasedByUser = true;
                SetCursorLocked(false);
            }

            if (PlayerInput.MouseLeftDown() && Cursor.lockState != CursorLockMode.Locked)
            {
                cursorReleasedByUser = false;
                SetCursorLocked(true);
            }

            // Потеря фокуса окна (alt-tab, клик по таскбару, второй монитор) тоже
            // отпускает захват: в фоне захваченный курсор сыпет «Screen position
            // out of view frustum». Отпускаем, НЕ помечая как пользовательский —
            // тогда возврат фокуса сам вернёт захват, и мышь не «замолкает»
            // до клика, о котором игрок не знает. Пользовательский Esc не
            // переопределяем.
            if (!UnityEngine.Application.isFocused && Cursor.lockState == CursorLockMode.Locked)
            {
                SetCursorLocked(false);
            }

            if (UnityEngine.Application.isFocused && !cursorReleasedByUser
                && Cursor.lockState != CursorLockMode.Locked)
            {
                SetCursorLocked(true);
            }
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
                NoteMouseIgnored("Target == null (корабль уничтожена)");
                return;
            }

            ComputeEyeOffset();
            Vector3 up = ComputeLocalUp();
            TransportLook(up);
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                NoteMouseIgnored(null);
                ApplyMouse(up);
            }
            else
            {
                NoteMouseIgnored("курсор не захвачен ("
                    + (UnityEngine.Application.isFocused ? "окно в фокусе" : "окно не в фокусе")
                    + (cursorReleasedByUser ? ", отпущен Esc" : "") + ")");
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

        /// <summary>
        /// Мышь: вправо/влево — азимут вокруг локальной вертикали, вверх/вниз —
        /// угол возвышения (как в обычных FPS, но на «низ» планеты).
        ///
        /// Взгляд НЕ поворачивается вектором вокруг «right»: при взгляде вниз
        /// (PitchClamp = 85°, то есть в 5° от полюса) один кадр накапливает весь
        /// взмах мыши (Mouse.delta — движение за кадр, а кадр у нас ≈ 1 с), и
        /// взгляд пролетал через полюс: −85° − 40° = −125°, то есть +55° ВВЕРХ.
        /// ClampPitch такое пропускал (наклон легальный), управление
        /// переворачивалось — «мышь перестала работать». Здесь наклон
        /// ограничивается ДО поворота, поэтому полюс недостижим в принципе.
        /// </summary>
        private void ApplyMouse(Vector3 up)
        {
            Vector2 md = PlayerInput.MouseDelta();
            float yaw = md.x * MouseSensitivity;
            float pitch = md.y * MouseSensitivity;

            // Мёртвая зона: при захваченном курсоре в редакторе новый Input System
            // может отдавать микро-шум дельты каждый кадр → камера непрерывно
            // чуть поворачивается («тряска» взгляда). Гасим мелкие значения.
            const float deadzone = 0.02f;
            if (Mathf.Abs(yaw) < deadzone)
            {
                yaw = 0f;
            }

            if (Mathf.Abs(pitch) < deadzone)
            {
                pitch = 0f;
            }

            // Азимут: горизонтальная составляющая взгляда. Ровно на полюсе она
            // вырождена — берём азимут ПРОШЛОГО кадра, он непрерывен.
            Vector3 flat = Vector3.ProjectOnPlane(lookDirection, up);
            bool degenerate = false;
            if (flat.sqrMagnitude < 1e-8f)
            {
                flat = lastFlatDirection;
                degenerate = true;
            }

            if (flat.sqrMagnitude < 1e-8f)
            {
                // Полюс в первый же кадр (взгляд строго по вертикали — такое
                // бывает при спавне с солнцем в зените): берём любой
                // фиксированный азимут, чтобы кадр не дёргался.
                flat = Vector3.ProjectOnPlane(Vector3.forward, up);
                if (flat.sqrMagnitude < 1e-6f)
                {
                    flat = Vector3.ProjectOnPlane(Vector3.right, up);
                }

                degenerate = true;
            }

            // Модуль горизонтали нужен ДО нормализации: после нормализации он
            // всегда 1, atan2 превращается в asin(dot) и снова теряет знак за
            // полюсом — ровно тот баг, который чиним.
            float flatMagnitude = flat.magnitude;
            flat.Normalize();
            lastFlatDirection = flat;

            // Возвышение из горизонтали: однозначно, пока известен азимут.
            // На вырожденном (ровно полюс) наклон берём по знаку — ±PitchClamp.
            float elevation = degenerate
                ? (Vector3.Dot(lookDirection, up) >= 0f ? PitchClamp : -PitchClamp)
                : Mathf.Atan2(Vector3.Dot(lookDirection, up), flatMagnitude) * Mathf.Rad2Deg;

            if (yaw != 0f)
            {
                flat = Quaternion.AngleAxis(yaw, up) * flat;
                lastFlatDirection = flat;
            }

            // Мышь вверх — взгляд вверх. Клампим ДО поворота: полюс недостижим.
            elevation = Mathf.Clamp(elevation + pitch, -PitchClamp, PitchClamp);

            float e = elevation * Mathf.Deg2Rad;
            lookDirection = (flat * Mathf.Cos(e)) + (up * Mathf.Sin(e));
        }

        /// <summary>
        /// Мышь игнорируется — пишем ПРИЧИНУ один раз на каждое изменение
        /// состояния. Раньше «мышь перестала работать» было нечем диагностировать:
        /// симптом одинаков и при отпущенном курсоре, и при потерянной цели.
        /// </summary>
        private void NoteMouseIgnored(string reason)
        {
            if (reason == mouseIgnoredReason)
            {
                return;
            }

            mouseIgnoredReason = reason;
            if (reason != null)
            {
                Debug.LogWarning("[Camera] мышь не управляет камерой: " + reason);
            }
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
        /// оффсет вдоль радиуса визуально нейтрален). Вплавь — низкий
        /// SwimEyeHeightMeters (пловец лежит), иначе голова не уходит под
        /// воду на мелководье.
        /// </summary>
        private void ComputeEyeOffset()
        {
            eyeOffset = Vector3.zero;
            if (Runner == null || Runner.Ship == null || Runner.DominantBody == null)
            {
                return;
            }

            double eye = Runner.PlayerMode == PlayerMode.Swimming ? SwimEyeHeightMeters : EyeHeightMeters;
            eyeOffset = ComputeLocalUp() * (float)eye;
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
            double groundHeight = 0d;
            if (body.Terrain != null)
            {
                body.SurfaceLatLonAt(Runner.PlayerPosition, Runner.TimeSeconds, out double latDeg, out double lonDeg);
                groundHeight = body.Terrain.GetHeightMeters(body, latDeg * (System.Math.PI / 180d), lonDeg * (System.Math.PI / 180d));
            }

            // near — от высоты над ЛОКАЛЬНЫМ рельефом (камера у земли).
            double altitude = System.Math.Max(0.5d, relative.Magnitude - (radius + groundHeight));
            double near = System.Math.Max(0.1d, altitude * 0.001d);

            // far — до физического горизонта с запасом на дальние ВЕРШИНЫ:
            // высота камеры и пиков считается от СРЕДНЕГО радиуса. Раньше высота
            // бралась над рельефом под наблюдателем: стоя на холме, «горизонт»
            // выходил ~1 км, far падал к минимуму 20 км, и всё, что дальше,
            // резалось фрустумом (видимая «стена»/обрыв и фолбэк атмосферы).
            double cameraHeight = System.Math.Max(0.5d, relative.Magnitude - radius);
            double peakHeight = 0d;
            if (body.Terrain is HeightfieldTerrain heightfield)
            {
                peakHeight = System.Math.Max(0d, heightfield.AmplitudeMeters)
                    * (1d + System.Math.Max(0d, heightfield.ContinentDepth));
            }

            double horizon = SkyPhysics.MaxSightDistance(radius, cameraHeight, peakHeight);
            double far = System.Math.Max((horizon * 1.1d) + 1e3d, 20000d);

            // far НЕ растягиваем под атмосферу: она рисуется камера-центрированным
            // куполом и всегда влезает во фрустум. Иначе near/far ~1e8 → z-fighting
            // (мерцание рельефа/диска даже без движения).
            camera.nearClipPlane = (float)near;
            camera.farClipPlane = (float)far;
        }
    }
}
