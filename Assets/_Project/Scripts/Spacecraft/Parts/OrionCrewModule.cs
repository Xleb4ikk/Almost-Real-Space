using UnityEngine;

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

        private void LateUpdate()
        {
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
