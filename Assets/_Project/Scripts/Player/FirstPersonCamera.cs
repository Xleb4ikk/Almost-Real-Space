using Galilego.Core;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// First-person camera: position is the ship point (GO with ShipView),
    /// look direction is the mouse (yaw/pitch, cursor locked, Esc releases).
    /// near/far are dynamic, based on altitude above the dominant body's terrain:
    /// at 1e6+ scales a fixed far causes z-fighting, while a static near
    /// on the ground "eats" depth. Horizon: far = 1.5·√(2R·alt + alt²) + margin.
    /// Called from LateUpdate AFTER ShipView (otherwise the frame lags by one).
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

        [Tooltip("Захват курсора (Esc — отпустить, ЛКМ — вернуть, Ctrl в стройке — подсмотреть).")]
        public bool LockCursor = true;

        [Tooltip("TAA на камере. Выключено: TAA покадрово дробит проекцию и «плывёт» яркой звездой/деталями.")]
        public bool TemporalAA = false;

        [Tooltip("SMAA — пространственное сглаживание (без TAA-гостинга). Включается, когда TAA выключен: убирает рваный силуэт рельефа на фоне неба.")]
        public bool SubpixelAA = true;

        [Tooltip("Диагностика: раз в секунду писать позицию камеры и цели. " +
                 "Стоит не каждый кадр, а раз в секунду (таймер diagnosticTimer), " +
                 "так что на FPS не влияет. Но в длинных прогонах забивает " +
                 "консоль, и в ней теряются реальные ошибки — по умолчанию " +
                 "выключено.")]
        public bool LogCameraDiagnostics = false;

        private float diagnosticTimer;

        [Tooltip("Высота глаз над точкой игрока (м; вдоль нормали поверхности). " +
            "Рост игрока 1.40 м — глаза 1.30 м (глаза не на макушке).")]
        public double EyeHeightMeters = 1.3d;

        [Tooltip("Высота глаз вплавь (м): пловец лежит, голова ~0.5 м над ногами. " +
            "Синхронизировано с SimulationRunner.SwimEyeHeightMeters/UnderwaterEffect: " +
            "иначе на мелководье у берега голова на полном росте никогда не уходит под воду.")]
        public double SwimEyeHeightMeters = 0.5d;

        [Tooltip("Высота глаз в приседе (м): игрок автоматически пригибается, " +
            "чтобы пройти низкий проём (люк капсулы Orion ~1.2 м).")]
        public double CrouchEyeMeters = 0.8d;

        [Tooltip("Высота глаз сидящего над НОГАМИ (м). Ноги PlayerView берёт у якоря " +
            "кресла минус EyeHeightMeters кресла, поэтому больше этого значения = " +
            "камера выше якоря (взгляд поверх консоли).")]
        public double SeatedEyeMeters = 1.15d;

        [Tooltip("Ближняя плоскость камеры СИДЯ (м). Обычная считается от высоты над рельефом " +
            "(высота*0.001, не меньше 0.1), а высота берётся из позиции игрока в double-мире: " +
            "при её ошибке/отставании ближняя плоскость раздувалась и срезала стены и мониторы " +
            "кабины (вхожу в стену, мерцание). В кабине всё рядом - фиксируем маленькую.")]
        public float SeatedNearClipMeters = 0.03f;

        [Tooltip("ПКМ — приблизиться «в упор» к тому, на что смотришь: максимум, м " +
            "(дальше — по поверхности, но не ближе зазора). 0 — выключено.")]
        public float LeanMaxMeters = 0.75f;

        [Tooltip("Минимальный зазор до поверхности при приближении, м (не влезаем в монитор).")]
        public float LeanMinClearance = 0.22f;

        [Tooltip("FOV при приближении (сужение для чтения мелкого). 0 — не менять.")]
        public float LeanFieldOfView = 45f;

        [Tooltip("Множитель чувствительности мыши в приближении (точнее целиться).")]
        [Range(0.1f, 1f)]
        public float LeanSensitivityScale = 0.4f;

#if UNITY_EDITOR
        /// <summary>
        /// Capture tool (Editor/SurfaceCaptureTool) keeps its before/after frames
        /// on one viewpoint, so it must set the look direction directly.
        /// LateUpdate unconditionally overwrites transform.rotation every frame,
        /// so the override is applied last.
        /// Editor only: no hook in builds and no camera code changes.
        /// </summary>
        public static bool DebugLookOverride;

        /// <summary>Look direction when <see cref="DebugLookOverride"/> is on, in world space.</summary>
        public static Vector3 DebugLookDirection = Vector3.up;

        /// <summary>
        /// Reset the override. Call before leaving Play: the static survives the
        /// domain restart and the next session would start with a locked look.
        /// </summary>
        public static void ClearDebugLook()
        {
            DebugLookOverride = false;
            DebugLookDirection = Vector3.up;
        }
