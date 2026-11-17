using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Galilego.Universe;

namespace Galilego.Spacecraft
{
    /// <summary>
    /// Orion crew module: hatch and deployable tablet-arm animations.
    /// The Animator sits on the model root (nested FBX); clips are separate
    /// .anim files: hatch open/close and arm deploy/retract. Animator states hold
    /// a final pose, so methods can be called in any order — mechanisms do not
    /// interfere with each other.
    ///
    /// Open door pose: the open clip only rotates the door 100°, which in the final
    /// pose covers half the opening. So in the Hatch_Opening/Hatch_Open states the
    /// door is rotated further about its side edge (the door's Y axis) by
    /// openAngleDegrees and pressed against the hull beside the opening; in closed
    /// states it returns to the closed pose.
    ///
    /// Cabin lighting: the Cabin_Lights strips are emissive only. HDRP's SSGI is
    /// off, so emissive surfaces do not light the HDRP/Lit walls: in vacuum the
    /// cabin was lit solely by the Sun through the hatch, and its brightness
    /// depended on the ship's attitude and on what the camera happened to see.
    /// Real point lights at the strip ring give the cabin a stable interior light.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class OrionCrewModule : MonoBehaviour
    {
        private static readonly int HatchOpenId = Animator.StringToHash("OpenHatch");
        private static readonly int HatchCloseId = Animator.StringToHash("CloseHatch");
        private static readonly int ArmDeployId = Animator.StringToHash("DeployTabletArm");
        private static readonly int ArmRetractId = Animator.StringToHash("RetractTabletArm");

        [Tooltip("Аниматор модели (по умолчанию — на этом же объекте).")]
        [SerializeField] private Animator animator;

        [Header("Освещение кабины")]
        [Tooltip("Яркость потолочных ламп кабины, лм (HDRP Lumen). 0 — лампы не создаются.")]
        [SerializeField] private float cabinLampLumens = 10f;

        [Tooltip("Радиус действия лампы, м.")]
        [SerializeField] private float cabinLampRange = 7f;

        [Tooltip("Цвет ламп (тёплый белый).")]
        [SerializeField] private Color cabinLampColor = new Color(1f, 0.94f, 0.86f, 1f);

        [Tooltip("Сколько ламп вдоль потолка (0 — выключить).")]
        [SerializeField] private int cabinLampCount = 3;

        [Tooltip("Расстояние между лампами, м.")]
        [SerializeField] private float cabinLampSpacing = 1.25f;

        [Tooltip("Множитель яркости ламп, пока КАМЕРА рядом с модулем (снаружи или внутри " +
            "кабины): интерьер не гаснет на фоне залитого солнцем корпуса и планеты и не " +
            "мигает при переходе кресло/проход/люк.")]
        [SerializeField] private float cabinLampOutsideBoost = 5f;

        [Tooltip("Дальность усиления, м (камера — модуль). Внутри и снаружи кабины лампа " +
            "горит одинаково; базовая яркость остаётся только для далёкой камеры.")]
        [SerializeField] private float cabinLampOutsideRange = 60f;

        [Tooltip("Небольшое ослабление ламп, когда игрок снаружи кабины (EVA/прогулка): " +
            "изнутри кабина чуть темнее, чем кажется снаружи через остекление и на фоне " +
            "тёмного корпуса. 1 — без ослабления.")]
        [SerializeField] private float cabinLampOutsideScale = 0.9f;

        [Tooltip("Время плавного перехода яркости при входе/выходе из кабины, с.")]
        [SerializeField] private float cabinLampOutsideBlendSeconds = 1.5f;

        [Tooltip("Время перехода яркости ламп, с.")]
        [SerializeField] private float cabinLampBlendSeconds = 0.4f;

        [Tooltip("Угол открытия створки на шарнире (град.). ~160° прижимает её к борту сбоку от проёма.")]
        [SerializeField] private float openAngleDegrees = 160f;

        [SerializeField] private float openSeconds = 1.7f;

        private Transform door;
        private Quaternion closedRotation;
        private Vector3 closedPosition;
        private float swingSign = 1f;
        private float swingAngle;
        private Collider doorCollider;
        private bool doorColliderDisabledByUs;

        private Light[] cabinLamps;
        private float cabinLampBoost = 1f;
        private float cabinLampInsideBlend = 1f;
        private SimulationRunner lampRunner;

        private void Reset()
        {
            animator = GetComponent<Animator>();
        }

        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            // Door (by model node name).
            Transform[] all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].name == "Hatch_Door")
                {
                    door = all[i];
                }
            }

            if (door != null)
            {
                closedPosition = door.localPosition;
                closedRotation = door.localRotation;
                swingSign = ResolveOutwardSign();
            }
        }

        private void Start()
        {
            EnsureCabinLamps();
            lampRunner = FindAnyObjectByType<SimulationRunner>();
            cabinLampInsideBlend = IsOnboard() ? 1f : 0f;
        }

        /// <summary>The player is in the cabin: seated, walking on the floor, or in the legacy InShip mode.</summary>
        private bool IsOnboard()
        {
            if (lampRunner == null)
            {
                lampRunner = FindAnyObjectByType<SimulationRunner>();
            }

            return lampRunner != null
                && (lampRunner.PlayerSeated || lampRunner.PlayerCarried
                    || lampRunner.PlayerMode == PlayerMode.InShip);
        }

        /// <summary>
        /// Cabin lamps hold the boosted brightness while the camera is near the module —
        /// inside the cabin, in the aisle, in the hatch or right outside it. The base
        /// brightness is only for a camera far away (the view through the hatch does not
        /// read as a dark hole against the sunlit hull).
        ///
        /// The test is the DISTANCE to the module, not the camera in the Cabin_Lights
        /// bounds: that bounds is the AABB of the ceiling strip mesh, offset from the seat.
        /// In the seat the eye fell just outside it (a few centimetres in height and ~0.7 m
        /// behind), while standing up put it inside — so the lamps dropped 3x exactly at
        /// the sit/stand and hatch transitions (bright seated, dark standing/after EVA).
        /// Proximity is continuous; the only remaining inside/outside difference is a
        /// small attenuation (cabinLampOutsideScale) faded over a couple of seconds, not
        /// a hard flip.
        ///
        /// Called from LateUpdate, after VesselMirror/PlayerView/FirstPersonCamera:
        /// in Update the vessel pose is one physics frame behind the camera, and at
        /// orbital speed the module can read as hundreds of metres away from its own
        /// camera — the near-camera gate then never opens and the cabin stays dim.
        /// </summary>
        private void UpdateCabinLamps()
        {
            if (cabinLamps == null || cabinLamps.Length == 0)
            {
                return;
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector3 eye = camera.transform.position;
            bool nearCabin = Vector3.Distance(eye, transform.position) <= cabinLampOutsideRange;
            float boostTarget = nearCabin ? Mathf.Max(1f, cabinLampOutsideBoost) : 1f;

            float boostRange = Mathf.Max(1f, cabinLampOutsideBoost) - 1f;
            cabinLampBoost = Mathf.MoveTowards(cabinLampBoost, boostTarget,
                Time.deltaTime * boostRange / Mathf.Max(0.01f, cabinLampBlendSeconds));

            // A slight outside-only attenuation, blended slowly: from outside the cabin
            // reads a touch brighter through the glazing and against the dark hull, so
            // the interior light is trimmed a little while the player is out. The slow
            // blend keeps the hatch transition unnoticeable (the old AABB test dropped
            // the lamps 3x in one step).
            float insideTarget = IsOnboard() ? 1f : 0f;
            cabinLampInsideBlend = Mathf.MoveTowards(cabinLampInsideBlend, insideTarget,
                Time.deltaTime / Mathf.Max(0.01f, cabinLampOutsideBlendSeconds));
            float outsideScale = Mathf.Clamp(cabinLampOutsideScale, 0.2f, 1f);
            float scale = Mathf.Lerp(outsideScale, 1f, cabinLampInsideBlend);

            float intensity = cabinLampLumens * cabinLampBoost * scale;
            for (int i = 0; i < cabinLamps.Length; i++)
            {
                if (cabinLamps[i] != null)
                {
                    cabinLamps[i].intensity = intensity;
                }
            }
        }

        /// <summary>
        /// Real interior lights at the Cabin_Lights strip ring. The strips are
        /// emissive only and HDRP's SSGI is off, so without them the HDRP/Lit
        /// cabin walls receive no light in vacuum (the Sun through the hatch is
        /// the only source, and it changes with the ship's attitude and view).
        /// </summary>
        private Renderer FindCabinStrips()
        {
            foreach (Renderer candidate in GetComponentsInChildren<Renderer>(true))
            {
                if (candidate.name == "Cabin_Lights")
                {
                    return candidate;
                }
            }

            return null;
        }

        private void EnsureCabinLamps()
        {
            if (cabinLampCount <= 0 || cabinLampLumens <= 0f)
            {
                return;
            }

            Renderer strips = FindCabinStrips();
            CockpitSeat seat = GetComponentInChildren<CockpitSeat>(true);
            if (strips == null || seat == null || seat.Anchor == null)
            {
                return;
            }

            Transform parent = strips.transform;
            // Guard against a repeated Start (object re-enabled, domain reload off).
            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i).name.StartsWith("CabinLamp"))
                {
                    return;
                }
            }

            // The seat's local +Z is its up (see CockpitSeat): use the seat axes so
            // the lamps sit at the ceiling whatever node scale the FBX carries.
            Vector3 up = seat.Anchor.TransformDirection(Vector3.forward);
            Vector3 right = seat.Anchor.TransformDirection(Vector3.right);
            Vector3 center = strips.bounds.center + (up * 1.1f);

            cabinLamps = new Light[cabinLampCount];
            for (int i = 0; i < cabinLampCount; i++)
            {
                float offset = (i - ((cabinLampCount - 1) * 0.5f)) * cabinLampSpacing;
                GameObject go = new GameObject("CabinLamp" + (i + 1));
                go.transform.SetParent(parent, false);
                go.transform.position = center + (right * offset);

                Light lamp = go.AddComponent<Light>();
                lamp.type = LightType.Point;
                lamp.range = cabinLampRange;
                lamp.intensity = cabinLampLumens;
                lamp.color = cabinLampColor;
                lamp.shadows = LightShadows.None;
                lamp.lightUnit = LightUnit.Lumen;
                lamp.lightmapBakeType = LightmapBakeType.Realtime;
                cabinLamps[i] = lamp;

                HDAdditionalLightData hd = go.GetComponent<HDAdditionalLightData>();
                if (hd == null)
                {
                    hd = go.AddComponent<HDAdditionalLightData>();
                }

                hd.affectDiffuse = true;
                hd.affectSpecular = true;
            }
        }

        private void LateUpdate()
        {
            UpdateCabinLamps();

            if (animator == null || door == null)
            {
                return;
            }

            AnimatorStateInfo s = animator.GetCurrentAnimatorStateInfo(0);
            bool wantOpen = s.IsName("Hatch_Opening") || s.IsName("Hatch_Open");
            swingAngle = Mathf.MoveTowards(swingAngle, wantOpen ? openAngleDegrees : 0f,
                openAngleDegrees / Mathf.Max(0.1f, openSeconds) * Time.deltaTime);
            float eased = openAngleDegrees * Mathf.SmoothStep(0f, 1f, swingAngle / openAngleDegrees);
            door.localPosition = closedPosition;
            door.localRotation = closedRotation * Quaternion.AngleAxis(swingSign * eased, Vector3.up);

            // OrionCollisionBuilder builds the door box after Awake, so find it lazily.
            if (doorCollider == null)
            {
                doorCollider = door.GetComponent<BoxCollider>();
            }

            if (doorCollider != null)
            {
                bool open = swingAngle > 5f;
                if (open && doorCollider.enabled)
                {
                    doorCollider.enabled = false; // the open door lies to the side and does not block passage
                    doorColliderDisabledByUs = true;
                }
                else if (!open && doorColliderDisabledByUs)
                {
                    doorCollider.enabled = true; // closed — a wall again
                    doorColliderDisabledByUs = false;
                }
            }
        }

        /// <summary>Hinge is the door's local Y axis (how the clip rotates it). The sign is resolved empirically: outward = farther from the capsule axis.</summary>
        private float ResolveOutwardSign()
        {
            Renderer nose = null, heat = null;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "Nose_Glass") nose = r;
                else if (r.name == "HeatShield") heat = r;
            }

            if (nose == null || heat == null) return 1f;

            Vector3 c0 = heat.bounds.center;
            Vector3 up = (nose.bounds.center - c0).normalized;
            MeshFilter mf = door.GetComponent<MeshFilter>();
            Vector3 local = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.center : Vector3.zero;

            float Dist(float sign)
            {
                door.localRotation = closedRotation * Quaternion.AngleAxis(sign * 90f, Vector3.up);
                Vector3 d = door.TransformPoint(local) - c0;
                return (d - Vector3.Dot(d, up) * up).magnitude;
            }

            float plus = Dist(1f), minus = Dist(-1f);
            door.localRotation = closedRotation;
            return plus >= minus ? 1f : -1f;
        }

        /// <summary>The hatch exists (the model has the animator and the door node).</summary>
        public bool HatchAvailable
        {
            get { return animator != null && door != null; }
        }

        /// <summary>The hatch is open or opening (the door is swung aside).</summary>
        public bool HatchOpen
        {
            get
            {
                if (animator == null || door == null)
                {
                    return false;
                }

                AnimatorStateInfo s = animator.GetCurrentAnimatorStateInfo(0);
                return s.IsName("Hatch_Opening") || s.IsName("Hatch_Open");
            }
        }

        /// <summary>Open the hatch (airlock outward).</summary>
        public void OpenHatch()
        {
            SetTrigger(HatchOpenId);
        }

        /// <summary>Close the hatch.</summary>
        public void CloseHatch()
        {
            SetTrigger(HatchCloseId);
        }

        /// <summary>
        /// Open or close by state. Ignores the command while the door is already
        /// moving: the animator has no transitions out of the opening/closing
        /// states, and a queued trigger would fire right after the clip ends.
        /// </summary>
        public void SetHatchOpen(bool open)
        {
            if (animator == null || door == null)
            {
                return;
            }

            AnimatorStateInfo s = animator.GetCurrentAnimatorStateInfo(0);
            if (s.IsName("Hatch_Opening") || s.IsName("Hatch_Closing"))
            {
                return;
            }

            bool isOpen = s.IsName("Hatch_Open");
            if (open && !isOpen)
            {
                OpenHatch();
            }
            else if (!open && isOpen)
            {
                CloseHatch();
            }
        }

        /// <summary>Deploy the tablet arm towards the pilot.</summary>
        public void DeployTabletArm()
        {
            SetTrigger(ArmDeployId);
        }

        /// <summary>Retract the tablet arm.</summary>
        public void RetractTabletArm()
        {
            SetTrigger(ArmRetractId);
        }

        private void SetTrigger(int id)
        {
            if (animator != null)
            {
                animator.SetTrigger(id);
            }
        }
    }
}
