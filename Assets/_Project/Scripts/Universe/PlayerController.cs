using Galilego.Core;
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
    /// E — универсальное действие: интеракция (луч из камеры ≤ 4 м) →
    /// сесть/встать → выйти/войти в корабль.
    /// </summary>
    [UnityEngine.DefaultExecutionOrder(-100)]
    public sealed class PlayerController : MonoBehaviour
    {
        [Tooltip("SimulationRunner сцены.")]
        public SimulationRunner Runner;

        [Tooltip("GO корабля (с ShipView) — для ходьбы внутри.")]
        public Transform ShipTransform;

        [Tooltip("Скорость ходьбы (м/с).")]
        public float WalkSpeed = 2f;

        [Tooltip("Скорость бега (м/с, Shift).")]
        public float RunSpeed = 5f;

        [Tooltip("Ускорение джетпака (м/с²).")]
        public float JetpackThrust = 20f;

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

        /// <summary>Интерактибл под прицелом (для подсказки и E).</summary>
        public Interactable Focused { get; private set; }

        /// <summary>Кресло, в котором сидим (null — не сидим).</summary>
        public CockpitSeat SeatedSeat { get; private set; }

        /// <summary>Локальный оффсет внутри корабля (заглушка интерьера, м).</summary>
        public Vector3 ShipLocalOffset { get; private set; }

        public bool Seated => SeatedSeat != null;

        private void Update()
        {
            if (Runner == null)
            {
                return;
            }

            bool jetpackToggle = PlayerInput.DoubleDown(GameKey.Space);
            UpdateFocus();
            if (PlayerInput.Down(GameKey.E))
            {
                HandleE();
            }

            UpdateIntent(jetpackToggle);
        }

        private void HandleE()
        {
            if (Focused != null)
            {
                Focused.Interact(this);
                return;
            }

            if (Seated)
            {
                // Встать с кресла: оффсет — у кресла, чтобы не телепортироваться.
                if (SeatedSeat.Anchor != null && ShipTransform != null)
                {
                    ShipLocalOffset = ShipTransform.InverseTransformPoint(SeatedSeat.Anchor.position)
                        + new Vector3(0f, 0f, -1.2f);
                }

                SeatedSeat = null;
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

        public void EnterSeat(CockpitSeat seat)
        {
            SeatedSeat = seat;
        }

        private void UpdateFocus()
        {
            Focused = null;
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            if (Physics.Raycast(
                    new Ray(camera.transform.position, camera.transform.forward),
                    out RaycastHit hit,
                    4f))
            {
                Focused = hit.collider.GetComponent<Interactable>();
            }
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

        private void UpdateIntent(bool jetpackToggle)
        {
            PlayerIntent intent = PlayerIntent.Idle;
            Camera camera = Camera.main;

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
                    ApplyJetpackInput(ref intent, camera, Runner.JetpackActive || jetpackToggle);
                    break;

                case PlayerMode.OnSurface:
                    intent.JetpackToggle = jetpackToggle;
                    if (camera != null)
                    {
                        Vector3 flatForward = Vector3.ProjectOnPlane(camera.transform.forward, camera.transform.up);
                        flatForward.Normalize();
                        Vector3 move = (flatForward * ((PlayerInput.Held(GameKey.W) ? 1f : 0f) - (PlayerInput.Held(GameKey.S) ? 1f : 0f)))
                            + (camera.transform.right * ((PlayerInput.Held(GameKey.D) ? 1f : 0f) - (PlayerInput.Held(GameKey.A) ? 1f : 0f)));
                        if (move.sqrMagnitude > 0f)
                        {
                            float speed = PlayerInput.Held(GameKey.LeftShift) ? RunSpeed : WalkSpeed;
                            Vector3d walkAstro = AstroFrame.ToAstro(move.normalized);
                            intent.WalkDirection = walkAstro.Normalized;
                            intent.WalkSpeed = speed;
                        }

                        intent.Jump = PlayerInput.Down(GameKey.Space) && !jetpackToggle;
                        ApplyJetpackInput(ref intent, camera, (Runner.JetpackActive && Runner.PlayerAirborne) || jetpackToggle);
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

        private void OnGUI()
        {
            if (Focused != null)
            {
                GUI.Label(new Rect(Screen.width * 0.5f - 150f, Screen.height * 0.5f + 30f, 300f, 30f), Focused.Prompt);
            }
        }
    }
}
