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

        [Tooltip("Ближняя плоскость камеры ВНУТРИ корабля (м): сидя, идя по кабине или «призраком» " +
            "в InShip. Обычная считается от высоты над рельефом (высота*0.001, не меньше 0.1), " +
            "а в кабине на высоте она раздувается до десятков метров и срезает корпус, стены и " +
            "мониторы (стоя в кабине на 10 км ближняя плоскость = 10 м: видна только земля). " +
            "Внутри корабля всё рядом — фиксируем маленькую.")]
        public float SeatedNearClipMeters = 0.03f;

        [Tooltip("Ближняя плоскость камеры в EVA (м): на орбите ближняя плоскость по высоте " +
            "(высота*0.001) уходит на сотни метров и срезает ракету, стоящую в нескольких " +
            "метрах от игрока. В свободном полёте рядом только корабль — держим маленькую.")]
        public float EvaNearClipMeters = 0.1f;

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

        [Tooltip("Время подлёта камеры к экрану (E) и обратно, с.")]
        public float FocusSeconds = 0.4f;

        private ICameraFocus focusPose;
        private bool focusWanted;
        private float focusBlend;

        /// <summary>Smoothly fly to the target (screen, console). The pose is queried from the target every frame.</summary>
        public void SetFocus(ICameraFocus target)
        {
            focusPose = target;
            focusWanted = target != null;
        }

        /// <summary>Smoothly return to the normal view (the target is kept until the return finishes).</summary>
        public void ClearFocus()
        {
            focusWanted = false;
        }

        private void ApplyFocusBlend(Camera cam)
        {
            // The target is a destroyed Unity object (ship removed): reset.
            if (focusPose is UnityEngine.Object destroyedCheck && destroyedCheck == null)
            {
                focusPose = null;
                focusWanted = false;
            }

            focusBlend = Mathf.MoveTowards(focusBlend, focusWanted ? 1f : 0f,
                Time.unscaledDeltaTime / Mathf.Max(0.05f, FocusSeconds));
            if (focusBlend <= 0f)
            {
                focusPose = null;
                return;
            }

            if (focusPose == null || cam == null
                || !focusPose.TryGetFocusPose(cam, out Vector3 position, out Quaternion rotation))
            {
                return;
            }

            float t = focusBlend * focusBlend * focusBlend * ((focusBlend * ((focusBlend * 6f) - 15f)) + 10f);
            transform.position = Vector3.Lerp(transform.position, position, t);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, t);
        }

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

        /// <summary>
        /// Cursor claims: UI owners (settings/cheat menus, the builder window, the
        /// expanded tablet, the cockpit screen focus) that need a free cursor. While any
        /// claim is held the camera neither captures the cursor nor reads the mouse.
        /// A counter, not a saved flag: a nested owner (menu opened over a window) cannot
        /// leave the cursor released forever by restoring a stale value — every owner
        /// releases exactly the claim it took, whatever the order of openings was.
        /// </summary>
        private static int cursorClaims;

        /// <summary>A UI currently owns the mouse: the camera yields it.</summary>
        public static bool CursorClaimed { get { return cursorClaims > 0; } }

        /// <summary>Take the cursor for a UI. Every claim needs exactly one release.</summary>
        public static void ClaimCursor(string owner)
        {
            cursorClaims++;
            Debug.Log("[Cursor] +" + cursorClaims + " (" + owner + ")");
        }

        /// <summary>Give the cursor back; the camera re-captures it on the next Update.</summary>
        public static void ReleaseCursor(string owner)
        {
            cursorClaims = Mathf.Max(0, cursorClaims - 1);
            Debug.Log("[Cursor] -" + cursorClaims + " (" + owner + ")");
        }

        /// <summary>Owner-side helper: claim/release once per state change (idempotent).</summary>
        public static void SetCursorClaim(ref bool claimed, bool claim, string owner)
        {
            if (claim == claimed)
            {
                return;
            }

            claimed = claim;
            if (claim)
            {
                ClaimCursor(owner);
            }
            else
            {
                ReleaseCursor(owner);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCursorClaims()
        {
            // The project enters Play with DisableDomainReload: a claim left by a
            // previous session would keep the cursor released forever.
            cursorClaims = 0;
        }

        /// <summary>Smooth stand ↔ crouch blend (0..1) for eye height.</summary>
        private float crouchBlend;

        /// <summary>Smooth standing ↔ seated blend (0..1) for eye height.</summary>
        private float seatedBlend;

        /// <summary>Whether the player was seated last frame (one-shot aim when sitting down).</summary>
        private bool wasSeated;

        /// <summary>Last drawn seat rotation while seated: the view follows its FULL delta (yaw included).</summary>
        private Quaternion lastSeatRotation;
        private bool hasSeatRotation;

        private PlayerController playerController;

        /// <summary>Smooth normal ↔ lean blend (0..1) for the RMB lean-in.</summary>
        private float leanBlend;

        private float baseFieldOfView = 60f;

        /// <summary>The camera's authored FOV: the settings menu's "reset" returns to it.</summary>
        private float defaultFieldOfView = 60f;

        public float DefaultFieldOfView { get { return defaultFieldOfView; } }

        private void Start()
        {
#if UNITY_EDITOR
            // The project enters Play with DisableDomainReload, so statics survive
            // the session: a look override from a previous capture would otherwise
            // stay on and the next Play would stare at the ceiling.
            ClearDebugLook();
#endif
            ApplyAntiAliasing();

            if (LockCursor)
            {
                SetCursorLocked(true);
            }

            Camera camComponent = GetComponent<Camera>();
            if (camComponent != null)
            {
                defaultFieldOfView = camComponent.fieldOfView;
                baseFieldOfView = defaultFieldOfView;
            }

            // First launch: the scene camera's own FOV becomes the setting's default.
            if (GameSettings.Data.fov <= 0f)
            {
                GameSettings.Data.fov = baseFieldOfView;
            }
        }

        /// <summary>
        /// Anti-aliasing from the settings menu (0 none, 1 FXAA, 2 SMAA, 3 TAA). Until the
        /// player picks one, the TemporalAA / SubpixelAA fields above decide, as before.
        /// </summary>
        public void ApplyAntiAliasing()
        {
            var hdCamera = GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
            if (hdCamera == null)
            {
                return;
            }

            SettingsData settings = GameSettings.Data;
            if (settings.antiAliasing < 0)
            {
                settings.antiAliasing = TemporalAA ? 3 : (SubpixelAA ? 2 : 0);
            }

            switch (settings.antiAliasing)
            {
                case 1:
                    hdCamera.antialiasing = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.FastApproximateAntialiasing;
                    break;
                case 2:
                    hdCamera.antialiasing = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    break;
                case 3:
                    hdCamera.antialiasing = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                    break;
                default:
                    hdCamera.antialiasing = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.None;
                    break;
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
            bool peek = Runner != null && Runner.BuildMode && PlayerInput.Held(GameAction.BuildPeek);

            // A UI (menu, tablet, screen focus) owns the mouse right now: leave its
            // cursor alone — it releases the claim when it closes.
            bool uiOwnsCursor = CursorClaimed;

            // Esc now belongs to the settings menu (SettingsMenu), which claims the
            // cursor while open and releases it on close (see SetCursorClaim).

            if (!uiOwnsCursor && PlayerInput.MouseLeftDown() && Cursor.lockState != CursorLockMode.Locked && !peek)
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

            if (UnityEngine.Application.isFocused && !cursorReleasedByUser && !peek && !uiOwnsCursor
                && Cursor.lockState != CursorLockMode.Locked)
            {
                SetCursorLocked(true);
            }

            if (peek && !uiOwnsCursor)
            {
                SetCursorLocked(false);
            }
        }

        private void LateUpdate()
        {
            // Diagnostic logging sits BEFORE the early return so it always runs.
            diagnosticTimer += Time.deltaTime;
            if (LogCameraDiagnostics && diagnosticTimer >= 1f)
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
            if (seated && playerController == null)
            {
                playerController = FindAnyObjectByType<PlayerController>();
            }

            seatedBlend = Mathf.MoveTowards(seatedBlend, seated ? 1f : 0f, Time.deltaTime * 5f);

            // Seating: one-shot aim toward the direction the seat requests (a couple of monitors).
            if (seated && !wasSeated)
            {
                AimAtSeatLook();
            }

            wasSeated = seated;

            // RMB leans in close (monitor buttons are readable and clickable
            // precisely). In build mode RMB is taken by placement/slot menu — leave it.
            bool lean = LeanMaxMeters > 0f && GameSettings.Data.lean && PlayerInput.Held(GameAction.Lean) && focusPose == null
                && (Runner == null || !Runner.BuildMode);
            leanBlend = Mathf.MoveTowards(leanBlend, lean ? 1f : 0f, Time.deltaTime * 6f);

            ComputeEyeOffset();
            Vector3 up = ComputeLocalUp();

            // Seated: the view is fixed to the SEAT, not to the world. Transport the look by the
            // seat's full rotation delta (yaw included); the up-only transport below would let
            // the cabin yaw around a stationary view while the player is strapped in.
            bool seatTracked = false;
            Quaternion seatRotation = Quaternion.identity;
            if (seated && playerController != null && playerController.SeatedSeat != null
                && playerController.SeatedSeat.Anchor != null)
            {
                seatRotation = playerController.SeatedSeat.Anchor.rotation;
                seatTracked = true;
            }

            if (seatTracked)
            {
                if (hasSeatRotation)
                {
                    lookDirection = (seatRotation * Quaternion.Inverse(lastSeatRotation)) * lookDirection;
                }

                lastSeatRotation = seatRotation;
                hasSeatRotation = true;
            }
            else
            {
                hasSeatRotation = false;
            }

            TransportLook(up, !seatTracked);
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                NoteMouseIgnored(null);
                ApplyMouse(up);
            }
            else
            {
            NoteMouseIgnored("курсор не захвачен ("
                + (UnityEngine.Application.isFocused ? "окно в фокусе" : "окно не в фокусе")
                + (cursorReleasedByUser ? ", отпущен Esc" : "")
                + ", заявок " + cursorClaims + ")");
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
            float fovSetting = GameSettings.Data.fov;
            baseFieldOfView = fovSetting > 0f ? fovSetting : defaultFieldOfView;

            if (cam != null && LeanFieldOfView > 0f && leanBlend > 0.0001f)
            {
                cam.fieldOfView = Mathf.Lerp(baseFieldOfView, LeanFieldOfView, leanBlend);
            }
            else if (cam != null)
            {
                cam.fieldOfView = baseFieldOfView;
            }

            ApplyFocusBlend(cam);
            UpdateClipPlanes();
        }

        /// <summary>
        /// The look is stored as a VECTOR (not a yaw number): when the local-up
        /// frame rotates (orbit, planet spin) the vector follows it, so the view
        /// turns physically instead of drifting away from the mouse.
        /// </summary>
        private void TransportLook(Vector3 up, bool transportFrame)
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
            else if (transportFrame && lastUp.sqrMagnitude > 1e-20f)
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
            float sensitivity = MouseSensitivity * GameSettings.Data.mouseSensitivity
                * Mathf.Lerp(1f, LeanSensitivityScale, leanBlend);
            float yaw = md.x * sensitivity;
            float pitch = md.y * sensitivity * (GameSettings.Data.invertY ? -1f : 1f);

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

            // Aboard a vessel the vertical is the cabin's: what presses the player to the floor
            // is the thrust along the cabin axis, not the planet's radial. Seated — the seat's
            // up (refreshed by PlayerView before this camera), carried — the platform's up.
            if (Runner.PlayerSeated && Runner.PlayerSeatedUpRender.sqrMagnitude > 1e-8f)
            {
                return Runner.PlayerSeatedUpRender.normalized;
            }

            if (Runner.TryGetCarriedUpRender(out Vector3 carriedUp))
            {
                return carriedUp;
            }

            // Free-flight EVA: the inertial camera/input vertical, inherited from the cabin at
            // the hatch. It must not follow the vessel's rotation (the view would turn with a
            // spinning craft); the collision body has its own hatch-aligned axis.
            if (Runner.PlayerMode == PlayerMode.EVA && Runner.TryGetEvaUpRender(out Vector3 evaUp))
            {
                return evaUp;
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

            // Inside a vessel — seated, walking the cabin (carried) or the InShip
            // interior — the near plane must not depend on altitude: at 10 km the
            // altitude-scaled plane (1 mm per metre) is 10 m and cuts the hull, the
            // cabin and the monitors away, leaving only the terrain (see
            // SeatedNearClipMeters). While carried this also hides the lag of the
            // double-world position used above.
            if (Runner.PlayerSeated || Runner.PlayerCarried || Runner.PlayerMode == PlayerMode.InShip)
            {
                near = SeatedNearClipMeters;
            }
            else if (Runner.PlayerMode == PlayerMode.EVA)
            {
                // In free flight the altitude-scaled near plane would be hundreds of
                // metres (at 200 km it is 200 m) and clip the whole vessel the player
                // just stepped out of. Near the ground the surface controller takes
                // over and the scaled plane is back.
                near = EvaNearClipMeters;
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
