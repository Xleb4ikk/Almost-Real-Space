using Galilego.Core;
using Galilego.Simulation.Player;
using UnityEngine;

namespace Galilego.Universe
{
    /// <summary>
    /// Ввод и намерения игрока (не физика — физику исполняет SimulationRunner).
    /// Режимы: InShip (ходьба внутри — заглушка вокруг точки корабля, интерьер
    /// и коллизии позже), EVA (джетпак: WASD вдоль камеры, Space вверх,
    /// LeftCtrl вниз; топливо — IJetpackFuel, сейчас бесконечное), OnSurface
    /// (WASD ходьба по касательной, Shift бег, Space прыжок), Swimming
    /// (WASD плавание по камере — взгляд вниз + W = нырнуть, Space всплыть,
    /// LeftCtrl погрузиться, Shift ускорение).
    /// E — универсальное действие: интеракция (область прицела ≤ 4 м) →
    /// сесть/встать → вне стройки — выйти/войти в корабль. Инвентарь стройки —
    /// на I (BuilderUI).
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

        [Tooltip("Ускорение джетпака (м/с²).")]
        public float JetpackThrust = 20f;

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
        /// Ввод заблокирован (открыто чит-меню): WASD/E игнорируются, чтобы
        /// нажатия на кнопки окна не двигали игрока. Ставит CheatMenu.
        /// </summary>
        public bool InputBlocked { get; set; }

        /// <summary>Интерактибл под прицелом (для подсказки и E).</summary>
        public Interactable Focused { get; private set; }

        /// <summary>Кресло, в котором сидим (null — не сидим).</summary>
        public CockpitSeat SeatedSeat { get; private set; }

        /// <summary>Локальный оффсет внутри корабля (заглушка интерьера, м).</summary>
        public Vector3 ShipLocalOffset { get; private set; }

        public bool Seated => SeatedSeat != null;

        /// <summary>Пробы конуса прицела (AimCone) — буфер, чтобы не аллоцировать в кадре.</summary>
        private readonly Vector3[] focusDirections = new Vector3[AimCone.Samples];

        private void Start()
        {
            if (CheatMenu == null)
            {
                CheatMenu = FindAnyObjectByType<CheatMenu>();
            }
        }

