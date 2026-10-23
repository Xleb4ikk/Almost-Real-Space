using Galilego.Core;
using Galilego.Simulation.Player;
using Galilego.Tablet;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Player input and intent (not physics — SimulationRunner executes physics).
    /// Modes: InShip (walking inside — placeholder around the ship point, interior
    /// and collisions later), EVA (jetpack: WASD in the horizontal plane, Space up
    /// along the local vertical, LeftCtrl down; fuel is IJetpackFuel, currently infinite), OnSurface
    /// (WASD walks along the tangent, Shift runs, Space jumps), Swimming
    /// (WASD swims along the camera — look down + W dives, Space surfaces,
    /// LeftCtrl descends, Shift speeds up).
    /// E is the universal action: interaction (aim area ≤ 4 m) →
    /// sit/stand → outside build mode enter/exit the ship. The build inventory
    /// is on I (BuilderUI).
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-100)]
    public sealed class PlayerController : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("GO корабля (с ShipView) — для ходьбы внутри.")]
        public Transform ShipTransform;

        [Tooltip("Чит-меню (ноуклип). Пусто — берётся первый в сцене при старте.")]
        public CheatMenu CheatMenu;

        [Tooltip("Скорость ходьбы (м/с).")]
        public float WalkSpeed = 2f;

        [Tooltip("Скорость бега (м/с, Shift).")]
        public float RunSpeed = 5f;

        [Tooltip("Скорость ходьбы ПО АППАРАТУ (м/с): внутри капсулы/на деталях тесно — медленнее.")]
        public float VesselWalkSpeed = 1f;

        [Tooltip("Скорость бега по аппарату (м/с, Shift).")]
        public float VesselRunSpeed = 1.8f;

        [Tooltip("Ускорение джетпака по вертикали (м/с², Space/Ctrl).")]
        public float JetpackThrust = 20f;

        [Tooltip("Скорость движения в воздухе (м/с, EVA и полёт) — одна и та же с джетпаком и без него.")]
        public float AirSpeed = 6f;

        [Tooltip("Скорость перемещения в висении (м/с, WASD).")]
        public float HoverSpeed = 6f;

        [Tooltip("Вертикальная скорость в висении (м/с, Space вверх / Shift вниз).")]
        public float HoverVerticalSpeed = 3f;

        [Tooltip("Скорость плавания (м/с).")]
        public float SwimSpeed = 2.5f;

        [Tooltip("Скорость плавания с ускорением (м/с, Shift).")]
        public float SwimFastSpeed = 5f;

        [Tooltip("Дистанция входа в корабль (м, в double-мире).")]
        public double EnterDistanceMeters = 5d;

        [Tooltip("Радиус ходьбы внутри корабля (заглушка интерьера, м).")]
        public float ShipWalkRadius = 3f;

        [Tooltip("Топливо джетпака. По умолчанию бесконечное (MVP); подмени на бак позже.")]
        public IJetpackFuel JetpackFuel = new InfiniteJetpackFuel();

        [Tooltip("Скорость ноуклипа (м/с), пока чит-меню не задало свою. Итоговую берёт чит-меню.")]
        public float NoclipSpeed = 100f;

        [Tooltip("Диагностика ходьбы: что блокирует и поднимает игрока (оверлей в углу экрана).")]
        public bool DebugOverlay;

        /// <summary>
        /// Input is blocked (cheat menu open): WASD/E are ignored so clicks on
        /// window buttons do not move the player. Set by CheatMenu.
        /// </summary>
        public bool InputBlocked { get; set; }

        /// <summary>Interactable under the aim (for the hint and E).</summary>
        public Interactable Focused { get; private set; }

        /// <summary>Seat we are sitting in (null — not seated).</summary>
        public CockpitSeat SeatedSeat { get; private set; }

        /// <summary>Local offset inside the ship (interior placeholder, m).</summary>
        public Vector3 ShipLocalOffset { get; private set; }

        public bool Seated => SeatedSeat != null;

        /// <summary>The screen we have moved close to (null — not focused).</summary>
        public InteractiveScreen FocusedScreen { get; private set; }

        private FirstPersonCamera screenCamera;
        private bool screenFocusActive;

        /// <summary>True while the camera is zoomed onto a cockpit screen (Esc / E leave it, the settings menu stays closed).</summary>
        public bool InScreenFocus { get { return screenFocusActive; } }

        /// <summary>The screen focus holds a cursor claim (the camera yields the mouse while focused).</summary>
        private bool screenCursorClaimed;

        /// <summary>Aim-cone (AimCone) probes — buffer to avoid per-frame allocation.</summary>
        private readonly Vector3[] focusDirections = new Vector3[AimCone.Samples];

        private void Start()
        {
            if (CheatMenu == null)
            {
                CheatMenu = FindAnyObjectByType<CheatMenu>();
            }
        }

        private void OnDisable()
        {
            // A disabled/destroyed controller must not keep the cursor claimed.
            FirstPersonCamera.SetCursorClaim(ref screenCursorClaimed, false, "ScreenFocus");
        }

        private void Update()
        {
            if (Runner == null)
            {
                return;
            }

            if (InputBlocked)
            {
                // Menu open: reset the intent, otherwise a stuck previous command
                // (jetpack WASD) would keep moving the player. Also reset the aimed
                // target, otherwise the "Open tablet (E)" hint would float over the
                // open tablet/menu.
                Focused = null;
                Runner.PlayerIntent = PlayerIntent.Idle;
                return;
            }

            if (Seated)
            {
                // Seated: walking and jumping are disabled, the look is free. The crosshair
                // looks for interactables available while seated (screens): E on a screen —
                // move closer, E into empty space — stand up. When focused: E / Esc — back away.
                Runner.PlayerIntent = PlayerIntent.Idle;
                if (screenFocusActive)
                {
                    Focused = null;
                    if (FocusedScreen == null || PlayerInput.Down(GameAction.Interact) || PlayerInput.EscapeDown())
                    {
                        ExitScreenFocus();
                    }
                    else
                    {
                        // The system cursor stays VISIBLE over the focused screen: the
                        // tablet's own arrow (drawn on the panel) is a small hint, not a
                        // replacement — hiding the OS cursor left the player with no
                        // pointer at all when the crosshair reached the screen.
                        Cursor.visible = true;
                    }

                    return;
                }

                UpdateFocus();
                if (PlayerInput.Down(GameAction.Interact))
                {
                    if (Focused != null)
                    {
                        Focused.Interact(this);
                    }
                    else
                    {
                        StandUp();
                    }
                }

                return;
            }

            bool jetpackToggle = PlayerInput.DoubleDown(GameAction.Jump);
            bool hoverToggle = PlayerInput.DoubleDown(GameAction.Sprint);
            UpdateFocus();
            // E is world interaction (seat, tablet, etc.) in ANY mode; in build
            // mode the inventory moved to I, so E no longer conflicts here.
            // Ship enter/exit stays outside build mode (HandleE).
            if (PlayerInput.Down(GameAction.Interact))
            {
                HandleE();
            }

            UpdateIntent(jetpackToggle, hoverToggle);
        }

        private void HandleE()
        {
            if (Focused != null)
            {
                Focused.Interact(this);
                return;
            }

            // Entering (invisible ghost) and exiting the ship only outside build
            // mode: during rocket assembly it would only get in the way.
            if (Runner == null || Runner.BuildMode)
            {
                return;
            }

            if (Runner.PlayerMode == PlayerMode.InShip)
            {
                Camera camera = Camera.main;
                Vector3 exitUnity = camera != null ? camera.transform.forward : Vector3.up;
                Vector3d exitDirection = AstroFrame.ToAstro(exitUnity);
                if (exitDirection.SqrMagnitude < 1e-12d)
                {
                    exitDirection = new Vector3d(0d, 0d, 1d);
                }

                Runner.TryExitShip(exitDirection.Normalized, 1d);
            }
            else
            {
                // A built vessel is boarded through its seat: the old InShip teleport
                // put the player at the ship point INSIDE the hull (no collisions, no
                // seat, see-through shell). Legacy scenes without a seat keep it.
                if (TryBoardNearestSeat())
                {
                    return;
                }

                Runner.TryEnterShip(EnterDistanceMeters);
            }
        }

        /// <summary>
        /// E next to the built vessel: sit in the nearest cockpit seat that belongs to
        /// a real vessel part (a ghost/preview has no VesselPartCollider). The seat
        /// anchor is inside the capsule, so its distance is the boarding distance.
        /// </summary>
        private bool TryBoardNearestSeat()
        {
            if (Runner == null || Runner.Ship == null)
            {
                return false;
            }

            // The player's feet are the floating-origin zero in EVA/on foot; the seat
            // anchor is already in the same render space.
            Vector3 playerPoint = FloatingOrigin.ToRender(Runner.PlayerPosition);
            CockpitSeat best = null;
            float bestDistance = (float)EnterDistanceMeters;
            CockpitSeat[] seats = FindObjectsByType<CockpitSeat>(FindObjectsInactive.Exclude);
            for (int i = 0; i < seats.Length; i++)
            {
                CockpitSeat seat = seats[i];
                if (seat == null || seat.Anchor == null)
                {
                    continue;
                }

                Galilego.Building.VesselPartCollider part =
                    seat.GetComponentInParent<Galilego.Building.VesselPartCollider>();
                if (part == null)
                {
                    continue;
                }

                // Near the hull counts even when the hatch is closed or the seat anchor
                // (inside the capsule) is farther than EnterDistanceMeters: E boards
                // through the seat instead of leaving the player locked out outside.
                float distance = Vector3.Distance(playerPoint, seat.Anchor.position);
                if (distance > bestDistance)
                {
                    distance = VesselSurfaceDistance(part, playerPoint, distance);
                }

                if (distance <= bestDistance)
                {
                    best = seat;
                    bestDistance = distance;
                }
            }

            return best != null && EnterSeat(best);
        }

        /// <summary>Distance from a point to a vessel part's hull (m); interaction-only colliders are not the hull.</summary>
        private static float VesselSurfaceDistance(Galilego.Building.VesselPartCollider part, Vector3 point, float fallback)
        {
            float best = fallback;
            Collider[] colliders = part.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger
                    || collider.GetComponentInParent<Galilego.Building.PlayerIgnore>() != null)
                {
                    continue;
                }

                float distance = Vector3.Distance(point, collider.ClosestPoint(point));
                if (distance < best)
                {
                    best = distance;
                }
            }

            return best;
        }

        /// <summary>
        /// Sit in a seat: the player pose is fixed at the anchor (the physics step
        /// freezes, the ship keeps moving). SimulationRunner converts render →
        /// double world via the site frame (like walking). false — no anchor/runner.
        /// </summary>
        public bool EnterSeat(CockpitSeat seat)
        {
            if (seat == null || seat.Anchor == null || Runner == null)
            {
                return false;
            }

            SeatedSeat = seat;
            Runner.SeatPlayerAtRender(seat.Anchor.position, SeatUpRender(seat), seat.EyeHeightMeters);
            return true;
        }

        /// <summary>Stand up: the player is placed on their feet beside the seat, walking resumes.</summary>
        public void StandUp()
        {
            if (SeatedSeat == null)
            {
                return;
            }

            ExitScreenFocus();
            CockpitSeat seat = SeatedSeat;
            SeatedSeat = null;
            if (Runner == null)
            {
                return;
            }

            Vector3 up = SeatUpRender(seat);
            Vector3 renderFeet = seat.Anchor.position - (up * seat.EyeHeightMeters)
                + seat.Anchor.TransformDirection(seat.StandOffsetMeters);
            if (Runner.PlayerMode == PlayerMode.InShip && ShipTransform != null)
            {
                ShipLocalOffset = ShipTransform.InverseTransformPoint(renderFeet);
            }

            // The part the seat belongs to: the player must stand up ALREADY carried by it,
            // otherwise a moving/accelerating vessel leaves them behind on the first frame.
            Galilego.Building.VesselPartCollider part = seat.GetComponentInParent<Galilego.Building.VesselPartCollider>();
            Runner.UnseatPlayerAtRender(renderFeet, part != null ? part.transform : null);
        }

        /// <summary>
        /// Move close to the screen: the camera smoothly flies right up to it, the cursor
        /// is released (Confined), and buttons on the screen are clicked with the mouse. Seated only.
        /// </summary>
        public bool EnterScreenFocus(InteractiveScreen screen)
        {
            if (screen == null || screenFocusActive || !Seated)
            {
                return false;
            }

            FirstPersonCamera cam = FindAnyObjectByType<FirstPersonCamera>();
            Camera camera = cam != null ? cam.GetComponent<Camera>() : null;
            if (cam == null || camera == null)
            {
                return false;
            }

            screenCamera = cam;
            // The focused screen is clicked with the mouse: CLAIM the cursor (the
            // camera stops capturing and reading it), release on exit — the camera
            // re-captures by itself. Counted claims cannot get stuck released.
            FirstPersonCamera.SetCursorClaim(ref screenCursorClaimed, true, "ScreenFocus");
            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = true;
            FocusedScreen = screen;
            screenFocusActive = true;
            screen.BeginFocus(camera);
            cam.SetFocus(screen);
            Debug.Log("[Focus] приближение к экрану: курсор=" + Cursor.lockState
                + " заявок=" + FirstPersonCamera.CursorClaimed
                + " mouseAim=" + (screen.Tablet != null ? screen.Tablet.MouseAim.ToString() : "-"));
            return true;
        }

        /// <summary>Back away from the screen: the camera smoothly returns, the cursor is locked again.</summary>
        public void ExitScreenFocus()
        {
            if (!screenFocusActive)
            {
                return;
            }

            screenFocusActive = false;
            if (FocusedScreen != null)
            {
                FocusedScreen.EndFocus();
            }

            FocusedScreen = null;
            if (screenCamera != null)
            {
                screenCamera.ClearFocus();
            }

            // Give the cursor back: the camera re-captures it on its next Update
            // (the claim model keeps Esc — which closed the screen — from leaving
            // the mouse dead, see FirstPersonCamera.SetCursorClaim).
            FirstPersonCamera.SetCursorClaim(ref screenCursorClaimed, false, "ScreenFocus");
            Debug.Log("[Focus] отход от экрана: курсор=" + Cursor.lockState
                + " заявок=" + FirstPersonCamera.CursorClaimed);
        }

        /// <summary>The seat's "up" in render world: the anchor's local +Z (local rotation is zero).</summary>
        private static Vector3 SeatUpRender(CockpitSeat seat)
        {
            return seat.Anchor.TransformDirection(Vector3.forward);
        }

        /// <summary>
        /// E target is the nearest collider IN THE AIM AREA: AimCone probes
        /// (center + ring). The nearest hit over ANY probe wins: a wall in front
        /// of a seat blocks it just the same.
        /// </summary>
        private void UpdateFocus()
        {
            Focused = null;
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            AimCone.FillDirections(camera.transform.forward, AimCone.Tangent(camera), focusDirections);
            float bestDistance = float.MaxValue;
            Interactable best = null;
            for (int i = 0; i < AimCone.Samples; i++)
            {
                if (Physics.Raycast(
                        new Ray(camera.transform.position, focusDirections[i]),
                        out RaycastHit hit,
                        4f)
                    && hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    // InParent: the collider may be a housing/screen inside a group with
                    // an Interactable. An unavailable target still occludes what is behind it.
                    Interactable candidate = hit.collider.GetComponentInParent<Interactable>();
                    best = candidate != null && candidate.IsAvailableTo(this) ? candidate : null;
                }
            }

            Focused = best;
        }

        /// <summary>
        /// Air control in EVA and while airborne: WASD sets the SAME horizontal
        /// speed with or without the jetpack (look pitch is ignored), released —
        /// the player brakes smoothly. The jetpack itself only lifts/lowers:
        /// Space/LeftCtrl along the local vertical.
        /// </summary>
        private void ApplyAirInput(ref PlayerIntent intent, Camera camera, bool jetpackActive)
        {
            if (camera == null)
            {
                return;
            }

            Vector3d up = LocalUpAstro();
            Vector3d look = AstroFrame.ToAstro(camera.transform.forward);
            Vector3d flatForward = look - (up * Vector3d.Dot(look, up));
            Vector3d right = AstroFrame.ToAstro(camera.transform.right);
            right -= up * Vector3d.Dot(right, up);
            right = right.SqrMagnitude > 1e-12d ? right.Normalized : Vector3d.Zero;
            flatForward = flatForward.SqrMagnitude > 1e-12d
                ? flatForward.Normalized
                : Vector3d.Cross(right, up);

            double forward = (PlayerInput.Held(GameAction.MoveForward) ? 1d : 0d) - (PlayerInput.Held(GameAction.MoveBack) ? 1d : 0d);
            double strafe = (PlayerInput.Held(GameAction.MoveRight) ? 1d : 0d) - (PlayerInput.Held(GameAction.MoveLeft) ? 1d : 0d);
            // In EVA/air Shift is the intuitive "down" (as in KSP) as well as Ctrl:
            // Sprint has no other meaning in flight (running only applies on the ground).
            double down = PlayerInput.Held(GameAction.Descend) ? 1d : 0d;
            if (down <= 0d && PlayerInput.Held(GameAction.Sprint))
            {
                down = 1d;
            }

            double vertical = (PlayerInput.Held(GameAction.Jump) ? 1d : 0d) - down;

            Vector3d horizontal = (flatForward * forward) + (right * strafe);
            intent.AirVelocity = horizontal.SqrMagnitude > 1e-12d
                ? horizontal.Normalized * AirSpeed
                : Vector3d.Zero;

            if (jetpackActive && vertical != 0d && JetpackFuel != null && JetpackFuel.TryConsume(Time.deltaTime))
            {
                intent.JetpackAccel = up * (vertical * JetpackThrust);
            }
        }

        /// <summary>Local vertical in astro axes: radial from the dominant body's center.</summary>
        private Vector3d LocalUpAstro()
        {
            OrbitingBody body = Runner != null ? Runner.DominantBody : null;
            if (body != null && Runner != null)
            {
                body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPos, out _);
                Vector3d radial = Runner.PlayerPosition - bodyPos;
                if (radial.SqrMagnitude > 1e-12d)
                {
                    return radial.Normalized;
                }
            }

            return new Vector3d(0d, 0d, 1d);
        }

        /// <summary>
        /// Hover input: WASD moves in the HORIZONTAL plane (the look is projected
        /// onto the local vertical plane — camera tilt does not drive up/down),
        /// Space up, Shift down. The velocity is the desired velocity relative to
        /// the local frame; zero with no input — the player holds still.
        /// Hover also drains jetpack fuel.
        /// </summary>
        private void ApplyHoverInput(ref PlayerIntent intent, Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            Vector3d up = LocalUpAstro();
            Vector3d look = AstroFrame.ToAstro(camera.transform.forward);
            Vector3d flatForward = look - (up * Vector3d.Dot(look, up));
            flatForward = flatForward.SqrMagnitude > 1e-12d ? flatForward.Normalized : Vector3d.Zero;

            Vector3d right = AstroFrame.ToAstro(camera.transform.right);
            right -= up * Vector3d.Dot(right, up);
            right = right.SqrMagnitude > 1e-12d ? right.Normalized : Vector3d.Zero;

            double forward = (PlayerInput.Held(GameAction.MoveForward) ? 1d : 0d) - (PlayerInput.Held(GameAction.MoveBack) ? 1d : 0d);
            double strafe = (PlayerInput.Held(GameAction.MoveRight) ? 1d : 0d) - (PlayerInput.Held(GameAction.MoveLeft) ? 1d : 0d);
            double vertical = (PlayerInput.Held(GameAction.Jump) ? 1d : 0d) - (PlayerInput.Held(GameAction.Sprint) ? 1d : 0d);

            Vector3d horizontal = (flatForward * forward) + (right * strafe);
            Vector3d velocity = horizontal.SqrMagnitude > 1e-12d
                ? horizontal.Normalized * HoverSpeed
                : Vector3d.Zero;
            if (System.Math.Abs(vertical) > 0.01d)
            {
                velocity += up * (vertical * HoverVerticalSpeed);
            }

            intent.HoverVelocity = velocity;
            if (JetpackFuel != null)
            {
                JetpackFuel.TryConsume(Time.deltaTime);
            }
        }

        private void UpdateIntent(bool jetpackToggle, bool hoverToggle)
        {
            PlayerIntent intent = PlayerIntent.Idle;
            Camera camera = Camera.main;

            if (Runner.NoclipActive)
            {
                UpdateNoclipIntent(ref intent, camera);
                Runner.PlayerIntent = intent;
                return;
            }

            switch (Runner.PlayerMode)
            {
                case PlayerMode.InShip:
                    if (!Seated && camera != null)
                    {
                        // Interior placeholder: walking in the tangent plane
                        // (the camera's local up is the planet vertical), around
                        // the ship point, limited by a radius.
                        Vector3 flatForward = Vector3.ProjectOnPlane(camera.transform.forward, camera.transform.up);
                        flatForward.Normalize();
                        Vector3 move = (flatForward * ((PlayerInput.Held(GameAction.MoveForward) ? 1f : 0f) - (PlayerInput.Held(GameAction.MoveBack) ? 1f : 0f)))
                            + (camera.transform.right * ((PlayerInput.Held(GameAction.MoveRight) ? 1f : 0f) - (PlayerInput.Held(GameAction.MoveLeft) ? 1f : 0f)));
                        if (move.sqrMagnitude > 0f)
                        {
                            float speed = PlayerInput.Held(GameAction.Sprint) ? RunSpeed : WalkSpeed;
                            ShipLocalOffset += move.normalized * (speed * Time.deltaTime);
                            if (ShipLocalOffset.magnitude > ShipWalkRadius)
                            {
                                ShipLocalOffset = ShipLocalOffset.normalized * ShipWalkRadius;
                            }
                        }
                    }

                    break;

                case PlayerMode.EVA:
                    intent.JetpackToggle = jetpackToggle;
                    intent.HoverToggle = hoverToggle;
                    if (Runner.PlayerHoverActive || hoverToggle)
                    {
                        ApplyHoverInput(ref intent, camera);
                    }
                    else
                    {
                        ApplyAirInput(ref intent, camera, Runner.JetpackActive || jetpackToggle);
                    }

                    break;

                case PlayerMode.OnSurface:
                    intent.JetpackToggle = jetpackToggle;
                    intent.HoverToggle = hoverToggle;
                    if (camera != null)
                    {
                        Vector3 flatForward = Vector3.ProjectOnPlane(camera.transform.forward, camera.transform.up);
                        flatForward.Normalize();
                        Vector3 move = (flatForward * ((PlayerInput.Held(GameAction.MoveForward) ? 1f : 0f) - (PlayerInput.Held(GameAction.MoveBack) ? 1f : 0f)))
                            + (camera.transform.right * ((PlayerInput.Held(GameAction.MoveRight) ? 1f : 0f) - (PlayerInput.Held(GameAction.MoveLeft) ? 1f : 0f)));
                        if (move.sqrMagnitude > 0f)
                        {
                            // The support is a vessel part (sourceId from VesselSupport):
                            // cramped interiors use a separate, slower speed.
                            bool onVessel = Runner.PlayerGroundSourceId >= VesselSupport.SourceIdBase;
                            float speed = PlayerInput.Held(GameAction.Sprint)
                                ? (onVessel ? VesselRunSpeed : RunSpeed)
                                : (onVessel ? VesselWalkSpeed : WalkSpeed);
                            Vector3d walkAstro = AstroFrame.ToAstro(move.normalized);
                            intent.WalkDirection = walkAstro.Normalized;
                            intent.WalkSpeed = speed;
                        }

                        // While hovering, Space/Shift are the hover vertical, not a jump.
                        bool hovering = Runner.PlayerHoverActive;
                        intent.Jump = PlayerInput.Down(GameAction.Jump) && !jetpackToggle && !hovering;
                        if (hovering || (hoverToggle && Runner.PlayerAirborne))
                        {
                            ApplyHoverInput(ref intent, camera);
                        }
                        else
                        {
                            ApplyAirInput(ref intent, camera, (Runner.JetpackActive && Runner.PlayerAirborne) || jetpackToggle);
                        }
                    }

                    break;

                case PlayerMode.Swimming:
                    if (camera != null)
                    {
                        // Full 3D along the camera: look down + W dives,
                        // Space/Ctrl are explicit surface/descend.
                        Vector3 swim = (camera.transform.forward * ((PlayerInput.Held(GameAction.MoveForward) ? 1f : 0f) - (PlayerInput.Held(GameAction.MoveBack) ? 1f : 0f)))
                            + (camera.transform.right * ((PlayerInput.Held(GameAction.MoveRight) ? 1f : 0f) - (PlayerInput.Held(GameAction.MoveLeft) ? 1f : 0f)))
                            + (camera.transform.up * ((PlayerInput.Held(GameAction.Jump) ? 1f : 0f) - (PlayerInput.Held(GameAction.Descend) ? 1f : 0f)));
                        if (swim.sqrMagnitude > 0f)
                        {
                            float speed = PlayerInput.Held(GameAction.Sprint) ? SwimFastSpeed : SwimSpeed;
                            Vector3d swimAstro = AstroFrame.ToAstro(swim.normalized);
                            intent.SwimDirection = swimAstro.Normalized;
                            intent.SwimSpeed = speed;
                        }
                    }

                    break;
            }

            Runner.PlayerIntent = intent;
        }

        /// <summary>
        /// Noclip intent: full 3D along the camera (WASD + Space/Ctrl), speed
        /// from CheatMenu.NoclipSpeed (fallback: the inspector field). The
        /// direction is normalized, the speed is used as-is: the cheat menu allows
        /// values from walking up to 1e9 m/s, and clamps here would break the top
        /// of the range.
        /// </summary>
        private void UpdateNoclipIntent(ref PlayerIntent intent, Camera camera)
        {
            intent.NoclipSpeed = CheatMenu != null ? CheatMenu.NoclipSpeed : NoclipSpeed;
            if (camera == null)
            {
                return;
            }

            Vector3 move = (camera.transform.forward * ((PlayerInput.Held(GameAction.MoveForward) ? 1f : 0f) - (PlayerInput.Held(GameAction.MoveBack) ? 1f : 0f)))
                + (camera.transform.right * ((PlayerInput.Held(GameAction.MoveRight) ? 1f : 0f) - (PlayerInput.Held(GameAction.MoveLeft) ? 1f : 0f)))
                + (camera.transform.up * ((PlayerInput.Held(GameAction.Jump) ? 1f : 0f) - (PlayerInput.Held(GameAction.Descend) ? 1f : 0f)));
            if (move.sqrMagnitude > 0f)
            {
                intent.NoclipDirection = AstroFrame.ToAstro(move.normalized);
            }
        }

        private void OnGUI()
        {
            MeshSupportBase.DebugEnabled = DebugOverlay;

            // EVA state is not visible anywhere else: without this the player cannot tell
            // whether Space/Shift should thrust (jetpack) or command a velocity (hover).
            if (Runner != null && !Runner.BuildMode && Runner.PlayerMode == PlayerMode.EVA)
            {
                string eva = "EVA   джетпак: " + (Runner.JetpackActive ? "ВКЛ" : "выкл")
                    + "   висение: " + (Runner.PlayerHoverActive ? "ВКЛ" : "выкл")
                    + "\nSpace — вверх, Shift/Ctrl — вниз. Двойной Space — джетпак, двойной Shift — висение.";
                GUI.Label(new Rect(12f, 96f, 900f, 44f), eva);
            }

            if (DebugOverlay && Runner != null)
            {
                GUI.Label(new Rect(20f, 20f, 1100f, 90f),
                    "blocker: " + MeshSupportBase.DebugBlocker
                    + "\nblocked=" + Runner.PlayerBlocked + "  height=" + Runner.PlayerCapsuleHeight.ToString("F2")
                    + "  airborne=" + Runner.PlayerAirborne
                    + "  lift=" + Runner.PlayerDebugLift.ToString("F3")
                    + "  ground=" + Runner.PlayerGroundSourceId
                    + "\n" + Runner.CabinDebug);
                if (Event.current.type == EventType.Repaint)
                {
                    MeshSupportBase.DebugBlocker = "-";
                }
            }

            // The "[E] ..." prompt is now drawn by InteractionHighlighter.
        }
    }
}