#endif

        private Vector3 lookDirection;
        private Vector3 lastUp;
        private Vector3 lastFlatDirection;
        private bool hasLook;
        private Vector3 eyeOffset;
        private bool cursorReleasedByUser;
        private string mouseIgnoredReason;

        /// <summary>Smooth stand ↔ crouch blend (0..1) for eye height.</summary>
        private float crouchBlend;

        /// <summary>Smooth standing ↔ seated blend (0..1) for eye height.</summary>
        private float seatedBlend;

        /// <summary>Whether the player was seated last frame (one-shot aim when sitting down).</summary>
        private bool wasSeated;

        private PlayerController playerController;

        /// <summary>Smooth normal ↔ lean blend (0..1) for the RMB lean-in.</summary>
        private float leanBlend;

        private float baseFieldOfView = 60f;

        private void Start()
        {
#if UNITY_EDITOR
            // The project enters Play with DisableDomainReload, so statics survive
            // the session: a look override from a previous capture would otherwise
            // stay on and the next Play would stare at the ceiling.
            ClearDebugLook();
#endif
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

            Camera camComponent = GetComponent<Camera>();
            if (camComponent != null)
            {
                baseFieldOfView = camComponent.fieldOfView;
            }
        }

        /// <summary>Lock/release the cursor together with its visibility (fewer
        /// "Screen position out of view frustum" editor warnings).</summary>
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

            // Ctrl in build mode peeks: while the key is held the cursor is
            // released (ΔV panel, hints). Release it and the lock returns on its own.
            bool peek = Runner != null && Runner.BuildMode && PlayerInput.Held(GameKey.LeftControl);

            if (PlayerInput.Down(GameKey.Escape))
            {
                cursorReleasedByUser = true;
                SetCursorLocked(false);
            }

            if (PlayerInput.MouseLeftDown() && Cursor.lockState != CursorLockMode.Locked && !peek)
            {
                cursorReleasedByUser = false;
                SetCursorLocked(true);
            }

            // Losing window focus (alt-tab, taskbar click, second monitor) also
            // releases the lock: a locked cursor in the background spams "Screen
            // position out of view frustum". Release it WITHOUT marking it as user
            // release, so regaining focus restores the lock automatically and the
            // mouse does not go silent until a click the player knows nothing about.
            // A user Esc release is not overridden.
            if (!UnityEngine.Application.isFocused && Cursor.lockState == CursorLockMode.Locked)
            {
                SetCursorLocked(false);
            }

            if (UnityEngine.Application.isFocused && !cursorReleasedByUser && !peek
                && Cursor.lockState != CursorLockMode.Locked)
            {
                SetCursorLocked(true);
            }

            if (peek)
            {
                SetCursorLocked(false);
            }
        }

        private void LateUpdate()
        {
            // Diagnostic logging sits BEFORE the early return so it always runs.
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

            // Crouch and seating are blended, not snapped. The eyes follow the
            // 0..1 crouch fraction rather than a "crouched" flag.
            float stoop = Runner != null ? Runner.PlayerStoopFraction : 0f;
            crouchBlend = Mathf.MoveTowards(crouchBlend, stoop, Time.deltaTime * 3f);
            bool seated = Runner != null && Runner.PlayerSeated;
            seatedBlend = Mathf.MoveTowards(seatedBlend, seated ? 1f : 0f, Time.deltaTime * 5f);

            // Seating: one-shot aim toward the direction the seat requests (a couple of monitors).
            if (seated && !wasSeated)
            {
                AimAtSeatLook();
            }

            wasSeated = seated;

            // RMB leans in close (monitor buttons are readable and clickable
            // precisely). In build mode RMB is taken by placement/slot menu — leave it.
            bool lean = LeanMaxMeters > 0f && PlayerInput.MouseRightHeld()
                && (Runner == null || !Runner.BuildMode);
            leanBlend = Mathf.MoveTowards(leanBlend, lean ? 1f : 0f, Time.deltaTime * 6f);

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

#if UNITY_EDITOR
            // Capture tool: the look comes from outside and the mouse does not
            // touch it. Applied AFTER TransportLook/ApplyMouse — both rewrite
            // lookDirection, and the override must have the last word.
            if (DebugLookOverride)
            {
                if (DebugLookDirection.sqrMagnitude > 1e-8f)
                {
                    lookDirection = DebugLookDirection.normalized;
                    lastFlatDirection = Vector3.ProjectOnPlane(lookDirection, up);
                }

                hasLook = true;
                lastUp = up;
            }
#endif

            // Reference up for roll is the local up. When the look is parallel to
            // it (sun exactly at zenith on a noon spawn), LookRotation degenerates
            // (arbitrary roll plus a snap when leaving the zenith): take the roll
            // from the PREVIOUS camera frame, which is continuous. On the very
            // first frame use any non-parallel axis.
            Vector3 rotationUp = up;
            if (Mathf.Abs(Vector3.Dot(lookDirection, up)) > 0.999f)
            {
                Vector3 previous = transform.up - (lookDirection * Vector3.Dot(transform.up, lookDirection));
                rotationUp = previous.sqrMagnitude > 1e-6f
                    ? previous.normalized
                    : (Mathf.Abs(up.y) < 0.9f ? Vector3.up : Vector3.right);
            }

            transform.rotation = Quaternion.LookRotation(lookDirection, rotationUp);

            // Lean: the camera moves forward along the look toward the point under
            // the crosshair, no closer than the clearance (don't clip the monitor).
            // No hit — travel the full distance.
            Vector3 eye = Target.position + eyeOffset;
            if (leanBlend > 0.0001f)
            {
                float travel = LeanMaxMeters;
                if (Physics.Raycast(new Ray(eye, lookDirection), out RaycastHit hit,
                        LeanMaxMeters + LeanMinClearance, ~0, QueryTriggerInteraction.Ignore))
                {
                    travel = Mathf.Max(0f, hit.distance - LeanMinClearance);
                }

                eye += lookDirection * (travel * leanBlend);
            }

            transform.position = eye;

            Camera cam = GetComponent<Camera>();
            if (cam != null && LeanFieldOfView > 0f && leanBlend > 0.0001f)
            {
                cam.fieldOfView = Mathf.Lerp(baseFieldOfView, LeanFieldOfView, leanBlend);
            }
            else if (cam != null)
            {
                cam.fieldOfView = baseFieldOfView;
            }

            UpdateClipPlanes();
        }

        /// <summary>
        /// The look is stored as a VECTOR (not a yaw number): when the local-up
        /// frame rotates (orbit, planet spin) the vector follows it, so the view
        /// turns physically instead of drifting away from the mouse.
        /// </summary>
        private void TransportLook(Vector3 up)
        {
            if (!hasLook)
            {
                // Initial look aims at the sun (from simulation data): the player
                // sees the star right at spawn. No star — fall back.
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
        /// Mouse: left/right is the azimuth around the local up, up/down is the
        /// elevation angle (like a normal FPS, but against the planet's "down").
        /// Pitch is clamped BEFORE the rotation, so the pole is unreachable by construction.
        /// </summary>
        private void ApplyMouse(Vector3 up)
        {
            Vector2 md = PlayerInput.MouseDelta();
            // Finer mouse in the lean — otherwise at arm's length the crosshair
            // skips over small monitor buttons.
            float sensitivity = MouseSensitivity * Mathf.Lerp(1f, LeanSensitivityScale, leanBlend);
            float yaw = md.x * sensitivity;
            float pitch = md.y * sensitivity;

            // Dead zone: with a locked cursor in the editor the new Input System
            // can feed micro delta noise every frame, continuously nudging the
            // camera (view jitter). Kill small values.
            const float deadzone = 0.02f;
            if (Mathf.Abs(yaw) < deadzone)
            {
                yaw = 0f;
            }

            if (Mathf.Abs(pitch) < deadzone)
            {
                pitch = 0f;
            }

            // Azimuth: the horizontal component of the look. Exactly at the pole
            // it degenerates — use the PREVIOUS frame's azimuth, which is continuous.
            Vector3 flat = Vector3.ProjectOnPlane(lookDirection, up);
            bool degenerate = false;
            if (flat.sqrMagnitude < 1e-8f)
            {
                flat = lastFlatDirection;
                degenerate = true;
            }

            if (flat.sqrMagnitude < 1e-8f)
            {
                // Pole on the very first frame (look straight along the vertical —
                // happens on a spawn with the sun at zenith): use any fixed
                // azimuth so the frame does not jerk.
                flat = Vector3.ProjectOnPlane(Vector3.forward, up);
                if (flat.sqrMagnitude < 1e-6f)
                {
                    flat = Vector3.ProjectOnPlane(Vector3.right, up);
                }

                degenerate = true;
            }

            // The horizontal magnitude is needed BEFORE normalization: after it
            // the magnitude is always 1 and atan2 degenerates to asin(dot),
            // losing the sign past the pole.
            float flatMagnitude = flat.magnitude;
            flat.Normalize();
            lastFlatDirection = flat;

            // Elevation from the horizontal: unambiguous while the azimuth is
            // known. When degenerate (exactly at the pole) take the tilt by sign — ±PitchClamp.
            float elevation = degenerate
                ? (Vector3.Dot(lookDirection, up) >= 0f ? PitchClamp : -PitchClamp)
                : Mathf.Atan2(Vector3.Dot(lookDirection, up), flatMagnitude) * Mathf.Rad2Deg;

            if (yaw != 0f)
            {
                flat = Quaternion.AngleAxis(yaw, up) * flat;
                lastFlatDirection = flat;
            }

            // Mouse up means look up. Clamp BEFORE the rotation: the pole is unreachable.
            elevation = Mathf.Clamp(elevation + pitch, -PitchClamp, PitchClamp);

            float e = elevation * Mathf.Deg2Rad;
            lookDirection = (flat * Mathf.Cos(e)) + (up * Mathf.Sin(e));
        }

        /// <summary>
        /// The mouse is ignored — log the REASON once per state change.
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

        /// <summary>Local up (from the double world, via the bridge).</summary>
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
        /// Eye offset along the dominant body's surface normal (on the ground the
        /// ship lies on the terrain, so without the offset the camera is
        /// underground; in flight the radial offset is visually neutral). When
        /// swimming use the low SwimEyeHeightMeters (a swimmer lies flat),
        /// otherwise the head never goes underwater in shallow water.
        /// </summary>
        private void ComputeEyeOffset()
        {
            eyeOffset = Vector3.zero;
            if (Runner == null || Runner.Ship == null)
            {
                return;
            }

            double eye;
            if (Runner.PlayerMode == PlayerMode.Swimming)
            {
                eye = SwimEyeHeightMeters;
            }
            else
            {
                // Eyes no higher than the top of the lowest capsule (0.8 m) minus
                // 10 cm, otherwise the camera ends up inside a sloped wall.
                double crouchEye = System.Math.Min(CrouchEyeMeters, 0.7d);
                eye = EyeHeightMeters + ((crouchEye - EyeHeightMeters) * crouchBlend);
            }

            Vector3 up = ComputeLocalUp();
            if (seatedBlend > 0f)
            {
                // Seated: the offset is along the seat's up (the ship may be
                // tilted), the height is SeatedEyeMeters; both blend smoothly.
                // The direction is taken directly in render axes: the astro
                // version through the flat bridge points wrong in the contact zone.
                Vector3 seatUp = Runner.PlayerSeatedUpRender.sqrMagnitude > 1e-8f
                    ? Runner.PlayerSeatedUpRender.normalized
                    : up;
                up = Vector3.Slerp(up, seatUp, seatedBlend);
                eye += (SeatedEyeMeters - eye) * seatedBlend;
            }

            eyeOffset = up * (float)eye;
        }

        /// <summary>One-shot after sitting down: aim where the seat asks (CockpitSeat.LocalLookDirection).</summary>
        private void AimAtSeatLook()
        {
            if (playerController == null)
            {
                playerController = FindAnyObjectByType<PlayerController>();
            }

            CockpitSeat seat = playerController != null ? playerController.SeatedSeat : null;
            if (seat == null || seat.Anchor == null || seat.LocalLookDirection.sqrMagnitude < 1e-6f)
            {
                return;
            }

            lookDirection = seat.Anchor.TransformDirection(seat.LocalLookDirection).normalized;
            hasLook = true;
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

            // near comes from the altitude above LOCAL terrain (camera at ground level).
            double altitude = System.Math.Max(0.5d, relative.Magnitude - (radius + groundHeight));
            double near = System.Math.Max(0.1d, altitude * 0.001d);

            // Seated in the cockpit: the near plane does not depend on altitude (see SeatedNearClipMeters).
            if (Runner.PlayerSeated)
            {
                near = SeatedNearClipMeters;
            }

            // far reaches the physical horizon with margin for distant PEAKS:
            // camera and peak heights are measured from the MEAN radius.
            double cameraHeight = System.Math.Max(0.5d, relative.Magnitude - radius);
            double peakHeight = 0d;
            if (body.Terrain is HeightfieldTerrain heightfield)
            {
                peakHeight = System.Math.Max(0d, heightfield.AmplitudeMeters)
                    * (1d + System.Math.Max(0d, heightfield.ContinentDepth));
            }

            double horizon = SkyPhysics.MaxSightDistance(radius, cameraHeight, peakHeight);
            double far = System.Math.Max((horizon * 1.1d) + 1e3d, 20000d);

            // far is NOT stretched for the atmosphere: it is drawn as a
            // camera-centered dome and always fits the frustum. Otherwise
            // near/far ~1e8 causes z-fighting (terrain/disc shimmer without motion).
            camera.nearClipPlane = (float)near;
            camera.farClipPlane = (float)far;
        }
    }
}