        private void Update()
        {
            if (Runner == null)
            {
                return;
            }

            if (InputBlocked)
            {
                // Меню открыто: сбрасываем намерение, иначе «залипшая» прошлая
                // команда (WASD джетпака) продолжала бы двигать игрока. Цель под
                // прицелом тоже сбрасываем: подсказка «Открыть планшет (E)»
                // иначе висела бы поверх открытого планшета/меню.
                Focused = null;
                Runner.PlayerIntent = PlayerIntent.Idle;
                return;
            }

            if (Seated)
            {
                // Сидим: шаг ходьбы и прыжки не бегут, взгляд свободен;
                // E — встать (или закрыть монитор, если он открыт — тогда
                // этим занимается сам планшет, ввод заблокирован выше).
                Focused = null;
                Runner.PlayerIntent = PlayerIntent.Idle;
                if (PlayerInput.Down(GameKey.E))
                {
                    StandUp();
                }

                return;
            }

            bool jetpackToggle = PlayerInput.DoubleDown(GameKey.Space);
            bool hoverToggle = PlayerInput.DoubleDown(GameKey.LeftShift);
            UpdateFocus();
            // E — взаимодействие мира (кресло, планшет и т.п.) в ЛЮБОМ режиме;
            // в стройке инвентарь переехал на I, так что E здесь больше не
            // конфликтует. Вход/выход из корабля остаются вне стройки (HandleE).
            if (PlayerInput.Down(GameKey.E))
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

            // Вход в (невидимый призрак) корабль и выход из него — только вне
            // стройки: при сборке ракеты это только мешало бы.
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
                Runner.TryEnterShip(EnterDistanceMeters);
            }
        }

        /// <summary>
        /// Сесть в кресло: поза игрока фиксируется у якоря (шаг физики замирает,
        /// корабль продолжает движение). Пересчёт render → double-мир делает
        /// SimulationRunner кадром места (как ходьба). false — якоря/раннера нет.
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

        /// <summary>Встать: игрок ставится на ноги рядом с креслом, ходьба возвращается.</summary>
        public void StandUp()
        {
            if (SeatedSeat == null)
            {
                return;
            }

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

            Runner.UnseatPlayerAtRender(renderFeet);
        }

        /// <summary>«Вверх» кресла в render-мире: местная +Z якоря (локальный поворот — нулевой).</summary>
        private static Vector3 SeatUpRender(CockpitSeat seat)
        {
            return seat.Anchor.TransformDirection(Vector3.forward);
        }

        /// <summary>
        /// Цель E — ближайший коллайдер В ОБЛАСТИ ПРИЦЕЛА: пробы конуса AimCone
        /// (центр + кольцо). Раньше был один центральный луч — подсказка висела
        /// по точке в центре, а область кольца ничего не значила. Ближайшее
        /// попадание по ЛЮБОЙ пробе: стена перед креслом так же заслоняет его.
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
                    best = hit.collider.GetComponent<Interactable>();
                }
            }

            Focused = best;
        }

        private void ApplyJetpackInput(ref PlayerIntent intent, Camera camera, bool active)
        {
            if (!active || camera == null || JetpackFuel == null)
            {
                return;
            }

            Vector3 thrust = (camera.transform.forward * ((PlayerInput.Held(GameKey.W) ? 1f : 0f) - (PlayerInput.Held(GameKey.S) ? 1f : 0f)))
                + (camera.transform.right * ((PlayerInput.Held(GameKey.D) ? 1f : 0f) - (PlayerInput.Held(GameKey.A) ? 1f : 0f)))
                + (camera.transform.up * ((PlayerInput.Held(GameKey.Space) ? 1f : 0f) - (PlayerInput.Held(GameKey.LeftControl) ? 1f : 0f)));
            if (thrust.sqrMagnitude > 0f && JetpackFuel.TryConsume(Time.deltaTime))
            {
                intent.JetpackAccel = AstroFrame.ToAstro(thrust.normalized * JetpackThrust);
            }
        }

        /// <summary>
        /// Ввод висения (hover): WASD — перемещение в ГОРИЗОНТАЛЬНОЙ плоскости
        /// (проекция взгляда на плоскость местной вертикали — наклон камеры не
        /// уводит вверх/вниз), Space — вверх, Shift — вниз. Скорость — желаемая
        /// скорость относительно местного кадра; без ввода ноль — игрок замирает.
        /// Висение тоже тратит топливо джетпака.
        /// </summary>
        private void ApplyHoverInput(ref PlayerIntent intent, Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            // Местная вертикаль (астро): направление от центра тела.
            Vector3d up = new Vector3d(0d, 0d, 1d);
            OrbitingBody body = Runner != null ? Runner.DominantBody : null;
            if (body != null && Runner != null)
            {
                body.EvaluateWorldState(Runner.TimeSeconds, out Vector3d bodyPos, out _);
                Vector3d radial = Runner.PlayerPosition - bodyPos;
                if (radial.SqrMagnitude > 1e-12d)
                {
                    up = radial.Normalized;
                }
            }

            Vector3d look = AstroFrame.ToAstro(camera.transform.forward);
            Vector3d flatForward = look - (up * Vector3d.Dot(look, up));
            flatForward = flatForward.SqrMagnitude > 1e-12d ? flatForward.Normalized : Vector3d.Zero;

            Vector3d right = AstroFrame.ToAstro(camera.transform.right);
            right -= up * Vector3d.Dot(right, up);
            right = right.SqrMagnitude > 1e-12d ? right.Normalized : Vector3d.Zero;

            double forward = (PlayerInput.Held(GameKey.W) ? 1d : 0d) - (PlayerInput.Held(GameKey.S) ? 1d : 0d);
            double strafe = (PlayerInput.Held(GameKey.D) ? 1d : 0d) - (PlayerInput.Held(GameKey.A) ? 1d : 0d);
            double vertical = (PlayerInput.Held(GameKey.Space) ? 1d : 0d) - (PlayerInput.Held(GameKey.LeftShift) ? 1d : 0d);

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
                        // Заглушка интерьера: ходьба в касательной плоскости
                        // (локальный up камеры = вертикаль планеты), вокруг
                        // точки корабля, ограниченная радиусом.
                        Vector3 flatForward = Vector3.ProjectOnPlane(camera.transform.forward, camera.transform.up);
                        flatForward.Normalize();
                        Vector3 move = (flatForward * ((PlayerInput.Held(GameKey.W) ? 1f : 0f) - (PlayerInput.Held(GameKey.S) ? 1f : 0f)))
                            + (camera.transform.right * ((PlayerInput.Held(GameKey.D) ? 1f : 0f) - (PlayerInput.Held(GameKey.A) ? 1f : 0f)));
                        if (move.sqrMagnitude > 0f)
                        {
                            float speed = PlayerInput.Held(GameKey.LeftShift) ? RunSpeed : WalkSpeed;
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
                        ApplyJetpackInput(ref intent, camera, Runner.JetpackActive || jetpackToggle);
                    }

                    break;

                case PlayerMode.OnSurface:
                    intent.JetpackToggle = jetpackToggle;
                    intent.HoverToggle = hoverToggle;
                    if (camera != null)
                    {
                        Vector3 flatForward = Vector3.ProjectOnPlane(camera.transform.forward, camera.transform.up);
                        flatForward.Normalize();
                        Vector3 move = (flatForward * ((PlayerInput.Held(GameKey.W) ? 1f : 0f) - (PlayerInput.Held(GameKey.S) ? 1f : 0f)))
                            + (camera.transform.right * ((PlayerInput.Held(GameKey.D) ? 1f : 0f) - (PlayerInput.Held(GameKey.A) ? 1f : 0f)));
                        if (move.sqrMagnitude > 0f)
                        {
                            // Опора — деталь аппарата (sourceId из VesselSupport):
                            // тесные интерьеры ходят отдельной, медленной скоростью.
                            bool onVessel = Runner.PlayerGroundSourceId >= VesselSupport.SourceIdBase;
                            float speed = PlayerInput.Held(GameKey.LeftShift)
                                ? (onVessel ? VesselRunSpeed : RunSpeed)
                                : (onVessel ? VesselWalkSpeed : WalkSpeed);
                            Vector3d walkAstro = AstroFrame.ToAstro(move.normalized);
                            intent.WalkDirection = walkAstro.Normalized;
                            intent.WalkSpeed = speed;
                        }

                        // Пока висим, Space/Shift — вертикаль висения, а не прыжок.
                        bool hovering = Runner.PlayerHoverActive;
                        intent.Jump = PlayerInput.Down(GameKey.Space) && !jetpackToggle && !hovering;
                        if (hovering || (hoverToggle && Runner.PlayerAirborne))
                        {
                            ApplyHoverInput(ref intent, camera);
                        }
                        else
                        {
                            ApplyJetpackInput(ref intent, camera, (Runner.JetpackActive && Runner.PlayerAirborne) || jetpackToggle);
                        }
                    }

                    break;

                case PlayerMode.Swimming:
                    if (camera != null)
                    {
                        // Полный 3D по камере: взгляд вниз + W = нырнуть,
                        // Space/Ctrl — явные всплытие/погружение.
                        Vector3 swim = (camera.transform.forward * ((PlayerInput.Held(GameKey.W) ? 1f : 0f) - (PlayerInput.Held(GameKey.S) ? 1f : 0f)))
                            + (camera.transform.right * ((PlayerInput.Held(GameKey.D) ? 1f : 0f) - (PlayerInput.Held(GameKey.A) ? 1f : 0f)))
                            + (camera.transform.up * ((PlayerInput.Held(GameKey.Space) ? 1f : 0f) - (PlayerInput.Held(GameKey.LeftControl) ? 1f : 0f)));
                        if (swim.sqrMagnitude > 0f)
                        {
                            float speed = PlayerInput.Held(GameKey.LeftShift) ? SwimFastSpeed : SwimSpeed;
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
        /// Намерение ноуклипа: полный 3D по камере (WASD + Space/Ctrl), скорость
        /// — из CheatMenu.NoclipSpeed (fallback — поле инспектора). Направление
        /// нормализуется, скорость — как есть: чит-меню разрешает величины от
        /// ходьбы до 1e9 м/с, и клампы здесь означали бы, что верх диапазона
        /// не работает.
        /// </summary>
        private void UpdateNoclipIntent(ref PlayerIntent intent, Camera camera)
        {
            intent.NoclipSpeed = CheatMenu != null ? CheatMenu.NoclipSpeed : NoclipSpeed;
            if (camera == null)
            {
                return;
            }

            Vector3 move = (camera.transform.forward * ((PlayerInput.Held(GameKey.W) ? 1f : 0f) - (PlayerInput.Held(GameKey.S) ? 1f : 0f)))
                + (camera.transform.right * ((PlayerInput.Held(GameKey.D) ? 1f : 0f) - (PlayerInput.Held(GameKey.A) ? 1f : 0f)))
                + (camera.transform.up * ((PlayerInput.Held(GameKey.Space) ? 1f : 0f) - (PlayerInput.Held(GameKey.LeftControl) ? 1f : 0f)));
            if (move.sqrMagnitude > 0f)
            {
                intent.NoclipDirection = AstroFrame.ToAstro(move.normalized);
            }
        }

        private void OnGUI()
        {
            MeshSupportBase.DebugEnabled = DebugOverlay;
            if (DebugOverlay && Runner != null)
            {
                GUI.Label(new Rect(20f, 20f, 1100f, 90f),
                    "blocker: " + MeshSupportBase.DebugBlocker
                    + "\nblocked=" + Runner.PlayerBlocked + "  height=" + Runner.PlayerCapsuleHeight.ToString("F2")
                    + "  airborne=" + Runner.PlayerAirborne
                    + "  lift=" + Runner.PlayerDebugLift.ToString("F3")
                    + "  ground=" + Runner.PlayerGroundSourceId);
                if (Event.current.type == EventType.Repaint)
                {
                    MeshSupportBase.DebugBlocker = "-";
                }
            }

            if (Focused != null)
            {
                GUI.Label(new Rect(Screen.width * 0.5f - 150f, Screen.height * 0.5f + 30f, 300f, 30f), Focused.Prompt);
            }
        }
    }
}
