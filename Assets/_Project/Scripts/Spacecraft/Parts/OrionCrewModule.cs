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
        [Tooltip("Яркость ламп кабины, лм (HDRP Lumen). 0 — лампы не создаются.")]
        [SerializeField] private float cabinLampLumens = 10f;

        [Tooltip("Радиус действия лампы, м.")]
        [SerializeField] private float cabinLampRange = 7f;

        [Tooltip("Цвет ламп (тёплый белый).")]
        [SerializeField] private Color cabinLampColor = new Color(1f, 0.94f, 0.86f, 1f);

        [Tooltip("Сколько ламп вдоль потолка (0 — выключить).")]
        [SerializeField] private int cabinLampCount = 3;

        [Tooltip("Расстояние между лампами, м.")]
        [SerializeField] private float cabinLampSpacing = 1.25f;

        [Tooltip("Смещение точечных ламп от центра полос Cabin_Lights вдоль «вверх» кресла, м. " +
            "0 — по центру кабины; положительное — к потолку. Потолочное смещение освещало " +
            "верхние стенки заметно сильнее нижних: из люка кабина казалась ярче, чем изнутри.")]
        [SerializeField] private float cabinLampHeightMeters = 0f;

        [Tooltip("Множитель яркости ламп, пока КАМЕРА рядом с модулем (снаружи или внутри " +
            "кабины): интерьер не гаснет на фоне залитого солнцем корпуса и планеты и не " +
            "мигает при переходе кресло/проход/люк.")]
        [SerializeField] private float cabinLampOutsideBoost = 5f;

        [Tooltip("Дальность усиления, м (камера — модуль). Внутри и снаружи кабины лампа " +
            "горит одинаково; базовая яркость остаётся только для далёкой камеры.")]
        [SerializeField] private float cabinLampOutsideRange = 60f;

        [Tooltip("Опциональное ослабление ламп снаружи кабины (1 — выключено). Оставлено для " +
            "подстройки: основной рычаг равномерности — cabinLampHeightMeters.")]
        [SerializeField] private float cabinLampOutsideScale = 1f;

        [Tooltip("Радиус «внутри кабины» от центра модуля, м: ближе — лампы на полной яркости.")]
        [SerializeField] private float cabinLampInsideRadius = 2.5f;

        [Tooltip("Радиус полного «снаружи», м: дальше — лампы на cabinLampOutsideScale.")]
        [SerializeField] private float cabinLampOutsideRadius = 5f;

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
        private bool cabinLayersApplied;
        private SimulationRunner lampRunner;

        /// <summary>HDRP light layer 2: the cabin interior. The sun does not light it; the cabin lamps do.</summary>
        private const uint CabinInteriorLightLayer = 1u << 1;

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

            if (!cabinLayersApplied)
            {
                TryApplyCabinLightLayers();
            }

            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector3 eye = camera.transform.position;
            float distance = Vector3.Distance(eye, transform.position);
            bool nearCabin = distance <= cabinLampOutsideRange;
            float boostTarget = nearCabin ? Mathf.Max(1f, cabinLampOutsideBoost) : 1f;

            float boostRange = Mathf.Max(1f, cabinLampOutsideBoost) - 1f;
            cabinLampBoost = Mathf.MoveTowards(cabinLampBoost, boostTarget,
                Time.deltaTime * boostRange / Mathf.Max(0.01f, cabinLampBlendSeconds));

            // A slight outside-only attenuation, blended slowly: from outside the cabin
            // reads a touch brighter through the glazing and against the dark hull, so
            // the interior light is trimmed a little while the player is out. The inside
            // test is the distance to the module, not the player state alone: in build
            // mode (no physics) the player is inside the capsule but not seated/carried.
            float insideTarget = IsOnboard()
                ? 1f
                : 1f - Mathf.Clamp01(Mathf.InverseLerp(cabinLampInsideRadius, cabinLampOutsideRadius, distance));
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
            Light[] existing = parent.GetComponentsInChildren<Light>(true);
            if (existing.Length > 0)
            {
                // Re-enabled object / domain reload off: reuse the lamps created earlier.
                cabinLamps = existing;
                return;
            }

            // The seat's local +Z is its up (see CockpitSeat): use the seat axes so the
            // lamps use the cabin's up/right whatever node scale the FBX carries. The
            // height offset is measured from the strip-ring center: 0 = mid-cabin, a
            // positive value pushes the lamps toward the ceiling.
            Vector3 up = seat.Anchor.TransformDirection(Vector3.forward);
            Vector3 right = seat.Anchor.TransformDirection(Vector3.right);
            Vector3 center = strips.bounds.center + (up * cabinLampHeightMeters);

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

        /// <summary>
        /// HDRP light layers: the cabin interior sits on layer 2. The sun is removed from
        /// that layer, so the direct sunlight through the hatch/windows no longer changes
        /// the cabin brightness with the view (bright at the hatch, dim over the console).
        /// The cabin lamps and the soft sky fill stay on layer 2, so the interior has a
        /// stable artificial light; the exterior hull remains on the default layer and is
        /// still sunlit. Retried from LateUpdate until the strip mesh bounds are valid.
        /// </summary>
        private bool TryApplyCabinLightLayers()
        {
            if (cabinLamps == null || cabinLamps.Length == 0)
            {
                return false;
            }

            Renderer strips = FindCabinStrips();
            if (strips == null)
            {
                return false;
            }

            Bounds cabin = strips.bounds;
            if (cabin.size.sqrMagnitude < 1e-6f)
            {
                return false; // mesh bounds not ready on the first frame
            }

            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r == strips || IsExteriorRenderer(r.name))
                {
                    continue;
                }

                if (!cabin.Contains(r.bounds.center))
                {
                    continue;
                }

                r.renderingLayerMask = CabinInteriorLightLayer;
            }

            for (int i = 0; i < cabinLamps.Length; i++)
            {
                Light lamp = cabinLamps[i];
                if (lamp == null)
                {
                    continue;
                }

                HDAdditionalLightData hd = lamp.GetComponent<HDAdditionalLightData>();
                if (hd != null)
                {
                    hd.lightlayersMask = (UnityEngine.Rendering.HighDefinition.RenderingLayerMask)CabinInteriorLightLayer;
                }
            }

            // Direct sun is exterior-only; the cabin lamps above take over the interior.
            // The soft, shadowless sky fill stays on the interior layer too: it is an
            // ambient-like term, not the hard sunlight that made the hatch view brighter.
            SkyEnvironment sky = FindAnyObjectByType<SkyEnvironment>();
            if (sky != null && sky.SunLight != null)
            {
                HDAdditionalLightData hd = sky.SunLight.GetComponent<HDAdditionalLightData>();
                if (hd != null)
                {
                    hd.lightlayersMask = (UnityEngine.Rendering.HighDefinition.RenderingLayerMask)(((uint)hd.lightlayersMask) & ~CabinInteriorLightLayer);
                }
            }

            if (sky != null && sky.SkyFillLight != null)
            {
                HDAdditionalLightData hd = sky.SkyFillLight.GetComponent<HDAdditionalLightData>();
                if (hd != null)
                {
                    hd.lightlayersMask = (UnityEngine.Rendering.HighDefinition.RenderingLayerMask)(((uint)hd.lightlayersMask) | CabinInteriorLightLayer);
                }
            }

            cabinLayersApplied = true;
            return true;
        }

        /// <summary>Hull, heat shield and glazing stay on the default layer (sunlit exterior).</summary>
        private static bool IsExteriorRenderer(string name)
        {
            return name.Contains("Backshell")
                || name.Contains("HeatShield")
                || name.Contains("Hatch_Frame")
                || name.Contains("Hatch_Door")
                || name.Contains("Glass")
                || name.Contains("Window")
                || name.Contains("Nose");
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
