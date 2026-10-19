using Galilego.Universe;
using UnityEngine;

namespace Galilego.Tablet
{
    /// <summary>
    /// A screen (monitor/tablet) the player can "walk up" to: while seated, aim at it
    /// -> the screen is outlined -> E -> the camera smoothly flies close, the cursor is
    /// released, and buttons are clicked with the mouse; E / Esc — back away.
    ///
    /// No manual setup: ShipTablet adds this component itself (see
    /// EnsureFor) to the parent of its ScreenRenderer, i.e. the monitor group. A new
    /// screen = ShipTablet with ScreenRenderer + a collider on the screen mesh, nothing else.
    /// The component can also be placed manually to change settings.
    ///
    /// The frame size and orientation come from the screen mesh (the thinnest axis is
    /// the normal, the screen "up" is the axis closest to the view at press time), so it
    /// does not matter how the model was exported.
    /// </summary>
    public sealed class InteractiveScreen : Interactable, ICameraFocus
    {
        [Tooltip("Планшет этого экрана. Пусто — ближайший ShipTablet выше по иерархии.")]
        public ShipTablet Tablet;

        [Tooltip("Меш экрана: по нему считается размер и поворот кадра. Пусто — Tablet.ScreenRenderer.")]
        public Renderer ScreenRenderer;

        [Tooltip("Приближаться только сидя (стоя экран не подсвечивается и не реагирует на E).")]
        public bool RequireSeated = true;

        [Tooltip("Запас по краям при приближении: 1 = экран впритык во весь кадр.")]
        [Min(1f)]
        public float FitMargin = 1.12f;

        [SerializeField]
        private string promptText = "Приблизить экран (E)";

        private int axisNormal;
        private int axisUp = 1;
        private float signNormal = 1f;
        private float signUp = 1f;
        private bool frameReady;

        public override string Prompt
        {
            get { return promptText; }
        }

        /// <summary>The mouse pointer is currently over the screen (for hiding the system cursor).</summary>
        public bool PointerOnScreen
        {
            get { return Tablet != null && Tablet.PointerOnScreen; }
        }

        public override bool IsAvailableTo(PlayerController player)
        {
            if (Tablet == null || ScreenRenderer == null || Tablet.Expanded)
            {
                return false;
            }

            return !RequireSeated || (player != null && player.Seated);
        }

        public override void Interact(PlayerController player)
        {
            if (player != null)
            {
                player.EnterScreenFocus(this);
            }
        }

        /// <summary>
        /// Find the ShipTablet and the screen mesh if they were not assigned manually.
        /// </summary>
        private void Resolve()
        {
            if (Tablet == null)
            {
                Tablet = GetComponentInParent<ShipTablet>();
            }

            if (ScreenRenderer == null && Tablet != null)
            {
                ScreenRenderer = Tablet.ScreenRenderer;
            }
        }

        private void Awake()
        {
            Resolve();
        }

        private void Reset()
        {
            Resolve();
        }

        private void OnDisable()
        {
            EndFocus();
        }

        /// <summary>
        /// Start focusing (called by PlayerController): remember which screen axis
        /// is "up" and from which side the player is looking, and enable mouse aiming.
        /// </summary>
        public void BeginFocus(Camera camera)
        {
            Resolve();
            frameReady = false;
            if (ScreenRenderer == null || camera == null)
            {
                return;
            }

            Transform t = ScreenRenderer.transform;
            Bounds b = ScreenRenderer.localBounds;
            float[] world =
            {
                t.TransformVector(new Vector3(b.size.x, 0f, 0f)).magnitude,
                t.TransformVector(new Vector3(0f, b.size.y, 0f)).magnitude,
                t.TransformVector(new Vector3(0f, 0f, b.size.z)).magnitude,
            };

            axisNormal = 0;
            if (world[1] < world[axisNormal])
            {
                axisNormal = 1;
            }

            if (world[2] < world[axisNormal])
            {
                axisNormal = 2;
            }

            int a = (axisNormal + 1) % 3;
            int c = (axisNormal + 2) % 3;
            Vector3 cameraUp = camera.transform.up;
            float ua = Vector3.Dot(t.TransformDirection(Axis(a)), cameraUp);
            float uc = Vector3.Dot(t.TransformDirection(Axis(c)), cameraUp);
            if (Mathf.Abs(ua) >= Mathf.Abs(uc))
            {
                axisUp = a;
                signUp = ua >= 0f ? 1f : -1f;
            }
            else
            {
                axisUp = c;
                signUp = uc >= 0f ? 1f : -1f;
            }

            Vector3 toCamera = camera.transform.position - t.TransformPoint(b.center);
            signNormal = Vector3.Dot(t.TransformDirection(Axis(axisNormal)), toCamera) >= 0f ? 1f : -1f;
            frameReady = true;

            if (Tablet != null)
            {
                Tablet.MouseAim = true;
            }
        }

        /// <summary>End focusing: aiming uses the screen center again.</summary>
        public void EndFocus()
        {
            frameReady = false;
            if (Tablet != null)
            {
                Tablet.MouseAim = false;
            }
        }

        public bool TryGetFocusPose(Camera camera, out Vector3 position, out Quaternion rotation)
        {
            position = default(Vector3);
            rotation = Quaternion.identity;
            if (!frameReady || ScreenRenderer == null || camera == null)
            {
                return false;
            }

            Transform t = ScreenRenderer.transform;
            Bounds b = ScreenRenderer.localBounds;
            Vector3 center = t.TransformPoint(b.center);
            Vector3 normal = (t.TransformDirection(Axis(axisNormal)) * signNormal).normalized;
            Vector3 up = (t.TransformDirection(Axis(axisUp)) * signUp).normalized;
            int axisRight = 3 - axisNormal - axisUp;
            float height = t.TransformVector(Axis(axisUp) * Part(b.size, axisUp)).magnitude;
            float width = t.TransformVector(Axis(axisRight) * Part(b.size, axisRight)).magnitude;

            float tanHalf = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float distance = Mathf.Max(
                (height * 0.5f) / tanHalf,
                (width * 0.5f) / (tanHalf * Mathf.Max(0.1f, camera.aspect))) * FitMargin;

            position = center + (normal * distance);
            rotation = Quaternion.LookRotation(-normal, up);
            return true;
        }

        private static Vector3 Axis(int i)
        {
            return i == 0 ? Vector3.right : (i == 1 ? Vector3.up : Vector3.forward);
        }

        private static float Part(Vector3 v, int i)
        {
            return i == 0 ? v.x : (i == 1 ? v.y : v.z);
        }

        /// <summary>
        /// Ensure a tablet screen has an InteractiveScreen (called by ShipTablet.Awake).
        /// We attach it to the parent of the screen mesh (the monitor group: then a ray
        /// into the housing counts too), but NOT to the model root and not to a group
        /// that already has other Interactables (the seat) — otherwise the component
        /// would intercept every collider in the model.
        /// </summary>
        public static InteractiveScreen EnsureFor(ShipTablet tablet)
        {
            if (tablet == null || tablet.ScreenRenderer == null)
            {
                return null;
            }

            Transform screen = tablet.ScreenRenderer.transform;
            InteractiveScreen existing = screen.GetComponentInParent<InteractiveScreen>();
            if (existing != null && (existing.ScreenRenderer == null || existing.ScreenRenderer == tablet.ScreenRenderer))
            {
                existing.Tablet = tablet;
                existing.ScreenRenderer = tablet.ScreenRenderer;
                return existing;
            }

            Transform host = screen.parent;
            if (host == null || host == tablet.transform || host.GetComponentInChildren<Interactable>(true) != null)
            {
                host = screen;
            }

            InteractiveScreen created = host.gameObject.AddComponent<InteractiveScreen>();
            created.Tablet = tablet;
            created.ScreenRenderer = tablet.ScreenRenderer;
            return created;
        }
    }
}
